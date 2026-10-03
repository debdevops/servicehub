using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// A durably recorded external signal — a deploy or config-change timestamp — that C3 (external-
/// signal correlation, roadmap §5.D) correlates anomaly onset against. This is the one genuinely
/// new persistence need M5 (persistence design §1.6) introduced: the signal being correlated
/// against is not evidence *of a finding* the way a <see cref="RecoveryLedgerEntry"/> or
/// <see cref="PlaybookEntry"/> is, so it does not belong in either ledger — it is the input a
/// correlation hypothesis will cite, not the hypothesis itself.
/// <para>
/// Deliberately not hash-chained: this is raw external input, not a system claim. Its
/// trustworthiness is "did the webhook/operator say so," not "did ServiceHub verify it," so
/// ledger-grade tamper-evidence does not apply here the way it does to
/// <see cref="RecoveryEvent"/>/<see cref="PlaybookEvent"/>.
/// </para>
/// </summary>
public sealed class ExternalSignalEvent
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Owner ID — every query and correlation is owner-scoped, same as every other table.</summary>
    public required string OwnerId { get; init; }

    /// <summary>Soft reference to the namespace this signal concerns — no FK. Null means a
    /// fleet-wide signal (e.g. a platform-wide deploy), matched against anomalies in any of the
    /// owner's namespaces.</summary>
    public Guid? NamespaceId { get; init; }

    /// <summary>The kind of signal.</summary>
    public required ExternalSignalType SignalType { get; init; }

    /// <summary>When the signal actually happened — not when it was ingested. This, not
    /// <see cref="IngestedAt"/>, is what correlation windows are measured against.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Where the signal came from, e.g. <c>"webhook:github-actions"</c>, <c>"manual"</c>.</summary>
    public required string Source { get; init; }

    /// <summary>Free-form detail (e.g. commit SHA, deploy ID) — never a message body. Passed
    /// through <c>LogRedactor.Redact</c> before persisting.</summary>
    public string? DetailJson { get; init; }

    /// <summary>When this signal was recorded in ServiceHub.</summary>
    public required DateTimeOffset IngestedAt { get; init; }
}
