using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// The current lifecycle status of a failure signature, plus how it got there.
/// Never null: a signature that has never been transitioned reports
/// <see cref="SignatureLifecycleStatus.Active"/> with no previous status or notes.
/// </summary>
public sealed record SignatureLifecycleSnapshot(
    SignatureLifecycleStatus Status,
    SignatureLifecycleStatus? PreviousStatus,
    DateTimeOffset? TransitionedAt,
    string? Notes);

/// <summary>One recorded lifecycle transition for a failure signature.</summary>
public sealed record SignatureLifecycleEvent(
    SignatureLifecycleStatus FromStatus,
    SignatureLifecycleStatus ToStatus,
    DateTimeOffset Timestamp,
    string? Notes);
