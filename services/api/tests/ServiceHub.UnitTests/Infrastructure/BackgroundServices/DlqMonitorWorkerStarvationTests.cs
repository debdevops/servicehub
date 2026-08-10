using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

/// <summary>
/// Regression pack for the auto-replay starvation defect: the bounded rule-evaluation batch
/// used to be a fixed oldest-N prefix. A message that matches no rule keeps its Active status,
/// so once the oldest <c>MaxRuleEvaluationBatch</c> messages all failed to match, every cycle
/// re-read the same rows and no message behind them was ever evaluated again — auto-replay was
/// silently dead for the rest of the DLQ.
///
/// These tests drive <see cref="DlqMonitorWorker.EvaluateAutoReplayRulesAsync"/> directly, one
/// cycle per call, so they assert the user-visible guarantee (the later message does get
/// replayed) with no timers, sleeps or scheduler dependence.
/// </summary>
public sealed class DlqMonitorWorkerStarvationTests : IAsyncLifetime
{
    private DlqDbContext _db = null!;
    private readonly DateTimeOffset _baseTime = DateTimeOffset.UtcNow.AddHours(-2);

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _db = new DlqDbContext(options);
        await _db.Database.OpenConnectionAsync();
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    // ── Harness ─────────────────────────────────────────────────────────────

    private static Namespace BuildNamespace(string name = "starve-ns") =>
        Namespace.Create(name,
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey12=",
            environment: EnvironmentType.Dev).Value;

    private async Task<AutoReplayRule> AddRuleAsync()
    {
        var rule = new AutoReplayRule
        {
            Name = "starvation-rule",
            OwnerId = "owner",
            Enabled = true,
            ConditionsJson = "[]",
            ActionsJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            MaxReplaysPerHour = 1000,
        };
        _db.AutoReplayRules.Add(rule);
        await _db.SaveChangesAsync();
        return rule;
    }

    /// <summary>
    /// Adds <paramref name="count"/> Active DLQ rows with strictly increasing DetectedAtUtc,
    /// so ordering by (DetectedAtUtc, Id) is unambiguous.
    /// </summary>
    private async Task<IReadOnlyList<string>> AddActiveMessagesAsync(
        Guid namespaceId, string prefix, int count, int startIndex = 0)
    {
        var ids = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var messageId = $"{prefix}-{startIndex + i:D4}";
            ids.Add(messageId);
            _db.DlqMessages.Add(new DlqMessage
            {
                MessageId = messageId,
                SequenceNumber = startIndex + i,
                BodyHash = $"hash-{messageId}",
                NamespaceId = namespaceId,
                OwnerId = "owner",
                EntityName = "orders",
                EntityType = ServiceBusEntityType.Queue,
                EnqueuedTimeUtc = _baseTime,
                DetectedAtUtc = _baseTime.AddSeconds(startIndex + i),
                Status = DlqMessageStatus.Active,
            });
        }

        await _db.SaveChangesAsync();
        return ids;
    }

    /// <summary>
    /// Builds a worker whose rule engine matches only the given message IDs and whose executor
    /// applies the same status transition the real <c>AutoReplayExecutor</c> applies (Active →
    /// Replayed), so "a replayed message leaves the Active set" holds exactly as in production.
    /// </summary>
    private (DlqMonitorWorker Worker, ServiceProvider Sp, Mock<IAutoReplayExecutor> Executor, List<string> Evaluated)
        BuildWorker(HashSet<string> matchingMessageIds, int batchSize, int delaySeconds = 0)
    {
        var evaluated = new List<string>();

        var action = new RuleAction { AutoReplay = true, DelaySeconds = delaySeconds };

        var ruleEngine = new Mock<IRuleEngine>();
        ruleEngine.Setup(e => e.FindMatchingRules(It.IsAny<DlqMessage>(), It.IsAny<IReadOnlyList<AutoReplayRule>>()))
            .Returns((DlqMessage msg, IReadOnlyList<AutoReplayRule> rules) =>
            {
                evaluated.Add(msg.MessageId);
                return matchingMessageIds.Contains(msg.MessageId)
                    ? rules.Select(r => (r, action)).ToList()
                    : new List<(AutoReplayRule, RuleAction)>();
            });

        var executor = new Mock<IAutoReplayExecutor>();
        executor.Setup(e => e.ExecuteAsync(
                It.IsAny<DlqMessage>(), It.IsAny<AutoReplayRule>(), It.IsAny<RuleAction>(), It.IsAny<CancellationToken>()))
            .Returns(async (DlqMessage msg, AutoReplayRule _, RuleAction _, CancellationToken ct) =>
            {
                msg.Status = DlqMessageStatus.Replayed;
                msg.ReplayedAt = DateTimeOffset.UtcNow;
                msg.ReplaySuccess = true;
                await _db.SaveChangesAsync(ct);
                return Result<string>.Success("Success");
            });

        var services = new ServiceCollection();
        services.AddSingleton(_db);
        services.AddSingleton(ruleEngine.Object);
        services.AddSingleton(executor.Object);
        services.AddSingleton(Mock.Of<IPlatformEventBus>());
        var sp = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DlqMonitor:MaxRuleEvaluationBatch"] = batchSize.ToString(),
            })
            .Build();

        var worker = new DlqMonitorWorker(sp, config, NullLogger<DlqMonitorWorker>.Instance);
        return (worker, sp, executor, evaluated);
    }

    // ── TEST 1 — a matching message beyond the first batch is eventually evaluated ──

    [Fact]
    public async Task MatchingMessageBeyondFirstBatch_IsEventuallyEvaluated()
    {
        var ns = BuildNamespace();
        await AddRuleAsync();
        var ids = await AddActiveMessagesAsync(ns.Id, "msg", count: 6);
        var matching = ids[5]; // strictly behind the first batch of 3

        var (worker, sp, executor, evaluated) = BuildWorker([matching], batchSize: 3);

        // Cycle 1 evaluates the first (entirely non-matching) page only.
        await worker.EvaluateAutoReplayRulesAsync(sp, ns, CancellationToken.None);
        evaluated.Should().Equal(ids[0], ids[1], ids[2]);
        executor.Invocations.Should().BeEmpty();

        // Cycle 2 must move past that page — this is the whole defect.
        await worker.EvaluateAutoReplayRulesAsync(sp, ns, CancellationToken.None);
        evaluated.Should().Contain(matching,
            "a non-matching first page must not hide later messages from rule evaluation");

        var replayed = await _db.DlqMessages.AsNoTracking().SingleAsync(m => m.MessageId == matching);
        replayed.Status.Should().Be(DlqMessageStatus.Replayed);

        await sp.DisposeAsync();
    }

    // ── TEST 2 — the same non-matching prefix never permanently blocks later messages ──

    [Fact]
    public async Task NonMatchingPrefix_DoesNotPermanentlyBlockLaterMessages()
    {
        var ns = BuildNamespace();
        await AddRuleAsync();
        var ids = await AddActiveMessagesAsync(ns.Id, "msg", count: 10);

        // Nothing matches: every row stays Active, which is precisely the condition that
        // froze the old fixed-prefix window.
        var (worker, sp, _, evaluated) = BuildWorker([], batchSize: 4);

        // ceil(10/4) = 3 cycles to sweep the backlog once.
        for (var cycle = 0; cycle < 3; cycle++)
            await worker.EvaluateAutoReplayRulesAsync(sp, ns, CancellationToken.None);

        evaluated.Should().BeEquivalentTo(ids,
            "every Active message must be evaluated within one full sweep");

        await sp.DisposeAsync();
    }

    // ── TEST 3 — matching messages replay exactly once ──

    [Fact]
    public async Task MatchingMessage_IsReplayedExactlyOnce_AcrossManyCycles()
    {
        var ns = BuildNamespace();
        await AddRuleAsync();
        var ids = await AddActiveMessagesAsync(ns.Id, "msg", count: 6);
        var matching = ids[4];

        var (worker, sp, executor, _) = BuildWorker([matching], batchSize: 3);

        // Ten cycles: several full sweeps plus wrap-arounds.
        for (var cycle = 0; cycle < 10; cycle++)
            await worker.EvaluateAutoReplayRulesAsync(sp, ns, CancellationToken.None);

        executor.Verify(e => e.ExecuteAsync(
                It.Is<DlqMessage>(m => m.MessageId == matching),
                It.IsAny<AutoReplayRule>(), It.IsAny<RuleAction>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "a replayed message leaves the Active set and must never be replayed again by a later sweep");

        executor.Invocations.Should().HaveCount(1);

        await sp.DisposeAsync();
    }

    // ── TEST 4 — non-matching messages are left strictly alone ──

    [Fact]
    public async Task NonMatchingMessages_RemainActive()
    {
        var ns = BuildNamespace();
        await AddRuleAsync();
        var ids = await AddActiveMessagesAsync(ns.Id, "msg", count: 8);
        var matching = ids[6];

        var (worker, sp, _, _) = BuildWorker([matching], batchSize: 3);

        for (var cycle = 0; cycle < 6; cycle++)
            await worker.EvaluateAutoReplayRulesAsync(sp, ns, CancellationToken.None);

        var rows = await _db.DlqMessages.AsNoTracking().ToListAsync();
        rows.Where(m => m.MessageId != matching)
            .Should().OnlyContain(m => m.Status == DlqMessageStatus.Active,
                "rule evaluation must never mutate a message merely to advance the window");
        rows.Single(m => m.MessageId == matching).Status.Should().Be(DlqMessageStatus.Replayed);

        await sp.DisposeAsync();
    }

    // ── TEST 5 — namespaces sweep independently ──

    [Fact]
    public async Task MultipleNamespaces_DoNotInterfereWithEachOther()
    {
        var nsA = BuildNamespace("ns-a");
        var nsB = BuildNamespace("ns-b");
        await AddRuleAsync();

        var idsA = await AddActiveMessagesAsync(nsA.Id, "a", count: 6);
        var idsB = await AddActiveMessagesAsync(nsB.Id, "b", count: 6);
        var matchA = idsA[5];
        var matchB = idsB[5];

        var (worker, sp, executor, _) = BuildWorker([matchA, matchB], batchSize: 3);

        // Interleaved, exactly as the parallel per-namespace scan tasks run them.
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await worker.EvaluateAutoReplayRulesAsync(sp, nsA, CancellationToken.None);
            await worker.EvaluateAutoReplayRulesAsync(sp, nsB, CancellationToken.None);
        }

        var rows = await _db.DlqMessages.AsNoTracking().ToListAsync();
        rows.Single(m => m.MessageId == matchA).Status.Should().Be(DlqMessageStatus.Replayed,
            "namespace A's cursor must not be advanced by namespace B's sweep");
        rows.Single(m => m.MessageId == matchB).Status.Should().Be(DlqMessageStatus.Replayed);
        executor.Invocations.Should().HaveCount(2);

        // No cross-namespace leakage: A's sweep must never evaluate B's rows.
        var (workerC, spC, _, evaluatedC) = BuildWorker([], batchSize: 100);
        await workerC.EvaluateAutoReplayRulesAsync(spC, nsA, CancellationToken.None);
        evaluatedC.Should().OnlyContain(id => id.StartsWith("a-", StringComparison.Ordinal));

        await sp.DisposeAsync();
        await spC.DisposeAsync();
    }

    // ── TEST 6 — restart is safe: no duplicate replay, no lost message, no stuck cursor ──

    [Fact]
    public async Task Restart_CausesNoDuplicateReplay_NoLostMessages_NoPermanentStarvation()
    {
        var ns = BuildNamespace();
        await AddRuleAsync();
        var ids = await AddActiveMessagesAsync(ns.Id, "msg", count: 9);
        var earlyMatch = ids[1];
        var lateMatch = ids[8];

        var (worker1, sp1, executor1, _) = BuildWorker([earlyMatch, lateMatch], batchSize: 3);

        // Two cycles before the "crash": the early match replays, the late one has not been reached.
        await worker1.EvaluateAutoReplayRulesAsync(sp1, ns, CancellationToken.None);
        await worker1.EvaluateAutoReplayRulesAsync(sp1, ns, CancellationToken.None);
        executor1.Invocations.Should().HaveCount(1);

        // Restart: a brand-new worker instance has no cursor at all (in-memory state is lost).
        var (worker2, sp2, executor2, evaluated2) = BuildWorker([earlyMatch, lateMatch], batchSize: 3);

        // Sweeping the remaining 8 Active rows takes ceil(8/3) = 3 cycles.
        for (var cycle = 0; cycle < 3; cycle++)
            await worker2.EvaluateAutoReplayRulesAsync(sp2, ns, CancellationToken.None);

        evaluated2.Should().NotContain(earlyMatch,
            "an already-replayed message is no longer Active and must not re-enter evaluation");
        executor2.Verify(e => e.ExecuteAsync(
                It.Is<DlqMessage>(m => m.MessageId == earlyMatch),
                It.IsAny<AutoReplayRule>(), It.IsAny<RuleAction>(), It.IsAny<CancellationToken>()),
            Times.Never, "restart must not cause a duplicate replay");

        var rows = await _db.DlqMessages.AsNoTracking().ToListAsync();
        rows.Single(m => m.MessageId == lateMatch).Status.Should().Be(DlqMessageStatus.Replayed,
            "a restart must rewind the sweep, never permanently skip the tail");
        rows.Count(m => m.Status == DlqMessageStatus.Replayed).Should().Be(2);

        await sp1.DisposeAsync();
        await sp2.DisposeAsync();
    }

    // ── TEST 7 — continuously arriving messages cannot starve older ones ──

    [Fact]
    public async Task ContinuouslyArrivingMessages_DoNotStarveOlderMessages()
    {
        var ns = BuildNamespace();
        await AddRuleAsync();
        var ids = await AddActiveMessagesAsync(ns.Id, "msg", count: 6);
        var oldMatch = ids[5]; // oldest region, but behind the first page

        var (worker, sp, _, _) = BuildWorker([oldMatch], batchSize: 2);

        // A fresh, larger burst of newer (non-matching) messages lands before every cycle.
        var next = 100;
        for (var cycle = 0; cycle < 3; cycle++)
        {
            await AddActiveMessagesAsync(ns.Id, "new", count: 5, startIndex: next);
            next += 100;
            await worker.EvaluateAutoReplayRulesAsync(sp, ns, CancellationToken.None);
        }

        var row = await _db.DlqMessages.AsNoTracking().SingleAsync(m => m.MessageId == oldMatch);
        row.Status.Should().Be(DlqMessageStatus.Replayed,
            "newer arrivals sort behind the cursor and must never displace older unevaluated rows");

        await sp.DisposeAsync();
    }

    // ── TEST 8 — the sweep wraps: a row that starts matching later is still picked up ──

    [Fact]
    public async Task CompletedSweep_WrapsAround_AndReEvaluatesRemainingActiveMessages()
    {
        var ns = BuildNamespace();
        await AddRuleAsync();
        var ids = await AddActiveMessagesAsync(ns.Id, "msg", count: 5);

        // First pass: nothing matches (e.g. the operator has not enabled the matching rule yet).
        var (worker, sp, _, evaluated) = BuildWorker([], batchSize: 2);
        for (var cycle = 0; cycle < 3; cycle++)
            await worker.EvaluateAutoReplayRulesAsync(sp, ns, CancellationToken.None);
        evaluated.Should().BeEquivalentTo(ids);

        // Cursor reset at the tail — the next cycle starts again from the oldest row.
        evaluated.Clear();
        await worker.EvaluateAutoReplayRulesAsync(sp, ns, CancellationToken.None);
        evaluated.Should().Equal(new List<string> { ids[0], ids[1] },
            "a completed sweep must wrap to the oldest row so still-Active messages are re-evaluated");

        await sp.DisposeAsync();
    }

    // ── Guard rails preserved by the fix ─────────────────────────────────────

    [Fact]
    public async Task ProductionNamespace_IsNeverEvaluated_RegardlessOfCursor()
    {
        var ns = Namespace.Create("prod-ns",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey12=",
            environment: EnvironmentType.Prod).Value;
        await AddRuleAsync();
        var ids = await AddActiveMessagesAsync(ns.Id, "msg", count: 6);

        var (worker, sp, executor, evaluated) = BuildWorker([.. ids], batchSize: 2);

        for (var cycle = 0; cycle < 4; cycle++)
            await worker.EvaluateAutoReplayRulesAsync(sp, ns, CancellationToken.None);

        evaluated.Should().BeEmpty();
        executor.Invocations.Should().BeEmpty();

        await sp.DisposeAsync();
    }

    [Fact]
    public async Task GracePeriod_StillSuppressesReplay_WhileCursorAdvances()
    {
        var ns = BuildNamespace();
        await AddRuleAsync();
        var ids = await AddActiveMessagesAsync(ns.Id, "msg", count: 4);

        // Every message matches, but a 1-hour grace period has not elapsed for any of them.
        var (worker, sp, executor, evaluated) = BuildWorker([.. ids], batchSize: 2, delaySeconds: 86_400);

        await worker.EvaluateAutoReplayRulesAsync(sp, ns, CancellationToken.None);
        await worker.EvaluateAutoReplayRulesAsync(sp, ns, CancellationToken.None);

        executor.Invocations.Should().BeEmpty("the grace period must still suppress the replay");
        evaluated.Should().BeEquivalentTo(ids, "but the sweep must still advance past them");

        var rows = await _db.DlqMessages.AsNoTracking().ToListAsync();
        rows.Should().OnlyContain(m => m.Status == DlqMessageStatus.Active);

        await sp.DisposeAsync();
    }

    [Fact]
    public async Task NoEnabledRules_EvaluatesNothing()
    {
        var ns = BuildNamespace();
        var ids = await AddActiveMessagesAsync(ns.Id, "msg", count: 4);

        var (worker, sp, executor, evaluated) = BuildWorker([.. ids], batchSize: 2);

        await worker.EvaluateAutoReplayRulesAsync(sp, ns, CancellationToken.None);

        evaluated.Should().BeEmpty();
        executor.Invocations.Should().BeEmpty();

        await sp.DisposeAsync();
    }
}
