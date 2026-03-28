using System.Security.Cryptography;

namespace ServiceHub.Api.Security;

/// <summary>
/// Generates and validates HMAC-based tokens for the co-hosted SPA.
/// The token is injected into index.html at serve time and sent back
/// by the browser on every API request. External tools (Postman, curl)
/// cannot obtain this token because they never load the HTML page.
/// </summary>
public sealed class SpaTokenProvider
{
    private readonly byte[] _secretKey;
    private readonly ILogger<SpaTokenProvider> _logger;
    private readonly bool _enabled;

    /// <summary>
    /// How long each token is valid. Kept short to limit replay windows.
    /// </summary>
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);

    public SpaTokenProvider(IConfiguration configuration, ILogger<SpaTokenProvider> logger)
    {
        _logger = logger;

        _enabled = configuration.GetValue("Security:SpaToken:Enabled", false);

        if (!_enabled)
        {
            _secretKey = Array.Empty<byte>();
            _logger.LogInformation("SPA token validation is disabled");
            return;
        }

        // Use a dedicated secret, falling back to the encryption key
        var secret = configuration.GetValue<string>("Security:SpaToken:Secret")
                     ?? configuration.GetValue<string>("Security:EncryptionKey");

        if (string.IsNullOrWhiteSpace(secret) || secret.StartsWith("CHANGE_THIS", StringComparison.OrdinalIgnoreCase)
                                                || secret.StartsWith("SET_VIA_", StringComparison.OrdinalIgnoreCase))
        {
            // Generate an ephemeral key — tokens won't survive app restarts but that's fine
            _secretKey = RandomNumberGenerator.GetBytes(32);
            _logger.LogWarning("SPA token using ephemeral key — configure Security:SpaToken:Secret for persistence across restarts");
        }
        else
        {
            try
            {
                _secretKey = Convert.FromHexString(secret);
            }
            catch (FormatException)
            {
                // Secret is not hex-encoded (e.g., plaintext dev/test key) — use ephemeral key
                _secretKey = RandomNumberGenerator.GetBytes(32);
                _logger.LogWarning("SPA token secret is not valid hex — using ephemeral key. Encode your secret as hex for persistence across restarts");
            }
        }

        _logger.LogInformation("SPA token validation enabled (lifetime: {Lifetime} min)", TokenLifetime.TotalMinutes);
    }

    public bool IsEnabled => _enabled;

    /// <summary>
    /// Generates a token containing a timestamp, signed with HMAC-SHA256.
    /// Format: {base64url(timestamp_bytes)}.{base64url(hmac)}
    /// </summary>
    public string GenerateToken()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timestampBytes = BitConverter.GetBytes(timestamp);

        using var hmac = new HMACSHA256(_secretKey);
        var signature = hmac.ComputeHash(timestampBytes);

        return $"{Convert.ToBase64String(timestampBytes)}.{Convert.ToBase64String(signature)}";
    }

    /// <summary>
    /// Validates a SPA token — checks the HMAC signature and expiry.
    /// </summary>
    public bool ValidateToken(string? token)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            var parts = token.Split('.');
            if (parts.Length != 2)
                return false;

            var timestampBytes = Convert.FromBase64String(parts[0]);
            var providedSignature = Convert.FromBase64String(parts[1]);

            if (timestampBytes.Length != 8)
                return false;

            // Verify HMAC
            using var hmac = new HMACSHA256(_secretKey);
            var expectedSignature = hmac.ComputeHash(timestampBytes);

            if (!CryptographicOperations.FixedTimeEquals(expectedSignature, providedSignature))
                return false;

            // Check expiry
            var timestamp = BitConverter.ToInt64(timestampBytes, 0);
            var tokenTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
            var age = DateTimeOffset.UtcNow - tokenTime;

            return age >= TimeSpan.Zero && age <= TokenLifetime;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
