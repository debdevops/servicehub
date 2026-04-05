namespace ServiceHub.Infrastructure.Configuration;

/// <summary>
/// Configuration options for Azure Entra ID (formerly Azure Active Directory) authentication.
/// Used when connecting to Service Bus namespaces via OAuth instead of connection strings.
/// </summary>
public sealed class EntraIdOptions
{
    /// <summary>Configuration section name in appsettings.</summary>
    public const string SectionName = "EntraId";

    /// <summary>
    /// ServiceHub's Azure App Registration Client ID (Application ID).
    /// Users must grant this App ID the "Azure Service Bus Data Owner" role
    /// on their Service Bus namespace before connecting.
    /// </summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// ServiceHub's Azure App Registration Client Secret.
    /// Used to authenticate ServiceHub itself when requesting tokens on behalf of users.
    /// Set via ENTRA_ID__CLIENTSECRET environment variable — never hardcode.
    /// </summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>
    /// ServiceHub's Azure Tenant ID. This is the tenant where ServiceHub's App Registration lives.
    /// Note: Users connect namespaces from THEIR tenant — they grant ServiceHub's App ID cross-tenant access.
    /// </summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>
    /// Whether Entra ID authentication is enabled for this ServiceHub deployment.
    /// Requires ClientId, ClientSecret, and TenantId to be configured.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>Returns true if all required fields are configured for service principal auth.</summary>
    public bool IsConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret) &&
        !string.IsNullOrWhiteSpace(TenantId);

    /// <summary>
    /// Returns true when Entra ID is enabled but no explicit credentials are provided.
    /// In this mode, <see cref="Azure.Identity.DefaultAzureCredential"/> is used
    /// (picks up az login, Managed Identity, environment variables, etc.).
    /// Useful for local development and Azure-hosted deployments with a Managed Identity.
    /// </summary>
    public bool IsDefaultCredentialMode =>
        Enabled &&
        string.IsNullOrWhiteSpace(ClientId) &&
        string.IsNullOrWhiteSpace(ClientSecret) &&
        string.IsNullOrWhiteSpace(TenantId);

    /// <summary>Returns true when Entra ID is usable via any auth mode.</summary>
    public bool IsAvailable => IsConfigured || IsDefaultCredentialMode;
}
