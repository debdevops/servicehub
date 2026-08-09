namespace ServiceHub.Api.Authorization;

/// <summary>
/// Parses the optional namespace-restriction values carried on a scoped API key
/// (<see cref="ApiKeyConfiguration.Namespaces"/>) or an OIDC token's <c>namespaces</c> claim into
/// the <see cref="HashSet{Guid}"/> stored on <c>HttpContext.Items["AllowedNamespaceIds"]</c>.
/// Shared by <c>ApiKeyAuthenticationMiddleware</c> and <c>OidcBearerAuthenticationMiddleware</c>
/// so both identity sources produce the exact same allow-list shape for downstream enforcement.
/// </summary>
internal static class NamespaceAllowList
{
    /// <summary>
    /// Returns null (unrestricted) when <paramref name="rawIds"/> is null/empty/has no valid GUIDs;
    /// otherwise the parsed set. Malformed entries are skipped rather than rejecting the whole key,
    /// matching how invalid scope strings are already tolerated elsewhere in this pipeline.
    /// </summary>
    public static HashSet<Guid>? Parse(IEnumerable<string>? rawIds)
    {
        if (rawIds is null)
        {
            return null;
        }

        HashSet<Guid>? allowed = null;
        foreach (var rawId in rawIds)
        {
            if (Guid.TryParse(rawId, out var id))
            {
                (allowed ??= []).Add(id);
            }
        }

        return allowed;
    }
}
