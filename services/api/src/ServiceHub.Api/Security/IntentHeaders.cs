namespace ServiceHub.Api.Security;

/// <summary>
/// Centralized explicit-intent header validation for risky operations.
/// </summary>
public static class IntentHeaders
{
    public const string IntentHeaderName = "X-ServiceHub-Intent";
    public const string ConfirmHeaderName = "X-ServiceHub-Confirm";

    public const string IntentReplayMessage = "messages:replay";
    public const string IntentPurgeMessage = "messages:purge";
    public const string IntentSendMessage = "messages:send";
    public const string IntentDeadLetter = "messages:deadletter";
    public const string IntentCancelScheduled = "messages:cancel-scheduled";
    public const string IntentDeleteNamespace = "namespaces:delete";
    public const string IntentShareNamespace = "namespaces:share";
    public const string IntentReplayAllRules = "rules:replay-all";
    public const string IntentBulkReplay = "bulk:replay";
    public const string IntentBulkPurge = "bulk:purge";
    public const string IntentSignatureReplay = "signature:replay";
    public const string IntentPurgeAuditLogs = "audit:purge";

    /// <summary>
    /// Validates that the caller supplied explicit intent headers for a risky operation.
    /// </summary>
    public static bool HasExplicitIntent(HttpContext context, string expectedIntent)
    {
        if (!context.Request.Headers.TryGetValue(IntentHeaderName, out var intentValues))
        {
            return false;
        }

        if (!context.Request.Headers.TryGetValue(ConfirmHeaderName, out var confirmValues))
        {
            return false;
        }

        var providedIntent = intentValues.ToString().Trim();
        var confirmed = bool.TryParse(confirmValues.ToString(), out var parsed) && parsed;

        return confirmed && string.Equals(providedIntent, expectedIntent, StringComparison.Ordinal);
    }

    /// <summary>
    /// Standard problem-detail message for missing explicit-intent headers.
    /// </summary>
    public static string BuildIntentRequiredDetail(string operationDescription)
    {
        return $"Explicit user intent is required for {operationDescription}. " +
               $"Include headers '{IntentHeaderName}' and '{ConfirmHeaderName}: true'.";
    }
}
