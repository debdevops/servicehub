namespace ServiceHub.Core.Models;

/// <summary>
/// Configuration options for the Investigate/Correlate/Prevent pillar findings' automatic
/// retention sweep (roadmap next-chapter M1.2, ADR-0009). Bound from the "Pillars:Retention"
/// section of appsettings.json.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="AuditRetentionOptions"/> (disabled by default — audit logs are compliance
/// records kept forever unless an operator opts in), this defaults <b>enabled</b> at a generous
/// window. The six tables this sweeps (<c>Anomaly</c>, <c>DriftFinding</c>,
/// <c>CorrelationFinding</c>, <c>Narration</c>, <c>BacklogForecast</c>,
/// <c>ExternalSignalCorrelation</c>) replaced a 24-hour, process-local TTL cache — "durable" was
/// never meant to mean "grows forever," and an operator who never configures this section should
/// not silently accumulate unbounded rows the way they would if this defaulted off.
/// </para>
/// <para>
/// One instance-wide policy sweeping every owner's findings — same convention as
/// <see cref="AuditRetentionOptions"/>. A finding a <c>PlaybookEntry</c> still cites is never
/// pruned regardless of age, enforced by the sweep itself
/// (<c>PillarFindingRetentionWorker</c>), not by this configuration.
/// </para>
/// </remarks>
public sealed class PillarFindingRetentionOptions
{
    /// <summary>Section name in configuration.</summary>
    public const string SectionName = "Pillars:Retention";

    /// <summary>Whether the automatic retention sweep is enabled. Defaults to <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Findings older than this many days are purged, unless a <c>PlaybookEntry</c> still cites
    /// them. Only consulted when <see cref="Enabled"/> is true.
    /// </summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>How often the background sweep checks for expired findings, in hours.</summary>
    public int SweepIntervalHours { get; set; } = 24;
}
