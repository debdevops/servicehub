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
}
