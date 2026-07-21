using System.Text.Json.Serialization;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.Webhooks;

/// <summary>
/// Slack Incoming Webhook format — a Block Kit <c>blocks</c> array, matching the "🚨 orders-dlq
/// +340 messages, top error: PaymentTimeout — [Investigate]" style alert card doc 15's product
/// review named as the re-engagement/retention loop, in place of the unreadable raw JSON the
/// generic format sends today.
/// </summary>
public sealed class SlackWebhookFormatter : IWebhookMessageFormatter
{
    /// <inheritdoc />
    public WebhookFormat Format => WebhookFormat.Slack;

    /// <inheritdoc />
    public object BuildDlqSpikePayload(DlqSpikeNotification n)
    {
        var blocks = new List<object>
        {
            new SlackHeaderBlock("🚨 DLQ Spike Detected"),
            new SlackSectionBlock(new[]
            {
                new SlackTextField($"*Namespace:*\n{n.NamespaceName}"),
                new SlackTextField($"*New Messages:*\n{n.NewMessageCount} (threshold: {n.Threshold})"),
            }),
            new SlackContextBlock($"Detected {n.DetectedAtUtc:yyyy-MM-dd HH:mm} UTC"),
        };

        if (n.InvestigateUrl is not null)
            blocks.Add(new SlackActionsBlock("Investigate", n.InvestigateUrl));

        return new SlackMessage(
            Text: $"🚨 {n.NamespaceName}: +{n.NewMessageCount} DLQ messages",
            Blocks: blocks);
    }

    /// <inheritdoc />
    public object BuildBulkOperationCompletedPayload(BulkOperationCompletedNotification n)
    {
        var (emoji, headline) = n.Status switch
        {
            BulkOperationStatus.Completed => ("✅", "Bulk operation completed"),
            BulkOperationStatus.CompletedWithErrors => ("⚠️", "Bulk operation completed with errors"),
            BulkOperationStatus.Failed => ("❌", "Bulk operation failed"),
            BulkOperationStatus.Cancelled => ("⏹️", "Bulk operation cancelled"),
            _ => ("ℹ️", "Bulk operation finished"),
        };

        var blocks = new List<object>
        {
            new SlackHeaderBlock($"{emoji} {headline}"),
            new SlackSectionBlock(new[]
            {
                new SlackTextField($"*Operation:*\n{n.OperationType} — {n.NamespaceName}"),
                new SlackTextField($"*Result:*\n{n.SuccessCount} succeeded, {n.FailureCount} failed, {n.SkippedCount} skipped (of {n.TotalMatched})"),
            }),
            new SlackContextBlock($"Completed {n.CompletedAtUtc:yyyy-MM-dd HH:mm} UTC"),
        };

        if (n.InvestigateUrl is not null)
            blocks.Add(new SlackActionsBlock("View DLQ History", n.InvestigateUrl));

        return new SlackMessage(
            Text: $"{emoji} Bulk {n.OperationType.ToString().ToLowerInvariant()} on {n.NamespaceName}: {n.SuccessCount}/{n.TotalMatched} succeeded",
            Blocks: blocks);
    }

    // ── Slack Block Kit shapes (Incoming Webhook payload) ───────────────────

    private sealed record SlackMessage(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("blocks")] IReadOnlyList<object> Blocks);

    private sealed record SlackHeaderBlock(
        [property: JsonPropertyName("text")] SlackPlainText Text)
    {
        [JsonPropertyName("type")]
        public string Type => "header";

        public SlackHeaderBlock(string text) : this(new SlackPlainText(text)) { }
    }

    private sealed record SlackPlainText(
        [property: JsonPropertyName("text")] string Text)
    {
        [JsonPropertyName("type")]
        public string Type => "plain_text";
    }

    private sealed record SlackSectionBlock(
        [property: JsonPropertyName("fields")] IReadOnlyList<SlackTextField> Fields)
    {
        [JsonPropertyName("type")]
        public string Type => "section";
    }

    private sealed record SlackTextField(
        [property: JsonPropertyName("text")] string Text)
    {
        [JsonPropertyName("type")]
        public string Type => "mrkdwn";
    }

    private sealed record SlackContextBlock(
        [property: JsonPropertyName("elements")] IReadOnlyList<SlackTextField> Elements)
    {
        [JsonPropertyName("type")]
        public string Type => "context";

        public SlackContextBlock(string text) : this([new SlackTextField(text)]) { }
    }

    private sealed record SlackActionsBlock(
        [property: JsonPropertyName("elements")] IReadOnlyList<SlackButtonElement> Elements)
    {
        [JsonPropertyName("type")]
        public string Type => "actions";

        public SlackActionsBlock(string label, string url) : this([new SlackButtonElement(label, url)]) { }
    }

    private sealed record SlackButtonElement(
        [property: JsonPropertyName("text")] SlackPlainText Text,
        [property: JsonPropertyName("url")] string Url)
    {
        [JsonPropertyName("type")]
        public string Type => "button";

        public SlackButtonElement(string label, string url) : this(new SlackPlainText(label), url) { }
    }
}
