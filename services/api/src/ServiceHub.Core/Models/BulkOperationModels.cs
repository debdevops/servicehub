using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>A message the gate would not let through, with why and what to do (never summarised as a count).</summary>
public sealed record BulkHeldBack(long DlqMessageId, string EntityName, string? DeadLetterReason, string ReasonCode, string Remedy);

/// <summary>Messages that failed the same way, and how many of them will be sent.</summary>
public sealed record BulkGroup(string Reason, int Selected, int WillReplay, int HeldBack);

/// <summary>What a bulk replay would do. Stored server-side; the run can only start from it.</summary>
public sealed record BulkPreview(
    Guid PreviewId,
    int Selected,
    int WillReplay,
    int HeldBackCount,
    IReadOnlyList<BulkGroup> Groups,
    IReadOnlyList<BulkHeldBack> HeldBack,
    double PerSecond,
    int StopAfterConsecutiveFailures,
    int ExpiresInMinutes,
    bool CanProveDlqAbsence,
    RecoveryOperationKind Kind = RecoveryOperationKind.Replay);

/// <summary>Where a bulk job is, for the running view and the Replayed tab.</summary>
public sealed record BulkProgress(
    Guid Id,
    BulkOperationStatus Status,
    int Selected,
    int WillReplay,
    int Sent,
    int Failed,
    int Unknown,
    int Remaining,
    int HeldBack,
    bool SampleOnly,
    string? EndedReason,
    DateTimeOffset PreviewedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    RecoveryOperationKind Kind = RecoveryOperationKind.Replay);
