namespace ServiceHub.Core.Enums;

/// <summary>The kind of place a message is dead-lettered from. The name is 4.0.0's; it covers all three clouds.</summary>
public enum ServiceBusEntityType
{
    /// <summary>A queue.</summary>
    Queue = 0,

    /// <summary>A topic subscription (or the AWS/GCP equivalent).</summary>
    Subscription = 1,
}
