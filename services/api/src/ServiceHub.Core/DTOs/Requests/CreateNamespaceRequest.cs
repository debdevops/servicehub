using ServiceHub.Core.Enums;

namespace ServiceHub.Core.DTOs.Requests;

/// <summary>
/// Request DTO for creating a new namespace configuration.
/// </summary>
/// <param name="Name">The fully qualified namespace name or simple name.</param>
/// <param name="ConnectionString">The connection string with SAS credentials. Required for connection string auth.</param>
/// <param name="AuthType">The authentication type to use.</param>
/// <param name="DisplayName">Optional display name for the namespace.</param>
/// <param name="Description">Optional description for the namespace.</param>
public sealed record CreateNamespaceRequest(
    string Name,
    string? ConnectionString,
    ConnectionAuthType AuthType,
    string? DisplayName = null,
    string? Description = null);
