using System.ComponentModel.DataAnnotations;
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
    [Required(ErrorMessage = "Namespace name is required")]
    [StringLength(256, MinimumLength = 6, ErrorMessage = "Namespace name must be between 6 and 256 characters")]
    [RegularExpression(@"^[a-zA-Z][a-zA-Z0-9-]*$", ErrorMessage = "Namespace name must start with a letter and contain only letters, numbers, and hyphens")]
    string Name,
    
    [StringLength(2048, ErrorMessage = "Connection string cannot exceed 2048 characters")]
    string? ConnectionString,
    
    ConnectionAuthType AuthType,
    
    [StringLength(128, ErrorMessage = "Display name cannot exceed 128 characters")]
    string? DisplayName = null,
    
    [StringLength(512, ErrorMessage = "Description cannot exceed 512 characters")]
    string? Description = null);
