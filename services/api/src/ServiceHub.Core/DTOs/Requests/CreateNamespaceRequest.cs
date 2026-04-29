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
/// <param name="Environment">The deployment environment (Dev, Uat, Prod). Defaults to Dev.</param>
public sealed record CreateNamespaceRequest(
    [Required(ErrorMessage = "Namespace name is required")]
    [StringLength(256, MinimumLength = 6, ErrorMessage = "Namespace name must be between 6 and 256 characters")]
    [RegularExpression(@"^[a-zA-Z][a-zA-Z0-9-]*(\.[a-zA-Z][a-zA-Z0-9-]*)*$", ErrorMessage = "Namespace name must start with a letter and contain only letters, numbers, hyphens, and dots")]
    string Name,
    
    [StringLength(4096, ErrorMessage = "Connection string cannot exceed 4096 characters")]
    string? ConnectionString,
    
    ConnectionAuthType AuthType,
    
    [StringLength(100, ErrorMessage = "Display name cannot exceed 100 characters")]
    string? DisplayName = null,
    
    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    string? Description = null,
    
    EnvironmentType Environment = EnvironmentType.Dev)
{
    /// <summary>
    /// Gets the cloud provider hosting this namespace. Defaults to Azure for backward compatibility.
    /// </summary>
    public CloudProviderType Provider { get; init; } = CloudProviderType.Azure;

    /// <summary>
    /// Gets the AWS region identifier (e.g., <c>us-east-1</c>).
    /// Only relevant when <see cref="Provider"/> is <see cref="CloudProviderType.Aws"/>.
    /// </summary>
    public string? AwsRegion { get; init; }

    /// <summary>
    /// Gets the GCP project identifier.
    /// Only relevant when <see cref="Provider"/> is <see cref="CloudProviderType.Gcp"/>.
    /// </summary>
    public string? GcpProjectId { get; init; }
}

