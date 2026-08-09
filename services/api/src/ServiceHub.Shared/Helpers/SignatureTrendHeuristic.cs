namespace ServiceHub.Shared.Helpers;

/// <summary>
/// Computes a light, deterministic "is this getting worse?" label for a failure signature.
/// Deliberately not predictive analytics (out of scope) — just a heuristic comparison of
/// how recently the signature last recurred relative to its own observed lifetime.
/// </summary>
public static class SignatureTrendHeuristic
{
    /// <summary>
    /// Returns "New" for a first-observation or single-occurrence signature, "Escalating" when
    /// a signature with 3+ occurrences last recurred within the most recent fifth of its own
    /// lifetime (still actively firing), or "Recurring" otherwise.
    /// </summary>
    public static string Compute(
        bool isNew, int occurrenceCount, DateTimeOffset firstSeenAt, DateTimeOffset lastSeenAt, DateTimeOffset now)
    {
        if (isNew || occurrenceCount <= 1)
            return "New";

        var age = now - firstSeenAt;
        var sinceLastSeen = now - lastSeenAt;

        if (occurrenceCount >= 3 && age > TimeSpan.Zero && sinceLastSeen <= age / 5)
            return "Escalating";

        return "Recurring";
    }
}
