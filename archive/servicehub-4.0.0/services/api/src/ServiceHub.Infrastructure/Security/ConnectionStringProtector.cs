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
/// Provides AES-GCM encryption for connection strings, backed by an <see cref="EncryptionKeyRegistry"/>
/// so an operator can rotate the active key without losing access to data encrypted under an older
/// one — see ADR "Encryption Key Rotation for ServiceHub" (project memory
/// <c>adr-encryption-key-rotation</c>), Phases 1-2.
/// <para>
/// Two envelope formats:
/// <list type="bullet">
/// <item><c>ENC[v1]:{base64}</c> — legacy, single-key. No AAD (predates key IDs). Used for new
/// encryptions only while the registry has not been explicitly configured
/// (<c>Security:EncryptionKeyRegistry</c> unset) — i.e. today's single-key deployments keep
/// producing exactly the bytes they always have.</item>
/// <item><c>ENC[v2:kid=&lt;id&gt;]:{base64}</c> — versioned, multi-key. The key ID is authenticated
/// as AES-GCM additional authenticated data, so tampering with the envelope (swapping the kid)
/// is detected at decrypt time, not silently accepted. Used once an operator opts into
/// <c>Security:EncryptionKeyRegistry</c>.</item>
/// </list>
/// Both formats are always decryptable regardless of which one <see cref="Protect"/> currently
/// produces — decryption looks up whichever key ID the envelope names, not just the active one.
/// </para>
/// </summary>
public sealed partial class ConnectionStringProtector : IConnectionStringProtector
{
    private const string V1Prefix = "ENC[v1]:";

    // Legacy formats for backward compatibility — both predate the key registry and are assumed
    // to have been encrypted under EncryptionKeyRegistry.LegacyKeyId, with no AAD.
    private const string LegacyV2Prefix = "ENC:V2:";
    private const string LegacyProtectedPrefix = "PROTECTED:";

    private const string MaskPattern = "SharedAccessKey=***MASKED***";
    private const int KeySizeBytes = 32; // 256 bits
    private const int NonceSizeBytes = 12; // 96 bits for AES-GCM
    private const int TagSizeBytes = 16; // 128 bits

    private readonly EncryptionKeyRegistry _registry;
    private readonly IReadOnlyDictionary<string, byte[]> _derivedKeys;
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
        _encryptionEnabled = configuration.GetValue("Security:EnableConnectionStringEncryption", true);

        // Fatal at startup on any registry violation — never lazily on first use. See
        // EncryptionKeyRegistry.Load for the full validation contract.
        _registry = EncryptionKeyRegistry.Load(configuration);
        _derivedKeys = _registry.Keys.ToDictionary(
            k => k.Id, k => DeriveKey(k.Material), StringComparer.Ordinal);

        // Validate that the *active* key was changed from the default / placeholder. Only the
        // active key matters here — it is the one new encryptions use.
        var activeMaterial = _registry.GetActive().Material;
        if (activeMaterial.Contains("CHANGE_THIS", StringComparison.OrdinalIgnoreCase) ||
            activeMaterial.Contains("SET_VIA_", StringComparison.OrdinalIgnoreCase) ||
            activeMaterial.Contains("DEV_KEY_NOT_FOR_PRODUCTION", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Security warning: Encryption key appears to be a placeholder or development key. " +
                "Set a cryptographically random value via the SECURITY__ENCRYPTIONKEY (or " +
                "SECURITY__ENCRYPTIONKEYREGISTRY) environment variable before using in production.");

            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "The active encryption key must be set to a cryptographically random value via " +
                    "the SECURITY__ENCRYPTIONKEY or SECURITY__ENCRYPTIONKEYREGISTRY environment " +
                    "variable in non-Development environments.");
            }
        }

        _logger.LogInformation(
            "Encryption key registry loaded: {KeyCount} key(s) (active={ActiveKeyId}, mode={Mode})",
            _registry.Keys.Count, _registry.ActiveKeyId, _registry.IsMultiKey ? "multi-key" : "single-key");
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

        // Already ENC[v2:kid=...] — re-encrypt to the active key only if it isn't already active.
        var v2Match = EncV2Regex().Match(connectionString);
        if (v2Match.Success)
        {
            var kid = v2Match.Groups[1].Value;
            if (string.Equals(kid, _registry.ActiveKeyId, StringComparison.Ordinal))
            {
                return Result.Success(connectionString);
            }

            var decryptResult = DecryptV2(connectionString, kid);
            if (decryptResult.IsFailure)
            {
                return Result.Failure<string>(decryptResult.Error);
            }

            return EncryptToActive(decryptResult.Value);
        }

        // Already ENC[v1]: — unchanged while the deployment stays single-key (kid is implicitly
        // always the active one); re-encrypted to the active v2 key once multi-key rotation is on.
        if (connectionString.StartsWith(V1Prefix, StringComparison.Ordinal))
        {
            if (!_registry.IsMultiKey ||
                string.Equals(_registry.ActiveKeyId, EncryptionKeyRegistry.LegacyKeyId, StringComparison.Ordinal))
            {
                return Result.Success(connectionString);
            }

            var decryptResult = DecryptLegacy(connectionString, V1Prefix.Length, aad: null);
            if (decryptResult.IsFailure)
            {
                return Result.Failure<string>(decryptResult.Error);
            }

            return EncryptToActive(decryptResult.Value);
        }

        // Handle legacy V2 format - re-encrypt with current format
        if (connectionString.StartsWith(LegacyV2Prefix, StringComparison.Ordinal))
        {
            var legacyResult = DecryptLegacy(connectionString, LegacyV2Prefix.Length, aad: null);
            if (legacyResult.IsFailure)
            {
                // Do not fall through and re-encrypt the undecryptable ciphertext itself —
                // that would silently and permanently discard the real connection string.
                return Result.Failure<string>(legacyResult.Error);
            }

            connectionString = legacyResult.Value;
            // Re-encrypt with current format below
        }

        // Handle legacy protected strings - decrypt first if needed
        if (connectionString.StartsWith(LegacyProtectedPrefix, StringComparison.Ordinal))
        {
            var legacyResult = UnprotectLegacy(connectionString);
            if (legacyResult.IsFailure)
            {
                return Result.Failure<string>(legacyResult.Error);
            }

            connectionString = legacyResult.Value;
        }

        if (!_encryptionEnabled)
        {
            _logger.LogDebug("Encryption disabled, storing connection string with legacy encoding");
            var bytes = Encoding.UTF8.GetBytes(connectionString);
            return Result.Success($"{LegacyProtectedPrefix}{Convert.ToBase64String(bytes)}");
        }

        try
        {
            return EncryptToActive(connectionString);
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

        var v2Match = EncV2Regex().Match(protectedConnectionString);
        if (v2Match.Success)
        {
            return DecryptV2(protectedConnectionString, v2Match.Groups[1].Value);
        }

        if (protectedConnectionString.StartsWith(V1Prefix, StringComparison.Ordinal))
        {
            return DecryptLegacy(protectedConnectionString, V1Prefix.Length, aad: null);
        }

        // Legacy V2 format (backward compatibility)
        if (protectedConnectionString.StartsWith(LegacyV2Prefix, StringComparison.Ordinal))
        {
            _logger.LogDebug("Decrypting legacy V2 format. Consider re-encrypting with versioned format.");
            return DecryptLegacy(protectedConnectionString, LegacyV2Prefix.Length, aad: null);
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

        var v2Match = EncV2Regex().Match(connectionString);
        if (v2Match.Success)
        {
            return $"[ENCRYPTED:v2:kid={v2Match.Groups[1].Value}]";
        }

        if (connectionString.StartsWith(V1Prefix, StringComparison.Ordinal))
        {
            return "[ENCRYPTED:v1]";
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

        // Mask AWS access key IDs and labelled secret/session-token fields
        masked = AwsAccessKeyIdRegex().Replace(masked, "***MASKED***");
        masked = AwsCredentialFieldRegex().Replace(masked, "$1=***MASKED***");

        // Mask GCP service-account private key material
        masked = GcpPrivateKeyFieldRegex().Replace(masked, "\"private_key\": \"***MASKED***\"");
        masked = GcpPrivateKeyIdFieldRegex().Replace(masked, "\"private_key_id\": \"***MASKED***\"");
        masked = PemPrivateKeyBlockRegex().Replace(masked, "[PRIVATE KEY MASKED]");

        return masked;
    }

    /// <inheritdoc/>
    public string GetKeyFingerprint()
    {
        // SHA-256 of the *derived* active key, not the operator-configured key string — the
        // derived key is already the product of HKDF/PBKDF2, so hashing it again cannot leak
        // any information that would help recover the original key material. Truncated to 16
        // hex chars: enough to distinguish keys across environments, short enough that nobody
        // mistakes it for a secret worth protecting.
        var hash = SHA256.HashData(_derivedKeys[_registry.ActiveKeyId]);
        return $"sha256:{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
    }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> with the currently active key, using the
    /// <c>ENC[v2:kid=...]</c> envelope once the registry is multi-key, or the legacy
    /// <c>ENC[v1]:</c> envelope while it is still single-key — so a deployment that has not opted
    /// into <see cref="EncryptionKeyRegistry.IsMultiKey"/> keeps producing exactly the bytes it
    /// always has.
    /// </summary>
    private Result<string> EncryptToActive(string plaintext)
    {
        var activeId = _registry.ActiveKeyId;
        var key = _derivedKeys[activeId];

        if (!_registry.IsMultiKey)
        {
            return Result.Success($"{V1Prefix}{EncryptAesGcm(key, plaintext, aad: null)}");
        }

        var envelopeTag = $"ENC[v2:kid={activeId}]";
        var aad = Encoding.UTF8.GetBytes(envelopeTag);
        return Result.Success($"{envelopeTag}:{EncryptAesGcm(key, plaintext, aad)}");
    }

    /// <summary>Decrypts an <c>ENC[v2:kid=&lt;kid&gt;]:</c> envelope, verifying the kid as AAD.</summary>
    private Result<string> DecryptV2(string encryptedString, string kid)
    {
        if (!_derivedKeys.TryGetValue(kid, out var key))
        {
            _logger.LogWarning(
                "Failed to decrypt connection string: key ID '{KeyId}' not found in registry " +
                "(possible key rotation without retaining the old key)", kid);
            return Result.Failure<string>(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                $"Key ID '{kid}' not found in registry. Update Security:EncryptionKeyRegistry to " +
                "include this key, or re-add this namespace."));
        }

        var envelopeTag = $"ENC[v2:kid={kid}]";
        var prefix = $"{envelopeTag}:";
        var aad = Encoding.UTF8.GetBytes(envelopeTag);
        return DecryptAesGcm(key, encryptedString, prefix.Length, aad);
    }

    /// <summary>
    /// Decrypts a pre-registry envelope (<c>ENC[v1]:</c> or the deprecated <c>ENC:V2:</c>), which
    /// always used <see cref="EncryptionKeyRegistry.LegacyKeyId"/> and no AAD.
    /// </summary>
    private Result<string> DecryptLegacy(string encryptedString, int prefixLength, byte[]? aad)
    {
        if (!_derivedKeys.TryGetValue(EncryptionKeyRegistry.LegacyKeyId, out var key))
        {
            _logger.LogWarning(
                "Failed to decrypt connection string: key ID '{KeyId}' not found in registry",
                EncryptionKeyRegistry.LegacyKeyId);
            return Result.Failure<string>(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                $"Key ID '{EncryptionKeyRegistry.LegacyKeyId}' not found in registry. Include the " +
                "prior Security:EncryptionKey value under this ID in Security:EncryptionKeyRegistry " +
                "to keep existing namespaces decryptable, or re-add this namespace."));
        }

        return DecryptAesGcm(key, encryptedString, prefixLength, aad);
    }

    /// <summary>
    /// Encrypts plaintext using AES-GCM authenticated encryption.
    /// Output format: Base64(nonce || ciphertext || tag)
    /// </summary>
    private static string EncryptAesGcm(byte[] key, string plaintext, byte[]? aad)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSizeBytes];
        var tag = new byte[TagSizeBytes];
        var ciphertext = new byte[plaintextBytes.Length];

        // Generate cryptographically secure random nonce
        RandomNumberGenerator.Fill(nonce);

        using var aesGcm = new AesGcm(key, TagSizeBytes);
        aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag, aad);

        // Combine: nonce + ciphertext + tag
        var combined = new byte[nonce.Length + ciphertext.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length + ciphertext.Length, tag.Length);

        return Convert.ToBase64String(combined);
    }

    /// <summary>
    /// Decrypts AES-GCM encrypted data whose base64 payload starts at <paramref name="prefixLength"/>.
    /// </summary>
    private Result<string> DecryptAesGcm(byte[] key, string encryptedString, int prefixLength, byte[]? aad)
    {
        try
        {
            var payload = encryptedString[prefixLength..];
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

            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, aad);

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
            // Do NOT pass the exception object to the logger — Application Insights captures exceptions
            // from ILogger calls, which would flood telemetry when a key rotation leaves old encrypted
            // connection strings in the database. Log the message only, not the full exception.
            _logger.LogWarning(
                "Failed to decrypt connection string (possible key rotation or data corruption): {ExceptionType}",
                ex.GetType().Name);
            return Result.Failure<string>(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "Failed to decrypt connection string. The encryption key may have changed — please re-add this namespace."));
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

    [GeneratedRegex(@"^ENC\[v2:kid=([A-Za-z0-9-]{1,64})\]:")]
    private static partial Regex EncV2Regex();

    [GeneratedRegex(@"SharedAccessKey=[^;]+", RegexOptions.IgnoreCase)]
    private static partial Regex SharedAccessKeyRegex();

    [GeneratedRegex(@"SharedAccessSignature=[^;]+", RegexOptions.IgnoreCase)]
    private static partial Regex SharedAccessSignatureRegex();

    [GeneratedRegex(@"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b")]
    private static partial Regex AwsAccessKeyIdRegex();

    [GeneratedRegex(@"(aws_secret_access_key|aws_session_token)""?\s*[:=]\s*""?[^""\r\n,;}]+""?", RegexOptions.IgnoreCase)]
    private static partial Regex AwsCredentialFieldRegex();

    [GeneratedRegex(@"""private_key""\s*:\s*""[^""]*""")]
    private static partial Regex GcpPrivateKeyFieldRegex();

    [GeneratedRegex(@"""private_key_id""\s*:\s*""[^""]*""")]
    private static partial Regex GcpPrivateKeyIdFieldRegex();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PemPrivateKeyBlockRegex();
}
