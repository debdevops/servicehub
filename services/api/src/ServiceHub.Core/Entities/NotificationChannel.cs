using ServiceHub.Core.Models;

namespace ServiceHub.Core.Entities;

/// <summary>
/// A place escalations are delivered besides the in-app bell (unit 6.3): a Slack, Teams or generic webhook set up from
/// Settings. The URL is a secret — anyone holding it can post into the channel — so it is stored encrypted, like a
/// connection string, and never returned by the API.
/// </summary>
public sealed class NotificationChannel
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Owner, for isolation.</summary>
    public required string OwnerId { get; init; }

    /// <summary>Slack, Teams or Generic — decides the message shape.</summary>
    public required WebhookFormat Format { get; init; }

    /// <summary>What a person calls it, e.g. <c>#ops-alerts</c>. Shown in Settings; never used to send.</summary>
    public required string Label { get; set; }

    /// <summary>The webhook URL, encrypted at rest (<c>ENC[…]</c>).</summary>
    public required string UrlEncrypted { get; set; }

    /// <summary>Whether escalations are sent here.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>When it was set up.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Who set it up.</summary>
    public required string CreatedBy { get; init; }

    /// <summary>When a message last arrived there, as far as the channel said.</summary>
    public DateTimeOffset? LastDeliveredAt { get; set; }

    /// <summary>Why the last delivery failed, in the channel's words (redacted), or null.</summary>
    public string? LastError { get; set; }
}
