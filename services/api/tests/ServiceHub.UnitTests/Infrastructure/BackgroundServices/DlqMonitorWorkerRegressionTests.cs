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
/// Regression pack for <see cref="DlqMonitorWorker"/> behaviours that guard
/// users from surprise auto-replays: the rule grace period must be honoured
/// (operators get time to inspect before a replay fires), and DLQ records
/// orphaned by a deleted namespace registration must be archived instead of
/// matching rules — and failing — forever.
/// </summary>
public sealed class DlqMonitorWorkerRegressionTests
{
    private static Namespace BuildNamespace(string name = "reg-ns", EnvironmentType environment = EnvironmentType.Dev) =>
        Namespace.Create(name,
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey12=",
            environment: environment).Value;

    private static DlqMessage BuildDlqMessage(Guid namespaceId, DateTimeOffset detectedAt, string messageId = "msg-1") =>
        new()
        {
            MessageId = messageId,
            SequenceNumber = 1,
            BodyHash = "hash",
            NamespaceId = namespaceId,
            OwnerId = "owner",
            EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = detectedAt.AddMinutes(-1),
            DetectedAtUtc = detectedAt,
            Status = DlqMessageStatus.Active,
        };

    private static AutoReplayRule BuildRule() =>
        new()
        {
            Name = "grace-rule",
            OwnerId = "owner",
            Enabled = true,
            ConditionsJson = "[]",
            ActionsJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static async Task<(DlqDbContext Db, ServiceProvider Sp, Mock<IAutoReplayExecutor> Executor)> BuildHarnessAsync(
        Namespace ns, RuleAction action, DateTimeOffset detectedAt)
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var db = new DlqDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        var rule = BuildRule();
        db.AutoReplayRules.Add(rule);
        db.DlqMessages.Add(BuildDlqMessage(ns.Id, detectedAt));
        await db.SaveChangesAsync();

        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));

        var monitor = new Mock<IDlqMonitorService>();
        monitor.Setup(m => m.ScanNamespaceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(0));

        // Every active message matches the rule with the given action.
        var ruleEngine = new Mock<IRuleEngine>();
        ruleEngine.Setup(e => e.FindMatchingRules(It.IsAny<DlqMessage>(), It.IsAny<IReadOnlyList<AutoReplayRule>>()))
            .Returns((DlqMessage _, IReadOnlyList<AutoReplayRule> rules) =>
                rules.Select(r => (r, action)).ToList());

        var executor = new Mock<IAutoReplayExecutor>();
        executor.Setup(e => e.ExecuteAsync(
                It.IsAny<DlqMessage>(), It.IsAny<AutoReplayRule>(), It.IsAny<RuleAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success("replayed"));

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(repo.Object);
        services.AddSingleton(monitor.Object);
        services.AddSingleton(Mock.Of<IPlatformEventBus>());
        services.AddSingleton(ruleEngine.Object);
        services.AddSingleton(executor.Object);
        var sp = services.BuildServiceProvider();

        return (db, sp, executor);
    }

    private static async Task RunOneCycleAsync(ServiceProvider sp)
    {
        var worker = new DlqMonitorWorker(sp, new ConfigurationBuilder().AddInMemoryCollection().Build(), NullLogger<DlqMonitorWorker>.Instance);
        // 5s initial delay + ~2s for at least one scan cycle (matches existing harness style).
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(7));
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AutoReplay_WithinGracePeriod_DoesNotFire()
    {
        var ns = BuildNamespace();
        // Message just detected; rule demands a 1-hour grace window.
        var (db, sp, executor) = await BuildHarnessAsync(
            ns,
            new RuleAction { AutoReplay = true, DelaySeconds = 3600 },
            detectedAt: DateTimeOffset.UtcNow);

        await RunOneCycleAsync(sp);

        // The whole point of the grace period: nothing replays before it elapses.
        executor.Verify(e => e.ExecuteAsync(
                It.IsAny<DlqMessage>(), It.IsAny<AutoReplayRule>(), It.IsAny<RuleAction>(), It.IsAny<CancellationToken>()),
            Times.Never);

        await db.Database.CloseConnectionAsync();
        await db.DisposeAsync();
        await sp.DisposeAsync();
    }

    [Fact]
    public async Task AutoReplay_AfterGracePeriodElapsed_Fires()
    {
        var ns = BuildNamespace();
        // Detected two hours ago; a 60s grace has long elapsed.
        var (db, sp, executor) = await BuildHarnessAsync(
            ns,
            new RuleAction { AutoReplay = true, DelaySeconds = 60 },
            detectedAt: DateTimeOffset.UtcNow.AddHours(-2));

        await RunOneCycleAsync(sp);

        executor.Verify(e => e.ExecuteAsync(
                It.IsAny<DlqMessage>(), It.IsAny<AutoReplayRule>(), It.IsAny<RuleAction>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        await db.Database.CloseConnectionAsync();
        await db.DisposeAsync();
        await sp.DisposeAsync();
    }

    [Fact]
    public async Task AutoReplay_DevelopmentNamespace_Fires()
    {
        var ns = BuildNamespace(environment: EnvironmentType.Dev);
        var (db, sp, executor) = await BuildHarnessAsync(
            ns,
            new RuleAction { AutoReplay = true, DelaySeconds = 60 },
            detectedAt: DateTimeOffset.UtcNow.AddHours(-2));

        await RunOneCycleAsync(sp);

        executor.Verify(e => e.ExecuteAsync(
                It.IsAny<DlqMessage>(), It.IsAny<AutoReplayRule>(), It.IsAny<RuleAction>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        await db.Database.CloseConnectionAsync();
        await db.DisposeAsync();
        await sp.DisposeAsync();
    }

    [Fact]
    public async Task AutoReplay_TestNamespace_Fires()
    {
        var ns = BuildNamespace(environment: EnvironmentType.Uat);
        var (db, sp, executor) = await BuildHarnessAsync(
            ns,
            new RuleAction { AutoReplay = true, DelaySeconds = 60 },
            detectedAt: DateTimeOffset.UtcNow.AddHours(-2));

        await RunOneCycleAsync(sp);

        executor.Verify(e => e.ExecuteAsync(
                It.IsAny<DlqMessage>(), It.IsAny<AutoReplayRule>(), It.IsAny<RuleAction>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        await db.Database.CloseConnectionAsync();
        await db.DisposeAsync();
        await sp.DisposeAsync();
    }

    [Fact]
    public async Task AutoReplay_ProductionNamespace_DoesNotFire()
    {
        var ns = BuildNamespace(environment: EnvironmentType.Prod);
        // Grace period elapsed and rule matches — the only thing standing between
        // this message and a replay must be the production guard.
        var (db, sp, executor) = await BuildHarnessAsync(
            ns,
            new RuleAction { AutoReplay = true, DelaySeconds = 60 },
            detectedAt: DateTimeOffset.UtcNow.AddHours(-2));

        await RunOneCycleAsync(sp);

        executor.Verify(e => e.ExecuteAsync(
                It.IsAny<DlqMessage>(), It.IsAny<AutoReplayRule>(), It.IsAny<RuleAction>(), It.IsAny<CancellationToken>()),
            Times.Never);

        await db.Database.CloseConnectionAsync();
        await db.DisposeAsync();
        await sp.DisposeAsync();
    }

    [Fact]
    public async Task OrphanedActiveRecords_ForUnregisteredNamespaces_AreArchived()
    {
        var ns = BuildNamespace();
        var orphanNamespaceId = Guid.NewGuid(); // registration was deleted

        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var db = new DlqDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        db.DlqMessages.Add(BuildDlqMessage(ns.Id, DateTimeOffset.UtcNow, "kept-msg"));
        db.DlqMessages.Add(BuildDlqMessage(orphanNamespaceId, DateTimeOffset.UtcNow, "orphan-msg"));
        await db.SaveChangesAsync();

        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));

        var monitor = new Mock<IDlqMonitorService>();
        monitor.Setup(m => m.ScanNamespaceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(0));

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(repo.Object);
        services.AddSingleton(monitor.Object);
        services.AddSingleton(Mock.Of<IPlatformEventBus>());
        var sp = services.BuildServiceProvider();

        await RunOneCycleAsync(sp);

        var kept = await db.DlqMessages.SingleAsync(m => m.MessageId == "kept-msg");
        var orphan = await db.DlqMessages.SingleAsync(m => m.MessageId == "orphan-msg");

        // A record without a registration can never be replayed — leaving it Active
        // made "Replay All" fail on every click. It must be archived out of the way.
        orphan.Status.Should().Be(DlqMessageStatus.Archived);
        orphan.ArchivedAt.Should().NotBeNull();
        kept.Status.Should().Be(DlqMessageStatus.Active);

        await db.Database.CloseConnectionAsync();
        await db.DisposeAsync();
        await sp.DisposeAsync();
    }
}
