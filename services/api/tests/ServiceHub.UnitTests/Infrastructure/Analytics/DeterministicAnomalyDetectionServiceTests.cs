using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Analytics;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class DeterministicAnomalyDetectionServiceTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly DeterministicAnomalyDetectionService _sut;
    private readonly Guid _namespaceId = Guid.NewGuid();
    private const string OwnerId = "key_testowner";
    private long _nextSequenceNumber;

    public DeterministicAnomalyDetectionServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _sut = new DeterministicAnomalyDetectionService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

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

    [Fact]
    public async Task DetectAnomaliesAsync_EndTimeNotAfterStartTime_ReturnsValidationFailure()
    {
        var now = DateTimeOffset.UtcNow;

        var result = await _sut.DetectAnomaliesAsync(_namespaceId, now, now);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task DetectAnomaliesAsync_WindowTooLargeToComputeBaseline_ReturnsValidationFailureInsteadOfThrowing()
    {
        // startTime near DateTimeOffset.MinValue with a window large enough that subtracting
        // BaselinePeriods (4) worth of ticks would overflow/underflow — must fail cleanly, not
        // throw an unhandled exception (this also protects CorrelationFindingsController, which
        // calls this method once per namespace with caller-supplied start/end times).
        var start = DateTimeOffset.MinValue.AddYears(2);
        var end = DateTimeOffset.MaxValue.AddYears(-2);

        var act = async () => await _sut.DetectAnomaliesAsync(_namespaceId, start, end);

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task DetectAnomaliesAsync_VolumeSpike_FlagsHighMessageVolume()
    {
        var now = DateTimeOffset.UtcNow;
        var windowLength = TimeSpan.FromDays(1);
        var start = now - windowLength;

        // 4 trailing baseline periods with a steady, well-above-noise-floor count.
        for (var i = 1; i <= 4; i++)
        {
            var periodStart = start - TimeSpan.FromTicks(windowLength.Ticks * i) + TimeSpan.FromHours(1);
            SeedMessages("orders-queue", periodStart, 10);
        }

        // Current window: a large spike.
        SeedMessages("orders-queue", start + TimeSpan.FromHours(1), 100);

        var result = await _sut.DetectAnomaliesAsync(_namespaceId, start, now);

        result.IsSuccess.Should().BeTrue();
        var anomaly = result.Value.Should().ContainSingle(a => a.EntityName == "orders-queue").Subject;
        anomaly.Type.Should().Be(AnomalyType.HighMessageVolume);
        anomaly.Metrics["currentCount"].Should().Be(100);
        anomaly.Metrics["baselineMean"].Should().Be(10);
    }

    [Fact]
    public async Task DetectAnomaliesAsync_VolumeDrop_FlagsLowMessageVolume()
    {
        var now = DateTimeOffset.UtcNow;
        var windowLength = TimeSpan.FromDays(1);
        var start = now - windowLength;

        for (var i = 1; i <= 4; i++)
        {
            var periodStart = start - TimeSpan.FromTicks(windowLength.Ticks * i) + TimeSpan.FromHours(1);
            SeedMessages("payments-queue", periodStart, 50);
        }

        // Current window: near-total silence versus a steady baseline of 50/period.
        SeedMessages("payments-queue", start + TimeSpan.FromHours(1), 1);

        var result = await _sut.DetectAnomaliesAsync(_namespaceId, start, now);

        result.IsSuccess.Should().BeTrue();
        var anomaly = result.Value.Should().ContainSingle(a => a.EntityName == "payments-queue").Subject;
        anomaly.Type.Should().Be(AnomalyType.LowMessageVolume);
    }

    [Fact]
    public async Task DetectAnomaliesAsync_SteadyVolume_NoAnomaly()
    {
        var now = DateTimeOffset.UtcNow;
        var windowLength = TimeSpan.FromDays(1);
        var start = now - windowLength;

        for (var i = 1; i <= 4; i++)
        {
            var periodStart = start - TimeSpan.FromTicks(windowLength.Ticks * i) + TimeSpan.FromHours(1);
            SeedMessages("steady-queue", periodStart, 20);
        }

        SeedMessages("steady-queue", start + TimeSpan.FromHours(1), 21);

        var result = await _sut.DetectAnomaliesAsync(_namespaceId, start, now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(a => a.EntityName == "steady-queue");
    }

    [Fact]
    public async Task DetectAnomaliesAsync_BelowNoiseFloor_IsIgnoredEvenIfProportionallyLarge()
    {
        var now = DateTimeOffset.UtcNow;
        var windowLength = TimeSpan.FromDays(1);
        var start = now - windowLength;

        for (var i = 1; i <= 4; i++)
        {
            var periodStart = start - TimeSpan.FromTicks(windowLength.Ticks * i) + TimeSpan.FromHours(1);
            SeedMessages("quiet-queue", periodStart, 1);
        }

        // 1 -> 3 is a 3x jump but both sides are below the minimum signal count.
        SeedMessages("quiet-queue", start + TimeSpan.FromHours(1), 3);

        var result = await _sut.DetectAnomaliesAsync(_namespaceId, start, now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(a => a.EntityName == "quiet-queue");
    }

    [Fact]
    public async Task DetectAnomaliesAsync_NoMessages_ReturnsEmptyList()
    {
        var now = DateTimeOffset.UtcNow;

        var result = await _sut.DetectAnomaliesAsync(_namespaceId, now.AddDays(-1), now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
