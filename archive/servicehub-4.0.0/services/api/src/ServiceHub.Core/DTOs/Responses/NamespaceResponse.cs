using ServiceHub.Core.Enums;

namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// Response DTO for namespace information.
/// </summary>
/// <param name="Id">The unique identifier of the namespace.</param>
/// <param name="Name">The fully qualified namespace name.</param>
/// <param name="DisplayName">The display name of the namespace.</param>
/// <param name="Description">The description of the namespace.</param>
/// <param name="AuthType">The authentication type used.</param>
/// <param name="IsActive">Whether the namespace is active.</param>
/// <param name="CreatedAt">When the namespace was created.</param>
/// <param name="ModifiedAt">When the namespace was last modified.</param>
/// <param name="LastConnectionTestAt">When the connection was last tested.</param>
/// <param name="LastConnectionTestSucceeded">Whether the last connection test succeeded.</param>
/// <param name="HasListenPermission">Whether the connection has Listen permission.</param>
/// <param name="HasSendPermission">Whether the connection has Send permission.</param>
/// <param name="HasManagePermission">Whether the connection has Manage permission.</param>
/// <param name="Environment">The deployment environment (Dev, Uat, Prod).</param>
public sealed record NamespaceResponse(
    Guid Id,
    string Name,
    string? DisplayName,
    string? Description,
    ConnectionAuthType AuthType,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt,
    DateTimeOffset? LastConnectionTestAt,
    bool? LastConnectionTestSucceeded,
    bool HasListenPermission,
    bool HasSendPermission,
    bool HasManagePermission,
    EnvironmentType Environment)
{
    /// <summary>
    /// Gets the cloud provider that hosts this namespace. Defaults to Azure for backward compatibility.
    /// </summary>
    public CloudProviderType Provider { get; init; } = CloudProviderType.Azure;

    /// <summary>
    /// Gets the AWS region identifier for AWS-backed namespaces.
    /// Null for non-AWS providers.
    /// </summary>
    public string? AwsRegion { get; init; }

    /// <summary>
    /// Gets the GCP project identifier for GCP-backed namespaces.
    /// Null for non-GCP providers.
    /// </summary>
    public string? GcpProjectId { get; init; }

    /// <summary>
    /// Owner IDs this namespace has been explicitly shared with, in addition to its owner.
    /// Only populated for the true owner's own view of a namespace they own — see
    /// <c>NamespacesController</c>. Empty for a namespace the caller can see only because it
    /// was shared with them.
    /// </summary>
    public IReadOnlyList<string> SharedWithOwnerIds { get; init; } = [];

    /// <summary>
    /// True if the caller can see this namespace only because its owner shared it with them,
    /// not because they own it — lets the UI distinguish "mine" from "shared with me".
    /// </summary>
    public bool IsSharedWithMe { get; init; }
}
