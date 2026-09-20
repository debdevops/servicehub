using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// What ServiceHub achieved, not how autonomous it is (roadmap next-chapter M4.1). Pure read-side
/// aggregation over rows <see cref="Entities.RecoveryLedgerEntry"/> and
/// <see cref="Entities.RecoveryEvent"/> already hold — no new schema, no new trust computation,
/// no modelled or estimated figure. Every number here is a count, an average, or a duration
/// derived directly from ledger rows an owner already has. Where <see cref="IAutonomyDashboardService"/>
/// answers "how much unattended trust has the fleet earned?", this answers "what did that trust
/// actually deliver this week?" — the artifact a platform lead takes into a budget conversation.
/// </summary>
public interface IOutcomeMetricsService
{
    /// <summary>
    /// Builds an outcome summary for <paramref name="ownerId"/> over the trailing
    /// <paramref name="window"/> (default 7 days), ending now.
    /// </summary>
    /// <param name="ownerId">Tenant/owner identifier for isolation.</param>
    /// <param name="window">The trailing window to summarise. Defaults to 7 days.</param>
    /// <param name="provider">When set (a cloud-specific Home), scopes every figure to ledger
    /// rows whose <see cref="Entities.RecoveryLedgerEntry.ProviderSnapshot"/> matches — already
    /// denormalised onto the row, so this needs no join or schema change. Null preserves the
    /// original fleet-wide behaviour.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OutcomeMetricsOverview> GetOverviewAsync(
        string ownerId,
        TimeSpan? window = null,
        CloudProviderType? provider = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What ServiceHub recovered, over a trailing window, traced entirely to ledger rows. Every
/// field must resolve to a specific query over <see cref="Entities.RecoveryLedgerEntry"/> or
/// <see cref="Entities.RecoveryEvent"/> — never an estimate, never an extrapolation.
/// </summary>
/// <param name="GeneratedAt">When this overview was computed.</param>
/// <param name="WindowStartUtc">Start of the trailing window this overview summarises.</param>
/// <param name="WindowEndUtc">End of the trailing window this overview summarises.</param>
/// <param name="MessagesRecovered">Entries that reached <see cref="Enums.RecoveryEntryState.Recovered"/>
/// — closed, no recurrence observed with adequate coverage — inside the window.</param>
/// <param name="MessagesAbandoned">Entries an operator declared unrecoverable
/// (<see cref="Enums.RecoveryEntryState.WrittenOff"/>) inside the window.</param>
/// <param name="MedianSecondsToVerifiedRecovery">Median seconds from <c>BegunAt</c> to
/// <c>ClosedAt</c> for entries that reached <see cref="Enums.RecoveryEntryState.Recovered"/> in
/// the window. Null if none closed.</param>
/// <param name="AutonomousRecoveries">Recovered entries whose operation's actor kind was
/// <c>Automation</c> or <c>System</c> — a recovery that reached a verified-safe outcome with no
/// human approval step in its path.</param>
/// <param name="GateRefusals">Count of <see cref="Enums.RecoveryEventType.EligibilityDeclined"/>
/// events in the window — attempts the eligibility gate refused before any provider was ever
/// contacted.</param>
public sealed record OutcomeMetricsOverview(
    DateTimeOffset GeneratedAt,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int MessagesRecovered,
    int MessagesAbandoned,
    double? MedianSecondsToVerifiedRecovery,
    int AutonomousRecoveries,
    int GateRefusals);
