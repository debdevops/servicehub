using FluentAssertions;
using ServiceHub.Shared.Helpers;

namespace ServiceHub.UnitTests.Shared.Helpers;

public sealed class SignatureTrendHeuristicTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Compute_IsNewTrue_ReturnsNew()
    {
        var trend = SignatureTrendHeuristic.Compute(
            isNew: true, occurrenceCount: 1, firstSeenAt: Now, lastSeenAt: Now, now: Now, isCurrentlyClustered: true);

        trend.Should().Be("New");
    }

    [Fact]
    public void Compute_SingleOccurrence_ReturnsNewEvenIfNotFlaggedNew()
    {
        var trend = SignatureTrendHeuristic.Compute(
            isNew: false, occurrenceCount: 1, firstSeenAt: Now.AddDays(-5), lastSeenAt: Now, now: Now, isCurrentlyClustered: true);

        trend.Should().Be("New");
    }

    [Fact]
    public void Compute_RecentRecurrenceWithinFifthOfLifetime_ReturnsEscalating()
    {
        // 10-day-old signature, last seen 1 day ago (within the most recent 20% of its lifetime).
        var trend = SignatureTrendHeuristic.Compute(
            isNew: false, occurrenceCount: 5, firstSeenAt: Now.AddDays(-10), lastSeenAt: Now.AddDays(-1), now: Now,
            isCurrentlyClustered: true);

        trend.Should().Be("Escalating");
    }

    [Fact]
    public void Compute_RecurrenceOutsideFifthOfLifetime_ReturnsRecurring()
    {
        // 10-day-old signature, last seen 8 days ago (well outside the most recent 20%).
        var trend = SignatureTrendHeuristic.Compute(
            isNew: false, occurrenceCount: 5, firstSeenAt: Now.AddDays(-10), lastSeenAt: Now.AddDays(-8), now: Now,
            isCurrentlyClustered: true);

        trend.Should().Be("Recurring");
    }

    [Fact]
    public void Compute_TwoOccurrencesRecentRecurrence_ReturnsRecurringNotEscalating()
    {
        // Escalating requires 3+ occurrences even if timing would otherwise qualify.
        var trend = SignatureTrendHeuristic.Compute(
            isNew: false, occurrenceCount: 2, firstSeenAt: Now.AddDays(-10), lastSeenAt: Now.AddDays(-1), now: Now,
            isCurrentlyClustered: true);

        trend.Should().Be("Recurring");
    }

    [Fact]
    public void Compute_NotCurrentlyClustered_ReturnsRecurringEvenIfTimingWouldEscalate()
    {
        // Same timing as Compute_RecentRecurrenceWithinFifthOfLifetime_ReturnsEscalating, but the
        // signature is absent from the current DLQ clustering pass (e.g. just fully replayed) —
        // it cannot be "still actively firing" regardless of how recent its last re-detection was.
        var trend = SignatureTrendHeuristic.Compute(
            isNew: false, occurrenceCount: 5, firstSeenAt: Now.AddDays(-10), lastSeenAt: Now.AddDays(-1), now: Now,
            isCurrentlyClustered: false);

        trend.Should().Be("Recurring");
    }
}
