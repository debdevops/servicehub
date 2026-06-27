using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ServiceHub.Core.Enums;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Entities;

/// <summary>
/// Represents an Azure Service Bus namespace configuration.
/// This entity encapsulates the connection details and metadata for a Service Bus namespace.
/// </summary>
public sealed class Namespace
{
    /// <summary>
    /// Maximum allowed length for the namespace name.
    /// </summary>
    public const int MaxNameLength = 256;

    /// <summary>
    /// Maximum allowed length for the display name.
    /// </summary>
    public const int MaxDisplayNameLength = 100;

    /// <summary>
    /// Maximum allowed length for the description.
    /// </summary>
    public const int MaxDescriptionLength = 500;

    /// <summary>
    /// Gets the unique identifier for this namespace configuration.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the fully qualified namespace name (e.g., mynamespace.servicebus.windows.net).
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// Gets the optional display name for the namespace.
    /// </summary>
    public string? DisplayName { get; private set; }

    /// <summary>
    /// Gets the optional description for the namespace.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets the connection string for the namespace. Null if using managed identity.
    /// </summary>
    public string? ConnectionString { get; private set; }

    /// <summary>
    /// Gets the authentication type used for this namespace.
    /// </summary>
    public ConnectionAuthType AuthType { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this namespace configuration is active.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets the date and time when this namespace was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Gets the date and time when this namespace was last modified.
    /// </summary>
    public DateTimeOffset? ModifiedAt { get; private set; }

    /// <summary>
    /// Gets the date and time when the connection was last tested successfully.
    /// </summary>
    public DateTimeOffset? LastConnectionTestAt { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the last connection test was successful.
    /// </summary>
    public bool? LastConnectionTestSucceeded { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the connection has Listen permission.
    /// </summary>
    public bool HasListenPermission { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the connection has Send permission.
    /// </summary>
    public bool HasSendPermission { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the connection has Manage permission.
    /// </summary>
    public bool HasManagePermission { get; private set; }

    /// <summary>
    /// Gets the deployment environment for this namespace (Dev, Uat, Prod).
    /// Controls safety guards and feature availability.
    /// </summary>
    public EnvironmentType Environment { get; private set; }

    /// <summary>
    /// Gets the cloud provider that hosts this messaging namespace.
    /// Defaults to Azure for backward compatibility.
    /// </summary>
    public CloudProviderType Provider { get; private set; } = CloudProviderType.Azure;

    /// <summary>
    /// Gets the AWS region identifier for AWS-backed namespaces.
    /// Null for non-AWS providers.
    /// </summary>
    public string? AwsRegion { get; private set; }

    /// <summary>
    /// Gets the GCP project identifier for GCP-backed namespaces.
    /// Null for non-GCP providers.
    /// </summary>
    public string? GcpProjectId { get; private set; }

    /// <summary>
    /// Gets the owner identifier for this namespace.
    /// Used to enforce tenant isolation — each caller identity maps to a stable owner ID.
    /// SPA token sessions and admin keys share "__spa__"; scoped API keys get a key-derived ID.
    /// Defaults to "__spa__" for backward compatibility with data created before isolation was added.
    /// </summary>
    public string OwnerId { get; init; } = SpaOwnerId;

    /// <summary>
    /// Gets the SHA-256 hash of the plaintext connection string.
    /// Stored alongside the encrypted value to enable fast duplicate detection
    /// without decrypting all stored namespaces (O(1) hash lookup vs O(n) decryption).
    /// </summary>
    public string? ConnectionStringHash { get; private set; }

    /// <summary>
    /// The stable owner-ID used for SPA token sessions and admin API keys.
    /// All traffic authenticated via the instance-level SPA secret shares this owner scope.
    /// </summary>
    public const string SpaOwnerId = "__spa__";

    /// <summary>
    /// Compiled Regex for extracting SharedAccessKeyName from an Azure connection string.
    /// Static to avoid recompiling on every validation call.
    /// </summary>
    private static readonly Regex SharedAccessKeyNameRegex = new(
        @"SharedAccessKeyName=([^;]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Private constructor to enforce factory method usage.
    /// </summary>
    private Namespace()
    {
        Name = string.Empty;
    }

    /// <summary>
    /// Computes a SHA-256 hash of the given connection string for deduplication.
    /// The hash is safe to store alongside an encrypted connection string — it reveals
    /// nothing about the plaintext but allows exact-match comparison at O(1) cost.
    /// </summary>
    /// <param name="connectionString">The plaintext connection string.</param>
    /// <returns>Lowercase hex-encoded SHA-256 hash, or null if the input is null/empty.</returns>
    public static string? ComputeConnectionStringHash(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return null;

        var normalised = connectionString.Trim();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalised));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Creates a new namespace configuration using a connection string.
    /// </summary>
    /// <param name="name">The fully qualified namespace name.</param>
    /// <param name="connectionString">The connection string with SAS credentials.</param>
    /// <param name="displayName">Optional display name.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="environment">The deployment environment (defaults to Dev).</param>
    /// <param name="provider">The cloud provider hosting this namespace (defaults to Azure).</param>
    /// <param name="ownerId">The caller-identity owner ID for tenant isolation. Defaults to the SPA owner.</param>
    /// <param name="connectionStringHash">Pre-computed SHA-256 hash of the plaintext connection string for deduplication.</param>
    /// <param name="awsRegion">AWS region identifier for AWS namespaces.</param>
    /// <param name="gcpProjectId">GCP project identifier for GCP namespaces.</param>
    /// <returns>A result containing the namespace or validation errors.</returns>
    public static Result<Namespace> Create(
        string name,
        string connectionString,
        string? displayName = null,
        string? description = null,
        EnvironmentType environment = EnvironmentType.Dev,
        CloudProviderType provider = CloudProviderType.Azure,
        string? ownerId = null,
        string? connectionStringHash = null,
        string? awsRegion = null,
        string? gcpProjectId = null)
    {
        var validationResult = ValidateConnectionStringAuth(name, connectionString, displayName, description, provider);
        if (validationResult.IsFailure)
        {
            return Result<Namespace>.Failure(validationResult.Errors);
        }

        // Detect permissions from connection string
        var permissions = DetectConnectionStringPermissions(connectionString);

        var authType = provider switch
        {
            CloudProviderType.Aws => ConnectionAuthType.AwsAccessKey,
            CloudProviderType.Gcp => ConnectionAuthType.GcpServiceAccount,
            _ => ConnectionAuthType.ConnectionString,
        };

        var ns = new Namespace
        {
            Id = Guid.NewGuid(),
            Name = name.Trim().ToLowerInvariant(),
            ConnectionString = connectionString.Trim(),
            DisplayName = displayName?.Trim(),
            Description = description?.Trim(),
            AuthType = authType,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            HasListenPermission = permissions.HasListen,
            HasSendPermission = permissions.HasSend,
            HasManagePermission = permissions.HasManage,
            Environment = environment,
            Provider = provider,
            AwsRegion = awsRegion?.Trim(),
            GcpProjectId = gcpProjectId?.Trim(),
            OwnerId = ownerId ?? SpaOwnerId,
            ConnectionStringHash = connectionStringHash,
        };

        return Result<Namespace>.Success(ns);
    }

    /// <summary>
    /// Creates a new namespace configuration using managed identity authentication.
    /// </summary>
    /// <param name="name">The fully qualified namespace name.</param>
    /// <param name="authType">The authentication type (must be identity-based).</param>
    /// <param name="displayName">Optional display name.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="environment">The deployment environment (defaults to Dev).</param>
    /// <param name="provider">The cloud provider hosting this namespace (defaults to Azure).</param>
    /// <param name="ownerId">The caller-identity owner ID for tenant isolation. Defaults to the SPA owner.</param>
    /// <param name="awsRegion">AWS region identifier for AWS namespaces.</param>
    /// <param name="gcpProjectId">GCP project identifier for GCP namespaces.</param>
    /// <returns>A result containing the namespace or validation errors.</returns>
    public static Result<Namespace> CreateWithManagedIdentity(
        string name,
        ConnectionAuthType authType = ConnectionAuthType.ManagedIdentity,
        string? displayName = null,
        string? description = null,
        EnvironmentType environment = EnvironmentType.Dev,
        CloudProviderType provider = CloudProviderType.Azure,
        string? ownerId = null,
        string? awsRegion = null,
        string? gcpProjectId = null)
    {
        // Allowlist: only accept known managed-identity types; reject ConnectionString
        // and any future/unknown enum values to prevent user-controlled bypass.
        if (authType is not (ConnectionAuthType.ManagedIdentity
            or ConnectionAuthType.ServicePrincipal
            or ConnectionAuthType.DefaultAzureCredential))
        {
            return Result<Namespace>.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringRequired,
                "Authentication type must be ManagedIdentity, ServicePrincipal, or DefaultAzureCredential. Use Create() for connection string authentication."));
        }

        var validationResult = ValidateManagedIdentityAuth(name, displayName, description);
        if (validationResult.IsFailure)
        {
            return Result<Namespace>.Failure(validationResult.Errors);
        }

        var ns = new Namespace
        {
            Id = Guid.NewGuid(),
            Name = name.Trim().ToLowerInvariant(),
            ConnectionString = null,
            DisplayName = displayName?.Trim(),
            Description = description?.Trim(),
            AuthType = authType,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            // Managed identity typically has full permissions
            HasListenPermission = true,
            HasSendPermission = true,
            HasManagePermission = true,
            Environment = environment,
            Provider = provider,
            AwsRegion = awsRegion?.Trim(),
            GcpProjectId = gcpProjectId?.Trim(),
            OwnerId = ownerId ?? SpaOwnerId,
        };

        return Result<Namespace>.Success(ns);
    }

    /// <summary>
    /// Updates the display name of the namespace.
    /// </summary>
    /// <param name="displayName">The new display name.</param>
    /// <returns>A result indicating success or validation errors.</returns>
    public Result UpdateDisplayName(string? displayName)
    {
        if (displayName is not null && displayName.Length > MaxDisplayNameLength)
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.NameTooLong,
                $"Display name cannot exceed {MaxDisplayNameLength} characters."));
        }

        DisplayName = displayName?.Trim();
        ModifiedAt = DateTimeOffset.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Updates the description of the namespace.
    /// </summary>
    /// <param name="description">The new description.</param>
    /// <returns>A result indicating success or validation errors.</returns>
    public Result UpdateDescription(string? description)
    {
        if (description is not null && description.Length > MaxDescriptionLength)
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.NameTooLong,
                $"Description cannot exceed {MaxDescriptionLength} characters."));
        }

        Description = description?.Trim();
        ModifiedAt = DateTimeOffset.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Updates the connection string for the namespace.
    /// </summary>
    /// <param name="connectionString">The new connection string.</param>
    /// <returns>A result indicating success or validation errors.</returns>
    public Result UpdateConnectionString(string connectionString)
    {
        if (AuthType != ConnectionAuthType.ConnectionString)
        {
            return Result.Failure(Error.BusinessRule(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "Cannot update connection string for a namespace using managed identity authentication."));
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringRequired,
                "Connection string is required."));
        }

        if (!IsValidConnectionString(connectionString))
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "The connection string format is invalid."));
        }

        ConnectionString = connectionString.Trim();
        ModifiedAt = DateTimeOffset.UtcNow;
        LastConnectionTestAt = null;
        LastConnectionTestSucceeded = null;
        return Result.Success();
    }

    /// <summary>
    /// Records the result of a connection test.
    /// </summary>
    /// <param name="succeeded">Whether the connection test succeeded.</param>
    public void RecordConnectionTest(bool succeeded)
    {
        LastConnectionTestAt = DateTimeOffset.UtcNow;
        LastConnectionTestSucceeded = succeeded;
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Activates the namespace configuration.
    /// </summary>
    public void Activate()
    {
        if (!IsActive)
        {
            IsActive = true;
            ModifiedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Deactivates the namespace configuration.
    /// </summary>
    public void Deactivate()
    {
        if (IsActive)
        {
            IsActive = false;
            ModifiedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Validates parameters for connection string authentication.
    /// </summary>
    private static Result ValidateConnectionStringAuth(
        string name,
        string connectionString,
        string? displayName,
        string? description,
        CloudProviderType provider)
    {
        var errors = new List<Error>();

        // Validate name
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(Error.Validation(
                ErrorCodes.Namespace.NameRequired,
                "Namespace name is required."));
        }
        else if (name.Length > MaxNameLength)
        {
            errors.Add(Error.Validation(
                ErrorCodes.Namespace.NameTooLong,
                $"Namespace name cannot exceed {MaxNameLength} characters."));
        }
        else if (!IsValidNamespaceName(name))
        {
            errors.Add(Error.Validation(
                ErrorCodes.Namespace.NameInvalid,
                "Namespace name contains invalid characters or format."));
        }

        // Validate connection string
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            errors.Add(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringRequired,
                "Connection string is required."));
        }
        else if (provider == CloudProviderType.Azure && !IsValidConnectionString(connectionString) && !IsEncryptedConnectionString(connectionString))
        {
            errors.Add(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "The connection string format is invalid."));
        }

        // Validate display name
        if (displayName is not null && displayName.Length > MaxDisplayNameLength)
        {
            errors.Add(Error.Validation(
                ErrorCodes.Namespace.NameTooLong,
                $"Display name cannot exceed {MaxDisplayNameLength} characters."));
        }

        // Validate description
        if (description is not null && description.Length > MaxDescriptionLength)
        {
            errors.Add(Error.Validation(
                ErrorCodes.Namespace.NameTooLong,
                $"Description cannot exceed {MaxDescriptionLength} characters."));
        }

        return errors.Count > 0 ? Result.Failure(errors) : Result.Success();
    }

    /// <summary>
    /// Validates parameters for managed identity authentication.
    /// </summary>
    private static Result ValidateManagedIdentityAuth(
        string name,
        string? displayName,
        string? description)
    {
        var errors = new List<Error>();

        // Validate name
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(Error.Validation(
                ErrorCodes.Namespace.NameRequired,
                "Namespace name is required."));
        }
        else if (name.Length > MaxNameLength)
        {
            errors.Add(Error.Validation(
                ErrorCodes.Namespace.NameTooLong,
                $"Namespace name cannot exceed {MaxNameLength} characters."));
        }
        else if (!IsValidNamespaceName(name))
        {
            errors.Add(Error.Validation(
                ErrorCodes.Namespace.NameInvalid,
                "Namespace name contains invalid characters or format."));
        }

        // Validate display name
        if (displayName is not null && displayName.Length > MaxDisplayNameLength)
        {
            errors.Add(Error.Validation(
                ErrorCodes.Namespace.NameTooLong,
                $"Display name cannot exceed {MaxDisplayNameLength} characters."));
        }

        // Validate description
        if (description is not null && description.Length > MaxDescriptionLength)
        {
            errors.Add(Error.Validation(
                ErrorCodes.Namespace.NameTooLong,
                $"Description cannot exceed {MaxDescriptionLength} characters."));
        }

        return errors.Count > 0 ? Result.Failure(errors) : Result.Success();
    }

    /// <summary>
    /// Validates the namespace name format.
    /// Accepts:
    /// - Azure Service Bus FQDN: *.servicebus.windows.net (and national cloud variants)
    /// - Simple short name: 6–50 alphanumeric-plus-hyphen chars (Azure namespace short name)
    /// - AWS SQS URL: https://sqs.{region}.amazonaws.com/{account}/{queue}
    /// - AWS simple name: alphanumeric, hyphens, underscores (SQS queue name conventions)
    /// - GCP project-based format: letters, digits, hyphens, underscores (Pub/Sub project ID)
    /// </summary>
    private static bool IsValidNamespaceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var trimmed = name.Trim();

        // Azure FQDN — must end with a known servicebus suffix
        if (trimmed.Contains('.') && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.EndsWith(".servicebus.windows.net", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith(".servicebus.chinacloudapi.cn", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith(".servicebus.usgovcloudapi.net", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith(".servicebus.cloudapi.de", StringComparison.OrdinalIgnoreCase);
        }

        // AWS SQS endpoint URL: https://sqs.{region}.amazonaws.com/{account}/{queue-or-topic}
        if (trimmed.StartsWith("https://sqs.", StringComparison.OrdinalIgnoreCase)
            && trimmed.Contains(".amazonaws.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Generic short name: alphanumeric, hyphens, underscores, dots (covers GCP project IDs,
        // AWS simple names, and Azure short namespace names). Length 1–256.
        if (trimmed.Length >= 1 && trimmed.Length <= MaxNameLength)
        {
            return trimmed.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
                && !trimmed.StartsWith('-')
                && !trimmed.EndsWith('-');
        }

        return false;
    }

    /// <summary>
    /// Validates the connection string format.
    /// </summary>
    private static bool IsValidConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        // Basic validation: must contain Endpoint and SharedAccessKey or SharedAccessSignature
        var hasEndpoint = connectionString.Contains("Endpoint=", StringComparison.OrdinalIgnoreCase);
        var hasAuth = connectionString.Contains("SharedAccessKey=", StringComparison.OrdinalIgnoreCase) ||
                      connectionString.Contains("SharedAccessSignature=", StringComparison.OrdinalIgnoreCase);

        return hasEndpoint && hasAuth;
    }

    /// <summary>
    /// Detects permissions from the connection string based on SharedAccessKeyName.
    /// NOTE: This is a best-effort detection based on naming conventions. Azure does not enforce
    /// any naming pattern for SAS policies, so a policy with full permissions might be named anything.
    /// We only flag as limited permissions if the key name explicitly indicates restricted access.
    /// </summary>
    private static (bool HasListen, bool HasSend, bool HasManage) DetectConnectionStringPermissions(string connectionString)
    {
        // Extract SharedAccessKeyName to infer permissions (uses pre-compiled Regex)
        var match = SharedAccessKeyNameRegex.Match(connectionString);

        if (!match.Success)
        {
            // No key name found, assume all permissions (for backward compatibility)
            return (true, true, true);
        }

        var keyName = match.Groups[1].Value.ToLowerInvariant();

        // Detect LIMITED permissions based on common naming patterns
        var explicitlyListenOnly = keyName.Contains("listen") && !keyName.Contains("send") && !keyName.Contains("manage");
        var explicitlySendOnly = keyName.Contains("send") && !keyName.Contains("manage") && !keyName.Contains("listen");

        var hasManage = keyName.Contains("manage") || keyName.Contains("root") || (!explicitlyListenOnly && !explicitlySendOnly);
        var hasSend = hasManage || keyName.Contains("send");
        var hasListen = true; // All policies have at least listen permission

        return (hasListen, hasSend, hasManage);
    }

    /// <summary>
    /// Checks if the connection string is encrypted.
    /// </summary>
    private static bool IsEncryptedConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        // Check for encrypted connection string formats
        return connectionString.StartsWith("ENC[v1]:", StringComparison.Ordinal) ||
               connectionString.StartsWith("ENC:V2:", StringComparison.Ordinal) ||
               connectionString.StartsWith("PROTECTED:", StringComparison.Ordinal);
    }
}
