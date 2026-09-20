using System.Text.Json.Serialization;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.Webhooks;

/// <summary>
/// Microsoft Teams Incoming Webhook format — the legacy <c>MessageCard</c> schema
/// (<c>http://schema.org/extensions</c>). Chosen over Adaptive Cards because it's the format
/// Teams' built-in "Incoming Webhook" connector accepts directly with no extra envelope, keeping
/// the operator setup to "paste a URL" — the same one-field setup the generic/Slack formats use.
/// </summary>
public sealed class TeamsWebhookFormatter : IWebhookMessageFormatter
{
    private const string DlqSpikeColor = "D32F2F"; // red
    private const string SuccessColor = "2E7D32"; // green
    private const string WarningColor = "F9A825"; // amber
    private const string ErrorColor = "D32F2F"; // red
    private const string NeutralColor = "757575"; // gray

    /// <inheritdoc />
    public WebhookFormat Format => WebhookFormat.Teams;

    /// <inheritdoc />
    public object BuildDlqSpikePayload(DlqSpikeNotification n)
    {
        var facts = new List<TeamsFact>
        {
            new("Namespace", n.NamespaceName),
            new("New Messages", $"{n.NewMessageCount} (threshold: {n.Threshold})"),
            new("Detected", $"{n.DetectedAtUtc:yyyy-MM-dd HH:mm} UTC"),
        };

        return new TeamsMessageCard(
            Summary: "DLQ Spike Detected",
            ThemeColor: DlqSpikeColor,
            Title: "🚨 DLQ Spike Detected",
            Sections: [new TeamsSection(facts)],
            PotentialAction: n.InvestigateUrl is null ? null : [TeamsOpenUriAction.To("Investigate", n.InvestigateUrl)]);
    }

    /// <inheritdoc />
    public object BuildBulkOperationCompletedPayload(BulkOperationCompletedNotification n)
    {
        var (color, headline) = n.Status switch
        {
            BulkOperationStatus.Completed => (SuccessColor, "✅ Bulk operation completed"),
            BulkOperationStatus.CompletedWithErrors => (WarningColor, "⚠️ Bulk operation completed with errors"),
            BulkOperationStatus.Failed => (ErrorColor, "❌ Bulk operation failed"),
            BulkOperationStatus.Cancelled => (NeutralColor, "⏹️ Bulk operation cancelled"),
            _ => (NeutralColor, "Bulk operation finished"),
        };

        var facts = new List<TeamsFact>
        {
            new("Operation", $"{n.OperationType} — {n.NamespaceName}"),
            new("Succeeded", n.SuccessCount.ToString()),
            new("Failed", n.FailureCount.ToString()),
            new("Skipped", n.SkippedCount.ToString()),
            new("Total Matched", n.TotalMatched.ToString()),
            new("Completed", $"{n.CompletedAtUtc:yyyy-MM-dd HH:mm} UTC"),
        };

        return new TeamsMessageCard(
            Summary: headline,
            ThemeColor: color,
            Title: headline,
            Sections: [new TeamsSection(facts)],
            PotentialAction: n.InvestigateUrl is null ? null : [TeamsOpenUriAction.To("View DLQ History", n.InvestigateUrl)]);
    }

    /// <inheritdoc />
    public object BuildAutonomyTransitionPayload(AutonomyTransitionNotification n)
    {
        var isPromotion = n.NewLevel > n.PreviousLevel;
        var (color, headline) = isPromotion
            ? (SuccessColor, "⬆️ Autonomy grant promoted")
            : (WarningColor, "⬇️ Autonomy grant demoted");

        var facts = new List<TeamsFact>
        {
            new("Signature", n.SignatureHash),
            new("Transition", $"{n.PreviousLevel} → {n.NewLevel}"),
            new("Reason", n.Reason),
            new("Transitioned", $"{n.TransitionedAtUtc:yyyy-MM-dd HH:mm} UTC"),
        };

        return new TeamsMessageCard(
            Summary: headline,
            ThemeColor: color,
            Title: headline,
            Sections: [new TeamsSection(facts)],
            PotentialAction: n.InvestigateUrl is null ? null : [TeamsOpenUriAction.To("View Signature", n.InvestigateUrl)]);
    }

    /// <inheritdoc />
    public object BuildCircuitBreakerTrippedPayload(CircuitBreakerTrippedNotification n)
    {
        var facts = new List<TeamsFact>
        {
            new("Rule", n.RuleName),
            new("Verified Success Rate", $"{n.VerifiedSuccessRate:P0} over last {n.SampleSize} outcomes"),
            new("Tripped", $"{n.TrippedAtUtc:yyyy-MM-dd HH:mm} UTC"),
        };

        return new TeamsMessageCard(
            Summary: "Circuit breaker tripped",
            ThemeColor: ErrorColor,
            Title: "🛑 Circuit breaker tripped",
            Sections: [new TeamsSection(facts)],
            PotentialAction: n.InvestigateUrl is null ? null : [TeamsOpenUriAction.To("View Rules", n.InvestigateUrl)]);
    }

    /// <inheritdoc />
    public object BuildInsightDetectedPayload(InsightDetectedNotification n)
    {
        var (emoji, color) = n.Kind switch
        {
            InsightKind.Anomaly => ("🔎", WarningColor),
            InsightKind.Drift => ("📐", WarningColor),
            InsightKind.Correlation => ("🔗", WarningColor),
            InsightKind.Narration => ("📝", NeutralColor),
            _ => ("ℹ️", NeutralColor),
        };

        var scope = n.NamespaceName is not null
            ? n.EntityName is not null ? $"{n.EntityName} — {n.NamespaceName}" : n.NamespaceName
            : "cross-namespace";

        var headline = $"{emoji} {n.Kind} detected";

        var facts = new List<TeamsFact>
        {
            new("Scope", scope),
            new("Severity", $"{n.Severity}/100"),
            new("Description", n.Description),
            new("Detected", $"{n.DetectedAtUtc:yyyy-MM-dd HH:mm} UTC"),
        };

        return new TeamsMessageCard(
            Summary: headline,
            ThemeColor: color,
            Title: headline,
            Sections: [new TeamsSection(facts)],
            PotentialAction: n.InvestigateUrl is null ? null : [TeamsOpenUriAction.To("Investigate", n.InvestigateUrl)]);
    }

    // ── Teams MessageCard shapes ─────────────────────────────────────────────

    private sealed record TeamsMessageCard(
        [property: JsonPropertyName("summary")] string Summary,
        [property: JsonPropertyName("themeColor")] string ThemeColor,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("sections")] IReadOnlyList<TeamsSection> Sections,
        [property: JsonPropertyName("potentialAction")] IReadOnlyList<TeamsOpenUriAction>? PotentialAction)
    {
        [JsonPropertyName("@type")]
        public string Type => "MessageCard";

        [JsonPropertyName("@context")]
        public string Context => "http://schema.org/extensions";
    }

    private sealed record TeamsSection(
        [property: JsonPropertyName("facts")] IReadOnlyList<TeamsFact> Facts);

    private sealed record TeamsFact(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("value")] string Value);

    private sealed record TeamsOpenUriAction(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("targets")] IReadOnlyList<TeamsTarget> Targets)
    {
        [JsonPropertyName("@type")]
        public string Type => "OpenUri";

        public static TeamsOpenUriAction To(string name, string url) => new(name, [new TeamsTarget("default", url)]);
    }

    private sealed record TeamsTarget(
        [property: JsonPropertyName("os")] string Os,
        [property: JsonPropertyName("uri")] string Uri);
}
