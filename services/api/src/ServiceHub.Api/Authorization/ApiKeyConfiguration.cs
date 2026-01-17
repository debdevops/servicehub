namespace ServiceHub.Api.Authorization;

/// <summary>
/// Represents an API key with associated scopes for authorization.
/// </summary>
public sealed class ApiKeyConfiguration
{
    /// <summary>
    /// Gets or sets the API key value.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets or sets the scopes granted to this API key.
    /// Empty or null means the key has admin scope (backward compatibility).
    /// </summary>
    public string[]? Scopes { get; init; }

    /// <summary>
    /// Gets or sets an optional description for this API key.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Checks if this API key has the specified scope.
    /// Keys with no scopes defined are treated as admin (all permissions).
    /// </summary>
    public bool HasScope(string requiredScope)
    {
        // Backward compatibility: keys without explicit scopes have admin access
        if (Scopes == null || Scopes.Length == 0)
        {
            return true;
        }

        foreach (var scope in Scopes)
        {
            if (ApiKeyScopes.Grants(scope, requiredScope))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets a safe representation for logging (never log the actual key).
    /// </summary>
    public string GetSafeKey()
    {
        return Key.Length > 8 ? $"{Key[..8]}***" : "***";
    }
}
