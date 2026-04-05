namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// An Azure Service Bus namespace discovered via Azure Resource Manager (ARM).
/// Returned when the user lists their namespaces after signing in with Azure.
/// </summary>
/// <param name="Name">The Service Bus namespace short name (e.g. mybus).</param>
/// <param name="FullyQualifiedHostname">The FQNS hostname (e.g. mybus.servicebus.windows.net).</param>
/// <param name="SubscriptionId">Azure subscription ID that owns this namespace.</param>
/// <param name="ResourceGroup">Azure resource group containing this namespace.</param>
/// <param name="Location">Azure region (e.g. eastus).</param>
/// <param name="Sku">Service Bus tier (Basic, Standard, or Premium).</param>
public sealed record AzureNamespaceInfo(
    string Name,
    string FullyQualifiedHostname,
    string SubscriptionId,
    string ResourceGroup,
    string Location,
    string Sku);
