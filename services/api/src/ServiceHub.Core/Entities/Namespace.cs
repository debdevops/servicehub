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
    /// Private constructor to enforce factory method usage.
    /// </summary>
    private Namespace()
    {
        Name = string.Empty;
    }

    /// <summary>
    /// Creates a new namespace configuration using a connection string.
    /// </summary>
    /// <param name="name">The fully qualified namespace name.</param>
    /// <param name="connectionString">The connection string with SAS credentials.</param>
    /// <param name="displayName">Optional display name.</param>
    /// <param name="description">Optional description.</param>
    /// <returns>A result containing the namespace or validation errors.</returns>
    public static Result<Namespace> Create(
        string name,
        string connectionString,
        string? displayName = null,
        string? description = null)
    {
        var validationResult = ValidateConnectionStringAuth(name, connectionString, displayName, description);
        if (validationResult.IsFailure)
        {
            return Result<Namespace>.Failure(validationResult.Errors);
        }

        var ns = new Namespace
        {
            Id = Guid.NewGuid(),
            Name = name.Trim().ToLowerInvariant(),
            ConnectionString = connectionString.Trim(),
            DisplayName = displayName?.Trim(),
            Description = description?.Trim(),
            AuthType = ConnectionAuthType.ConnectionString,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
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
    /// <returns>A result containing the namespace or validation errors.</returns>
    public static Result<Namespace> CreateWithManagedIdentity(
        string name,
        ConnectionAuthType authType = ConnectionAuthType.ManagedIdentity,
        string? displayName = null,
        string? description = null)
    {
        if (authType == ConnectionAuthType.ConnectionString)
        {
            return Result<Namespace>.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringRequired,
                "Connection string authentication requires a connection string. Use Create() method instead."));
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
            CreatedAt = DateTimeOffset.UtcNow
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

        // Validate connection string
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            errors.Add(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringRequired,
                "Connection string is required."));
        }
        else if (!IsValidConnectionString(connectionString) && !IsEncryptedConnectionString(connectionString))
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
    /// </summary>
    private static bool IsValidNamespaceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();

        // Must end with .servicebus.windows.net for FQDN format
        // Or be a simple name (alphanumeric and hyphens)
        if (trimmed.Contains('.'))
        {
            return trimmed.EndsWith(".servicebus.windows.net", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.EndsWith(".servicebus.chinacloudapi.cn", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.EndsWith(".servicebus.usgovcloudapi.net", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.EndsWith(".servicebus.cloudapi.de", StringComparison.OrdinalIgnoreCase);
        }

        // Simple name validation: alphanumeric and hyphens, 6-50 chars
        return trimmed.Length >= 6 &&
               trimmed.Length <= 50 &&
               trimmed.All(c => char.IsLetterOrDigit(c) || c == '-') &&
               !trimmed.StartsWith('-') &&
               !trimmed.EndsWith('-');
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
