namespace ServiceHub.Core.Enums;

/// <summary>
/// Represents the type of Service Bus entity (queue or subscription).
/// </summary>
public enum ServiceBusEntityType
{
    /// <summary>Azure Service Bus Queue.</summary>
    Queue = 0,

    /// <summary>Azure Service Bus Topic Subscription.</summary>
    Subscription = 1
}
