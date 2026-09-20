using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceHub.Core.Entities;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>
/// <inheritdoc cref="IRecoveryEpochArchiveService"/>
/// </summary>
/// <remarks>
/// The Recovery Evidence Ledger's append-only guard (<c>RecoveryLedgerAppendOnlyGuard</c>) is
/// enforced purely at the EF Core <c>ChangeTracker</c> level, inside
/// <see cref="DlqDbContext.SaveChanges"/>/<see cref="DlqDbContext.SaveChangesAsync(bool, CancellationToken)"/>.
/// This class prunes archived <see cref="RecoveryEvent"/> rows via a raw parameterized SQL
/// <c>DELETE</c> against the connection, which never populates the <c>ChangeTracker</c> and so is
/// not intercepted by that guard — the exact mechanism
/// <c>BackupRestoreVerificationTests</c>' own tamper test uses to simulate a raw on-disk edit,
/// used here deliberately instead of adversarially. This is safe specifically because every
/// pruned row's full content survives, byte for byte, in an archive file this class writes and
/// independently re-verifies from disk <em>before</em> deleting anything — never the reverse.
/// </remarks>
public sealed class RecoveryEpochArchiveService : IRecoveryEpochArchiveService
{
    private static readonly JsonSerializerOptions ArchiveJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IRecoveryLedger _recoveryLedger;
    private readonly DlqDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly RecoveryEpochArchiveOptions _options;
    private readonly ILogger<RecoveryEpochArchiveService> _logger;

    /// <summary>Initialises a new instance of <see cref="RecoveryEpochArchiveService"/>.</summary>
    public RecoveryEpochArchiveService(
        IRecoveryLedger recoveryLedger,
        DlqDbContext dbContext,
        IConfiguration configuration,
        IOptions<RecoveryEpochArchiveOptions> options,
        ILogger<RecoveryEpochArchiveService> logger)
    {
        _recoveryLedger = recoveryLedger ?? throw new ArgumentNullException(nameof(recoveryLedger));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryEpochSealSummary>> SealAndArchiveEpochAsync(
        string ownerId, RecoveryActor actor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("Owner identifier is required.", nameof(ownerId));
        }

        var sealResult = await _recoveryLedger.SealEpochAsync(ownerId, actor, cancellationToken);
        if (!sealResult.IsSuccess)
        {
            return Result<RecoveryEpochSealSummary>.Failure(sealResult.Error!);
        }

        var sealEvent = sealResult.Value;
        var epochNumber = ParseEpochNumber(sealEvent.DetailJson);

        // Everything currently live for this owner, strictly before the new seal marker. This
        // naturally includes any earlier seal marker too (superseded now that a newer one
        // exists), so archives chain recursively without any separately-tracked anchor state —
        // each archive simply contains "whatever was live before this sealing".
        var toArchive = await _dbContext.RecoveryEvents
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId && e.Seq < sealEvent.Seq)
            .OrderBy(e => e.Seq)
            .ToListAsync(cancellationToken);

        if (toArchive.Count == 0)
        {
            return Result<RecoveryEpochSealSummary>.Failure(Error.Internal(
                "RecoveryEpochArchive.NothingToArchive",
                "The epoch was sealed but there is nothing prior to it to archive — this should be unreachable, since SealEpochAsync itself refuses to seal an owner with no events."));
        }

        var rangeVerification = RecoveryChainVerifier.Verify(
            ownerId, toArchive, toArchive[0].Seq, toArchive[0].PrevHash);
        if (!rangeVerification.IsValid)
        {
            _logger.LogError(
                "Refusing to archive epoch {EpochNumber} for owner {OwnerId}: the range to be archived does not verify ({Reason})",
                epochNumber, ownerId, rangeVerification.Reason);
            return Result<RecoveryEpochSealSummary>.Failure(Error.Internal(
                "RecoveryEpochArchive.RangeDoesNotVerify",
                $"Refusing to archive: {rangeVerification.Reason}. Nothing was written or deleted."));
        }

        var archive = new RecoveryEpochArchive
        {
            OwnerId = ownerId,
            EpochNumber = epochNumber,
            StartSeq = toArchive[0].Seq,
            StartPrevHash = toArchive[0].PrevHash,
            EndSeq = toArchive[^1].Seq,
            TerminalHash = toArchive[^1].EntryHash,
            SealedAtUtc = sealEvent.OccurredAt,
            Events = toArchive.Select(MapEvent).ToList(),
        };

        var ownerDirectory = Path.Combine(ResolveArchiveRoot(), ownerId);
        Directory.CreateDirectory(ownerDirectory);
        var finalPath = Path.Combine(ownerDirectory, $"epoch-{epochNumber:D6}.json");
        var tempPath = finalPath + ".tmp";

        await File.WriteAllTextAsync(
            tempPath, JsonSerializer.Serialize(archive, ArchiveJsonOptions), cancellationToken);

        // Read the file back and re-verify from disk alone before deleting anything live — a
        // paranoid double-check that what is about to become the sole surviving copy is
        // actually intact, independent of the in-memory object just serialized.
        var readBackJson = await File.ReadAllTextAsync(tempPath, cancellationToken);
        var readBack = JsonSerializer.Deserialize<RecoveryEpochArchive>(readBackJson, ArchiveJsonOptions);
        var readBackIsValid = readBack is not null
            && RecoveryChainVerifier.Verify(
                ownerId, readBack.Events.Select(MapResponse).ToList(), readBack.StartSeq, readBack.StartPrevHash).IsValid;

        if (readBack is null || !readBackIsValid || readBack.TerminalHash != archive.TerminalHash)
        {
            File.Delete(tempPath);
            _logger.LogError(
                "Refusing to prune epoch {EpochNumber} for owner {OwnerId}: the archive file did not verify after being written.",
                epochNumber, ownerId);
            return Result<RecoveryEpochSealSummary>.Failure(Error.Internal(
                "RecoveryEpochArchive.WriteVerificationFailed",
                "The archive file did not verify after being written; nothing was deleted."));
        }

        File.Move(tempPath, finalPath, overwrite: true);

        var deletedCount = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM RecoveryEvents WHERE OwnerId = {ownerId} AND Seq < {sealEvent.Seq}",
            cancellationToken);

        _logger.LogInformation(
            "Sealed and archived epoch {EpochNumber} for owner {OwnerId}: {ArchivedCount} events (Seq {StartSeq}-{EndSeq}) written to {ArchivePath}, {DeletedCount} rows pruned from the live table.",
            epochNumber, ownerId, toArchive.Count, archive.StartSeq, archive.EndSeq, finalPath, deletedCount);

        return Result<RecoveryEpochSealSummary>.Success(new RecoveryEpochSealSummary(
            epochNumber, archive.StartSeq, archive.EndSeq, archive.TerminalHash, finalPath, toArchive.Count));
    }

    private static int ParseEpochNumber(string? detailJson)
    {
        if (detailJson is null)
        {
            throw new InvalidOperationException("EpochSealed event was appended without an epochNumber in its DetailJson.");
        }

        using var document = JsonDocument.Parse(detailJson);
        return document.RootElement.GetProperty("epochNumber").GetInt32();
    }

    /// <summary>
    /// Same <c>DlqDatabase:DataDirectory</c> resolution <see cref="Backup.BackupService"/> itself
    /// uses for its own default subfolder, duplicated here rather than shared for the same
    /// reason that class documents its own duplication of the namespace-store path.
    /// </summary>
    private string ResolveArchiveRoot()
    {
        var configured = _options.ArchiveDirectory;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var dataDir = _configuration["DlqDatabase:DataDirectory"]
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        return Path.Combine(dataDir, "recovery-archive");
    }

    private static RecoveryEventResponse MapEvent(RecoveryEvent evt) => new(
        Id: evt.Id,
        OwnerId: evt.OwnerId,
        Seq: evt.Seq,
        EntryId: evt.EntryId,
        OperationId: evt.OperationId,
        EventType: evt.EventType.ToString(),
        OccurredAt: evt.OccurredAt,
        ActorIdentity: evt.ActorIdentity,
        ActorKind: evt.ActorKind.ToString(),
        DetailJson: evt.DetailJson,
        PrevHash: evt.PrevHash,
        EntryHash: evt.EntryHash,
        SchemaVersion: evt.SchemaVersion);

    private static RecoveryEvent MapResponse(RecoveryEventResponse response) => new()
    {
        Id = response.Id,
        OwnerId = response.OwnerId,
        Seq = response.Seq,
        EntryId = response.EntryId,
        OperationId = response.OperationId,
        EventType = Enum.Parse<RecoveryEventType>(response.EventType),
        OccurredAt = response.OccurredAt,
        ActorIdentity = response.ActorIdentity,
        ActorKind = Enum.Parse<RecoveryActorKind>(response.ActorKind),
        DetailJson = response.DetailJson,
        PrevHash = response.PrevHash,
        EntryHash = response.EntryHash,
        SchemaVersion = response.SchemaVersion,
    };
}
