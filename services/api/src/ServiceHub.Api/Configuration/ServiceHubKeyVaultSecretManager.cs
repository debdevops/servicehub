using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;

namespace ServiceHub.Api.Configuration;

/// <summary>
/// Maps Azure Key Vault secret names to .NET configuration keys.
/// Key Vault secret names use hyphens; .NET config uses colons.
/// 
/// Mapping rules:
///   servicehub-encryption-key         → Security:EncryptionKey
///   servicehub-api-key-admin          → Security:Authentication:ScopedApiKeys:0:Key
///   servicehub-api-key-readonly       → Security:Authentication:ScopedApiKeys:1:Key
///   All other secrets                 → default double-dash (--) to colon mapping
/// </summary>
public sealed class ServiceHubKeyVaultSecretManager : KeyVaultSecretManager
{
    private static readonly Dictionary<string, string> SecretMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["servicehub-encryption-key"] = "Security:EncryptionKey"
    };

    // API key secrets that map into the ScopedApiKeys array
    private static readonly (string SecretName, int Index, string[] Scopes, string Description)[] ApiKeySecrets =
    [
        ("servicehub-api-key-admin", 0, [], "Admin key (all scopes)"),
        ("servicehub-api-key-readonly", 1, ["namespaces:read", "queues:read", "topics:read", "subscriptions:read", "messages:peek", "dlq:read", "anomalies:read"], "Read-only key")
    ];

    public override bool Load(SecretProperties secret)
    {
        // Load all secrets — we filter in GetKey
        return true;
    }

    public override string GetKey(KeyVaultSecret secret)
    {
        var name = secret.Name;

        // Direct mapping (e.g., encryption key)
        if (SecretMapping.TryGetValue(name, out var configKey))
        {
            return configKey;
        }

        // API key secrets map to ScopedApiKeys array entries
        // The key value itself maps to the Key property
        foreach (var (secretName, index, _, _) in ApiKeySecrets)
        {
            if (string.Equals(name, secretName, StringComparison.OrdinalIgnoreCase))
            {
                return $"Security:Authentication:ScopedApiKeys:{index}:Key";
            }
        }

        // Default: replace -- with : (standard Azure Key Vault convention)
        return name.Replace("--", ConfigurationPath.KeyDelimiter);
    }
}
