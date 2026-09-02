using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using ServiceHub.Infrastructure.Security;

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

        // Validate EncryptionKey / EncryptionKeyRegistry — a registry, when present, takes
        // precedence over the single key (see EncryptionKeyRegistry.Load), so the single-key
        // format checks below only apply when no registry is configured.
        var encryptionKeyRegistry = configuration["Security:EncryptionKeyRegistry"];
        if (!string.IsNullOrWhiteSpace(encryptionKeyRegistry))
        {
            try
            {
                EncryptionKeyRegistry.Load(configuration);
            }
            catch (InvalidOperationException ex)
            {
                errors.Add($"Security:EncryptionKeyRegistry is invalid: {ex.Message}");
            }
        }
        else
        {
            var encryptionKey = configuration["Security:EncryptionKey"];
            if (string.IsNullOrWhiteSpace(encryptionKey))
            {
                errors.Add("Security:EncryptionKey is required in production (set via SECURITY__ENCRYPTIONKEY environment variable, or configure SECURITY__ENCRYPTIONKEYREGISTRY for multi-key rotation)");
            }
            else if (IsPlaceholderValue(encryptionKey))
            {
                errors.Add("Security:EncryptionKey has placeholder value — generate a random 32-byte key via: openssl rand -hex 32");
            }
            else if (!IsValidHexString(encryptionKey, 64))
            {
                errors.Add("Security:EncryptionKey must be a 64-character hexadecimal string (32 bytes). Generate via: openssl rand -hex 32");
            }
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
        else if (IsPlaceholderValue(spaTokenSecret))
        {
            errors.Add("Security:SpaToken:Secret has placeholder value — generate a random secret via: openssl rand -hex 32");
        }
        else if (spaTokenSecret.Length < 32)
        {
            errors.Add("Security:SpaToken:Secret must be at least 32 characters long for production security");
        }

        // Validate authentication configuration in production
        var authenticationEnabled = configuration.GetValue<bool>("Security:Authentication:Enabled");
        if (authenticationEnabled)
        {
            var spaTokenEnabled = configuration.GetValue<bool>("Security:SpaToken:Enabled");
            var easyAuthEnabled = configuration.GetValue<bool>("Security:EasyAuth:Enabled");
            var oidcEnabled = configuration.GetValue<bool>("Security:Oidc:Enabled");

            var hasApiKeys = HasConfiguredApiKeys(configuration);
            var hasOidcConfig = oidcEnabled
                && !string.IsNullOrWhiteSpace(configuration["Security:Oidc:Authority"])
                && !string.IsNullOrWhiteSpace(configuration["Security:Oidc:Audience"]);

            if (!hasApiKeys && !spaTokenEnabled && !easyAuthEnabled && !hasOidcConfig)
            {
                errors.Add(
                    "Security:Authentication:Enabled is true in production but no usable authentication method is configured. " +
                    "Configure at least one of: " +
                    "Security:Authentication:ApiKeys/ScopedApiKeys, " +
                    "Security:SpaToken:Enabled=true, " +
                    "Security:EasyAuth:Enabled=true, " +
                    "or Security:Oidc:Enabled=true with Authority and Audience.");
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

    private static bool HasConfiguredApiKeys(IConfiguration configuration)
    {
        // Check simple ApiKeys array
        var simpleKeys = configuration.GetSection("Security:Authentication:ApiKeys").Get<string[]>();
        if (simpleKeys is { Length: > 0 })
        {
            foreach (var key in simpleKeys)
            {
                if (!string.IsNullOrWhiteSpace(key) && !IsPlaceholderKey(key))
                {
                    return true;
                }
            }
        }

        // Check ScopedApiKeys
        var scopedKeysSection = configuration.GetSection("Security:Authentication:ScopedApiKeys");
        if (scopedKeysSection.Exists())
        {
            foreach (var child in scopedKeysSection.GetChildren())
            {
                var keyValue = child.GetValue<string>("Key");
                if (!string.IsNullOrWhiteSpace(keyValue) && !IsPlaceholderKey(keyValue))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsPlaceholderKey(string key)
    {
        return key.StartsWith("REPLACED_BY_KEYVAULT", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("SET_VIA_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("CHANGE_THIS", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlaceholderValue(string value)
    {
        return value.StartsWith("SET_VIA_", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("CHANGE_THIS", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("DEV_KEY_", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("REPLACED_BY_", StringComparison.OrdinalIgnoreCase);
    }
}
