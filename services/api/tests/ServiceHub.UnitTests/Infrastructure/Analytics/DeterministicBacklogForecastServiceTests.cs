using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Analytics;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class DeterministicBacklogForecastServiceTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly DeterministicBacklogForecastService _sut;
    private readonly Guid _namespaceId = Guid.NewGuid();
    private const string OwnerId = "key_testowner";
    private long _nextSequenceNumber;

    public DeterministicBacklogForecastServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _sut = new DeterministicBacklogForecastService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    // Every seeded message defaults to DlqMessageStatus.Active and nothing in these tests
    // resolves/replays it, so the total seeded across all buckets doubles as the entity's
    // current active backlog count — matching how a real, never-drained queue accumulates.
    private void SeedMessages(string entityName, DateTimeOffset timestamp, int count)
    {
        for (var i = 0; i < count; i++)
        {
            _dbContext.DlqMessages.Add(new DlqMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                SequenceNumber = _nextSequenceNumber++,
                BodyHash = Guid.NewGuid().ToString("N"),
                NamespaceId = _namespaceId,
                OwnerId = OwnerId,
                EntityName = entityName,
                EntityType = ServiceBusEntityType.Queue,
                EnqueuedTimeUtc = timestamp,
                DetectedAtUtc = timestamp,
            });
        }

        _dbContext.SaveChanges();
    }

    // Seeds 4 equal-length trailing buckets ending at `end` with the given per-bucket counts,
    // in trend order (oldest first, current window last).
    private void SeedTrendBuckets(string entityName, DateTimeOffset end, TimeSpan bucketLength, params int[] bucketCounts)
    {
        var trendStart = end - TimeSpan.FromTicks(bucketLength.Ticks * bucketCounts.Length);
        for (var i = 0; i < bucketCounts.Length; i++)
        {
            var bucketStart = trendStart + TimeSpan.FromTicks(bucketLength.Ticks * i) + TimeSpan.FromMinutes(1);
            SeedMessages(entityName, bucketStart, bucketCounts[i]);
        }
    }

    [Fact]
    public async Task ForecastAsync_EndTimeNotAfterStartTime_ReturnsValidationFailure()
    {
        var now = DateTimeOffset.UtcNow;

        var result = await _sut.ForecastAsync(_namespaceId, now, now);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ForecastAsync_NonPositiveThreshold_ReturnsValidationFailure()
    {
        var now = DateTimeOffset.UtcNow;

        var result = await _sut.ForecastAsync(_namespaceId, now.AddHours(-1), now, alertThreshold: 0);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ForecastAsync_WindowTooLargeToComputeTrend_ReturnsValidationFailureInsteadOfThrowing()
    {
        var start = DateTimeOffset.MinValue.AddYears(2);
        var end = DateTimeOffset.MaxValue.AddYears(-2);

        var act = async () => await _sut.ForecastAsync(_namespaceId, start, end);

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ForecastAsync_NoMessages_ReturnsEmptyList()
    {
        var now = DateTimeOffset.UtcNow;

        var result = await _sut.ForecastAsync(_namespaceId, now.AddHours(-1), now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ForecastAsync_GrowingBacklog_ProjectsBreachWithinHorizon()
    {
        var now = DateTimeOffset.UtcNow;
        var bucketLength = TimeSpan.FromHours(1);

        // Steadily increasing arrivals of +10/bucket -> a fitted growth rate of 10/hour, and a
        // current (cumulative, all still Active) backlog of 100.
        SeedTrendBuckets("orders-queue", now, bucketLength, 10, 20, 30, 40);

        var result = await _sut.ForecastAsync(_namespaceId, now - bucketLength, now, alertThreshold: 150);

        result.IsSuccess.Should().BeTrue();
        var forecast = result.Value.Should().ContainSingle(f => f.EntityName == "orders-queue").Subject;
        forecast.CurrentBacklogCount.Should().Be(100);
        forecast.GrowthRatePerHour.Should().BeApproximately(10, 0.01);
        forecast.AlertThreshold.Should().Be(150);
        forecast.ProjectedHoursToBreach.Should().BeApproximately(5, 0.1);
        forecast.ProjectedBreachAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(5), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ForecastAsync_FlatBacklog_NoForecast()
    {
        var now = DateTimeOffset.UtcNow;
        var bucketLength = TimeSpan.FromHours(1);

        SeedTrendBuckets("steady-queue", now, bucketLength, 25, 25, 25, 25);

        var result = await _sut.ForecastAsync(_namespaceId, now - bucketLength, now, alertThreshold: 150);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(f => f.EntityName == "steady-queue");
    }

    [Fact]
    public async Task ForecastAsync_AlreadyAtOrAboveThreshold_NoForecast()
    {
        var now = DateTimeOffset.UtcNow;
        var bucketLength = TimeSpan.FromHours(1);

        SeedTrendBuckets("hot-queue", now, bucketLength, 10, 20, 30, 40);

        // Current backlog (100) already meets/exceeds this threshold -> not a forward-looking
        // projection anymore, so it's out of scope for this signal.
        var result = await _sut.ForecastAsync(_namespaceId, now - bucketLength, now, alertThreshold: 50);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(f => f.EntityName == "hot-queue");
    }

    [Fact]
    public async Task ForecastAsync_BelowMinimumSignalCount_NoForecast()
    {
        var now = DateTimeOffset.UtcNow;
        var bucketLength = TimeSpan.FromHours(1);

        // Cumulative active backlog of 4 total -- below the noise floor even though it's growing.
        SeedTrendBuckets("quiet-queue", now, bucketLength, 1, 1, 1, 1);

        var result = await _sut.ForecastAsync(_namespaceId, now - bucketLength, now, alertThreshold: 150);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(f => f.EntityName == "quiet-queue");
    }

    [Fact]
    public async Task ForecastAsync_ProjectedBreachBeyondHorizon_NoForecast()
    {
        var now = DateTimeOffset.UtcNow;
        var bucketLength = TimeSpan.FromHours(1);

        // Slow growth (fitted slope 0.3/hour) against a threshold far enough away that the
        // projected breach falls outside the 168-hour forecast horizon.
        SeedTrendBuckets("slow-queue", now, bucketLength, 20, 20, 20, 21);

        var result = await _sut.ForecastAsync(_namespaceId, now - bucketLength, now, alertThreshold: 200);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(f => f.EntityName == "slow-queue");
    }

    [Fact]
    public async Task ForecastAsync_ShrinkingBacklog_NoForecast()
    {
        var now = DateTimeOffset.UtcNow;
        var bucketLength = TimeSpan.FromHours(1);

        SeedTrendBuckets("draining-queue", now, bucketLength, 40, 30, 20, 10);

        var result = await _sut.ForecastAsync(_namespaceId, now - bucketLength, now, alertThreshold: 150);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(f => f.EntityName == "draining-queue");
    }

    [Fact]
    public async Task ForecastAsync_DefaultThreshold_UsedWhenNotSupplied()
    {
        var now = DateTimeOffset.UtcNow;
        var bucketLength = TimeSpan.FromHours(1);

        SeedTrendBuckets("default-threshold-queue", now, bucketLength, 5, 10, 15, 20);

        var result = await _sut.ForecastAsync(_namespaceId, now - bucketLength, now);

        result.IsSuccess.Should().BeTrue();
        var forecast = result.Value.Should().ContainSingle(f => f.EntityName == "default-threshold-queue").Subject;
        forecast.AlertThreshold.Should().Be(DeterministicBacklogForecastService.DefaultAlertThreshold);
    }
}
