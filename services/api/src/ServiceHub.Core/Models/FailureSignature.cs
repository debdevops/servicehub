using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// A recurring operational failure pattern recognized by the system.
///
/// A Failure Signature represents a single distinct failure class that has been observed
/// multiple times across different DLQ messages. Users should think in terms of
/// "recurring failures" rather than "clusters" or "fingerprints".
///
/// Signature lifecycle:
/// 1. First occurrence: Created with FirstSeen = now, OccurrenceCount = 1
/// 2. Subsequent occurrences: OccurrenceCount incremented, LastSeen updated
/// 3. Never deleted: Preserved for historical context and pattern learning
///
/// Extensible for future features:
/// - Resolution Notes, Runbook, Owner (Phase 3+)
/// - Replay by Signature, Timeline (Phase 4+)
/// - Knowledge Base, Reliability Score (Phase 5+)
/// - Incident Links, Automated Remediation (Phase 6+)
/// </summary>
public sealed record FailureSignature(
    long Id,
    FailureFingerprint Fingerprint,
    string DisplayName,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    int OccurrenceCount,
    CloudProviderType Provider,
    string? FailureCategory,
    double Confidence,
    SignatureStatus Status)
{
    /// <summary>
    /// Friendly version of the signature for logging and display.
    /// Format: "Signature#{Id}: {DisplayName} (Seen {OccurrenceCount}x, Confidence {Confidence:P0})"
    /// </summary>
    public override string ToString() =>
        $"Signature#{Id}: {DisplayName} (Seen {OccurrenceCount}x, Confidence {Confidence:P0})";
}

/// <summary>
/// Lifecycle status of a Failure Signature.
/// </summary>
public enum SignatureStatus
{
    /// <summary>Failure is appearing in current scans and requires attention.</summary>
    Active = 0,

    /// <summary>Failure has been fixed; expectation is that it will not recur.</summary>
    Resolved = 1,

    /// <summary>Acknowledged but not yet fixed; hide from primary UI view.</summary>
    Suppressed = 2,

    /// <summary>Old pattern with no recent activity; keep for historical context only.</summary>
    Archived = 3,
}
