using System.Collections.Concurrent;

namespace ServiceHub.Infrastructure.OAuth;

/// <summary>
/// Thread-safe in-memory store for OAuth sessions and pending PKCE states.
/// Sessions survive for 8 hours; PKCE states expire after 10 minutes.
/// Note: sessions are lost on server restart — users simply re-authenticate.
/// </summary>
internal sealed class InMemoryOAuthStore
{
    private readonly ConcurrentDictionary<string, OAuthSession> _sessions = new();
    private readonly ConcurrentDictionary<string, PkceState> _pkceStates = new();

    // ── Session management ────────────────────────────────────────────────────

    /// <summary>Stores a newly created session.</summary>
    internal void StoreSession(OAuthSession session) =>
        _sessions[session.SessionId] = session;

    /// <summary>
    /// Retrieves a session by ID. Returns null if not found or expired.
    /// Automatically cleans up expired sessions.
    /// </summary>
    internal OAuthSession? GetSession(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return null;

        if (session.IsExpired)
        {
            _sessions.TryRemove(sessionId, out _);
            return null;
        }

        return session;
    }

    /// <summary>Removes a session (sign-out or revocation).</summary>
    internal void RemoveSession(string sessionId) =>
        _sessions.TryRemove(sessionId, out _);

    // ── PKCE state management ─────────────────────────────────────────────────

    /// <summary>
    /// Stores a pending PKCE state → code verifier mapping with a 10-minute TTL.
    /// Called before redirecting the user to Azure sign-in.
    /// </summary>
    internal void StorePkceState(string state, string codeVerifier) =>
        _pkceStates[state] = new PkceState(codeVerifier, DateTimeOffset.UtcNow.AddMinutes(10));

    /// <summary>
    /// Atomically retrieves and removes the code verifier for a state token.
    /// Returns null if the state is not found or has expired.
    /// One-time use prevents replay attacks.
    /// </summary>
    internal string? GetAndConsumePkceVerifier(string state)
    {
        if (!_pkceStates.TryRemove(state, out var pkce))
            return null;

        if (DateTimeOffset.UtcNow > pkce.Expiry)
            return null;

        return pkce.CodeVerifier;
    }

    // ── Background cleanup ────────────────────────────────────────────────────

    /// <summary>
    /// Removes all expired sessions and PKCE states.
    /// Suitable for periodic background calls to prevent unbounded memory growth.
    /// </summary>
    internal void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var (key, session) in _sessions)
        {
            if (session.IsExpired)
                _sessions.TryRemove(key, out _);
        }

        foreach (var (key, pkce) in _pkceStates)
        {
            if (now > pkce.Expiry)
                _pkceStates.TryRemove(key, out _);
        }
    }
}
