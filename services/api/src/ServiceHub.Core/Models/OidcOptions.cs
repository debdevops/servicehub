namespace ServiceHub.Core.Models;

/// <summary>
/// Configuration options for BYO-IdP OIDC Bearer token authentication.
/// Bound from the "Security:Oidc" section of appsettings.json.
/// <para>
/// This is the provider-neutral counterpart to <c>Security:EasyAuth</c>: Easy Auth only works
/// behind Azure App Service's built-in auth proxy, so it gives per-user identity exclusively to
/// Azure-hosted deployments. Any standards-compliant OIDC identity provider (Entra ID, Okta,
/// Auth0, Ping, Google Workspace, ...) can issue a token this validates instead — the same
/// mechanism works identically whether ServiceHub runs on Azure App Service, AWS ECS, GCP Cloud
/// Run, Docker Compose, or bare metal.
/// </para>
/// </summary>
public sealed class OidcOptions
{
    /// <summary>Section name in configuration.</summary>
    public const string SectionName = "Security:Oidc";

    /// <summary>Whether OIDC Bearer token authentication is enabled. Defaults to off.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The identity provider's issuer URL (e.g. <c>https://login.microsoftonline.com/{tenant}/v2.0</c>,
    /// <c>https://your-org.okta.com/oauth2/default</c>). Used both as the expected <c>iss</c> claim
    /// and to derive the OIDC discovery document URL (<c>{Authority}/.well-known/openid-configuration</c>)
    /// from which signing keys are fetched and automatically rotated.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// The expected <c>aud</c> claim — the client/application ID this ServiceHub instance was
    /// registered as with the identity provider.
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Allowed clock skew, in seconds, when validating token expiry. Defaults to 2 minutes.</summary>
    public int ClockSkewSeconds { get; set; } = 120;
}
