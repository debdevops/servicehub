using System.Text.RegularExpressions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// Seven regex / keyword patterns that fire when the deterministic classifier
/// finds no match. Confidence is intentionally lower (0.50 – 0.85).
/// </summary>
internal static partial class HeuristicAnalyser
{
    internal sealed record Hit(FailureCategory Category, double Confidence, string RootCause);

    // Pre-compiled regexes via source generator
    [GeneratedRegex(@"timeout|timed\s*out", RegexOptions.IgnoreCase | RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex TimeoutPattern();

    [GeneratedRegex(@"schema|validation|invalid\s+format", RegexOptions.IgnoreCase | RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex SchemaPattern();

    [GeneratedRegex(@"unauthorized|forbidden|permission|access\s+denied", RegexOptions.IgnoreCase | RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex AuthPattern();

    [GeneratedRegex(@"not\s*found|404|resource\s+missing", RegexOptions.IgnoreCase | RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex NotFoundPattern();

    [GeneratedRegex(@"quota|size\s+exceeded|too\s+large|entity\s+full", RegexOptions.IgnoreCase | RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex QuotaPattern();

    [GeneratedRegex(@"database|sqlexception|connection\s+refused|service\s+unavailable|transient", RegexOptions.IgnoreCase | RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex TransientPattern();

    [GeneratedRegex(@"exception|error|failed|processing", RegexOptions.IgnoreCase | RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex GenericErrorPattern();

    /// <summary>
    /// Runs all seven heuristic patterns against the message signals.
    /// </summary>
    internal static Hit? Evaluate(DlqMessage msg)
    {
        var text = SignalExtractor.CombinedText(msg);

        // Pattern 1 – Timeout / timed-out
        if (TimeoutPattern().IsMatch(text))
            return new Hit(FailureCategory.Transient, 0.80,
                "Heuristic: timeout-related keywords detected in error text.");

        // Pattern 2 – Schema / validation
        if (SchemaPattern().IsMatch(text))
            return new Hit(FailureCategory.DataQuality, 0.78,
                "Heuristic: schema or validation keywords found.");

        // Pattern 3 – Auth keywords
        if (AuthPattern().IsMatch(text))
            return new Hit(FailureCategory.Authorization, 0.82,
                "Heuristic: authorisation-related keywords detected.");

        // Pattern 4 – Not found / 404
        if (NotFoundPattern().IsMatch(text))
            return new Hit(FailureCategory.ResourceNotFound, 0.75,
                "Heuristic: resource-not-found indicators detected.");

        // Pattern 5 – Quota / size
        if (QuotaPattern().IsMatch(text))
            return new Hit(FailureCategory.QuotaExceeded, 0.78,
                "Heuristic: quota or size-exceeded keywords found.");

        // Pattern 6 – Transient / infra
        if (TransientPattern().IsMatch(text))
            return new Hit(FailureCategory.Transient, 0.70,
                "Heuristic: transient infrastructure keywords detected.");

        // Pattern 7 – Generic errors (catch-all, low confidence)
        if (GenericErrorPattern().IsMatch(text))
            return new Hit(FailureCategory.ProcessingError, 0.50,
                "Heuristic: generic error keywords — manual review recommended.");

        return null;
    }
}
