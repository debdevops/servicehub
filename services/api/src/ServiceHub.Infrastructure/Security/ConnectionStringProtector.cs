using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Security;

/// <summary>
/// Provides AES-GCM encryption for connection strings.
/// Uses authenticated encryption to ensure both confidentiality and integrity.
/// </summary>
public sealed partial class ConnectionStringProtector : IConnectionStringProtector
{
    private const string EncryptedPrefixV2 = "ENC:V2:";
    private const string LegacyProtectedPrefix = "PROTECTED:";
    private const string MaskPattern = "SharedAccessKey=***MASKED***";
    private const int KeySizeBytes = 32; // 256 bits
    private const int NonceSizeBytes = 12; // 96 bits for AES-GCM
    private const int TagSizeBytes = 16; // 128 bits

    private readonly byte[] _encryptionKey;
    private readonly ILogger<ConnectionStringProtector> _logger;
    private readonly bool _encryptionEnabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionStringProtector"/> class.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="logger">The logger instance.</param>
    public ConnectionStringProtector(
        IConfiguration configuration,
        ILogger<ConnectionStringProtector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var keyString = configuration["Security:EncryptionKey"]
            ?? throw new InvalidOperationException("Security:EncryptionKey is not configured.");

        _encryptionEnabled = configuration.GetValue("Security:EnableConnectionStringEncryption", true);

        // Derive a proper 256-bit key from the configured key using SHA256
        _encryptionKey = DeriveKey(keyString);

        // Validate that the key was changed from the default
        if (keyString.Contains("CHANGE_THIS", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Security warning: Using default encryption key. " +
                "Set Security:EncryptionKey to a secure value in production.");
        }
    }

    /// <inheritdoc/>
    public Result<string> Protect(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Result.Failure<string>(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringRequired,
                "Connection string is required."));
        }

        // Already encrypted with V2
        if (connectionString.StartsWith(EncryptedPrefixV2, StringComparison.Ordinal))
        {
            return Result.Success(connectionString);
        }

        // Handle legacy protected strings - decrypt first if needed
        if (connectionString.StartsWith(LegacyProtectedPrefix, StringComparison.Ordinal))
        {
            var legacyResult = UnprotectLegacy(connectionString);
            if (legacyResult.IsSuccess)
            {
                connectionString = legacyResult.Value;
            }
        }

        if (!_encryptionEnabled)
        {
            _logger.LogDebug("Encryption disabled, storing connection string with legacy encoding");
            var bytes = Encoding.UTF8.GetBytes(connectionString);
            return Result.Success($"{LegacyProtectedPrefix}{Convert.ToBase64String(bytes)}");
        }

        try
        {
            var encrypted = EncryptAesGcm(connectionString);
            return Result.Success($"{EncryptedPrefixV2}{encrypted}");
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Failed to encrypt connection string");
            return Result.Failure<string>(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                "Failed to encrypt connection string."));
        }
    }

    /// <inheritdoc/>
    public Result<string> Unprotect(string protectedConnectionString)
    {
        if (string.IsNullOrWhiteSpace(protectedConnectionString))
        {
            return Result.Failure<string>(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringRequired,
                "Protected connection string is required."));
        }

        // V2 encrypted format
        if (protectedConnectionString.StartsWith(EncryptedPrefixV2, StringComparison.Ordinal))
        {
            return DecryptAesGcm(protectedConnectionString);
        }

        // Legacy Base64 format (backward compatibility)
        if (protectedConnectionString.StartsWith(LegacyProtectedPrefix, StringComparison.Ordinal))
        {
            var result = UnprotectLegacy(protectedConnectionString);
            if (result.IsSuccess)
            {
                _logger.LogDebug(
                    "Decrypted legacy protected connection string. Consider re-encrypting with V2 format.");
            }
            return result;
        }

        // Not protected, return as-is
        return Result.Success(protectedConnectionString);
    }

    /// <inheritdoc/>
    public string Mask(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return string.Empty;
        }

        // For encrypted strings, just indicate they're encrypted
        if (connectionString.StartsWith(EncryptedPrefixV2, StringComparison.Ordinal))
        {
            return "[ENCRYPTED]";
        }

        // Remove protection prefix if present for masking
        var stringToMask = connectionString;
        if (connectionString.StartsWith(LegacyProtectedPrefix, StringComparison.Ordinal))
        {
            var unprotectResult = UnprotectLegacy(connectionString);
            if (unprotectResult.IsSuccess)
            {
                stringToMask = unprotectResult.Value;
            }
            else
            {
                return "[PROTECTED]";
            }
        }

        // Mask the SharedAccessKey
        var masked = SharedAccessKeyRegex().Replace(stringToMask, MaskPattern);

        // Also mask SharedAccessSignature if present
        masked = SharedAccessSignatureRegex().Replace(masked, "SharedAccessSignature=***MASKED***");

        return masked;
    }

    /// <summary>
    /// Encrypts plaintext using AES-GCM authenticated encryption.
    /// Output format: Base64(nonce || ciphertext || tag)
    /// </summary>
    private string EncryptAesGcm(string plaintext)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSizeBytes];
        var tag = new byte[TagSizeBytes];
        var ciphertext = new byte[plaintextBytes.Length];

        // Generate cryptographically secure random nonce
        RandomNumberGenerator.Fill(nonce);

        using var aesGcm = new AesGcm(_encryptionKey, TagSizeBytes);
        aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Combine: nonce + ciphertext + tag
        var combined = new byte[nonce.Length + ciphertext.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length + ciphertext.Length, tag.Length);

        return Convert.ToBase64String(combined);
    }

    /// <summary>
    /// Decrypts AES-GCM encrypted data.
    /// </summary>
    private Result<string> DecryptAesGcm(string encryptedString)
    {
        try
        {
            var payload = encryptedString[EncryptedPrefixV2.Length..];
            var combined = Convert.FromBase64String(payload);

            if (combined.Length < NonceSizeBytes + TagSizeBytes + 1)
            {
                return Result.Failure<string>(Error.Validation(
                    ErrorCodes.Namespace.ConnectionStringInvalid,
                    "The encrypted connection string is too short."));
            }

            var nonce = new byte[NonceSizeBytes];
            var ciphertextLength = combined.Length - NonceSizeBytes - TagSizeBytes;
            var ciphertext = new byte[ciphertextLength];
            var tag = new byte[TagSizeBytes];

            Buffer.BlockCopy(combined, 0, nonce, 0, NonceSizeBytes);
            Buffer.BlockCopy(combined, NonceSizeBytes, ciphertext, 0, ciphertextLength);
            Buffer.BlockCopy(combined, NonceSizeBytes + ciphertextLength, tag, 0, TagSizeBytes);

            var plaintext = new byte[ciphertextLength];

            using var aesGcm = new AesGcm(_encryptionKey, TagSizeBytes);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

            return Result.Success(Encoding.UTF8.GetString(plaintext));
        }
        catch (FormatException)
        {
            return Result.Failure<string>(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "The encrypted connection string format is invalid."));
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt connection string - possible tampering or wrong key");
            return Result.Failure<string>(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "Failed to decrypt connection string. The data may be corrupted or the encryption key may have changed."));
        }
    }

    /// <summary>
    /// Decrypts legacy Base64-encoded connection strings for backward compatibility.
    /// </summary>
    private static Result<string> UnprotectLegacy(string protectedConnectionString)
    {
        try
        {
            var encoded = protectedConnectionString[LegacyProtectedPrefix.Length..];
            var bytes = Convert.FromBase64String(encoded);
            var connectionString = Encoding.UTF8.GetString(bytes);
            return Result.Success(connectionString);
        }
        catch (FormatException)
        {
            return Result.Failure<string>(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "The protected connection string format is invalid."));
        }
    }

    /// <summary>
    /// Derives a 256-bit key from the configured key string using SHA256.
    /// </summary>
    private static byte[] DeriveKey(string keyString)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(keyString));
    }

    [GeneratedRegex(@"SharedAccessKey=[^;]+", RegexOptions.IgnoreCase)]
    private static partial Regex SharedAccessKeyRegex();

    [GeneratedRegex(@"SharedAccessSignature=[^;]+", RegexOptions.IgnoreCase)]
    private static partial Regex SharedAccessSignatureRegex();
}
