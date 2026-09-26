namespace ServiceHub.Api.Security;

/// <summary>
/// The header that says a caller meant a dangerous request. A delete cannot be triggered by a stray
/// prefetch, a link crawler or a URL pasted out of DevTools, because none of them send it.
/// </summary>
/// <remarks>Not authentication — it closes the common accident, not a determined attacker.</remarks>
public static class IntentHeaders
{
    /// <summary>The header's name; the SPA sends the same one (<c>lib/api/intentHeaders.ts</c>).</summary>
    public const string HeaderName = "X-ServiceHub-Intent";

    /// <summary>Connecting a namespace stores a credential.</summary>
    public const string CreateNamespace = "create-namespace";

    /// <summary>Removing a namespace forgets a credential and everything watched through it.</summary>
    public const string DeleteNamespace = "delete-namespace";

    /// <summary>Intent for replaying one dead letter.</summary>
    public const string ReplayMessage = "replay-message";

    /// <summary>Intent for starting a bulk replay from a preview.</summary>
    public const string BulkReplay = "bulk-replay";

    /// <summary>
    /// Intent for looking at a namespace's dead letters now. Where a cloud has no repeatable peek the look is a
    /// receive, which counts as a delivery attempt — so it is never triggered by a prefetch.
    /// </summary>
    public const string LookAtDeadLetters = "look-at-dead-letters";

    /// <summary>Intent for pausing an agent — it will not run its cycles until resumed.</summary>
    public const string PauseAgent = "pause-agent";

    /// <summary>Intent for resuming a paused agent — it gives back authority, so it too must be meant.</summary>
    public const string ResumeAgent = "resume-agent";

    /// <summary>Intent for approving what an agent asked — it replays, through the one gated route.</summary>
    public const string ApproveEscalation = "approve-escalation";

    /// <summary>Intent for declining what an agent asked — recorded with the person's name and reason.</summary>
    public const string DeclineEscalation = "decline-escalation";

    /// <summary>Intent for granting a governance role.</summary>
    public const string GrantRole = "grant-role";

    /// <summary>Intent for revoking a governance role.</summary>
    public const string RevokeRole = "revoke-role";

    /// <summary>Whether the request declared exactly <paramref name="expected"/>.</summary>
    public static bool Declares(HttpRequest request, string expected)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Headers.TryGetValue(HeaderName, out var values)
            && string.Equals(values.ToString().Trim(), expected, StringComparison.Ordinal);
    }

    /// <summary>The sentence a caller reads when the header is missing: it names the fix.</summary>
    public static string MissingDetail(string action, string expected) =>
        $"Confirm you meant to {action} by sending the header '{HeaderName}: {expected}'.";
}
