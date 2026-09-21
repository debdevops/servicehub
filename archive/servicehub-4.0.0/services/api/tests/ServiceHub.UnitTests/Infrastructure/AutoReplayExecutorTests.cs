using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.AI;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure;

public class AutoReplayExecutorTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly Mock<IMessageOperationsService> _messageOperations = new();
    private readonly Mock<IPlaybookLedger> _playbookLedger = new();
    private readonly Mock<ILogger<AutoReplayExecutor>> _logger = new();
    private readonly IRecoveryLedger _recoveryLedger;
    private readonly IRecoveryEligibilityGate _eligibilityGate;
    private readonly IFailureFeatureExtractor _featureExtractor = new FailureFeatureExtractor();
    private readonly IFailureFingerprintBuilder _fingerprintBuilder = new FailureFingerprintBuilder();
    private readonly IConfiguration _configuration =
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
    private readonly AutoReplayExecutor _executor;
    private readonly Namespace _testNamespace = Namespace.Create(
        "test-namespace", "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
        ownerId: TestConstants.TestOwnerId).Value;

    public AutoReplayExecutorTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _recoveryLedger = new RecoveryLedgerService(_dbContext);
        _eligibilityGate = new RecoveryEligibilityGate(_recoveryLedger, NullLogger<RecoveryEligibilityGate>.Instance);

        // Default: no existing open ReplayPlan proposal — the dedup-guard query
        // ProposeReplayPlanAsync runs before every ProposeAsync call finds nothing to dedup
        // against, so tests that don't care about the guard behave as if it were absent.
        _playbookLedger
            .Setup(p => p.QueryEntriesAsync(
                It.IsAny<string>(), It.IsAny<PillarKind?>(), It.IsAny<Guid?>(), It.IsAny<PlaybookEntryState?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PlaybookEntry>>(Array.Empty<PlaybookEntry>()));

        // Default: every ProposeAsync call succeeds with a throwaway entry — tests that care about
        // the ReplayPlan proposal itself override this with their own Setup/Verify.
        _playbookLedger
            .Setup(p => p.ProposeAsync(It.IsAny<ProposePlaybookEntryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProposePlaybookEntryRequest request, CancellationToken _) => Result<PlaybookEntry>.Success(new PlaybookEntry
            {
                OwnerId = request.OwnerId,
                PillarKind = request.PillarKind,
                ProposalKind = request.ProposalKind,
                EvidenceRefJson = request.EvidenceRefJson,
                ProposalJson = request.ProposalJson,
                ProposedAt = DateTimeOffset.UtcNow,
                ProposerIdentity = request.Proposer.Identity,
                ProposerKind = request.Proposer.Kind,
                SignatureHashSnapshot = request.SignatureHashSnapshot,
                NamespaceId = request.NamespaceId,
                ExpiresAt = DateTimeOffset.UtcNow + request.ExpiresAfter,
            }));

        _executor = new AutoReplayExecutor(
            _dbContext, _messageOperations.Object, _recoveryLedger, _eligibilityGate, _playbookLedger.Object,
            _featureExtractor, _fingerprintBuilder, _configuration, _logger.Object);
    }

    /// <summary>
    /// Seeds an <c>AutonomyGrant</c> at Standing (L4) for the exact SignatureHash the executor
    /// will independently compute for <paramref name="message"/> — predicate 5 (roadmap §9.4.3)
    /// now requires an earned grant before <c>AutoReplayExecutor</c> may execute unattended, so
    /// every test exercising the actual replay/provider path must earn one first.
    /// </summary>
    private async Task SeedStandingGrantAsync(DlqMessage message, string ownerId = TestConstants.TestOwnerId)
    {
        var features = (await _featureExtractor.ExtractAsync(message)).Value;
        var fingerprint = (await _fingerprintBuilder.ComputeAsync(features)).Value;
        var result = await _recoveryLedger.RecordAutonomyGrantTransitionAsync(
            ownerId, fingerprint.Hash, RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "test: earned standing", evidenceJson: null);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
    }

    /// <summary>
    /// Opens a real <see cref="RecoveryOperation"/> so <c>ExecuteAsync</c>'s
    /// <c>BeginEntryAsync</c> call has something to attach to — a random Guid would fail
    /// with <c>RecoveryLedger.OperationNotFound</c> before the replay is ever attempted.
    /// </summary>
    private async Task<Guid> OpenOperationAsync(AutoReplayRule rule)
    {
        var result = await _recoveryLedger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = rule.OwnerId,
            Kind = RecoveryOperationKind.Replay,
            Trigger = RecoveryTrigger.AutoRule,
            Actor = new RecoveryActor("test-rule", RecoveryActorKind.Automation),
            ScopeDescription = "test",
            SourceRuleId = rule.Id,
            TargetCount = 1,
        });
        return result.Value.Id;
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private DlqMessage CreateMessage(
        long seq = 1,
        CloudProviderType provider = CloudProviderType.Azure,
        ServiceBusEntityType entityType = ServiceBusEntityType.Queue,
        string entityName = "test-queue",
        string? topicName = null)
    {
        var msg = new DlqMessage
        {
            MessageId = $"msg-{seq}", SequenceNumber = seq, BodyHash = $"hash-{seq}",
            NamespaceId = Guid.NewGuid(), OwnerId = TestConstants.TestOwnerId, EntityName = entityName,
            EntityType = entityType,
            TopicName = topicName,
            CloudProvider = provider,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddHours(-1),
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeliveryCount = 5, MessageSize = 100,
            Status = DlqMessageStatus.Active
        };
        _dbContext.DlqMessages.Add(msg);
        _dbContext.SaveChanges();
        return msg;
    }

    private AutoReplayRule CreateRule(string name = "Test Rule", int maxPerHour = 100)
    {
        var rule = new AutoReplayRule
        {
            Name = name,
            OwnerId = TestConstants.TestOwnerId,
            Enabled = true,
            ConditionsJson = "[]", ActionsJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            MaxReplaysPerHour = maxPerHour
        };
        _dbContext.AutoReplayRules.Add(rule);
        _dbContext.SaveChanges();
        return rule;
    }

    // ── Constructor ──────────────────────────────────────────

    [Fact]
    public void Constructor_NullDbContext_Throws()
    {
        var act = () => new AutoReplayExecutor(
            null!, _messageOperations.Object, new RecoveryLedgerService(_dbContext), _eligibilityGate, _playbookLedger.Object,
            _featureExtractor, _fingerprintBuilder, _configuration, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
    }

    [Fact]
    public void Constructor_NullMessageOperations_Throws()
    {
        var act = () => new AutoReplayExecutor(
            _dbContext, null!, new RecoveryLedgerService(_dbContext), _eligibilityGate, _playbookLedger.Object,
            _featureExtractor, _fingerprintBuilder, _configuration, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("messageOperations");
    }

    [Fact]
    public void Constructor_NullEligibilityGate_Throws()
    {
        var act = () => new AutoReplayExecutor(
            _dbContext, _messageOperations.Object, new RecoveryLedgerService(_dbContext), null!, _playbookLedger.Object,
            _featureExtractor, _fingerprintBuilder, _configuration, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("eligibilityGate");
    }

    [Fact]
    public void Constructor_NullPlaybookLedger_Throws()
    {
        var act = () => new AutoReplayExecutor(
            _dbContext, _messageOperations.Object, new RecoveryLedgerService(_dbContext), _eligibilityGate, null!,
            _featureExtractor, _fingerprintBuilder, _configuration, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("playbookLedger");
    }

    [Fact]
    public void Constructor_NullFeatureExtractor_Throws()
    {
        var act = () => new AutoReplayExecutor(
            _dbContext, _messageOperations.Object, new RecoveryLedgerService(_dbContext), _eligibilityGate, _playbookLedger.Object,
            null!, _fingerprintBuilder, _configuration, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("featureExtractor");
    }

    [Fact]
    public void Constructor_NullFingerprintBuilder_Throws()
    {
        var act = () => new AutoReplayExecutor(
            _dbContext, _messageOperations.Object, new RecoveryLedgerService(_dbContext), _eligibilityGate, _playbookLedger.Object,
            _featureExtractor, null!, _configuration, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("fingerprintBuilder");
    }

    [Fact]
    public void Constructor_NullConfiguration_Throws()
    {
        var act = () => new AutoReplayExecutor(
            _dbContext, _messageOperations.Object, new RecoveryLedgerService(_dbContext), _eligibilityGate, _playbookLedger.Object,
            _featureExtractor, _fingerprintBuilder, null!, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new AutoReplayExecutor(
            _dbContext, _messageOperations.Object, new RecoveryLedgerService(_dbContext), _eligibilityGate, _playbookLedger.Object,
            _featureExtractor, _fingerprintBuilder, _configuration, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── CanReplayAsync ──────────────────────────────────────

    [Fact]
    public async Task CanReplay_RuleNotFound_ReturnsFalse()
    {
        var result = await _executor.CanReplayAsync(999);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CanReplay_UnderLimit_ReturnsTrue()
    {
        var rule = CreateRule(maxPerHour: 100);
        var result = await _executor.CanReplayAsync(rule.Id);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanReplay_AtLimit_ReturnsFalse()
    {
        var rule = CreateRule(maxPerHour: 2);
        var msg = CreateMessage(1);

        // Add 2 replay histories within the last hour
        for (int i = 0; i < 2; i++)
        {
            _dbContext.ReplayHistories.Add(new ReplayHistory
            {
                DlqMessageId = msg.Id, RuleId = rule.Id,
                ReplayedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                ReplayedBy = "test", ReplayStrategy = "original",
                ReplayedToEntity = "q", OutcomeStatus = "Success"
            });
        }
        await _dbContext.SaveChangesAsync();

        var result = await _executor.CanReplayAsync(rule.Id);
        result.Should().BeFalse();
    }

    // ── CanReplayFleetWideAsync ───────────────────────────────

    private AutoReplayExecutor CreateExecutorWithFleetCap(int cap)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RecoveryEvidence:FleetReplayVelocityCapPerHour"] = cap.ToString()
            })
            .Build();
        return new AutoReplayExecutor(
            _dbContext, _messageOperations.Object, _recoveryLedger, _eligibilityGate, _playbookLedger.Object,
            _featureExtractor, _fingerprintBuilder, configuration, _logger.Object);
    }

    [Fact]
    public async Task CanReplayFleetWide_NoHistory_ReturnsTrue()
    {
        var result = await _executor.CanReplayFleetWideAsync(TestConstants.TestOwnerId);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanReplayFleetWide_UnderCap_ReturnsTrue()
    {
        var executor = CreateExecutorWithFleetCap(cap: 5);
        var result = await executor.CanReplayFleetWideAsync(TestConstants.TestOwnerId);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanReplayFleetWide_CapExceededAcrossMultipleRules_ReturnsFalse()
    {
        // Two separate rules, each individually well under its own per-rule limit, but their
        // combined recent replay volume exceeds the fleet-wide cap.
        var ruleA = CreateRule("Rule A", maxPerHour: 100);
        var ruleB = CreateRule("Rule B", maxPerHour: 100);
        var msg = CreateMessage(1);

        for (int i = 0; i < 2; i++)
        {
            _dbContext.ReplayHistories.Add(new ReplayHistory
            {
                DlqMessageId = msg.Id, RuleId = ruleA.Id,
                ReplayedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                ReplayedBy = "test", ReplayStrategy = "original",
                ReplayedToEntity = "q", OutcomeStatus = "Success"
            });
            _dbContext.ReplayHistories.Add(new ReplayHistory
            {
                DlqMessageId = msg.Id, RuleId = ruleB.Id,
                ReplayedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                ReplayedBy = "test", ReplayStrategy = "original",
                ReplayedToEntity = "q", OutcomeStatus = "Success"
            });
        }
        await _dbContext.SaveChangesAsync();

        var executor = CreateExecutorWithFleetCap(cap: 3);
        var result = await executor.CanReplayFleetWideAsync(TestConstants.TestOwnerId);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CanReplayFleetWide_OnlyCountsSameOwner()
    {
        var otherOwnerRule = new AutoReplayRule
        {
            Name = "Other Owner Rule", OwnerId = "other-owner", Enabled = true,
            ConditionsJson = "[]", ActionsJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow, MaxReplaysPerHour = 100,
        };
        _dbContext.AutoReplayRules.Add(otherOwnerRule);
        await _dbContext.SaveChangesAsync();

        var msg = CreateMessage(1);
        for (int i = 0; i < 5; i++)
        {
            _dbContext.ReplayHistories.Add(new ReplayHistory
            {
                DlqMessageId = msg.Id, RuleId = otherOwnerRule.Id,
                ReplayedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                ReplayedBy = "test", ReplayStrategy = "original",
                ReplayedToEntity = "q", OutcomeStatus = "Success"
            });
        }
        await _dbContext.SaveChangesAsync();

        var executor = CreateExecutorWithFleetCap(cap: 3);
        var result = await executor.CanReplayFleetWideAsync(TestConstants.TestOwnerId);
        result.Should().BeTrue();
    }

    // ── ExecuteAsync ────────────────────────────────────────

    [Fact]
    public async Task Execute_RateLimited_ReturnsFailure()
    {
        var rule = CreateRule(maxPerHour: 0); // 0 limit = always rate limited
        var msg = CreateMessage(1);
        var action = new RuleAction();

        // Add a replay to trigger limit
        _dbContext.ReplayHistories.Add(new ReplayHistory
        {
            DlqMessageId = msg.Id, RuleId = rule.Id,
            ReplayedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ReplayedBy = "test", ReplayStrategy = "original",
            ReplayedToEntity = "q", OutcomeStatus = "Success"
        });
        await _dbContext.SaveChangesAsync();

        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, await OpenOperationAsync(rule));
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_ReplaySucceeds_UpdatesMessageAndRecordsHistory()
    {
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();
        await SeedStandingGrantAsync(msg);

        _messageOperations
            .Setup(m => m.ReplayMessageAsync(msg.NamespaceId, "test-queue", null, msg.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, await OpenOperationAsync(rule));

        result.IsSuccess.Should().BeTrue();
        msg.Status.Should().Be(DlqMessageStatus.Replayed);
        msg.ReplaySuccess.Should().BeTrue();
        rule.SuccessCount.Should().Be(1);
        rule.MatchCount.Should().Be(1);

        var history = _dbContext.ReplayHistories.Single();
        history.DlqMessageId.Should().Be(msg.Id);
        history.RuleId.Should().Be(rule.Id);
        history.OutcomeStatus.Should().Be("Success");
        history.ReplayedToEntity.Should().Be("test-queue");
    }

    [Fact]
    public async Task Execute_ReplayFails_MarksMessageReplayFailedAndRecordsHistory()
    {
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();
        await SeedStandingGrantAsync(msg);

        _messageOperations
            .Setup(m => m.ReplayMessageAsync(msg.NamespaceId, "test-queue", null, msg.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(Error.NotFound("NS_NOT_FOUND", "Namespace not found")));

        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, await OpenOperationAsync(rule));

        result.IsFailure.Should().BeTrue();
        msg.Status.Should().Be(DlqMessageStatus.ReplayFailed);
        msg.ReplaySuccess.Should().BeFalse();
        rule.SuccessCount.Should().Be(0);
        rule.MatchCount.Should().Be(1);

        var history = _dbContext.ReplayHistories.Single();
        history.OutcomeStatus.Should().Be("Failed");
        history.ErrorDetails.Should().Be("Namespace not found");
    }

    [Fact]
    public async Task Execute_ReplayThrows_RecordsErrorHistoryAndReturnsFailure()
    {
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();
        await SeedStandingGrantAsync(msg);

        _messageOperations
            .Setup(m => m.ReplayMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, await OpenOperationAsync(rule));

        result.IsFailure.Should().BeTrue();
        msg.Status.Should().Be(DlqMessageStatus.ReplayFailed);

        var history = _dbContext.ReplayHistories.Single();
        history.OutcomeStatus.Should().Be("Error");
        history.ErrorDetails.Should().Be("boom");
    }

    [Fact]
    public async Task Execute_AwsReplayAmbiguous_RecordsExecutionUnknown_NotExecutionFailed()
    {
        // Regresses the "duplicate replay risk" gap: AwsMessageReceiver.ReplayMessageAsync
        // returns AWS.SQS.ReplayAmbiguous when Send to the source queue succeeded but Delete
        // from the DLQ failed — the message is genuinely duplicated-if-retried, not a normal
        // failure. AutoReplayExecutor must route that to the Recovery Ledger's
        // RecoveryEntryState.ExecutionUnknown (a non-terminal state that keeps the entry live for
        // later review), never RecoveryEntryState.ExecutionFailed (which asserts nothing
        // happened and implicitly signals "safe to retry").
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();
        await SeedStandingGrantAsync(msg);

        _messageOperations
            .Setup(m => m.ReplayMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(Error.Conflict("AWS.SQS.ReplayAmbiguous",
                "Message was sent to the source queue but could not be deleted from the DLQ.")));

        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, await OpenOperationAsync(rule));

        result.IsFailure.Should().BeTrue();

        var entry = _dbContext.RecoveryLedgerEntries.Single(e => e.DlqMessageId == msg.Id);
        entry.State.Should().Be(RecoveryEntryState.ExecutionUnknown);
    }

    [Fact]
    public async Task Execute_AwsReplayOrdinaryFailure_RecordsExecutionFailed()
    {
        // Contrast case: an ordinary provider rejection (nothing sent) still records the
        // terminal ExecutionFailed state, not ExecutionUnknown.
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();
        await SeedStandingGrantAsync(msg);

        _messageOperations
            .Setup(m => m.ReplayMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(Error.ExternalService("AWS.SQS.ReplayFailed", "throttled")));

        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, await OpenOperationAsync(rule));

        result.IsFailure.Should().BeTrue();

        var entry = _dbContext.RecoveryLedgerEntries.Single(e => e.DlqMessageId == msg.Id);
        entry.State.Should().Be(RecoveryEntryState.ExecutionFailed);
    }

    [Fact]
    public async Task Execute_TargetEntityOverride_ReplaysToAlternateEntity()
    {
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction { TargetEntity = "retry-queue" };
        await SeedStandingGrantAsync(msg);

        _messageOperations
            .Setup(m => m.ReplayMessageAsync(msg.NamespaceId, "retry-queue", null, msg.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, await OpenOperationAsync(rule));

        result.IsSuccess.Should().BeTrue();
        var history = _dbContext.ReplayHistories.Single();
        history.ReplayedToEntity.Should().Be("retry-queue");
        history.ReplayStrategy.Should().Be("alternate-entity");
    }

    [Fact]
    public async Task Execute_SubscriptionMessage_ExtractsSubscriptionNameFromEntityPath()
    {
        var rule = CreateRule();
        var msg = CreateMessage(
            1,
            entityType: ServiceBusEntityType.Subscription,
            entityName: "orders-topic/subscriptions/orders-sub",
            topicName: "orders-topic");
        var action = new RuleAction();
        await SeedStandingGrantAsync(msg);

        _messageOperations
            .Setup(m => m.ReplayMessageAsync(msg.NamespaceId, "orders-topic", "orders-sub", msg.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, await OpenOperationAsync(rule));

        result.IsSuccess.Should().BeTrue();
        _messageOperations.Verify(
            m => m.ReplayMessageAsync(msg.NamespaceId, "orders-topic", "orders-sub", msg.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(CloudProviderType.Aws)]
    [InlineData(CloudProviderType.Gcp)]
    public async Task Execute_NonAzureProviderMessage_ReplaysViaMessageOperations(CloudProviderType provider)
    {
        var rule = CreateRule();
        var msg = CreateMessage(1, provider: provider);
        var action = new RuleAction();
        await SeedStandingGrantAsync(msg);

        _messageOperations
            .Setup(m => m.ReplayMessageAsync(msg.NamespaceId, "test-queue", null, msg.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, await OpenOperationAsync(rule));

        result.IsSuccess.Should().BeTrue();
        msg.Status.Should().Be(DlqMessageStatus.Replayed);
        _messageOperations.Verify(
            m => m.ReplayMessageAsync(msg.NamespaceId, "test-queue", null, msg.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── C2: concurrent replay ───────────────────────────────

    [Fact]
    public async Task Execute_MessageClaimedByAnotherWorkerBeforeThisCall_SkipsWithoutCallingProvider()
    {
        // Own shared-cache SQLite DB (not the class fixture's private ":memory:" one) so a
        // second, independent DlqDbContext can race against the message this call is about
        // to process — simulating bulk-replay or signature-replay claiming it first.
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var keeperConnection = new SqliteConnection(connectionString);
        keeperConnection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options;
        using var dbContext = new DlqDbContext(options);
        dbContext.Database.EnsureCreated();

        var rule = new AutoReplayRule
        {
            Name = "Test Rule", OwnerId = TestConstants.TestOwnerId, Enabled = true,
            ConditionsJson = "[]", ActionsJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow, MaxReplaysPerHour = 100
        };
        dbContext.AutoReplayRules.Add(rule);

        var msg = new DlqMessage
        {
            MessageId = "msg-1", SequenceNumber = 1, BodyHash = "hash-1",
            NamespaceId = Guid.NewGuid(), OwnerId = TestConstants.TestOwnerId, EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddHours(-1),
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeliveryCount = 5, MessageSize = 100,
            Status = DlqMessageStatus.Active
        };
        dbContext.DlqMessages.Add(msg);
        await dbContext.SaveChangesAsync();
        // `dbContext` now tracks `msg` with original Status = Active — mirroring
        // DlqMonitorWorker's own query, which is what hands AutoReplayExecutor its message.

        // A different worker (its own context) claims and replays the same message first.
        using (var racingContext = new DlqDbContext(options))
        {
            var racingCopy = await racingContext.DlqMessages.SingleAsync(m => m.Id == msg.Id);
            racingCopy.Status = DlqMessageStatus.Replayed;
            racingCopy.ReplayedAt = DateTimeOffset.UtcNow;
            racingCopy.ReplaySuccess = true;
            await racingContext.SaveChangesAsync();
        }

        var messageOperations = new Mock<IMessageOperationsService>();
        var racingLedger = new RecoveryLedgerService(dbContext);
        var executor = new AutoReplayExecutor(
            dbContext, messageOperations.Object, racingLedger,
            new RecoveryEligibilityGate(racingLedger, NullLogger<RecoveryEligibilityGate>.Instance), _playbookLedger.Object,
            _featureExtractor, _fingerprintBuilder, _configuration, _logger.Object);
        var action = new RuleAction();

        // Earn predicate 5's grant against the same ledger this executor reads from — this test
        // is exercising the concurrency claim, not autonomy enforcement.
        var features = (await _featureExtractor.ExtractAsync(msg)).Value;
        var fingerprint = (await _fingerprintBuilder.ComputeAsync(features)).Value;
        await racingLedger.RecordAutonomyGrantTransitionAsync(
            TestConstants.TestOwnerId, fingerprint.Hash, RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "test: earned standing", evidenceJson: null);

        var result = await executor.ExecuteAsync(msg, rule, action, _testNamespace, Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);

        messageOperations.Verify(
            m => m.ReplayMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var stored = await dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == msg.Id);
        stored.Status.Should().Be(DlqMessageStatus.Replayed); // the racing worker's write stands, untouched by this call

        var historyCount = await dbContext.ReplayHistories.CountAsync(h => h.DlqMessageId == msg.Id);
        historyCount.Should().Be(0); // this call never wrote history — it never reached the provider
    }

    // ── Recurrence-lineage safety cap (Phase A) ──────────────

    /// <summary>
    /// Seeds a prior <see cref="RecoveryLedgerEntry"/> directly against the DbContext (bypassing
    /// the executor) so its lineage-relevant fields — <see cref="RecoveryLedgerEntry.BegunAt"/>,
    /// <see cref="RecoveryLedgerEntry.MarkerApplied"/>,
    /// <see cref="RecoveryLedgerEntry.VerificationConfidence"/>,
    /// <see cref="RecoveryLedgerEntry.SignatureHashSnapshot"/> — can be set precisely, simulating
    /// a past automatic-replay attempt on this lineage.
    /// </summary>
    private RecoveryLedgerEntry SeedLineageEntry(
        string ownerId,
        Guid namespaceId,
        string entityName,
        string bodyHash,
        DateTimeOffset begunAt,
        bool markerApplied = true,
        VerificationConfidence? confidence = null,
        string? signatureHash = null)
    {
        var operation = new RecoveryOperation
        {
            OwnerId = ownerId,
            Kind = RecoveryOperationKind.Replay,
            Trigger = RecoveryTrigger.AutoRule,
            ActorIdentity = "test-rule",
            ActorKind = RecoveryActorKind.Automation,
            ScopeDescription = "test",
            ServiceVersion = "test",
            OpenedAt = begunAt,
            TargetCount = 1,
        };
        _dbContext.RecoveryOperations.Add(operation);

        var entry = new RecoveryLedgerEntry
        {
            OperationId = operation.Id,
            OwnerId = ownerId,
            NamespaceId = namespaceId,
            EntityNameSnapshot = entityName,
            BodyHash = bodyHash,
            TargetEntity = entityName,
            BegunAt = begunAt,
            State = RecoveryEntryState.Recovered,
            MarkerApplied = markerApplied,
            VerificationConfidence = confidence,
            SignatureHashSnapshot = signatureHash,
            // Distinct provider identity per seeded entry — proves the lineage match never
            // depends on these fields (roadmap: provider MessageId/SequenceNumber are never
            // recovery identity).
            SourceMessageIdSnapshot = $"provider-msg-{Guid.NewGuid():N}",
            SourceSequenceNumberSnapshot = Random.Shared.NextInt64(),
        };
        _dbContext.RecoveryLedgerEntries.Add(entry);
        _dbContext.SaveChanges();
        return entry;
    }

    [Fact]
    public async Task Execute_FewerThanThreePriorLineageMatches_NotBlocked()
    {
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();

        SeedLineageEntry(TestConstants.TestOwnerId, _testNamespace.Id, "test-queue", "hash-1", DateTimeOffset.UtcNow.AddDays(-1));
        SeedLineageEntry(TestConstants.TestOwnerId, _testNamespace.Id, "test-queue", "hash-1", DateTimeOffset.UtcNow.AddDays(-2));
        await SeedStandingGrantAsync(msg);

        _messageOperations
            .Setup(m => m.ReplayMessageAsync(msg.NamespaceId, "test-queue", null, msg.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, await OpenOperationAsync(rule));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_ThreePriorExactConfidenceLineageMatches_BlocksAndRecordsDeclinedEntry()
    {
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();

        for (var i = 0; i < 3; i++)
        {
            SeedLineageEntry(
                TestConstants.TestOwnerId, _testNamespace.Id, "test-queue", "hash-1",
                DateTimeOffset.UtcNow.AddDays(-i - 1), confidence: VerificationConfidence.Exact);
        }

        var operationId = await OpenOperationAsync(rule);
        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, operationId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AutoReplay.RecurrenceCapExceeded");

        _messageOperations.Verify(
            m => m.ReplayMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var stored = await _dbContext.DlqMessages.AsNoTracking().SingleAsync(m => m.Id == msg.Id);
        stored.Status.Should().Be(DlqMessageStatus.Active); // untouched — manual replay remains available

        var events = await _recoveryLedger.GetEventsForOperationAsync(operationId, TestConstants.TestOwnerId);
        events.Should().ContainSingle(e => e.EventType == RecoveryEventType.EligibilityDeclined
                                            && e.DetailJson!.Contains("RECURRENCE_CAP_EXCEEDED"));

        var entries = await _recoveryLedger.QueryEntriesAsync(new RecoveryEntryQuery
        {
            OwnerId = TestConstants.TestOwnerId,
            OperationId = operationId,
        });
        entries.Should().ContainSingle(e => e.State == RecoveryEntryState.Declined
                                             && e.Disposition == RecoveryDisposition.Declined);
    }

    [Fact]
    public async Task Execute_ThreePriorLineageMatchesForDifferentOwner_DoesNotBlock()
    {
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();

        for (var i = 0; i < 3; i++)
        {
            SeedLineageEntry(
                TestConstants.AltOwnerId, _testNamespace.Id, "test-queue", "hash-1",
                DateTimeOffset.UtcNow.AddDays(-i - 1), confidence: VerificationConfidence.Exact);
        }
        await SeedStandingGrantAsync(msg);

        _messageOperations
            .Setup(m => m.ReplayMessageAsync(msg.NamespaceId, "test-queue", null, msg.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, await OpenOperationAsync(rule));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_ThreePriorLineageMatchesOutsideNinetyDayWindow_DoesNotBlock()
    {
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();

        for (var i = 0; i < 3; i++)
        {
            SeedLineageEntry(
                TestConstants.TestOwnerId, _testNamespace.Id, "test-queue", "hash-1",
                DateTimeOffset.UtcNow.AddDays(-91 - i), confidence: VerificationConfidence.Exact);
        }
        await SeedStandingGrantAsync(msg);

        _messageOperations
            .Setup(m => m.ReplayMessageAsync(msg.NamespaceId, "test-queue", null, msg.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, await OpenOperationAsync(rule));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_ThreePriorLineageMatchesJustInsideNinetyDayWindow_Blocks()
    {
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();

        for (var i = 0; i < 3; i++)
        {
            SeedLineageEntry(
                TestConstants.TestOwnerId, _testNamespace.Id, "test-queue", "hash-1",
                DateTimeOffset.UtcNow.AddDays(-89 - (i * 0.1)), confidence: VerificationConfidence.Exact);
        }

        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, await OpenOperationAsync(rule));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AutoReplay.RecurrenceCapExceeded");
    }

    [Fact]
    public async Task Execute_AmbiguousBodyHashCollisionAcrossDistinctSignatures_FailsSafeAndBlocks()
    {
        // Roadmap §7.5 item 5's "200 identical test payloads" scenario: entries sharing BodyHash
        // but carrying different SignatureHashSnapshot values are independent messages that
        // happen to hash the same body, not the same lineage — but the cap still fails safe and
        // counts them, under a distinct reason code, rather than silently allowing an unbounded
        // replay loop through.
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();

        SeedLineageEntry(
            TestConstants.TestOwnerId, _testNamespace.Id, "test-queue", "hash-1",
            DateTimeOffset.UtcNow.AddDays(-1), markerApplied: false, confidence: null, signatureHash: "sig-A");
        SeedLineageEntry(
            TestConstants.TestOwnerId, _testNamespace.Id, "test-queue", "hash-1",
            DateTimeOffset.UtcNow.AddDays(-2), markerApplied: false, confidence: null, signatureHash: "sig-B");
        SeedLineageEntry(
            TestConstants.TestOwnerId, _testNamespace.Id, "test-queue", "hash-1",
            DateTimeOffset.UtcNow.AddDays(-3), markerApplied: false, confidence: null, signatureHash: "sig-B");

        var operationId = await OpenOperationAsync(rule);
        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, operationId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AutoReplay.RecurrenceCapExceeded");

        var events = await _recoveryLedger.GetEventsForOperationAsync(operationId, TestConstants.TestOwnerId);
        events.Should().ContainSingle(e => e.EventType == RecoveryEventType.EligibilityDeclined
                                            && e.DetailJson!.Contains("RECURRENCE_CAP_AMBIGUOUS_COLLISION"));
    }

    [Fact]
    public async Task Execute_ReturnedMessageWithNewProviderIdentitySameBodyHash_StillBlockedAfterCap()
    {
        // A replayed message returns to the DLQ as a brand-new DlqMessage row (new MessageId,
        // new SequenceNumber, new Id) — the exact gap roadmap §7.3 describes. Three prior entries
        // for the same lineage (matched purely on BodyHash/EntityName, never provider identity)
        // must still block a 4th attempt against this freshly-arrived row.
        var rule = CreateRule();
        // Fresh DlqMessage row (its own Id/MessageId/SequenceNumber) carrying the same BodyHash
        // as the seeded lineage below — simulating the row DlqMonitorWorker sees after a replayed
        // message returns to the DLQ under a brand-new provider identity.
        var returnedMsg = CreateMessage(1);

        for (var i = 0; i < 3; i++)
        {
            SeedLineageEntry(
                TestConstants.TestOwnerId, _testNamespace.Id, "test-queue", "hash-1",
                DateTimeOffset.UtcNow.AddDays(-i - 1), confidence: VerificationConfidence.Exact);
        }

        var action = new RuleAction();
        var result = await _executor.ExecuteAsync(returnedMsg, rule, action, _testNamespace, await OpenOperationAsync(rule));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AutoReplay.RecurrenceCapExceeded");
    }

    // ── Deterministic SignatureHash (roadmap: close the autonomous recovery loop) ────

    private async Task<string> ComputeExpectedHashAsync(DlqMessage message)
    {
        var features = (await _featureExtractor.ExtractAsync(message)).Value;
        return (await _fingerprintBuilder.ComputeAsync(features)).Value.Hash;
    }

    [Fact]
    public async Task Execute_SuccessfulReplay_LedgerEntrySignatureHashIsNonNullAndMatchesEligibilityGateInput()
    {
        // Proves the gate-hash == ledger-hash invariant: predicate 5 could only have Allowed
        // using this exact hash (the seeded grant is keyed on it), and the persisted
        // RecoveryLedgerEntry must carry that same value — never a null/second hash.
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();
        await SeedStandingGrantAsync(msg);

        _messageOperations
            .Setup(m => m.ReplayMessageAsync(msg.NamespaceId, "test-queue", null, msg.SequenceNumber, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var operationId = await OpenOperationAsync(rule);
        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, operationId);

        result.IsSuccess.Should().BeTrue();

        var expectedHash = await ComputeExpectedHashAsync(msg);
        expectedHash.Should().NotBeNullOrEmpty();

        var entries = await _recoveryLedger.QueryEntriesAsync(new RecoveryEntryQuery
        {
            OwnerId = TestConstants.TestOwnerId,
            OperationId = operationId,
        });
        entries.Should().ContainSingle(e => e.SignatureHashSnapshot == expectedHash);
    }

    [Fact]
    public async Task Execute_NoAutonomyGrant_DeclinedLedgerEntryStillCarriesTheComputedSignatureHash()
    {
        // Predicate 5 escalating (no grant yet) must not fall back to a null SignatureHash on the
        // Declined entry either — the hash is computed unconditionally, before the gate call.
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();

        var operationId = await OpenOperationAsync(rule);
        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, operationId);

        result.IsFailure.Should().BeTrue();

        var expectedHash = await ComputeExpectedHashAsync(msg);

        var entries = await _recoveryLedger.QueryEntriesAsync(new RecoveryEntryQuery
        {
            OwnerId = TestConstants.TestOwnerId,
            OperationId = operationId,
        });
        entries.Should().ContainSingle(e => e.State == RecoveryEntryState.Declined
                                             && e.SignatureHashSnapshot == expectedHash);
    }

    // ── ReplayPlan Playbook Ledger proposal (roadmap item 14, Recover side) ──

    [Fact]
    public async Task Execute_NoAutonomyGrant_ProposesReplayPlanPlaybookEntry()
    {
        // Predicate 5 escalating for AUTONOMY_GRANT_INSUFFICIENT is exactly the L2 "Recommend"
        // moment — a computed plan that hasn't earned unattended execution — so it must propose a
        // Recover-pillar ReplayPlan into the Playbook Ledger for human review.
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();
        var expectedHash = await ComputeExpectedHashAsync(msg);

        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, await OpenOperationAsync(rule));

        result.IsFailure.Should().BeTrue();

        _playbookLedger.Verify(p => p.ProposeAsync(
            It.Is<ProposePlaybookEntryRequest>(r =>
                r.PillarKind == PillarKind.Recover
                && r.ProposalKind == "ReplayPlan"
                && r.OwnerId == rule.OwnerId
                && r.SignatureHashSnapshot == expectedHash
                && r.NamespaceId == _testNamespace.Id
                && r.ProposalJson.Contains("test-queue")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Execute_NoAutonomyGrant_SubscriptionTypeMessage_ProposalUsesFullEntityPath()
    {
        // Regression: EntityName in the proposal must be message.EntityName (the full
        // "topic/subscriptions/sub" path RecoveryLedgerEntry.EntityNameSnapshot always carries via
        // BuildBeginEntryRequest), not the topic-stripped dispatch name ExecuteAsync computes for
        // ReplayMessageAsync — otherwise BacktestService's join (FindEntriesForEntitySinceAsync
        // matches on EntityNameSnapshot) would never corroborate a Subscription-type ReplayPlan.
        var rule = CreateRule();
        var msg = CreateMessage(
            entityType: ServiceBusEntityType.Subscription,
            entityName: "my-topic/subscriptions/my-sub",
            topicName: "my-topic");
        var action = new RuleAction();

        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, await OpenOperationAsync(rule));

        result.IsFailure.Should().BeTrue();

        _playbookLedger.Verify(p => p.ProposeAsync(
            It.Is<ProposePlaybookEntryRequest>(r => r.ProposalJson.Contains("my-topic/subscriptions/my-sub")
                                                     && !r.ProposalJson.Contains("\"EntityName\":\"my-topic\"")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Execute_NoAutonomyGrant_OpenReplayPlanAlreadyProposedForSameSignature_SkipsDuplicateProposal()
    {
        // Regression: DlqMonitorWorker's fairness sweep re-evaluates a still-Active message every
        // poll cycle (default 10s) — without this guard, each cycle would propose its own
        // near-duplicate ReplayPlan for the same signature until the recurrence cap intervenes.
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();
        var expectedHash = await ComputeExpectedHashAsync(msg);

        _playbookLedger
            .Setup(p => p.QueryEntriesAsync(
                rule.OwnerId, PillarKind.Recover, _testNamespace.Id, PlaybookEntryState.Proposed, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PlaybookEntry>>(new[]
            {
                new PlaybookEntry
                {
                    OwnerId = rule.OwnerId,
                    PillarKind = PillarKind.Recover,
                    ProposalKind = "ReplayPlan",
                    EvidenceRefJson = "{}",
                    ProposalJson = "{}",
                    ProposedAt = DateTimeOffset.UtcNow,
                    ProposerIdentity = "System:AutoReplayExecutor",
                    ProposerKind = PlaybookActorKind.System,
                    SignatureHashSnapshot = expectedHash,
                    NamespaceId = _testNamespace.Id,
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                    State = PlaybookEntryState.Proposed,
                },
            }));

        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, await OpenOperationAsync(rule));

        result.IsFailure.Should().BeTrue();

        _playbookLedger.Verify(
            p => p.ProposeAsync(It.IsAny<ProposePlaybookEntryRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Execute_RecurrenceCapExceeded_DoesNotProposeReplayPlanPlaybookEntry()
    {
        // The recurrence-lineage cap is a safety stop, not a plan awaiting trust — it must not be
        // proposed into the Playbook Ledger as though a human review could unblock it.
        var rule = CreateRule();
        var msg = CreateMessage(1);
        var action = new RuleAction();

        for (var i = 0; i < 3; i++)
        {
            SeedLineageEntry(
                TestConstants.TestOwnerId, _testNamespace.Id, "test-queue", "hash-1",
                DateTimeOffset.UtcNow.AddDays(-i - 1), confidence: VerificationConfidence.Exact);
        }

        var result = await _executor.ExecuteAsync(msg, rule, action, _testNamespace, await OpenOperationAsync(rule));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AutoReplay.RecurrenceCapExceeded");

        _playbookLedger.Verify(p => p.ProposeAsync(
            It.IsAny<ProposePlaybookEntryRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ComputedSignatureHash_TwoMessagesWithIdenticalFailureCharacteristics_AreEqual()
    {
        var msg1 = CreateMessage(1, entityName: "orders-queue");
        var msg2 = CreateMessage(2, entityName: "orders-queue");

        var hash1 = await ComputeExpectedHashAsync(msg1);
        var hash2 = await ComputeExpectedHashAsync(msg2);

        hash1.Should().Be(hash2);
        // ...despite carrying distinct provider-generated identity.
        msg1.MessageId.Should().NotBe(msg2.MessageId);
        msg1.SequenceNumber.Should().NotBe(msg2.SequenceNumber);
    }

    [Fact]
    public async Task ComputedSignatureHash_MessagesWithDifferentEntityName_AreDifferent()
    {
        var msg1 = CreateMessage(1, entityName: "orders-queue");
        var msg2 = CreateMessage(2, entityName: "payments-queue");

        var hash1 = await ComputeExpectedHashAsync(msg1);
        var hash2 = await ComputeExpectedHashAsync(msg2);

        hash1.Should().NotBe(hash2);
    }
}
