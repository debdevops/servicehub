namespace ServiceHub.Core.Models;

/// <summary>How one thing the person should know before replaying stands.</summary>
/// <param name="Id">Stable id: <c>environment</c>, <c>frequency</c>, <c>verification</c>, <c>status</c>.</param>
/// <param name="Label">The check, in words.</param>
/// <param name="State"><c>passed</c>, <c>warning</c> (does not block) or <c>blocked</c>.</param>
/// <param name="Detail">The fact behind it, or null.</param>
public sealed record ReplayCheck(string Id, string Label, string State, string? Detail);

/// <summary>
/// What replaying one dead letter would do — shown BEFORE anything runs (unit 2.7). Only facts ServiceHub
/// actually has appear here: no check is listed that was not evaluated.
/// </summary>
/// <param name="DlqMessageId">The dead letter.</param>
/// <param name="MessageId">The broker's id.</param>
/// <param name="SourceEntity">The dead-letter queue it leaves.</param>
/// <param name="TargetEntity">The queue it is sent back to.</param>
/// <param name="NamespaceName">The namespace.</param>
/// <param name="Provider">The cloud, lower-case.</param>
/// <param name="Environment">Development, Uat or Prod.</param>
/// <param name="StampsRecoveryMarker">Whether the cloud will carry a recovery marker on the replayed copy.</param>
/// <param name="PriorAttempts">Earlier replays of this same message body in this queue (last 90 days).</param>
/// <param name="AttemptCap">The count at which the recurrence cap applies.</param>
/// <param name="OthersLikeIt">Other active dead letters in this queue with the same recorded reason.</param>
/// <param name="ObservationWindowHours">How long ServiceHub will watch for it coming back.</param>
/// <param name="CanConfirm">Whether this cloud can prove the queue stayed empty (R4).</param>
/// <param name="Verdict">The gate's verdict.</param>
/// <param name="ReasonCode">The gate's named reason, or null on a clean Allow.</param>
/// <param name="Approvable">True only for Escalate.</param>
/// <param name="CanExecute">Whether Replay can be pressed now.</param>
/// <param name="BlockedCode">Why not, as a code (gate reason, or <c>NOT_ACTIVE</c>), when it cannot.</param>
/// <param name="Checks">The checks, in the order shown.</param>
public sealed record ReplayProposal(
    long DlqMessageId,
    string MessageId,
    string SourceEntity,
    string TargetEntity,
    string NamespaceName,
    string Provider,
    string Environment,
    bool StampsRecoveryMarker,
    int PriorAttempts,
    int AttemptCap,
    int OthersLikeIt,
    double ObservationWindowHours,
    bool CanConfirm,
    string Verdict,
    string? ReasonCode,
    bool Approvable,
    bool CanExecute,
    string? BlockedCode,
    IReadOnlyList<ReplayCheck> Checks);

/// <summary>What happened when a replay ran. The attempt is in the ledger whatever the answer.</summary>
/// <param name="EntryId">The ledger entry.</param>
/// <param name="OperationId">The ledger operation.</param>
/// <param name="Result"><c>accepted</c>, <c>rejected</c> or <c>unknown</c>.</param>
/// <param name="State">The entry's state: Observing, ExecutionFailed or ExecutionUnknown.</param>
/// <param name="MarkerApplied">Whether the recovery marker made it onto the replayed copy.</param>
/// <param name="ObservationWindowEndsAt">When the watch ends, when one was opened.</param>
/// <param name="Message">A sentence for a person.</param>
/// <param name="ErrorCode">The provider's error code when it rejected.</param>
public sealed record ReplayOutcome(
    Guid EntryId,
    Guid OperationId,
    string Result,
    string State,
    bool MarkerApplied,
    DateTimeOffset? ObservationWindowEndsAt,
    string Message,
    string? ErrorCode);

/// <summary>
/// The honest answer to "did it work?" for one replay. <b>Same shape for every cloud</b> — only the status and the
/// reason change (rule C5). <c>verified</c> is only ever claimed where the cloud can prove the queue stayed empty (R4).
/// </summary>
/// <param name="Status">
/// <c>watching</c>, <c>verified</c>, <c>verification_required</c>, <c>returned</c>, <c>not_sent</c> or <c>unknown</c>.
/// <c>verification_required</c> is not a failure: the replay may have worked perfectly; what is unproven is the confirmation.
/// </param>
/// <param name="ReasonCode">Why, as a code (<c>AWS_NO_ABSENCE_PROOF</c>…), when there is one.</param>
/// <param name="Confidence">For <c>returned</c>: <c>Exact</c> (matched by the recovery ID) or <c>Heuristic</c> (matched by contents).</param>
/// <param name="WatchUntil">When the watch window ends.</param>
/// <param name="CanConfirm">Whether this namespace's cloud can prove the queue stayed empty.</param>
/// <param name="Remedy"><c>SETUP_DLQ_OBSERVER</c> when the way to a verified result is the DLQ observer, else null (C4).</param>
public sealed record ReplayVerification(
    string Status, string? ReasonCode, string? Confidence, DateTimeOffset? WatchUntil, bool CanConfirm, string? Remedy);

/// <summary>
/// Who acted, exactly as far as ServiceHub knows (unit 2.9, R6): a name only when an identity source supplied one.
/// </summary>
/// <param name="Identity">The identity string the ledger recorded.</param>
/// <param name="Kind"><c>user</c>, <c>apiKey</c>, <c>automation</c> or <c>system</c>.</param>
/// <param name="Label">The words to show. For a browser session, never a name.</param>
/// <param name="IsSession">True when the actor is known only as a browser session.</param>
public sealed record ReplayActor(string Identity, string Kind, string Label, bool IsSession);

/// <summary>One line of the Replayed tab.</summary>
public sealed record ReplayListItem(
    long Id,
    long DlqMessageId,
    Guid NamespaceId,
    string Provider,
    string MessageId,
    string SourceEntity,
    string TargetEntity,
    DateTimeOffset ReplayedAt,
    string ReplayedBy,
    ReplayActor Actor,
    string OutcomeStatus,
    string? EntryState,
    DateTimeOffset? ObservationWindowEndsAt,
    bool MarkerApplied,
    ReplayVerification Verification);

/// <summary>A page of replays, newest first.</summary>
public sealed record ReplayPage(IReadOnlyList<ReplayListItem> Items, int Total, int Page, int PageSize);
