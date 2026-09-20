namespace ServiceHub.Api.Authorization;

/// <summary>
/// Named bundles of <see cref="ApiKeyScopes"/> — a role is shorthand for a fixed set of
/// scopes, so an operator (or an enterprise identity provider's group/role mapping) can grant
/// "Viewer" instead of enumerating seven read-only scope strings by hand. A role name is valid
/// anywhere a scope is accepted — <see cref="ApiKeyConfiguration.Scopes"/> and an OIDC token's
/// <c>scope</c> claim both go through <see cref="Expand"/>.
/// </summary>
/// <remarks>
/// There is no separate "Admin" role: the existing <see cref="ApiKeyScopes.Admin"/> scope
/// already grants everything (see <see cref="ApiKeyScopes.Grants"/>), and an API key or OIDC
/// token with no scopes at all is already treated as admin for backward compatibility — adding
/// a redundant "Admin" role bundle would just be another name for the same thing.
/// </remarks>
public static class ApiKeyRoles
{
    /// <summary>Read-only access: browse namespaces, entities, and messages; no mutation.</summary>
    public const string Viewer = "Viewer";

    /// <summary>Viewer plus the ability to send messages and replay/purge DLQ messages.</summary>
    public const string Operator = "Operator";

    /// <summary>Viewer plus audit trail access — for compliance/security review without operational access.</summary>
    public const string Auditor = "Auditor";

    private static readonly IReadOnlyDictionary<string, string[]> Bundles =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [Viewer] =
            [
                ApiKeyScopes.NamespacesRead,
                ApiKeyScopes.QueuesRead,
                ApiKeyScopes.TopicsRead,
                ApiKeyScopes.SubscriptionsRead,
                ApiKeyScopes.MessagesPeek,
                ApiKeyScopes.DlqRead,
                ApiKeyScopes.AnomaliesRead,
                ApiKeyScopes.DriftFindingsRead,
                ApiKeyScopes.CorrelationFindingsRead,
                ApiKeyScopes.NarrationsRead,
                ApiKeyScopes.BacklogForecastsRead,
            ],
            [Operator] =
            [
                ApiKeyScopes.NamespacesRead,
                ApiKeyScopes.QueuesRead,
                ApiKeyScopes.TopicsRead,
                ApiKeyScopes.SubscriptionsRead,
                ApiKeyScopes.MessagesPeek,
                ApiKeyScopes.MessagesSend,
                ApiKeyScopes.DlqRead,
                ApiKeyScopes.DlqWrite,
                ApiKeyScopes.AnomaliesRead,
                ApiKeyScopes.DriftFindingsRead,
                ApiKeyScopes.CorrelationFindingsRead,
                ApiKeyScopes.NarrationsRead,
                ApiKeyScopes.BacklogForecastsRead,
            ],
            [Auditor] =
            [
                ApiKeyScopes.NamespacesRead,
                ApiKeyScopes.QueuesRead,
                ApiKeyScopes.TopicsRead,
                ApiKeyScopes.SubscriptionsRead,
                ApiKeyScopes.MessagesPeek,
                ApiKeyScopes.DlqRead,
                ApiKeyScopes.AnomaliesRead,
                ApiKeyScopes.DriftFindingsRead,
                ApiKeyScopes.CorrelationFindingsRead,
                ApiKeyScopes.NarrationsRead,
                ApiKeyScopes.AuditRead,
            ],
        };

    /// <summary>
    /// Expands any role names in <paramref name="scopesOrRoles"/> into their bundled scopes.
    /// A value that isn't a known role name (a literal scope like <c>"dlq:read"</c>, or
    /// anything unrecognised) passes through unchanged — <see cref="ApiKeyScopes.Grants"/>
    /// simply never matches an unrecognised value, so this never silently drops input.
    /// </summary>
    public static string[] Expand(IEnumerable<string> scopesOrRoles)
    {
        var expanded = new List<string>();
        foreach (var value in scopesOrRoles)
        {
            if (Bundles.TryGetValue(value, out var bundle))
            {
                expanded.AddRange(bundle);
            }
            else
            {
                expanded.Add(value);
            }
        }

        return expanded.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
