using System.Security.Cryptography;
using System.Text;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;

namespace ServiceHub.Core.Entities;

/// <summary>
/// A connected cloud messaging account: an Azure Service Bus namespace, an AWS region/account or a
/// GCP project. The connection string is stored encrypted (ADR-0004); this entity never sees the
/// plaintext once it has been through the secret protector.
/// </summary>
public sealed class Namespace
{
    /// <summary>Longest accepted namespace name.</summary>
    public const int MaxNameLength = 256;

    /// <summary>Longest accepted display name.</summary>
    public const int MaxDisplayNameLength = 100;

    /// <summary>Longest accepted description.</summary>
    public const int MaxDescriptionLength = 500;

    /// <summary>Owner used until identity exists (R6): every namespace belongs to the single browser session.</summary>
    public const string SpaOwnerId = "__spa__";

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; }

    /// <summary>Lower-cased namespace name, endpoint or project identifier.</summary>
    public string Name { get; private set; }

    /// <summary>Friendly name shown instead of <see cref="Name"/>.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>Free-text description.</summary>
    public string? Description { get; private set; }

    /// <summary>The (encrypted) connection string. Null for credential-free identity types.</summary>
    public string? ConnectionString { get; private set; }

    /// <summary>How ServiceHub authenticates to the cloud.</summary>
    public ConnectionAuthType AuthType { get; private set; }

    /// <summary>Whether the namespace is being watched.</summary>
    public bool IsActive { get; private set; }

    /// <summary>When it was connected.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When it was last changed.</summary>
    public DateTimeOffset? ModifiedAt { get; private set; }

    /// <summary>When the connection was last tested.</summary>
    public DateTimeOffset? LastConnectionTestAt { get; private set; }

    /// <summary>The outcome of the last test; null when never tested.</summary>
    public bool? LastConnectionTestSucceeded { get; private set; }

    /// <summary>Dev, Uat or Prod.</summary>
    public EnvironmentType Environment { get; private set; }

    /// <summary>Which cloud backs this namespace.</summary>
    public CloudProviderType Provider { get; private set; } = CloudProviderType.Azure;

    /// <summary>AWS region, when <see cref="Provider"/> is AWS.</summary>
    public string? AwsRegion { get; private set; }

    /// <summary>GCP project, when <see cref="Provider"/> is GCP.</summary>
    public string? GcpProjectId { get; private set; }

    /// <summary>Owner key; see <see cref="SpaOwnerId"/>.</summary>
    public string OwnerId { get; init; } = SpaOwnerId;

    /// <summary>SHA-256 of the plaintext connection string, for duplicate detection without decrypting.</summary>
    public string? ConnectionStringHash { get; private set; }

    private Namespace()
    {
        Name = string.Empty;
    }

    /// <summary>SHA-256 (lower-case hex) of the trimmed connection string; null when empty.</summary>
    public static string? ComputeConnectionStringHash(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(connectionString.Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Creates a namespace that authenticates with a connection string or access key.</summary>
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
        var errors = ValidateCommon(name, displayName, description);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            errors.Add(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringRequired,
                "Connection string is required."));
        }
        else if (provider == CloudProviderType.Azure
            && !IsValidAzureConnectionString(connectionString)
            && !IsEncryptedConnectionString(connectionString))
        {
            errors.Add(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "The connection string format is invalid."));
        }

        if (errors.Count > 0)
        {
            return Result<Namespace>.Failure(errors);
        }

        var authType = provider switch
        {
            CloudProviderType.Aws => ConnectionAuthType.AwsAccessKey,
            CloudProviderType.Gcp => ConnectionAuthType.GcpServiceAccount,
            _ => ConnectionAuthType.ConnectionString,
        };

        return Result<Namespace>.Success(Build(
            name, connectionString.Trim(), authType, displayName, description, environment, provider,
            ownerId, connectionStringHash, awsRegion, gcpProjectId));
    }

    /// <summary>Creates a namespace that authenticates with an ambient, credential-free identity.</summary>
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
        // Allowlist: only known credential-free identity types, so a caller cannot slip a
        // connection-string type past the check by passing an unexpected enum value.
        if (authType is not (ConnectionAuthType.ManagedIdentity
            or ConnectionAuthType.ServicePrincipal
            or ConnectionAuthType.DefaultAzureCredential
            or ConnectionAuthType.AwsOidc
            or ConnectionAuthType.GcpWorkloadIdentity))
        {
            return Result<Namespace>.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringRequired,
                "Authentication type must be ManagedIdentity, ServicePrincipal, DefaultAzureCredential, AwsOidc, or GcpWorkloadIdentity. Use Create() for connection string authentication."));
        }

        var errors = ValidateCommon(name, displayName, description);
        if (errors.Count > 0)
        {
            return Result<Namespace>.Failure(errors);
        }

        return Result<Namespace>.Success(Build(
            name, null, authType, displayName, description, environment, provider,
            ownerId, null, awsRegion, gcpProjectId));
    }

    /// <summary>Records the outcome of a connection test.</summary>
    public void RecordConnectionTest(bool succeeded)
    {
        LastConnectionTestAt = DateTimeOffset.UtcNow;
        LastConnectionTestSucceeded = succeeded;
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    private static Namespace Build(
        string name, string? connectionString, ConnectionAuthType authType, string? displayName,
        string? description, EnvironmentType environment, CloudProviderType provider,
        string? ownerId, string? connectionStringHash, string? awsRegion, string? gcpProjectId) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name.Trim().ToLowerInvariant(),
            ConnectionString = connectionString,
            DisplayName = displayName?.Trim(),
            Description = description?.Trim(),
            AuthType = authType,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            Environment = environment,
            Provider = provider,
            AwsRegion = awsRegion?.Trim(),
            GcpProjectId = gcpProjectId?.Trim(),
            OwnerId = ownerId ?? SpaOwnerId,
            ConnectionStringHash = connectionStringHash,
        };

    private static List<Error> ValidateCommon(string name, string? displayName, string? description)
    {
        var errors = new List<Error>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(Error.Validation(ErrorCodes.Namespace.NameRequired, "Namespace name is required."));
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

        if (displayName is not null && displayName.Length > MaxDisplayNameLength)
        {
            errors.Add(Error.Validation(
                ErrorCodes.Namespace.NameTooLong,
                $"Display name cannot exceed {MaxDisplayNameLength} characters."));
        }

        if (description is not null && description.Length > MaxDescriptionLength)
        {
            errors.Add(Error.Validation(
                ErrorCodes.Namespace.NameTooLong,
                $"Description cannot exceed {MaxDescriptionLength} characters."));
        }

        return errors;
    }

    private static bool IsValidNamespaceName(string name)
    {
        var trimmed = name.Trim();

        // FQDN — must end with a known servicebus suffix (Azure) or amazonaws.com (AWS regional
        // endpoint hostnames, e.g. sqs.us-east-1.amazonaws.com).
        if (trimmed.Contains('.') && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.EndsWith(".servicebus.windows.net", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith(".servicebus.chinacloudapi.cn", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith(".servicebus.usgovcloudapi.net", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith(".servicebus.cloudapi.de", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase);
        }

        // AWS SQS endpoint URL: https://sqs.{region}.amazonaws.com/{account}/{queue-or-topic}
        if (trimmed.StartsWith("https://sqs.", StringComparison.OrdinalIgnoreCase)
            && trimmed.Contains(".amazonaws.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Generic short name (GCP project ids, AWS simple names, Azure short names), 3–256 chars.
        return trimmed.Length >= 3
            && trimmed.Length <= MaxNameLength
            && trimmed.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
            && !trimmed.StartsWith('-')
            && !trimmed.EndsWith('-');
    }

    private static bool IsValidAzureConnectionString(string connectionString) =>
        connectionString.Contains("Endpoint=", StringComparison.OrdinalIgnoreCase)
        && (connectionString.Contains("SharedAccessKey=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("SharedAccessSignature=", StringComparison.OrdinalIgnoreCase));

    private static bool IsEncryptedConnectionString(string connectionString) =>
        connectionString.StartsWith("ENC[v1]:", StringComparison.Ordinal)
        || connectionString.StartsWith("ENC[v2:kid=", StringComparison.Ordinal)
        || connectionString.StartsWith("ENC:V2:", StringComparison.Ordinal)
        || connectionString.StartsWith("PROTECTED:", StringComparison.Ordinal);
}
