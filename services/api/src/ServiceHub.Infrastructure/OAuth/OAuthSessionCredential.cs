using Azure.Core;

namespace ServiceHub.Infrastructure.OAuth;

/// <summary>
/// A <see cref="TokenCredential"/> implementation backed by a user's OAuth session.
/// Used by the Azure Service Bus SDK to authenticate on behalf of the signed-in user.
///
/// The credential:
/// 1. Returns a cached Service Bus token when it has more than 5 minutes remaining.
/// 2. Otherwise uses the stored refresh token to request a new SB-scoped token from Azure AD.
/// 3. Updates the session with the new token and any rotated refresh token.
///
/// Thread-safe via SemaphoreSlim.
/// </summary>
internal sealed class OAuthSessionCredential : TokenCredential
{
    private const string ServiceBusScope = "https://servicebus.azure.com/user_impersonation";

    private readonly OAuthSession _session;
    private readonly Func<OAuthSession, CancellationToken, Task<(string Token, DateTimeOffset Expiry, string NewRefreshToken)>> _refreshToken;
    private readonly SemaphoreSlim _lock = new(1, 1);

    internal OAuthSessionCredential(
        OAuthSession session,
        Func<OAuthSession, CancellationToken, Task<(string Token, DateTimeOffset Expiry, string NewRefreshToken)>> refreshToken)
    {
        _session = session;
        _refreshToken = refreshToken;
    }

    /// <inheritdoc/>
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        GetTokenAsync(requestContext, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        // Fast path: return cached token if valid for 5+ more minutes
        if (_session.SbAccessToken is not null &&
            DateTimeOffset.UtcNow < _session.SbTokenExpiry.AddMinutes(-5))
        {
            return new AccessToken(_session.SbAccessToken, _session.SbTokenExpiry);
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check inside lock
            if (_session.SbAccessToken is not null &&
                DateTimeOffset.UtcNow < _session.SbTokenExpiry.AddMinutes(-5))
            {
                return new AccessToken(_session.SbAccessToken, _session.SbTokenExpiry);
            }

            var (token, expiry, newRefreshToken) = await _refreshToken(_session, cancellationToken).ConfigureAwait(false);
            _session.SbAccessToken = token;
            _session.SbTokenExpiry = expiry;

            if (!string.IsNullOrEmpty(newRefreshToken))
                _session.RefreshToken = newRefreshToken;

            return new AccessToken(token, expiry);
        }
        finally
        {
            _lock.Release();
        }
    }
}
