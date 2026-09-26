using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.BulkOperations;

/// <summary>
/// Bulk replay's front door (unit 3.2). The preview is computed here, stored, and is the only thing a run can start from —
/// "the preview cannot be skipped" is a property of the data, not a convention of the UI.
/// </summary>
/// <remarks>
/// Each message is judged by the eligibility gate on its own (never a summary of the batch), so a message the gate refuses
/// is held back with its reason and a remedy. The run judges it again at the moment of sending, because a preview is a
/// picture of a moment.
/// </remarks>
public sealed class BulkOperationService : IBulkOperationService
{
    /// <summary>Most messages one job may carry: a run a person can still be expected to have read.</summary>
    public const int MaxMessages = 500;

    /// <summary>How long a preview stays startable.</summary>
    public static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(30);

    private const double DefaultPerSecond = 2;
    private const int DefaultStopAfter = 5;

    private readonly ServiceHubDbContext _db;
    private readonly INamespaceRepository _namespaces;
    private readonly IDlqReplayService _replay;
    private readonly ICloudProviderRouter _router;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;

    /// <summary>Creates the service.</summary>
    public BulkOperationService(
        ServiceHubDbContext db, INamespaceRepository namespaces, IDlqReplayService replay, ICloudProviderRouter router, IConfiguration configuration,
        TimeProvider? time = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _namespaces = namespaces ?? throw new ArgumentNullException(nameof(namespaces));
        _replay = replay ?? throw new ArgumentNullException(nameof(replay));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<Result<BulkPreview>> PreviewAsync(
        string ownerId, IReadOnlySet<Guid>? allowed, RecoveryActor actor, IReadOnlyList<long> dlqMessageIds, CancellationToken ct,
        RecoveryOperationKind kind = RecoveryOperationKind.Replay, string? reason = null)
    {
        if (kind == RecoveryOperationKind.Purge && string.IsNullOrWhiteSpace(reason))
        {
            return Result<BulkPreview>.Failure(Error.Validation("RecoveryLedger.ReasonRequired", "Say why these are being purged — the reason is kept with every one."));
        }

        var ids = dlqMessageIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return Result<BulkPreview>.Failure(Error.Validation(ErrorCodes.ValidationFailed, "Choose at least one dead letter to replay."));
        }

        if (ids.Count > MaxMessages)
        {
            return Result<BulkPreview>.Failure(Error.Validation(ErrorCodes.ValidationFailed, $"A bulk replay carries at most {MaxMessages} messages; choose fewer."));
        }

        var messages = await _db.DlqMessages.AsNoTracking().Where(m => m.OwnerId == ownerId && ids.Contains(m.Id)).ToListAsync(ct).ConfigureAwait(false);
        var byId = messages.ToDictionary(m => m.Id);
        var nsCache = new Dictionary<Guid, Namespace?>();

        var job = new BulkOperationJob
        {
            OwnerId = ownerId, ActorIdentity = actor.Identity, ActorKind = actor.Kind, PreviewedAt = _time.GetUtcNow(),
            Kind = kind, Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            PerSecond = Math.Clamp(_configuration.GetValue("BulkReplay:PerSecond", DefaultPerSecond), 0.1, 50),
            StopAfterConsecutiveFailures = Math.Clamp(_configuration.GetValue("BulkReplay:StopAfterConsecutiveFailures", DefaultStopAfter), 1, 100),
        };

        var position = 0;
        foreach (var id in ids)
        {
            if (!byId.TryGetValue(id, out var message))
            {
                job.Items.Add(new BulkOperationItem
                {
                    DlqMessageId = id, NamespaceId = Guid.Empty, EntityName = "?", State = BulkItemState.HeldBack, Position = position++,
                    ReasonCode = "NOT_FOUND", Remedy = Remedy("NOT_FOUND"),
                });
                continue;
            }

            if (!nsCache.TryGetValue(message.NamespaceId, out var ns))
            {
                var found = await _namespaces.GetByIdAsync(message.NamespaceId, ct).ConfigureAwait(false);
                ns = found.IsSuccess && found.Value.OwnerId == ownerId && (allowed is null || allowed.Contains(found.Value.Id)) ? found.Value : null;
                nsCache[message.NamespaceId] = ns;
            }

            var (state, code) =
                ns is null ? (BulkItemState.HeldBack, "NOT_FOUND")
                : message.Status != DlqMessageStatus.Active ? (BulkItemState.HeldBack, "NOT_ACTIVE")
                // Purge only where the cloud can delete one message — read from capability, never a name (R4).
                : kind == RecoveryOperationKind.Purge && !_router.Resolve(ns.Provider).Capabilities.SupportsPurge ? (BulkItemState.HeldBack, "PURGE_UNSUPPORTED")
                : (BulkItemState.Queued, (string?)null);

            if (state == BulkItemState.Queued)
            {
                var decision = await _replay.CheckEligibilityAsync(message.Id, ns!, actor, kind, ct).ConfigureAwait(false);
                if (decision is null)
                {
                    (state, code) = (BulkItemState.HeldBack, "NOT_FOUND");
                }
                else if (decision.Verdict != EligibilityVerdict.Allow)
                {
                    (state, code) = (BulkItemState.HeldBack, decision.ReasonCode ?? decision.Verdict.ToString().ToUpperInvariant());
                }
            }

            job.Items.Add(new BulkOperationItem
            {
                DlqMessageId = message.Id, NamespaceId = message.NamespaceId, EntityName = message.EntityName,
                DeadLetterReason = message.DeadLetterReason, State = state, ReasonCode = code, Remedy = code is null ? null : Remedy(code),
                Position = position++,
            });
        }

        bool canProve;
        // Can a fix be proven for everything in this run? Read from capability, never from a provider's name (R4).
        canProve = nsCache.Values.OfType<Namespace>().All(n => _router.Resolve(n.Provider).Capabilities.CanProveDlqAbsence);

        _db.BulkOperationJobs.Add(job);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var groups = job.Items.GroupBy(i => i.DeadLetterReason ?? "Unknown")
            .Select(g => new BulkGroup(g.Key, g.Count(), g.Count(i => i.State == BulkItemState.Queued), g.Count(i => i.State == BulkItemState.HeldBack)))
            .OrderByDescending(g => g.Selected).ThenBy(g => g.Reason, StringComparer.Ordinal).ToList();
        var held = job.Items.Where(i => i.State == BulkItemState.HeldBack)
            .Select(i => new BulkHeldBack(i.DlqMessageId, i.EntityName, i.DeadLetterReason, i.ReasonCode!, i.Remedy!)).ToList();

        return Result<BulkPreview>.Success(new BulkPreview(
            job.Id, job.Items.Count, job.Items.Count(i => i.State == BulkItemState.Queued), held.Count, groups, held,
            job.PerSecond, job.StopAfterConsecutiveFailures, (int)PreviewLifetime.TotalMinutes, canProve, job.Kind));
    }

    /// <inheritdoc />
    public async Task<Result<BulkProgress>> StartAsync(string ownerId, Guid previewId, bool sampleOnly, CancellationToken ct, RecoveryOperationKind kind = RecoveryOperationKind.Replay)
    {
        var job = await LoadAsync(ownerId, previewId, ct).ConfigureAwait(false);
        if (job is null)
        {
            return NotFound(previewId);
        }

        // The start must say what it starts: a replay intent can never set off a purge preview, nor the other way round.
        if (job.Kind != kind)
        {
            return Result<BulkProgress>.Failure(Error.Conflict("BULK_KIND_MISMATCH", $"This preview is a {job.Kind.ToString().ToLowerInvariant()}, not a {kind.ToString().ToLowerInvariant()}."));
        }

        if (job.Status != BulkOperationStatus.Previewed)
        {
            return Result<BulkProgress>.Failure(Error.Conflict("BULK_NOT_PREVIEWED", "This preview has already been used. Make a new preview to run it again."));
        }

        if (_time.GetUtcNow() - job.PreviewedAt > PreviewLifetime)
        {
            job.Status = BulkOperationStatus.Expired;
            job.EndedAt = _time.GetUtcNow();
            job.EndedReason = "The preview expired before it was started.";
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<BulkProgress>.Failure(Error.Conflict("BULK_PREVIEW_EXPIRED", "That preview is too old to run — what it described may have changed. Make a new one."));
        }

        if (job.Items.All(i => i.State != BulkItemState.Queued))
        {
            return Result<BulkProgress>.Failure(Error.Conflict("BULK_NOTHING_TO_REPLAY", "Nothing in this preview can be replayed."));
        }

        job.Status = BulkOperationStatus.Running;
        job.StartedAt = _time.GetUtcNow();
        job.SampleOnly = sampleOnly;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<BulkProgress>.Success(Progress(job));
    }

    /// <inheritdoc />
    public async Task<Result<BulkProgress>> GetAsync(string ownerId, Guid id, CancellationToken ct)
    {
        var job = await LoadAsync(ownerId, id, ct, tracking: false).ConfigureAwait(false);
        return job is null ? NotFound(id) : Result<BulkProgress>.Success(Progress(job));
    }

    /// <inheritdoc />
    public async Task<Result<BulkProgress>> CancelAsync(string ownerId, Guid id, CancellationToken ct)
    {
        var job = await LoadAsync(ownerId, id, ct).ConfigureAwait(false);
        if (job is null)
        {
            return NotFound(id);
        }

        if (job.Status is BulkOperationStatus.Running)
        {
            job.CancelRequested = true;
        }
        else if (job.Status is BulkOperationStatus.Previewed)
        {
            job.Status = BulkOperationStatus.Cancelled;
            job.EndedAt = _time.GetUtcNow();
            job.EndedReason = "Cancelled before it started.";
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<BulkProgress>.Success(Progress(job));
    }

    /// <summary>The progress numbers of a job.</summary>
    public static BulkProgress Progress(BulkOperationJob job)
    {
        int Count(BulkItemState s) => job.Items.Count(i => i.State == s);
        var willReplay = job.Items.Count(i => i.State != BulkItemState.HeldBack);
        return new BulkProgress(
            job.Id, job.Status, job.Items.Count, willReplay, Count(BulkItemState.Sent), Count(BulkItemState.Failed), Count(BulkItemState.Unknown),
            Count(BulkItemState.Queued) + Count(BulkItemState.Sending), Count(BulkItemState.HeldBack), job.SampleOnly, job.EndedReason,
            job.PreviewedAt, job.StartedAt, job.EndedAt, job.Kind);
    }

    /// <summary>The words that say what to do about a held-back message.</summary>
    public static string Remedy(string reasonCode) => reasonCode switch
    {
        "NOT_FOUND" => "ServiceHub could not find this message any more. Refresh the list.",
        "NOT_ACTIVE" => "It is no longer in the dead-letter queue, so there is nothing to replay.",
        "PRODUCTION_ELEVATION_REQUIRED" => "This is a production namespace. A person with production approval has to allow it.",
        "RECURRENCE_CAP_EXCEEDED" or "RECURRENCE_CAP_EXCEEDED_HEURISTIC" or "RECURRENCE_CAP_AMBIGUOUS_COLLISION" =>
            "It has already been replayed and came back too many times. Fix the cause first, then replay it.",
        "RATE_LIMITED" or "FLEET_RATE_LIMITED" => "Too many replays have happened recently. Try again in a few minutes.",
        "EMERGENCY_STOP_ACTIVE" => "Emergency stop is on. Nothing is sent until it is lifted.",
        "PROVIDER_CANNOT_VERIFY_ABSENCE" => "This cloud can't prove a fix held, so ServiceHub asks a person first.",
        "AUTONOMY_GRANT_INSUFFICIENT" => "ServiceHub hasn't earned the right to replay this kind of failure on its own yet.",
        "PURGE_UNSUPPORTED" => "This cloud cannot delete one message on its own, so it is left where it is.",
        "PURGE_AUTOMATION_PROHIBITED" => "Only a person can purge, never an automatic rule.",
        _ => $"A safety check held it back ({reasonCode}). A person with approval rights has to decide.",
    };

    private async Task<BulkOperationJob?> LoadAsync(string ownerId, Guid id, CancellationToken ct, bool tracking = true)
    {
        var query = tracking ? _db.BulkOperationJobs : _db.BulkOperationJobs.AsNoTracking();
        return await query.Include(j => j.Items).FirstOrDefaultAsync(j => j.Id == id && j.OwnerId == ownerId, ct).ConfigureAwait(false);
    }

    private static Result<BulkProgress> NotFound(Guid id) =>
        Result<BulkProgress>.Failure(Error.NotFound("BULK_NOT_FOUND", $"Bulk replay '{id}' was not found."));
}
