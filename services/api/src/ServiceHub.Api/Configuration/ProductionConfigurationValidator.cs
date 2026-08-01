using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;

namespace ServiceHub.Api.Configuration;

public static class ProductionConfigurationValidator
{
    public static void ValidateProduction(IConfiguration configuration, IHostEnvironment environment, ILogger logger)
    {
        if (!environment.IsProduction())
            return;

        var errors = new List<string>();

        // Validate AllowedHosts
        var allowedHosts = configuration["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(allowedHosts))
        {
            errors.Add("AllowedHosts is required in production (set via AllowedHosts environment variable)");
        }
        else if (allowedHosts.Equals("SET_VIA_ENV_VAR", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("AllowedHosts has placeholder value 'SET_VIA_ENV_VAR' — set the actual hostname(s) via AllowedHosts environment variable");
        }
        else if (allowedHosts == "*")
        {
            errors.Add("AllowedHosts cannot be '*' in production — this disables host-header filtering and opens the app to cache poisoning attacks");
        }

        // Validate EncryptionKey
        var encryptionKey = configuration["Security:EncryptionKey"];
        if (string.IsNullOrWhiteSpace(encryptionKey))
        {
            errors.Add("Security:EncryptionKey is required in production (set via SECURITY__ENCRYPTIONKEY environment variable)");
        }
        else if (encryptionKey.Equals("SET_VIA_ENV_VAR", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Security:EncryptionKey has placeholder value 'SET_VIA_ENV_VAR' — generate a random 32-byte key via: openssl rand -hex 32");
        }
        else if (!IsValidHexString(encryptionKey, 64))
        {
            errors.Add("Security:EncryptionKey must be a 64-character hexadecimal string (32 bytes). Generate via: openssl rand -hex 32");
        }

        // Validate SiteUrl
        var siteUrl = configuration["SiteUrl"];
        if (string.IsNullOrWhiteSpace(siteUrl))
        {
            errors.Add("SiteUrl is required in production (set via SITEURL environment variable) — this is the URL users visit");
        }
        else if (siteUrl.Equals("SET_VIA_ENV_VAR", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("SiteUrl has placeholder value 'SET_VIA_ENV_VAR' — set it to your actual deployment URL (e.g., https://servicehub.example.com)");
        }
        else if (!IsValidUrl(siteUrl))
        {
            errors.Add($"SiteUrl '{siteUrl}' is not a valid URL");
        }

        // Validate SPA Token Secret
        var spaTokenSecret = configuration["Security:SpaToken:Secret"];
        if (string.IsNullOrWhiteSpace(spaTokenSecret))
        {
            errors.Add("Security:SpaToken:Secret is required in production (set via SECURITY__SPATOKEN__SECRET environment variable)");
        }
        else if (spaTokenSecret.Equals("SET_VIA_ENV_VAR", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Security:SpaToken:Secret has placeholder value 'SET_VIA_ENV_VAR' — generate a random secret via: openssl rand -hex 32");
        }
        else if (spaTokenSecret.Length < 32)
        {
            errors.Add("Security:SpaToken:Secret must be at least 32 characters long for production security");
        }

        // Validate API Keys are configured (optional but recommend checking in production)
        var spaTokenEnabled = configuration.GetValue<bool>("Security:SpaToken:Enabled");
        if (spaTokenEnabled)
        {
            var apiKeysSection = configuration.GetSection("Security:Authentication:ScopedApiKeys");
            if (!apiKeysSection.Exists() || !apiKeysSection.GetChildren().Any())
            {
                logger.LogWarning("No API keys configured in Security:Authentication:ScopedApiKeys. Users will need to authenticate via browser SPA token or OIDC.");
            }
        }

        if (errors.Any())
        {
            var errorMessage = string.Join(Environment.NewLine, new[] { "❌ PRODUCTION CONFIGURATION VALIDATION FAILED:" }.Concat(
                errors.Select((e, i) => $"   {i + 1}. {e}")
            ));
            logger.LogError(errorMessage);
            throw new InvalidOperationException($"Production configuration is incomplete. See logs above for details.");
        }

        logger.LogInformation("✅ Production configuration validation passed");
    }

    private static bool IsValidHexString(string value, int expectedLength)
    {
        if (value.Length != expectedLength)
            return false;
        return Regex.IsMatch(value, $@"^[a-fA-F0-9]{{{expectedLength}}}$");
    }

    private static bool IsValidUrl(string value)
    {
        try
        {
            var uri = new Uri(value, UriKind.Absolute);
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }
        catch
        {
            return false;
        }
    }
}
