using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Configuration;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.OAuth;

/// <summary>
/// Implements Azure OAuth 2.0 Authorization Code + PKCE flow for user-delegated authentication.
///
/// Security guarantees:
/// • PKCE (S256): authorization code is useless without the server-held code_verifier
/// • CSRF state: random token verified on callback; one-time use
/// • Tokens stored server-side only — browser only holds an opaque session ID in an HttpOnly cookie
/// • Refresh token rotated on every use
/// • Sessions expire after 8 hours
/// </summary>
internal sealed class AzureOAuthService : IOAuthService
{
    private const string ArmScope = "https://management.azure.com/user_impersonation";
    private const string SbScope = "https://servicebus.azure.com/user_impersonation";
    private const string ArmApiVersion = "2022-12-01";
    private const string SbrmApiVersion = "2021-11-01";
    private const string AuthorityBase = "https://login.microsoftonline.com/common/oauth2/v2.0";

    private readonly OAuthOptions _opts;
    private readonly InMemoryOAuthStore _store;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AzureOAuthService> _logger;

    public AzureOAuthService(
        IOptions<OAuthOptions> opts,
        InMemoryOAuthStore store,
        HttpClient httpClient,
        ILogger<AzureOAuthService> logger)
    {
        _opts = opts.Value;
        _store = store;
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool IsConfigured => _opts.IsConfigured;

    // ── Sign-in URL generation ────────────────────────────────────────────────

    /// <inheritdoc/>
    public (string AuthorizationUrl, string State) GenerateSignInUrl()
    {
        // CSRF state: 32 bytes → base64url
        var state = GenerateRandomBase64Url(32);

        // PKCE: 96-byte code_verifier → base64url; code_challenge = SHA-256 of verifier
        var codeVerifier = GenerateRandomBase64Url(96);
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        // Store state → verifier server-side (10-minute TTL)
        _store.StorePkceState(state, codeVerifier);

        var scopes = Uri.EscapeDataString($"{ArmScope} offline_access openid profile email");
        var redirectUri = Uri.EscapeDataString(_opts.RedirectUri);

        var url = $"{AuthorityBase}/authorize" +
                  $"?client_id={_opts.ClientId}" +
                  $"&response_type=code" +
                  $"&redirect_uri={redirectUri}" +
                  $"&scope={scopes}" +
                  $"&code_challenge={codeChallenge}" +
                  $"&code_challenge_method=S256" +
                  $"&state={state}" +
                  $"&prompt=select_account";

        return (url, state);
    }

    // ── Code exchange ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Result<string>> ExchangeCodeAsync(
        string code,
        string state,
        CancellationToken cancellationToken = default)
    {
        // Retrieve and consume the PKCE verifier (one-time, auto-expires after 10 min)
        var codeVerifier = _store.GetAndConsumePkceVerifier(state);
        if (codeVerifier is null)
        {
            _logger.LogWarning("OAuth callback: state token not found or expired. Possible CSRF attack.");
            return Result<string>.Failure(Error.Validation("OAuth.InvalidState",
                "The sign-in state is invalid or has expired. Please try again."));
        }

        var tokenResponse = await ExchangeCodeForTokensAsync(code, codeVerifier, cancellationToken);
        if (tokenResponse is null)
        {
            return Result<string>.Failure(Error.ExternalService("OAuth.TokenExchangeFailed",
                "Failed to exchange authorization code for tokens. Please try signing in again."));
        }

        // Extract user info from the ID token (JWT payload is base64url, middle segment)
        var (upn, tenantId) = ExtractUserInfo(tokenResponse.IdToken);

        // Create session
        var sessionId = GenerateRandomBase64Url(32);
        var session = new OAuthSession
        {
            SessionId = sessionId,
            UserPrincipalName = upn,
            TenantId = tenantId,
            ArmAccessToken = tokenResponse.AccessToken,
            ArmTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60),
            RefreshToken = tokenResponse.RefreshToken ?? string.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _store.StoreSession(session);

        _logger.LogInformation(
            "OAuth session created for user {Upn} in tenant {TenantId}",
            MaskUpn(upn), tenantId);

        return Result<string>.Success(sessionId);
    }

    // ── Namespace listing ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AzureNamespaceInfo>>> ListNamespacesAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = _store.GetSession(sessionId);
        if (session is null)
        {
            return Result<IReadOnlyList<AzureNamespaceInfo>>.Failure(
                Error.Validation("OAuth.SessionNotFound", "Session not found or expired. Please sign in again."));
        }

        // Ensure ARM token is still valid (refresh if expiring within 5 min)
        var armToken = await EnsureArmTokenAsync(session, cancellationToken);
        if (armToken is null)
        {
            return Result<IReadOnlyList<AzureNamespaceInfo>>.Failure(
                Error.ExternalService("OAuth.TokenRefreshFailed", "Failed to refresh Azure token. Please sign in again."));
        }

        try
        {
            // List all subscriptions the user has access to
            var subscriptions = await ListSubscriptionsAsync(armToken, cancellationToken);

            // For each subscription, list Service Bus namespaces (in parallel, max 5 at once)
            var semaphore = new SemaphoreSlim(5, 5);
            var tasks = subscriptions.Select(async subId =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return await ListNamespacesInSubscriptionAsync(armToken, subId, cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            var namespaces = results.SelectMany(x => x).OrderBy(n => n.Name).ToList();

            _logger.LogInformation(
                "Listed {Count} Service Bus namespaces for user {Upn}",
                namespaces.Count, MaskUpn(session.UserPrincipalName));

            return Result<IReadOnlyList<AzureNamespaceInfo>>.Success(namespaces);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list namespaces for session {SessionId}", sessionId[..8]);
            return Result<IReadOnlyList<AzureNamespaceInfo>>.Failure(
                Error.ExternalService("OAuth.ListFailed", "Failed to list your Azure Service Bus namespaces."));
        }
    }

    // ── Session info ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public OAuthSessionInfo? GetSessionInfo(string sessionId)
    {
        var session = _store.GetSession(sessionId);
        if (session is null) return null;

        return new OAuthSessionInfo(
            session.SessionId,
            session.UserPrincipalName,
            session.TenantId,
            session.ExpiresAt);
    }

    /// <inheritdoc/>
    public void RevokeSession(string sessionId)
    {
        _store.RemoveSession(sessionId);
        _logger.LogInformation("OAuth session {SessionId} revoked", sessionId[..8]);
    }

    // ── Token credential for Service Bus SDK ──────────────────────────────────

    /// <inheritdoc/>
    public TokenCredential? GetTokenCredential(string sessionId)
    {
        var session = _store.GetSession(sessionId);
        if (session is null) return null;

        return new OAuthSessionCredential(session, RefreshServiceBusTokenAsync);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private async Task<string?> EnsureArmTokenAsync(OAuthSession session, CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow < session.ArmTokenExpiry.AddMinutes(-5))
            return session.ArmAccessToken;

        // Refresh ARM token using refresh token
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _opts.ClientId,
            ["client_secret"] = _opts.ClientSecret,
            ["refresh_token"] = session.RefreshToken,
            ["scope"] = $"{ArmScope} offline_access",
        };

        var result = await PostTokenRequestAsync(form, ct);
        if (result is null) return null;

        session.ArmAccessToken = result.AccessToken;
        session.ArmTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(result.ExpiresIn - 60);
        if (!string.IsNullOrEmpty(result.RefreshToken))
            session.RefreshToken = result.RefreshToken;

        return session.ArmAccessToken;
    }

    private async Task<(string Token, DateTimeOffset Expiry, string NewRefreshToken)> RefreshServiceBusTokenAsync(
        OAuthSession session,
        CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _opts.ClientId,
            ["client_secret"] = _opts.ClientSecret,
            ["refresh_token"] = session.RefreshToken,
            ["scope"] = $"{SbScope} offline_access",
        };

        var result = await PostTokenRequestAsync(form, ct)
            ?? throw new InvalidOperationException("Failed to refresh Service Bus token via refresh token.");

        var expiry = DateTimeOffset.UtcNow.AddSeconds(result.ExpiresIn - 60);
        return (result.AccessToken, expiry, result.RefreshToken ?? string.Empty);
    }

    private async Task<TokenResponseData?> ExchangeCodeForTokensAsync(
        string code,
        string codeVerifier,
        CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _opts.ClientId,
            ["client_secret"] = _opts.ClientSecret,
            ["redirect_uri"] = _opts.RedirectUri,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
        };

        return await PostTokenRequestAsync(form, ct);
    }

    private async Task<TokenResponseData?> PostTokenRequestAsync(
        Dictionary<string, string> form,
        CancellationToken ct)
    {
        try
        {
            var response = await _httpClient
                .PostAsync($"{AuthorityBase}/token", new FormUrlEncodedContent(form), ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning("Azure token endpoint returned {StatusCode}: {Body}",
                    response.StatusCode, RedactTokenFromLog(errorBody));
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct).ConfigureAwait(false);
            if (json is null) return null;

            return new TokenResponseData(
                AccessToken: json["access_token"]?.GetValue<string>() ?? string.Empty,
                RefreshToken: json["refresh_token"]?.GetValue<string>(),
                IdToken: json["id_token"]?.GetValue<string>(),
                ExpiresIn: json["expires_in"]?.GetValue<int>() ?? 3600);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token request to Azure AD failed");
            return null;
        }
    }

    private async Task<IEnumerable<string>> ListSubscriptionsAsync(string armToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://management.azure.com/subscriptions?api-version={ArmApiVersion}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", armToken);

        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return [];

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct).ConfigureAwait(false);
        var items = json?["value"]?.AsArray();
        if (items is null) return [];

        return items
            .Select(x => x?["subscriptionId"]?.GetValue<string>())
            .Where(x => !string.IsNullOrEmpty(x))
            .Select(x => x!);
    }

    private async Task<IEnumerable<AzureNamespaceInfo>> ListNamespacesInSubscriptionAsync(
        string armToken,
        string subscriptionId,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.ServiceBus/namespaces?api-version={SbrmApiVersion}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", armToken);

        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return [];

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct).ConfigureAwait(false);
        var items = json?["value"]?.AsArray();
        if (items is null) return [];

        var results = new List<AzureNamespaceInfo>();
        foreach (var item in items)
        {
            var name = item?["name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(name)) continue;

            var props = item?["properties"]?.AsObject();
            var location = item?["location"]?.GetValue<string>() ?? "unknown";
            var skuName = item?["sku"]?["name"]?.GetValue<string>() ?? "Standard";
            var sbHostname = props?["serviceBusEndpoint"]?.GetValue<string>();

            // Extract hostname from service bus endpoint URL (https://name.servicebus.windows.net:443/)
            string fqns;
            if (!string.IsNullOrEmpty(sbHostname) &&
                Uri.TryCreate(sbHostname, UriKind.Absolute, out var uri))
                fqns = uri.Host;
            else
                fqns = $"{name}.servicebus.windows.net";

            // Extract resource group from ARM resource ID
            var resourceId = item?["id"]?.GetValue<string>() ?? string.Empty;
            var rg = ExtractResourceGroup(resourceId);

            results.Add(new AzureNamespaceInfo(name, fqns, subscriptionId, rg, location, skuName));
        }

        return results;
    }

    // ── PKCE helpers ──────────────────────────────────────────────────────────

    private static string GenerateRandomBase64Url(int byteCount)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        return Base64UrlEncode(bytes);
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    // ── JWT parsing ───────────────────────────────────────────────────────────

    private static (string Upn, string TenantId) ExtractUserInfo(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken))
            return ("unknown@unknown.onmicrosoft.com", "unknown");

        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2) return ("unknown@unknown.onmicrosoft.com", "unknown");

            // Pad base64url to standard base64
            var payload = parts[1];
            payload = payload.Replace('-', '+').Replace('_', '/');
            var padded = (payload.Length % 4) switch
            {
                2 => payload + "==",
                3 => payload + "=",
                _ => payload,
            };

            var json = JsonNode.Parse(Convert.FromBase64String(padded));
            var upn = json?["upn"]?.GetValue<string>()
                   ?? json?["preferred_username"]?.GetValue<string>()
                   ?? json?["email"]?.GetValue<string>()
                   ?? "unknown@unknown.onmicrosoft.com";
            var tid = json?["tid"]?.GetValue<string>() ?? "common";
            return (upn, tid);
        }
        catch
        {
            return ("unknown@unknown.onmicrosoft.com", "unknown");
        }
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    private static string ExtractResourceGroup(string resourceId)
    {
        // Format: /subscriptions/{sub}/resourceGroups/{rg}/providers/...
        var parts = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var rgIndex = Array.FindIndex(parts, p => p.Equals("resourceGroups", StringComparison.OrdinalIgnoreCase));
        return rgIndex >= 0 && rgIndex + 1 < parts.Length ? parts[rgIndex + 1] : "unknown";
    }

    private static string MaskUpn(string upn)
    {
        var atIdx = upn.IndexOf('@');
        if (atIdx <= 0) return "***";
        return upn[..2] + "***" + upn[atIdx..];
    }

    private static string RedactTokenFromLog(string body)
    {
        // Never log tokens from error responses
        if (body.Contains("access_token", StringComparison.OrdinalIgnoreCase))
            return "[REDACTED — contains token data]";
        return body;
    }

    // ── Internal DTO ──────────────────────────────────────────────────────────

    private sealed record TokenResponseData(
        string AccessToken,
        string? RefreshToken,
        string? IdToken,
        int ExpiresIn);
}
