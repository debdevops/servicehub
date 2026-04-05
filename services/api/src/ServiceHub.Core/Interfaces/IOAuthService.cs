using Azure.Core;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Service for Azure OAuth 2.0 user-delegated authentication.
/// Manages the full authorization code + PKCE flow:
/// sign-in URL generation → Azure login → code exchange → session management →
/// ARM namespace discovery → Service Bus token provisioning.
/// </summary>
public interface IOAuthService
{
    /// <summary>
    /// Returns true if OAuth user-delegated sign-in is configured on this ServiceHub instance.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Generates an Azure OAuth 2.0 authorization URL for the user to navigate to.
    /// Includes a PKCE code challenge (S256) and a cryptographically random CSRF state token.
    /// </summary>
    /// <returns>The authorization URL and the state token (saved server-side for verification).</returns>
    (string AuthorizationUrl, string State) GenerateSignInUrl();

    /// <summary>
    /// Exchanges the authorization code received from Azure for access and refresh tokens.
    /// Verifies the CSRF state token and PKCE code verifier before proceeding.
    /// On success, creates an in-memory session and returns the session ID to set as cookie.
    /// </summary>
    /// <param name="code">The authorization code from Azure.</param>
    /// <param name="state">The CSRF state token to verify.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Result containing the new session ID on success, or an error on failure.</returns>
    Task<Result<string>> ExchangeCodeAsync(string code, string state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the user's Azure Service Bus namespaces across all their subscriptions.
    /// Calls the Azure Resource Manager API using the user's ARM access token.
    /// </summary>
    /// <param name="sessionId">The session ID from the HttpOnly session cookie.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of Service Bus namespace details, or an error if the session is invalid.</returns>
    Task<Result<IReadOnlyList<AzureNamespaceInfo>>> ListNamespacesAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current sign-in session information (no tokens or secrets).
    /// Safe to return to the frontend.
    /// </summary>
    /// <param name="sessionId">The session ID from the HttpOnly session cookie.</param>
    /// <returns>Session info, or null if not signed in or session has expired.</returns>
    OAuthSessionInfo? GetSessionInfo(string sessionId);

    /// <summary>
    /// Revokes the OAuth session and clears all stored tokens.
    /// Call this on sign-out.
    /// </summary>
    /// <param name="sessionId">The session ID to revoke.</param>
    void RevokeSession(string sessionId);

    /// <summary>
    /// Returns a <see cref="TokenCredential"/> backed by the user's delegated Service Bus token.
    /// The credential automatically refreshes tokens using the stored refresh token when needed.
    /// Returns null if the session is not found or has expired.
    /// </summary>
    /// <param name="sessionId">The session ID to retrieve credentials for.</param>
    /// <returns>A TokenCredential for Azure Service Bus SDK operations, or null.</returns>
    TokenCredential? GetTokenCredential(string sessionId);

    /// <summary>
    /// Uses the user's ARM token to retrieve a Service Bus namespace connection string via the ARM
    /// listKeys API. Prefers non-root authorization rules; falls back to RootManageSharedAccessKey.
    /// Does not require the <c>https://servicebus.azure.com</c> enterprise app to be provisioned.
    /// </summary>
    /// <param name="sessionId">The session ID from the HttpOnly session cookie.</param>
    /// <param name="subscriptionId">The Azure subscription ID that owns the namespace.</param>
    /// <param name="resourceGroup">The resource group containing the namespace.</param>
    /// <param name="namespaceName">The short namespace name (e.g. my-servicebus, not the FQDN).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The primary connection string on success, or an error.</returns>
    Task<Result<string>> GetConnectionStringAsync(
        string sessionId,
        string subscriptionId,
        string resourceGroup,
        string namespaceName,
        CancellationToken cancellationToken = default);
}
