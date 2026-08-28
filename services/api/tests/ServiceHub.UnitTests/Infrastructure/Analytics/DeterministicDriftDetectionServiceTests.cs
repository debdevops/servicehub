using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Analytics;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class DeterministicDriftDetectionServiceTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly DeterministicDriftDetectionService _sut;
    private readonly Guid _namespaceId = Guid.NewGuid();
    private const string OwnerId = "key_testowner";
    private long _nextSequenceNumber;

    public DeterministicDriftDetectionServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _sut = new DeterministicDriftDetectionService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private void SeedFeatureRecords(
        string entityName,
        DateTimeOffset timestamp,
        int count,
        string schemaFingerprint = "fp-baseline",
        string payloadShape = "json_object",
        long bodySizeBytes = 512)
    {
        for (var i = 0; i < count; i++)
        {
            var dlqMessage = new DlqMessage
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
            };

            _dbContext.DlqMessages.Add(dlqMessage);
            _dbContext.MessageFeatureRecords.Add(new MessageFeatureRecord
            {
                DlqMessage = dlqMessage,
                NamespaceId = _namespaceId,
                OwnerId = OwnerId,
                CapturedAt = timestamp,
                BodySizeBytes = bodySizeBytes,
                Provider = CloudProviderType.Azure,
                EntityName = entityName,
                DeadletterReason = "MaxDeliveryCountExceeded",
                ExceptionType = string.Empty,
                ContentType = "application/json",
                PayloadShape = payloadShape,
                ErrorTextNormalised = string.Empty,
                SchemaFingerprint = schemaFingerprint,
                FeatureVersion = 1,
            });
        }

        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task DetectDriftAsync_EndTimeNotAfterStartTime_ReturnsValidationFailure()
    {
        var now = DateTimeOffset.UtcNow;

        var result = await _sut.DetectDriftAsync(_namespaceId, now, now);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task DetectDriftAsync_WindowTooLargeToComputeBaseline_ReturnsValidationFailureInsteadOfThrowing()
    {
        // startTime near DateTimeOffset.MinValue with a window large enough that subtracting
        // BaselinePeriods (4) worth of ticks would underflow past MinValue — must fail cleanly,
        // not throw an unhandled exception from inside the detection pipeline.
        var start = DateTimeOffset.MinValue.AddYears(2);
        var end = DateTimeOffset.MaxValue.AddYears(-2);

        var act = async () => await _sut.DetectDriftAsync(_namespaceId, start, end);

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task DetectDriftAsync_ExactTieInBaselineShape_PicksDeterministically()
    {
        var now = DateTimeOffset.UtcNow;
        var windowLength = TimeSpan.FromDays(1);
        var start = now - windowLength;

        // Baseline is an exact 50/50 split between two fingerprints — DominantShareThreshold
        // (0.5) still passes via >=, so the tie-break (lexicographic, not row order) must decide
        // which one is "the baseline" consistently across runs.
        for (var i = 1; i <= 4; i++)
        {
            var periodStart = start - TimeSpan.FromTicks(windowLength.Ticks * i) + TimeSpan.FromHours(1);
            SeedFeatureRecords("tie-queue", periodStart, 5, schemaFingerprint: "fp-a");
            SeedFeatureRecords("tie-queue", periodStart, 5, schemaFingerprint: "fp-z");
        }

        // Current window entirely "fp-z" — a full drift away from "fp-a" (the lexicographically
        // smaller, and therefore deterministically chosen, baseline).
        SeedFeatureRecords("tie-queue", start + TimeSpan.FromHours(1), 10, schemaFingerprint: "fp-z");

        var result1 = await _sut.DetectDriftAsync(_namespaceId, start, now);
        var result2 = await _sut.DetectDriftAsync(_namespaceId, start, now);

        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();

        var finding1 = result1.Value.Should().ContainSingle(f => f.EntityName == "tie-queue" && f.Type == DriftFindingType.SchemaShapeDrift).Subject;
        var finding2 = result2.Value.Should().ContainSingle(f => f.EntityName == "tie-queue" && f.Type == DriftFindingType.SchemaShapeDrift).Subject;

        finding1.Description.Should().Contain("fp-a");
        finding2.Description.Should().Contain("fp-a");
    }

    [Fact]
    public async Task DetectDriftAsync_NoRecords_ReturnsEmptyList()
    {
        var now = DateTimeOffset.UtcNow;

        var result = await _sut.DetectDriftAsync(_namespaceId, now.AddDays(-1), now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectDriftAsync_NovelSchemaFingerprintDominatesCurrentWindow_FlagsSchemaShapeDrift()
    {
        var now = DateTimeOffset.UtcNow;
        var windowLength = TimeSpan.FromDays(1);
        var start = now - windowLength;

        // 4 trailing baseline periods with a consistent shape.
        for (var i = 1; i <= 4; i++)
        {
            var periodStart = start - TimeSpan.FromTicks(windowLength.Ticks * i) + TimeSpan.FromHours(1);
            SeedFeatureRecords("orders-queue", periodStart, 5, schemaFingerprint: "fp-known");
        }

        // Current window: mostly a new, unseen shape.
        SeedFeatureRecords("orders-queue", start + TimeSpan.FromHours(1), 6, schemaFingerprint: "fp-novel");
        SeedFeatureRecords("orders-queue", start + TimeSpan.FromHours(2), 4, schemaFingerprint: "fp-known");

        var result = await _sut.DetectDriftAsync(_namespaceId, start, now);

        result.IsSuccess.Should().BeTrue();
        var finding = result.Value.Should()
            .ContainSingle(f => f.Type == DriftFindingType.SchemaShapeDrift)
            .Subject;
        finding.EntityName.Should().Be("orders-queue");
        finding.Metrics["driftCount"].Should().Be(6);
        finding.Metrics["currentTotal"].Should().Be(10);
    }

    [Fact]
    public async Task DetectDriftAsync_PayloadFormatChanges_FlagsPayloadFormatDrift()
    {
        var now = DateTimeOffset.UtcNow;
        var windowLength = TimeSpan.FromDays(1);
        var start = now - windowLength;

        for (var i = 1; i <= 4; i++)
        {
            var periodStart = start - TimeSpan.FromTicks(windowLength.Ticks * i) + TimeSpan.FromHours(1);
            SeedFeatureRecords("payments-queue", periodStart, 5, schemaFingerprint: "fp-stable", payloadShape: "json_object");
        }

        // Current window keeps the same schema fingerprint (no schema-shape drift) but the
        // payload format itself changed to plain text.
        SeedFeatureRecords("payments-queue", start + TimeSpan.FromHours(1), 8, schemaFingerprint: "fp-stable", payloadShape: "text");

        var result = await _sut.DetectDriftAsync(_namespaceId, start, now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(f => f.Type == DriftFindingType.SchemaShapeDrift);
        var finding = result.Value.Should()
            .ContainSingle(f => f.Type == DriftFindingType.PayloadFormatDrift)
            .Subject;
        finding.EntityName.Should().Be("payments-queue");
        finding.Metrics["baselinePayloadShare"].Should().Be(1.0);
        finding.Metrics["currentPayloadShare"].Should().Be(1.0);
    }

    [Fact]
    public async Task DetectDriftAsync_SteadyShape_NoFinding()
    {
        var now = DateTimeOffset.UtcNow;
        var windowLength = TimeSpan.FromDays(1);
        var start = now - windowLength;

        for (var i = 1; i <= 4; i++)
        {
            var periodStart = start - TimeSpan.FromTicks(windowLength.Ticks * i) + TimeSpan.FromHours(1);
            SeedFeatureRecords("steady-queue", periodStart, 10);
        }

        SeedFeatureRecords("steady-queue", start + TimeSpan.FromHours(1), 10);

        var result = await _sut.DetectDriftAsync(_namespaceId, start, now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(f => f.EntityName == "steady-queue");
    }

    [Fact]
    public async Task DetectDriftAsync_BelowMinimumSignalCount_IsIgnored()
    {
        var now = DateTimeOffset.UtcNow;
        var windowLength = TimeSpan.FromDays(1);
        var start = now - windowLength;

        for (var i = 1; i <= 4; i++)
        {
            var periodStart = start - TimeSpan.FromTicks(windowLength.Ticks * i) + TimeSpan.FromHours(1);
            SeedFeatureRecords("quiet-queue", periodStart, 1, schemaFingerprint: "fp-known");
        }

        // Only 2 messages in the current window — below MinimumSignalCount even though the
        // shape is entirely novel.
        SeedFeatureRecords("quiet-queue", start + TimeSpan.FromHours(1), 2, schemaFingerprint: "fp-novel");

        var result = await _sut.DetectDriftAsync(_namespaceId, start, now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(f => f.EntityName == "quiet-queue");
    }

    [Fact]
    public async Task DetectDriftAsync_HeterogeneousBaseline_NoDominantShape_NoFinding()
    {
        var now = DateTimeOffset.UtcNow;
        var windowLength = TimeSpan.FromDays(1);
        var start = now - windowLength;

        // Baseline itself has no clear majority shape (three roughly-even fingerprints), so
        // there is nothing meaningful to call "the accepted shape" and no drift is flagged.
        for (var i = 1; i <= 4; i++)
        {
            var periodStart = start - TimeSpan.FromTicks(windowLength.Ticks * i) + TimeSpan.FromHours(1);
            SeedFeatureRecords("chaotic-queue", periodStart, 2, schemaFingerprint: "fp-a");
            SeedFeatureRecords("chaotic-queue", periodStart, 2, schemaFingerprint: "fp-b");
            SeedFeatureRecords("chaotic-queue", periodStart, 2, schemaFingerprint: "fp-c");
        }

        SeedFeatureRecords("chaotic-queue", start + TimeSpan.FromHours(1), 10, schemaFingerprint: "fp-d");

        var result = await _sut.DetectDriftAsync(_namespaceId, start, now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(f => f.EntityName == "chaotic-queue" && f.Type == DriftFindingType.SchemaShapeDrift);
    }
}
