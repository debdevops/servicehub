namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// Who ServiceHub believes is asking, and how it knows. <b>It never invents a name:</b> with no
/// identity configured, <see cref="Actor"/> is the browser session and says so.
/// </summary>
/// <param name="OwnerId">The owner every query for this caller is scoped to.</param>
/// <param name="AuthMethod">
/// <c>session</c> when nothing is configured or presented; otherwise <c>EasyAuth</c>, <c>Oidc</c>
/// or <c>ApiKey</c>.
/// </param>
/// <param name="Actor">The caller as the audit trail will record them.</param>
/// <param name="EffectiveRole">
/// The caller's fleet-wide governance role (unit 5.7): Viewer, Operator, Approver or Admin — Admin while governance is
/// inactive. Null means the caller has no role at all (governance active, nothing granted).
/// </param>
/// <param name="GovernanceActive">True once any grant has ever existed for this owner; until then everyone is Admin.</param>
/// <param name="Grantors">Who can grant roles fleet-wide (Admins), as people read them.</param>
/// <param name="RecoverRole">The role for recovery actions (replay, approve…) fleet-wide.</param>
/// <param name="NamespaceRecoverRoles">The role for recovery actions in each namespace the caller can see — a namespace grant can add to the fleet role.</param>
public sealed record MeResponse(
    string OwnerId, string AuthMethod, ActorResponse Actor, string? EffectiveRole, bool GovernanceActive = false, IReadOnlyList<string>? Grantors = null,
    string? RecoverRole = null, IReadOnlyDictionary<Guid, string?>? NamespaceRecoverRoles = null);

/// <summary>An actor as a person reads it.</summary>
/// <param name="Identity">The identity string stored on audit rows.</param>
/// <param name="Kind">user, apiKey, automation or system.</param>
/// <param name="Label">The words to show: a name, an <c>ApiKey:</c> credential, or "from this browser session".</param>
/// <param name="IsSession">True when the actor is known only as a browser session.</param>
public sealed record ActorResponse(string Identity, string Kind, string Label, bool IsSession);

/// <summary>One audit-trail entry.</summary>
/// <param name="Id">The entry's identifier.</param>
/// <param name="Timestamp">When it was recorded.</param>
/// <param name="Actor">Who did it.</param>
/// <param name="Action">A stable name such as <c>Namespace.Connect</c>.</param>
/// <param name="Outcome">Success or Failure.</param>
/// <param name="NamespaceId">The namespace it concerned, if any.</param>
/// <param name="NamespaceName">The namespace's name as it was then.</param>
/// <param name="CloudProvider">azure, aws or gcp, if a namespace was involved.</param>
/// <param name="Environment">The namespace's environment as it was then.</param>
/// <param name="ResourceName">What was acted on, if more specific than the namespace.</param>
/// <param name="ErrorDetails">A sanitised sentence when the action failed.</param>
/// <param name="CorrelationId">Ties the entry to the request and the server logs.</param>
public sealed record AuditEntryResponse(
    Guid Id,
    DateTimeOffset Timestamp,
    ActorResponse Actor,
    string Action,
    string Outcome,
    Guid? NamespaceId,
    string? NamespaceName,
    string? CloudProvider,
    string? Environment,
    string? ResourceName,
    string? ErrorDetails,
    string? CorrelationId);

/// <summary>One page of audit history, newest first.</summary>
/// <param name="Items">The entries on this page.</param>
/// <param name="Page">The 1-based page number.</param>
/// <param name="PageSize">The page size actually used (clamped).</param>
/// <param name="Total">How many entries match in all.</param>
public sealed record AuditPageResponse(IReadOnlyList<AuditEntryResponse> Items, int Page, int PageSize, int Total);
