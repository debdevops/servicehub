using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.UnitTests;

namespace ServiceHub.UnitTests.Infrastructure;

public class AuditServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DlqDbContext _dbContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly Mock<ILogger<AuditService>> _logger;
    private readonly AuditService _auditService;

    public AuditServiceTests()
    {
        // 1. Keep a single SQLite connection open for the lifetime of the test class.
        // This ensures the in-memory schema and data persist across separate DbContext scope resolutions.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();

        services.AddDbContext<DlqDbContext>(options =>
            options.UseSqlite(_connection));

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<DlqDbContext>();
        _dbContext.Database.EnsureCreated();

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        _logger = new Mock<ILogger<AuditService>>();

        _auditService = new AuditService(scopeFactory, _logger.Object);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Close();
        _connection.Dispose();
        if (_serviceProvider is IDisposable disp)
        {
            disp.Dispose();
        }
    }

    private async Task SeedAuditLogsAsync()
    {
        var logs = new List<AuditLog>
        {
            new AuditLog
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10),
                OwnerId = "__spa__",
                UserIdentity = "user1@test.com",
                Action = "Messages.Replay",
                Outcome = "Success",
                NamespaceName = "ns1",
                NamespaceId = Guid.Parse("00000000-0000-0000-0000-000000000001")
            },
            new AuditLog
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
                OwnerId = "__spa__",
                UserIdentity = "user2@test.com",
                Action = "Rule.Create",
                Outcome = "Success",
                NamespaceName = "ns1",
                NamespaceId = Guid.Parse("00000000-0000-0000-0000-000000000001")
            },
            new AuditLog
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                OwnerId = "__spa__",
                UserIdentity = "user1@test.com",
                Action = "Namespace.Connect",
                Outcome = "Failure",
                ErrorDetails = "Authentication failed",
                NamespaceName = "ns2",
                NamespaceId = Guid.Parse("00000000-0000-0000-0000-000000000002")
            },
            new AuditLog
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                OwnerId = "other_owner",
                UserIdentity = "user@other.com",
                Action = "Messages.Replay",
                Outcome = "Success"
            }
        };

        _dbContext.AuditLogs.AddRange(logs);
        await _dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task GetLogsAsync_FiltersByOwnerId()
    {
        await SeedAuditLogsAsync();

        var result = await _auditService.GetLogsAsync("__spa__");

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(3);
        result.Value.TotalCount.Should().Be(3);
        result.Value.Items.Should().OnlyContain(l => l.OwnerId == "__spa__");
    }

    [Fact]
    public async Task GetLogsAsync_FiltersByNamespaceId()
    {
        await SeedAuditLogsAsync();
        var targetNs = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var result = await _auditService.GetLogsAsync("__spa__", namespaceId: targetNs);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items.Should().OnlyContain(l => l.NamespaceId == targetNs);
    }

    // ── Regression: namespace allow-list isolation (security fix) ──────────
    //
    // Before this fix, GetLogsAsync/GetSummaryAsync/ExportAsync ignored a caller's
    // AllowedNamespaceIds allow-list entirely, so a namespace-restricted API key could read
    // audit logs for every namespace its owner has, not just the allow-listed subset.

    [Fact]
    public async Task GetLogsAsync_AllowedNamespaceIds_ExcludesLogsOutsideAllowList()
    {
        await SeedAuditLogsAsync();
        var allowedNs = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var allowedNamespaceIds = new HashSet<Guid> { allowedNs };

        var result = await _auditService.GetLogsAsync("__spa__", allowedNamespaceIds: allowedNamespaceIds);

        result.IsSuccess.Should().BeTrue();
        // ns1 (2 entries) plus the one entry with no NamespaceId (instance-level action) remain;
        // ns2's entry must be excluded.
        result.Value.Items.Should().NotContain(l => l.NamespaceId == Guid.Parse("00000000-0000-0000-0000-000000000002"));
        result.Value.Items.Should().OnlyContain(l => l.NamespaceId == null || l.NamespaceId == allowedNs);
    }

    [Fact]
    public async Task GetSummaryAsync_AllowedNamespaceIds_ExcludesStatsOutsideAllowList()
    {
        await SeedAuditLogsAsync();
        var allowedNs = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var allowedNamespaceIds = new HashSet<Guid> { allowedNs };

        var result = await _auditService.GetSummaryAsync("__spa__", allowedNamespaceIds: allowedNamespaceIds);

        result.IsSuccess.Should().BeTrue();
        // Without the ns2 entry (a Failure), total events for __spa__ drops from 3 to 2.
        result.Value.TotalEvents.Should().Be(2);
        result.Value.FailureCount.Should().Be(0);
    }

    [Fact]
    public async Task ExportAsync_AllowedNamespaceIds_ExcludesEntriesOutsideAllowList()
    {
        await SeedAuditLogsAsync();
        var allowedNs = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var allowedNamespaceIds = new HashSet<Guid> { allowedNs };

        var result = await _auditService.ExportAsync("__spa__", allowedNamespaceIds: allowedNamespaceIds);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(l => l.NamespaceId == Guid.Parse("00000000-0000-0000-0000-000000000002"));
    }

    [Fact]
    public async Task GetLogsAsync_FiltersByActionType()
    {
        await SeedAuditLogsAsync();

        var result = await _auditService.GetLogsAsync("__spa__", actionType: "Rule");

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].Action.Should().Be("Rule.Create");
    }

    [Fact]
    public async Task GetLogsAsync_FiltersByOutcome()
    {
        await SeedAuditLogsAsync();

        var result = await _auditService.GetLogsAsync("__spa__", outcome: "Failure");

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].Outcome.Should().Be("Failure");
    }

    [Fact]
    public async Task GetLogsAsync_FiltersBySearchText()
    {
        await SeedAuditLogsAsync();

        var result = await _auditService.GetLogsAsync("__spa__", search: "user2@test.com");

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].UserIdentity.Should().Be("user2@test.com");
    }

    [Fact]
    public async Task GetSummaryAsync_ComputesCorrectStats()
    {
        await SeedAuditLogsAsync();

        var result = await _auditService.GetSummaryAsync("__spa__");

        result.IsSuccess.Should().BeTrue();
        var summary = result.Value;
        summary.TotalEvents.Should().Be(3);
        summary.SuccessCount.Should().Be(2);
        summary.FailureCount.Should().Be(1);
        summary.ActiveUsers.Should().Be(2);
        summary.SuccessRate.Should().Be(66.7);
    }

    [Fact]
    public async Task ExportAsync_FiltersAndReturnsList()
    {
        await SeedAuditLogsAsync();

        var result = await _auditService.ExportAsync("__spa__", outcome: "Success");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().OnlyContain(l => l.Outcome == "Success");
    }

    [Fact]
    public async Task BackgroundService_FlushesEnqueuedLogs()
    {
        // Start the background service writer
        var cts = new CancellationTokenSource();
        var runTask = _auditService.StartAsync(cts.Token);

        var log = new AuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            OwnerId = "__spa__",
            UserIdentity = "background@test.com",
            Action = "Test.Background",
            Outcome = "Success"
        };

        _auditService.Enqueue(log);

        // Give it a brief moment to process the channel message
        await Task.Delay(100);

        // Stop the background service (this triggers flushing of remaining entries)
        await _auditService.StopAsync(CancellationToken.None);
        await runTask;

        // Verify log was saved to the Db
        var savedLog = await _dbContext.AuditLogs
            .FirstOrDefaultAsync(l => l.UserIdentity == "background@test.com");

        savedLog.Should().NotBeNull();
        savedLog!.Action.Should().Be("Test.Background");
    }

    // ── Namespace snapshot at write time ─────────────────────────────────────
    //
    // AuditLog.NamespaceName is documented as "snapshotted at write time so deleted namespaces
    // still appear correctly in audit history", and both the Audit page's namespace chip and the
    // CSV export's NamespaceName/CloudProvider columns read it. No caller ever supplied it (a
    // request thread must not block on the database to log), so every row persisted with those
    // two columns null and both surfaces were permanently blank. The writer takes the snapshot.

    [Fact]
    public async Task BackgroundService_SnapshotsNamespaceNameAndProvider_FromNamespaceId()
    {
        var ns = Namespace.Create(
            "sb-servicehub-dev",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=servicehub;SharedAccessKey=dGVzdGtleQ==",
            displayName: "Azure DEV",
            ownerId: "__spa__").Value;
        _dbContext.Namespaces.Add(ns);
        await _dbContext.SaveChangesAsync();

        var cts = new CancellationTokenSource();
        var runTask = _auditService.StartAsync(cts.Token);

        _auditService.Enqueue(new AuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            OwnerId = "__spa__",
            UserIdentity = "snapshot@test.com",
            Action = "messages:replay",
            Outcome = "Success",
            NamespaceId = ns.Id,
        });

        await Task.Delay(100);
        await _auditService.StopAsync(CancellationToken.None);
        await runTask;

        var saved = await _dbContext.AuditLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.UserIdentity == "snapshot@test.com");

        saved.Should().NotBeNull();
        saved!.NamespaceName.Should().Be("Azure DEV", "the display name is what every other surface shows");
        saved.CloudProvider.Should().Be("azure");
    }

    [Fact]
    public async Task BackgroundService_LeavesNamespaceNameNull_WhenTheNamespaceNoLongerExists()
    {
        var cts = new CancellationTokenSource();
        var runTask = _auditService.StartAsync(cts.Token);

        _auditService.Enqueue(new AuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            OwnerId = "__spa__",
            UserIdentity = "orphan@test.com",
            Action = "namespaces:delete",
            Outcome = "Success",
            NamespaceId = Guid.NewGuid(),
        });

        await Task.Delay(100);
        await _auditService.StopAsync(CancellationToken.None);
        await runTask;

        var saved = await _dbContext.AuditLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.UserIdentity == "orphan@test.com");

        saved.Should().NotBeNull();
        saved!.NamespaceName.Should().BeNull("an unknown name must stay unknown, never be fabricated");
        saved.CloudProvider.Should().BeNull();
    }

    [Fact]
    public async Task BackgroundService_KeepsACallerSuppliedSnapshot_RatherThanOverwritingIt()
    {
        var ns = Namespace.Create(
            "sb-servicehub-dev",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=servicehub;SharedAccessKey=dGVzdGtleQ==",
            displayName: "Azure DEV",
            ownerId: "__spa__").Value;
        _dbContext.Namespaces.Add(ns);
        await _dbContext.SaveChangesAsync();

        var cts = new CancellationTokenSource();
        var runTask = _auditService.StartAsync(cts.Token);

        _auditService.Enqueue(new AuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            OwnerId = "__spa__",
            UserIdentity = "explicit@test.com",
            Action = "messages:replay",
            Outcome = "Success",
            NamespaceId = ns.Id,
            NamespaceName = "Name as it was when the action happened",
            CloudProvider = "azure",
        });

        await Task.Delay(100);
        await _auditService.StopAsync(CancellationToken.None);
        await runTask;

        var saved = await _dbContext.AuditLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.UserIdentity == "explicit@test.com");

        saved!.NamespaceName.Should().Be("Name as it was when the action happened");
    }

    // ── PurgeExpiredAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task PurgeExpiredAsync_DeletesOnlyEntriesOlderThanCutoff()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        _dbContext.AuditLogs.AddRange(
            new AuditLog { Id = Guid.NewGuid(), Timestamp = cutoff.AddDays(-10), OwnerId = "__spa__", UserIdentity = "old@test.com", Action = "Old.Action", Outcome = "Success" },
            new AuditLog { Id = Guid.NewGuid(), Timestamp = cutoff.AddDays(10), OwnerId = "__spa__", UserIdentity = "recent@test.com", Action = "Recent.Action", Outcome = "Success" });
        await _dbContext.SaveChangesAsync();

        var result = await _auditService.PurgeExpiredAsync(cutoff);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        var remaining = await _dbContext.AuditLogs.ToListAsync();
        remaining.Should().ContainSingle(l => l.UserIdentity == "recent@test.com");
    }

    [Fact]
    public async Task PurgeExpiredAsync_PurgesAcrossAllOwners_NotJustOne()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        _dbContext.AuditLogs.AddRange(
            new AuditLog { Id = Guid.NewGuid(), Timestamp = cutoff.AddDays(-5), OwnerId = "__spa__", UserIdentity = "a@test.com", Action = "A", Outcome = "Success" },
            new AuditLog { Id = Guid.NewGuid(), Timestamp = cutoff.AddDays(-5), OwnerId = "other_owner", UserIdentity = "b@test.com", Action = "B", Outcome = "Success" });
        await _dbContext.SaveChangesAsync();

        var result = await _auditService.PurgeExpiredAsync(cutoff);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
    }

    [Fact]
    public async Task PurgeExpiredAsync_NothingExpired_ReturnsZero()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        _dbContext.AuditLogs.Add(
            new AuditLog { Id = Guid.NewGuid(), Timestamp = DateTimeOffset.UtcNow, OwnerId = "__spa__", UserIdentity = "recent@test.com", Action = "Recent", Outcome = "Success" });
        await _dbContext.SaveChangesAsync();

        var result = await _auditService.PurgeExpiredAsync(cutoff);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }
}
