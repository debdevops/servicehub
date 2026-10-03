using ServiceHub.Core.Entities;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// The durable history of what was done, by whom, to what. Home's Recent Activity and the audit
/// endpoint read it; every consequential action writes to it.
/// </summary>
/// <remarks>
/// A row names its actor by the identity <see cref="IActorIdentityResolver"/> produced — never a
/// caller-supplied string — so the trail says exactly as much as the product knows: a person, an
/// API key, or only "this browser session".
/// </remarks>
public interface IAuditTrail
{
    /// <summary>
    /// Appends one entry. The namespace's name, provider and environment are snapshotted onto the
    /// row so a removed namespace still reads correctly in history.
    /// </summary>
    Task RecordAsync(AuditLog entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of history for an owner, newest first. Entries about a namespace outside
    /// the allow-list in the query are not returned — and not counted.
    /// </summary>
    Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);
}

/// <summary>What to read from the audit trail.</summary>
/// <param name="OwnerId">Whose history.</param>
/// <param name="AllowedNamespaceIds">The caller's namespace allow-list; null when unrestricted.</param>
/// <param name="NamespaceId">Only entries about this namespace, when set.</param>
/// <param name="ScopeNamespaceIds">Only entries about these namespaces, when set — the namespaces of a chosen cloud or environment.
/// Entries about no namespace in particular are left out, since they belong to no cloud or environment.</param>
/// <param name="Action">Only entries of this action, when set.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Entries per page.</param>
public sealed record AuditQuery(
    string OwnerId,
    IReadOnlySet<Guid>? AllowedNamespaceIds = null,
    Guid? NamespaceId = null,
    string? Action = null,
    int Page = 1,
    int PageSize = 50,
    IReadOnlySet<Guid>? ScopeNamespaceIds = null);

/// <summary>One page of audit history.</summary>
/// <param name="Items">The entries, newest first.</param>
/// <param name="Total">How many entries match, across all pages.</param>
public sealed record AuditPage(IReadOnlyList<AuditLog> Items, int Total);
