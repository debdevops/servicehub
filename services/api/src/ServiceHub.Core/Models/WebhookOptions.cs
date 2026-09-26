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

    /// <summary>
    /// The payload shape to send. Defaults to <see cref="WebhookFormat.Generic"/> — the
    /// original flat-JSON shape — so upgrading ServiceHub never changes an existing
    /// deployment's webhook payload unless the operator opts in to <c>Slack</c> or <c>Teams</c>.
    /// </summary>
    public WebhookFormat Format { get; set; } = WebhookFormat.Generic;

    /// <summary>
    /// Optional externally-reachable base URL for this ServiceHub instance (e.g.
    /// <c>https://servicehub.mycompany.com</c>), used to add an "Investigate" link/button to
    /// Slack and Teams notifications. ServiceHub is self-hosted and has no way to know its own
    /// public URL automatically, so this is opt-in — notifications are still sent without it,
    /// just without the deep link.
    /// </summary>
    public string? PublicUrl { get; set; }
}
