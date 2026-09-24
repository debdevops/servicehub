namespace ServiceHub.Core.Models;

/// <summary>How many entries are in one state. Zeros are present, not omitted: "0 failed" is information (V4).</summary>
public sealed record RecoveryStateCount(string State, int Count);

/// <summary>How the entries that came back were matched: by the recovery ID (<c>Exact</c>) or by contents (<c>Heuristic</c>, V3).</summary>
public sealed record RecoveryConfidenceCounts(int Exact, int Heuristic);

/// <summary>One cloud's share of the summary.</summary>
public sealed record RecoveryProviderSummary(string Provider, int Total, IReadOnlyList<RecoveryStateCount> States, double? StayedFixedRate);

/// <summary>
/// "How did recoveries end?" — computed once, here, so Simple's percentage and Advanced's breakdown can never
/// disagree (unit 2.12). <b>Recovered and Unverified are never merged (V1)</b>: they are proof and hope.
/// </summary>
/// <param name="Window">The window asked for: <c>24h</c>, <c>7d</c>, <c>30d</c> or <c>all</c>.</param>
/// <param name="Total">Every entry begun in the window.</param>
/// <param name="States">A count for every state, in lifecycle order.</param>
/// <param name="ByProvider">The same split per cloud that has any entries.</param>
/// <param name="StayedFixedRate">
/// <c>Recovered / (Recovered + Returned)</c>. <c>Unverified</c> is on neither side. Null — never 0 — when there is
/// nothing to divide: no rate is not a bad rate.
/// </param>
/// <param name="ReturnedConfidence">How the returned entries were matched.</param>
/// <param name="ReplaysAccepted">
/// Replays the cloud accepted: those now being watched plus those that ended Recovered, Returned or Unverified.
/// Counted here so "replayed today" is one number everywhere.
/// </param>
public sealed record RecoverySummary(
    string Window,
    int Total,
    IReadOnlyList<RecoveryStateCount> States,
    IReadOnlyList<RecoveryProviderSummary> ByProvider,
    double? StayedFixedRate,
    RecoveryConfidenceCounts ReturnedConfidence,
    int ReplaysAccepted);

/// <summary>One row of the Recovery Ledger.</summary>
public sealed record RecoveryEntryListItem(
    Guid Id,
    Guid OperationId,
    DateTimeOffset BegunAt,
    string Kind,
    string EntityName,
    string TargetEntity,
    string? Provider,
    string? NamespaceName,
    ReplayActor Actor,
    string State,
    string? Confidence,
    long? DlqMessageId,
    DateTimeOffset? ClosedAt);

/// <summary>A page of ledger entries, newest first.</summary>
public sealed record RecoveryEntryPage(IReadOnlyList<RecoveryEntryListItem> Items, int Total, int Page, int PageSize);

/// <summary>One event of an entry's history, with its place in the hash chain.</summary>
public sealed record RecoveryEventItem(
    long Seq, string EventType, DateTimeOffset OccurredAt, ReplayActor Actor, string? Detail, string PrevHash, string EntryHash);

/// <summary>One entry opened: what it is, its full history and its evidence. Read-only.</summary>
public sealed record RecoveryEntryDetail(
    RecoveryEntryListItem Entry,
    string? RecoveryMarker,
    bool MarkerApplied,
    string? DeadLetterReason,
    string? VerificationResult,
    DateTimeOffset? ObservationWindowEndsAt,
    long LastEventSeq,
    IReadOnlyList<RecoveryEventItem> Events);
