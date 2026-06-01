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

    EnvironmentType Environment = EnvironmentType.Dev) : IValidatableObject
{
    /// <summary>
    /// Gets the cloud provider hosting this namespace. Defaults to Azure for backward compatibility.
    /// </summary>
    public CloudProviderType Provider { get; init; } = CloudProviderType.Azure;

    /// <summary>
    /// Gets the AWS region identifier (e.g., <c>us-east-1</c>).
    /// Only relevant when <see cref="Provider"/> is <see cref="CloudProviderType.Aws"/>.
    /// </summary>
    [StringLength(30, ErrorMessage = "AwsRegion cannot exceed 30 characters")]
    public string? AwsRegion { get; init; }

    /// <summary>
    /// Gets the GCP project identifier.
    /// Only relevant when <see cref="Provider"/> is <see cref="CloudProviderType.Gcp"/>.
    /// </summary>
    [StringLength(30, ErrorMessage = "GcpProjectId cannot exceed 30 characters")]
    public string? GcpProjectId { get; init; }

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Provider == CloudProviderType.Aws)
        {
            if (string.IsNullOrWhiteSpace(AwsRegion))
            {
                yield return new ValidationResult(
                    "AwsRegion is required when Provider is Aws. Example: us-east-1",
                    [nameof(AwsRegion)]);
            }
            else if (!IsValidAwsRegion(AwsRegion))
            {
                yield return new ValidationResult(
                    "AwsRegion format is invalid. Expected format: us-east-1, eu-west-2, ap-southeast-1",
                    [nameof(AwsRegion)]);
            }
        }

        if (Provider == CloudProviderType.Gcp)
        {
            if (string.IsNullOrWhiteSpace(GcpProjectId))
            {
                yield return new ValidationResult(
                    "GcpProjectId is required when Provider is Gcp. Example: my-project-123",
                    [nameof(GcpProjectId)]);
            }
            else if (!IsValidGcpProjectId(GcpProjectId))
            {
                yield return new ValidationResult(
                    "GcpProjectId format is invalid. Must be 6\u201330 characters, lowercase letters, digits, and hyphens only, starting with a letter.",
                    [nameof(GcpProjectId)]);
            }
        }

        if (Provider == CloudProviderType.Azure
            && AuthType != ConnectionAuthType.ManagedIdentity
            && AuthType != ConnectionAuthType.ServicePrincipal
            && AuthType != ConnectionAuthType.DefaultAzureCredential
            && string.IsNullOrWhiteSpace(ConnectionString))
        {
            yield return new ValidationResult(
                "ConnectionString is required for Azure namespaces unless using Managed Identity.",
                [nameof(ConnectionString)]);
        }
    }

    // AWS region pattern: 2-3 lowercase letters, hyphen, direction/number, hyphen, digit
    // Examples: us-east-1, eu-west-2, ap-southeast-1, ca-central-1
    private static bool IsValidAwsRegion(string region) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            region,
            @"^[a-z]{2,3}-(?:east|west|north|south|central|northeast|northwest|southeast|southwest|gov)-\d$",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromMilliseconds(100));

    // GCP project ID: 6-30 chars, lowercase letters/digits/hyphens, starts with letter, ends with letter/digit
    private static bool IsValidGcpProjectId(string projectId) =>
        projectId.Length >= 6
        && projectId.Length <= 30
        && System.Text.RegularExpressions.Regex.IsMatch(
            projectId,
            @"^[a-z][a-z0-9\-]{4,28}[a-z0-9]$",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromMilliseconds(100));
}

