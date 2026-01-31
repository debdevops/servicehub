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
    bool HasManagePermission);
