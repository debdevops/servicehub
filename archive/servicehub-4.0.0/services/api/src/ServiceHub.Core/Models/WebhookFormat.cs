namespace ServiceHub.Core.Models;

/// <summary>
/// The payload shape <see cref="Interfaces.IWebhookNotifier"/> sends to <see cref="WebhookOptions.Url"/>.
/// </summary>
public enum WebhookFormat
{
    /// <summary>
    /// Flat JSON matching the original (pre-Phase-2) payload shape exactly — the default, so
    /// existing deployments that already consume the generic payload see no behavior change.
    /// </summary>
    Generic = 0,

    /// <summary>Slack Incoming Webhook format (Block Kit <c>blocks</c> array).</summary>
    Slack = 1,

    /// <summary>Microsoft Teams Incoming Webhook format (legacy <c>MessageCard</c> schema).</summary>
    Teams = 2
}
