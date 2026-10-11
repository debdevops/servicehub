using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Everything waiting for a person (units 5.1, 5.6): replays an agent stopped and asked about, and agents that have
/// stopped working. Read from what is already durable (the ledger) and what the running process knows (the agent
/// registry) — no table of its own, no read/unread state. An item leaves the list only when the work is resolved.
/// </summary>
public interface IPendingWorkService
{
    /// <summary>The pending work the caller may see, most urgent first, narrowed to a cloud / namespace / environment.</summary>
    Task<PendingWorkPage> ListAsync(PendingWorkScope scope, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// Who is asking and what they narrowed to. <see cref="AllowedNamespaceIds"/> null = unrestricted. <see cref="EntryId"/> asks for
/// exactly one waiting approval, however many others wait — a list is capped, so a lookup must not depend on it.
/// </summary>
public sealed record PendingWorkScope(
    string OwnerId,
    IReadOnlySet<Guid>? AllowedNamespaceIds,
    CloudProviderType? Provider = null,
    Guid? NamespaceId = null,
    EnvironmentType? Environment = null,
    string? ReasonCode = null,
    Guid? EntryId = null);

/// <summary>One thing waiting for a person.</summary>
/// <param name="Kind"><c>approval</c> — a replay waits for yes or no · <c>rule</c> — a rule switched itself off · <c>agent</c> — an agent has stopped working · <c>unresolved</c> — an attempt to put a message back has no recorded answer, so nothing may try again until a person says what happened.</param>
/// <param name="Id">Stable: the entry id for an approval, <c>rule:{id}</c> for a rule, <c>agent:{id}</c> for an agent, the entry id for an unresolved attempt.</param>
/// <param name="EntryId">The Declined ledger entry (approvals).</param>
/// <param name="AgentId">The agent (agent items).</param>
/// <param name="DlqMessageId">The dead letter it is about (approvals) — what Approve replays.</param>
/// <param name="NamespaceId">Where.</param>
/// <param name="NamespaceName">The namespace's name when it was asked.</param>
/// <param name="Provider">azure · aws · gcp; null for an agent (agents serve every cloud).</param>
/// <param name="Environment">dev · uat · prod.</param>
/// <param name="Entity">The queue or subscription.</param>
/// <param name="DeadLetterReason">What the cloud recorded, if anything.</param>
/// <param name="RuleId">The rule that wanted to act.</param>
/// <param name="RuleName">Its name.</param>
/// <param name="ReasonCode">The gate's code (or AGENT_STALE / AGENT_FAILING), never flattened.</param>
/// <param name="Reason">The same, in one plain sentence.</param>
/// <param name="Since">When it started waiting.</param>
public sealed record PendingWorkItem(
    string Kind, string Id, Guid? EntryId, string? AgentId, long? DlqMessageId, Guid? NamespaceId, string? NamespaceName,
    string? Provider, string? Environment, string? Entity, string? DeadLetterReason, long? RuleId, string? RuleName,
    string ReasonCode, string Reason, DateTimeOffset Since);

/// <summary>A count per cloud — "2 waiting, both on AWS".</summary>
public sealed record PendingWorkProviderCount(string Provider, int Count);

/// <summary>The list, its full count, and the count per cloud (agent items have no cloud and are counted apart).</summary>
public sealed record PendingWorkPage(IReadOnlyList<PendingWorkItem> Items, int Total, IReadOnlyList<PendingWorkProviderCount> ByProvider, int Agents);
