namespace ServiceHub.Infrastructure.Configuration;

/// <summary>
/// Configuration options for Azure OAuth 2.0 user-delegated authentication.
/// Enables users to sign in with their own Microsoft identity — no connection strings or SAS keys needed.
///
/// <para><strong>One-time setup for DevOps / SRE (no coding required):</strong></para>
/// <list type="number">
///   <item>Register ServiceHub as a multi-tenant app in Azure Entra ID (see azure-entra-id/oauth/README.md)</item>
///   <item>Add delegated API permissions:
///     <list type="bullet">
///       <item>Azure Service Bus → user_impersonation</item>
///       <item>Azure Management (management.azure.com) → user_impersonation</item>
///     </list>
///   </item>
///   <item>Create a client secret (stays on the server — users never see it)</item>
///   <item>Register the redirect URI: https://yourapp.azurewebsites.net/api/v1/auth/azure/callback</item>
///   <item>Set these environment variables (Azure App Service → Configuration → App Settings):
///     <list type="bullet">
///       <item>AzureOAuth__Enabled = true</item>
///       <item>AzureOAuth__ClientId = &lt;your-app-registration-client-id&gt;</item>
///       <item>AzureOAuth__ClientSecret = &lt;your-client-secret&gt;</item>
///       <item>AzureOAuth__RedirectUri = https://yourapp.azurewebsites.net/api/v1/auth/azure/callback</item>
///       <item>AzureOAuth__FrontendBaseUrl = https://yourapp.azurewebsites.net</item>
///     </list>
///   </item>
/// </list>
/// </summary>
public sealed class OAuthOptions
{
    /// <summary>Configuration section name in appsettings.</summary>
    public const string SectionName = "AzureOAuth";

    /// <summary>
    /// ServiceHub's Azure App Registration Client ID (Application ID).
    /// Found in: Azure Portal → Entra ID → App registrations → ServiceHub → Application (client) ID.
    /// </summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// ServiceHub's Azure App Registration Client Secret.
    /// Server-side only — never exposed to end users or the browser.
    /// Set via env var: AzureOAuth__ClientSecret
    /// </summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>
    /// The OAuth redirect URI registered in Azure App Registration.
    /// Must exactly match one of the Redirect URIs configured in your app registration.
    /// Local dev: http://localhost:5153/api/v1/auth/azure/callback
    /// Production: https://yourapp.azurewebsites.net/api/v1/auth/azure/callback
    /// </summary>
    public string RedirectUri { get; init; } = string.Empty;

    /// <summary>
    /// The base URL of the ServiceHub frontend.
    /// Used to redirect back to the UI after OAuth callback.
    /// Local dev: http://localhost:3000
    /// Production: https://yourapp.azurewebsites.net
    /// </summary>
    public string FrontendBaseUrl { get; init; } = "http://localhost:3000";

    /// <summary>Whether user-delegated OAuth sign-in is enabled for this ServiceHub instance.</summary>
    public bool Enabled { get; init; }

    /// <summary>Returns true when all required fields are configured and Enabled = true.</summary>
    public bool IsConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret) &&
        !string.IsNullOrWhiteSpace(RedirectUri);
}
