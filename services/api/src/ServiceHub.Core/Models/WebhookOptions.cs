namespace ServiceHub.Core.Models;

/// <summary>
/// Configuration options for the webhook notification system.
/// Bound from the "Webhooks" section of appsettings.json.
/// </summary>
public sealed class WebhookOptions
{
    /// <summary>Section name in configuration.</summary>
    public const string SectionName = "Webhooks";

    /// <summary>Whether webhook notifications are enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>The base URL to POST webhook payloads to.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Minimum number of new DLQ messages in a single scan cycle to trigger a notification.
    /// </summary>
    public int DlqSpikeThreshold { get; set; } = 10;

    /// <summary>
    /// Cooldown period in seconds between notifications for the same namespace.
    /// Prevents alert storms during sustained spikes.
    /// </summary>
    public int CooldownSeconds { get; set; } = 300;
}
