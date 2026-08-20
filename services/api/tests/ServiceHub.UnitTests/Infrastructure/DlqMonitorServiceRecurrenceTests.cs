using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;
using ServiceHub.Infrastructure.Routing;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure;

/// <summary>
/// Phase 4 (Recovery Verification) coverage for <see cref="DlqMonitorService"/>'s scan-time
/// recurrence-detection hook: a newly-detected DLQ message is attributed back to the open
/// <see cref="RecoveryLedgerEntry"/> it recurs from, via the exact
/// <c>x-servicehub-recovery-id</c> marker when present, or a body-hash heuristic fallback when
/// it isn't. Uses the real <see cref="RecoveryLedgerService"/> (backed by the same in-memory
/// <see cref="DlqDbContext"/>), not a mock, so these are effectively small integration tests of
/// the detection-to-ledger-transition path.
/// </summary>
public sealed class DlqMonitorServiceRecurrenceTests : IDisposable
{
    private const string EntityName = "orders-dlq";
    private const string BodyHash = "shared-body-hash";

    private readonly DlqDbContext _dbContext;
    private readonly RecoveryLedgerService _recoveryLedger;
    private readonly Mock<INamespaceRepository> _repoMock = new();
    private readonly Mock<IForensicEngineRouter> _forensicMock = new();
    private readonly Mock<ICloudMessagingProvider> _providerMock = new();
    private readonly Mock<IMessageReceiver> _receiverMock = new();

    private readonly Guid _namespaceId = Guid.NewGuid();
    private readonly Namespace _namespace;

    public DlqMonitorServiceRecurrenceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _recoveryLedger = new RecoveryLedgerService(_dbContext);

        _namespace = Namespace.Create("test-ns", "PROTECTED:encrypted-data").Value;
        typeof(Namespace).GetProperty("Id")!.SetValue(_namespace, _namespaceId);

        _repoMock.Setup(r => r.GetByIdAsync(_namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(_namespace));

        _providerMock.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Azure);
        _providerMock.SetupGet(p => p.Capabilities).Returns(ProviderCapabilities.Azure);
        _providerMock.Setup(p => p.GetMessageReceiver()).Returns(_receiverMock.Object);
        _providerMock.Setup(p => p.ListEntitiesAsync(_namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<CloudEntity>>.Success(
                new[] { new CloudEntity { Name = EntityName, EntityType = "Queue", DeadLetterCount = 1, Provider = CloudProviderType.Azure } }));

        _forensicMock.Setup(f => f.Analyse(It.IsAny<DlqMessage>()))
            .Returns(new ForensicEngineResult(FailureCategory.MaxDelivery, 0.99, "Max delivery", "Safe", "Deterministic"));
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private DlqMonitorService CreateSut()
    {
        var router = new CloudProviderRouter(new[] { _providerMock.Object });
        var configuration = new ConfigurationBuilder().Build();
        return new DlqMonitorService(
            _dbContext, _repoMock.Object, router, _forensicMock.Object,
            configuration, _recoveryLedger, new DlqNotMonitoredLogGuard(),
            NullLogger<DlqMonitorService>.Instance);
    }

    private void SetupPeek(Message message)
    {
        _receiverMock.Setup(r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(new[] { message }));
    }

    private static Message MakeMessage(long sequenceNumber, string? marker = null) =>
        new()
        {
            MessageId = Guid.NewGuid().ToString(),
            SequenceNumber = sequenceNumber,
            Body = "test body",
            EnqueuedTime = DateTimeOffset.UtcNow,
            DeliveryCount = 1,
            ApplicationProperties = marker is null
                ? null
                : new Dictionary<string, object> { ["x-servicehub-recovery-id"] = marker },
        };

    /// <summary>Opens an operation and begins+accepts an entry, landing it in Observing with the
    /// given marker/body-hash — the state a real replay leaves behind.</summary>
    private async Task<RecoveryLedgerEntry> BeginObservingEntryAsync(
        string bodyHash = BodyHash, bool markerApplied = false, string? explicitMarker = null)
    {
        var operationResult = await _recoveryLedger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = _namespace.OwnerId,
            Kind = RecoveryOperationKind.Replay,
            Trigger = RecoveryTrigger.Manual,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            NamespaceId = _namespaceId,
            ScopeDescription = $"entity={EntityName}",
            TargetCount = 1,
        });
        operationResult.IsSuccess.Should().BeTrue();

        var beginResult = await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operationResult.Value.Id,
            OwnerId = _namespace.OwnerId,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            NamespaceId = _namespaceId,
            EntityNameSnapshot = EntityName,
            BodyHash = bodyHash,
            TargetEntity = EntityName,
        });
        beginResult.IsSuccess.Should().BeTrue();
        var entry = beginResult.Value;

        var marker = explicitMarker ?? (markerApplied ? entry.Id.ToString() : null);
        var executed = await _recoveryLedger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = _namespace.OwnerId,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            Outcome = RecoveryExecutionOutcome.Accepted,
            RecoveryMarker = marker,
            MarkerApplied = markerApplied,
        });
        executed.IsSuccess.Should().BeTrue();
        executed.Value.State.Should().Be(RecoveryEntryState.Observing);

        return executed.Value;
    }

    // ── Exact marker match ───────────────────────────────────────────────────

    [Fact]
    public async Task Scan_NewMessageCarriesMarker_MatchingObservingEntry_TransitionsToReturned_ExactConfidence()
    {
        var entry = await BeginObservingEntryAsync(markerApplied: true);

        var sut = CreateSut();
        SetupPeek(MakeMessage(sequenceNumber: 999, marker: entry.Id.ToString()));

        await sut.ScanNamespaceAsync(_namespaceId);

        var updated = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        updated.State.Should().Be(RecoveryEntryState.Returned);
        updated.Disposition.Should().Be(RecoveryDisposition.Returned);
        updated.VerificationConfidence.Should().Be(VerificationConfidence.Exact);
    }

    [Fact]
    public async Task Scan_NewMessageCarriesUnknownMarker_DoesNotAffectUnrelatedEntry()
    {
        var entry = await BeginObservingEntryAsync(markerApplied: true);

        var sut = CreateSut();
        SetupPeek(MakeMessage(sequenceNumber: 999, marker: Guid.NewGuid().ToString()));

        await sut.ScanNamespaceAsync(_namespaceId);

        var updated = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        updated.State.Should().Be(RecoveryEntryState.Observing);
    }

    [Fact]
    public async Task Scan_ProviderGeneratedSequenceNumberOrMessageId_NeverUsedAsCorrelationIdentity()
    {
        // P4: replay mints a brand-new provider identity on every provider. A newly-detected
        // message whose SequenceNumber happens to equal the entry's pre-replay
        // SourceSequenceNumberSnapshot must NOT be treated as a match — only the marker or the
        // body hash may attribute a recurrence.
        var entry = await BeginObservingEntryAsync(bodyHash: "distinct-hash", markerApplied: false);

        var sut = CreateSut();
        // No marker, and a body that hashes to something else entirely — only the sequence
        // number coincidentally lines up with a plausible "original" identity.
        SetupPeek(MakeMessage(sequenceNumber: 42));

        await sut.ScanNamespaceAsync(_namespaceId);

        var updated = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        updated.State.Should().Be(RecoveryEntryState.Observing);
    }

    // ── Heuristic body-hash fallback ─────────────────────────────────────────

    [Fact]
    public async Task Scan_NewMessageNoMarker_MatchesSingleOpenEntryByBodyHash_HeuristicConfidence()
    {
        // The scanned message's body hashes to ComputeExpectedBodyHash("test body") via
        // ComputeBodyHash — the entry's own BodyHash must be pinned to that same value up front,
        // since RecoveryLedgerEntry.BodyHash is immutable once the entry is begun.
        var computedHash = ComputeExpectedBodyHash("test body");
        var entry = await BeginObservingEntryAsync(bodyHash: computedHash, markerApplied: false);

        var sut = CreateSut();
        SetupPeek(MakeMessage(sequenceNumber: 999)); // no marker

        await sut.ScanNamespaceAsync(_namespaceId);

        var updated = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        updated.State.Should().Be(RecoveryEntryState.Returned);
        updated.VerificationConfidence.Should().Be(VerificationConfidence.Heuristic);
    }

    [Fact]
    public async Task Scan_NewMessageNoMarker_MultipleOpenEntriesShareBodyHash_AllRecordedReturned()
    {
        var computedHash = ComputeExpectedBodyHash("test body");
        var first = await BeginObservingEntryAsync(bodyHash: computedHash, markerApplied: false);
        var second = await BeginObservingEntryAsync(bodyHash: computedHash, markerApplied: false);

        var sut = CreateSut();
        SetupPeek(MakeMessage(sequenceNumber: 999));

        await sut.ScanNamespaceAsync(_namespaceId);

        var updatedFirst = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == first.Id);
        var updatedSecond = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == second.Id);

        // Ambiguous: ServiceHub cannot tell which of the two it was, so neither is silently
        // dropped nor falsely marked Recovered — both are recorded as Returned.
        updatedFirst.State.Should().Be(RecoveryEntryState.Returned);
        updatedSecond.State.Should().Be(RecoveryEntryState.Returned);
    }

    [Fact]
    public async Task Scan_MarkerAppliedEntry_NeverMatchedByHeuristicFallback()
    {
        // An entry whose marker WAS applied only ever matches exactly — a scan that doesn't
        // carry its marker (e.g. a genuinely different message with a coincidentally equal body)
        // must not fall back to heuristically matching it too.
        var computedHash = ComputeExpectedBodyHash("test body");
        var entry = await BeginObservingEntryAsync(bodyHash: computedHash, markerApplied: true);

        var sut = CreateSut();
        SetupPeek(MakeMessage(sequenceNumber: 999)); // no marker on the scanned message

        await sut.ScanNamespaceAsync(_namespaceId);

        var updated = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        updated.State.Should().Be(RecoveryEntryState.Observing);
    }

    [Fact]
    public async Task Scan_ExistingTrackedMessageReappearing_DoesNotTriggerRecurrenceDetection()
    {
        // A message already tracked in DlqMessages (same MessageId/SequenceNumber) going back to
        // Active is not a "newly-detected" message — recurrence detection only applies to
        // messages that are new to this scan, per the design: replay always mints a new identity.
        var entry = await BeginObservingEntryAsync(markerApplied: true);

        _dbContext.DlqMessages.Add(new DlqMessage
        {
            MessageId = "already-tracked",
            SequenceNumber = 5,
            BodyHash = "irrelevant",
            NamespaceId = _namespaceId,
            OwnerId = _namespace.OwnerId,
            CloudProvider = CloudProviderType.Azure,
            EntityName = EntityName,
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = DlqMessageStatus.Resolved,
        });
        await _dbContext.SaveChangesAsync();

        var sut = CreateSut();
        // Same MessageId/SequenceNumber as the tracked row, even carrying the marker — this hits
        // the existingMessage branch, not the new-message branch.
        SetupPeek(new Message
        {
            MessageId = "already-tracked",
            SequenceNumber = 5,
            Body = "test body",
            EnqueuedTime = DateTimeOffset.UtcNow,
            ApplicationProperties = new Dictionary<string, object> { ["x-servicehub-recovery-id"] = entry.Id.ToString() },
        });

        await sut.ScanNamespaceAsync(_namespaceId);

        var updated = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        updated.State.Should().Be(RecoveryEntryState.Observing);
    }

    private static string ComputeExpectedBodyHash(string body)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
