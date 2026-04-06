using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Security;

/// <summary>
/// Provides AES-GCM encryption for connection strings with key versioning support.
/// Uses authenticated encryption to ensure both confidentiality and integrity.
/// Format: ENC[v1]:{base64-ciphertext} for versioned encryption.
/// </summary>
public sealed partial class ConnectionStringProtector : IConnectionStringProtector
{
    // Current encryption version - increment when changing encryption algorithm
    private const string CurrentVersion = "v1";
    private const string EncryptedPrefix = "ENC[v1]:";
    
    // Legacy formats for backward compatibility
    private const string LegacyV2Prefix = "ENC:V2:";
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
    /// <param name="environment">The host environment.</param>
    /// <param name="logger">The logger instance.</param>
    public ConnectionStringProtector(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<ConnectionStringProtector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var keyString = configuration["Security:EncryptionKey"]
            ?? throw new InvalidOperationException("Security:EncryptionKey is not configured.");

        if (string.IsNullOrWhiteSpace(keyString))
        {
            throw new InvalidOperationException(
                "Security:EncryptionKey is empty or whitespace. " +
                "Set a secure random value via the SECURITY__ENCRYPTIONKEY environment variable " +
                "or Azure App Service Application Settings.");
        }

        _encryptionEnabled = configuration.GetValue("Security:EnableConnectionStringEncryption", true);

        // Derive a proper 256-bit key from the configured key using SHA256
        _encryptionKey = DeriveKey(keyString);

        // Validate that the key was changed from the default / placeholder
        if (keyString.Contains("CHANGE_THIS", StringComparison.OrdinalIgnoreCase) ||
            keyString.Contains("SET_VIA_", StringComparison.OrdinalIgnoreCase) ||
            keyString.Contains("DEV_KEY_NOT_FOR_PRODUCTION", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Security warning: Encryption key appears to be a placeholder or development key. " +
                "Set a cryptographically random value via the SECURITY__ENCRYPTIONKEY environment variable " +
                "or Azure App Service Application Settings before using in production.");

            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Security:EncryptionKey must be set to a cryptographically random value via " +
                    "the SECURITY__ENCRYPTIONKEY environment variable in non-Development environments.");
            }
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

        // Already encrypted with current version
        if (connectionString.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
        {
            return Result.Success(connectionString);
        }

        // Handle legacy V2 format - re-encrypt with new versioned format
        if (connectionString.StartsWith(LegacyV2Prefix, StringComparison.Ordinal))
        {
            var legacyResult = UnprotectLegacyV2(connectionString);
            if (legacyResult.IsSuccess)
            {
                connectionString = legacyResult.Value;
                // Re-encrypt with new format below
            }
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
            return Result.Success($"{EncryptedPrefix}{encrypted}");
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

        // Current versioned format
        if (protectedConnectionString.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
        {
            return DecryptAesGcm(protectedConnectionString, EncryptedPrefix);
        }

        // Legacy V2 format (backward compatibility)
        if (protectedConnectionString.StartsWith(LegacyV2Prefix, StringComparison.Ordinal))
        {
            _logger.LogDebug("Decrypting legacy V2 format. Consider re-encrypting with versioned format.");
            return DecryptAesGcm(protectedConnectionString, LegacyV2Prefix);
        }

        // Legacy Base64 format (backward compatibility)
        if (protectedConnectionString.StartsWith(LegacyProtectedPrefix, StringComparison.Ordinal))
        {
            var result = UnprotectLegacy(protectedConnectionString);
            if (result.IsSuccess)
            {
                _logger.LogDebug(
                    "Decrypted legacy protected connection string. Consider re-encrypting with versioned format.");
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

        // For encrypted strings, just indicate they're encrypted with version
        if (connectionString.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
        {
            return $"[ENCRYPTED:{CurrentVersion}]";
        }

        if (connectionString.StartsWith(LegacyV2Prefix, StringComparison.Ordinal))
        {
            return "[ENCRYPTED:V2-LEGACY]";
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
    /// Decrypts AES-GCM encrypted data with specified prefix.
    /// </summary>
    private Result<string> DecryptAesGcm(string encryptedString, string prefix)
    {
        try
        {
            var payload = encryptedString[prefix.Length..];
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
    /// Decrypts legacy ENC:V2: formatted connection strings for backward compatibility.
    /// </summary>
    private Result<string> UnprotectLegacyV2(string protectedConnectionString)
    {
        _logger.LogInformation("Decrypting legacy V2 format connection string - consider re-encrypting to new format");
        return DecryptAesGcm(protectedConnectionString, LegacyV2Prefix);
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
    /// Derives a 256-bit AES key from the configured key material.
    ///
    /// BREAKING CHANGE: This method was changed from single-round SHA-256 to
    /// HKDF (for 64-char hex keys) / PBKDF2-100k (for other keys) in v3.1.0.
    /// Any connection strings encrypted before this upgrade must be re-added —
    /// they cannot be decrypted with the new derived key.
    /// </summary>
    private static byte[] DeriveKey(string keyString)
    {
        // High-entropy path: 64 hex chars = 32 bytes from `openssl rand -hex 32`
        // HKDF is appropriate when the input key material is already random.
        if (keyString.Length == 64)
        {
            try
            {
                var rawKey = Convert.FromHexString(keyString);
                return HKDF.DeriveKey(
                    hashAlgorithmName: HashAlgorithmName.SHA256,
                    ikm: rawKey,
                    outputLength: KeySizeBytes,
                    info: "servicehub-connection-string-v1"u8.ToArray());
            }
            catch (FormatException)
            {
                // Not valid hex — fall through to PBKDF2
            }
        }

        // Low-entropy / password path: PBKDF2 with 100,000 iterations
        return Rfc2898DeriveBytes.Pbkdf2(
            password: keyString,
            salt: "servicehub-key-derivation-salt-v1"u8.ToArray(),
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: KeySizeBytes);
    }

    [GeneratedRegex(@"SharedAccessKey=[^;]+", RegexOptions.IgnoreCase)]
    private static partial Regex SharedAccessKeyRegex();

    [GeneratedRegex(@"SharedAccessSignature=[^;]+", RegexOptions.IgnoreCase)]
    private static partial Regex SharedAccessSignatureRegex();
}
