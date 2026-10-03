using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace ServiceHub.Infrastructure.Security;

/// <summary>
/// Status of a key entry in an <see cref="EncryptionKeyRegistry"/>. Purely informational today —
/// only <see cref="EncryptionKeyRegistry.ActiveKeyId"/> decides which key encrypts new data. The
/// <c>Compromised</c> value exists so the wire format matches ADR "Encryption Key Rotation for
/// ServiceHub" §Compromise-Procedure; the bulk re-encryption workflow that acts on it (Phase 4 of
/// that ADR) is intentionally not built yet.
/// </summary>
public enum EncryptionKeyStatus
{
    Active,
    Retired,
    Compromised,
}

/// <summary>One key entry in an <see cref="EncryptionKeyRegistry"/>.</summary>
public sealed class EncryptionKeyEntry
{
    /// <summary>Opaque identifier, alphanumeric + hyphen, max 64 chars — e.g. "prod-active-2".</summary>
    public required string Id { get; init; }

    /// <summary>Key material: 64 hex chars (from <c>openssl rand -hex 32</c>) or an arbitrary
    /// password string (PBKDF2-derived) — never persisted, only ever read from environment
    /// variables or an external secret provider.</summary>
    public required string Material { get; init; }

    public EncryptionKeyStatus Status { get; init; } = EncryptionKeyStatus.Active;

    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>
/// Multi-key registry backing <see cref="ConnectionStringProtector"/>'s <c>ENC[v2:kid=&lt;id&gt;]</c>
/// envelope — see ADR "Encryption Key Rotation for ServiceHub" (project memory
/// <c>adr-encryption-key-rotation</c>). Loaded once at startup from
/// <c>Security:EncryptionKeyRegistry</c> (a JSON blob, normally supplied via the
/// <c>SECURITY__ENCRYPTIONKEYREGISTRY</c> environment variable) or, when that is absent, wrapped
/// from the single legacy <c>Security:EncryptionKey</c> value under the well-known key ID
/// <see cref="LegacyKeyId"/> — the same ID <see cref="ConnectionStringProtector"/> assumes for any
/// connection string still tagged with the old <c>ENC[v1]:</c> prefix.
/// </summary>
public sealed class EncryptionKeyRegistry
{
    /// <summary>Well-known key ID assumed for connection strings encrypted before this registry
    /// existed (the <c>ENC[v1]:</c> envelope carries no key ID of its own) and for a
    /// single-key deployment that has not opted into <c>Security:EncryptionKeyRegistry</c>.</summary>
    public const string LegacyKeyId = "legacy-v1";

    private static readonly System.Text.RegularExpressions.Regex KeyIdPattern =
        new(@"^[A-Za-z0-9-]{1,64}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public required string ActiveKeyId { get; init; }

    public required IReadOnlyList<EncryptionKeyEntry> Keys { get; init; }

    /// <summary>True when the registry was explicitly configured via
    /// <c>Security:EncryptionKeyRegistry</c> — as opposed to being wrapped from a single
    /// <c>Security:EncryptionKey</c> value. Determines whether newly-encrypted connection strings
    /// use the legacy <c>ENC[v1]:</c> envelope (single-key deployments, unchanged behaviour) or the
    /// versioned <c>ENC[v2:kid=&lt;id&gt;]:</c> envelope (once an operator has opted into rotation).</summary>
    public required bool IsMultiKey { get; init; }

    public EncryptionKeyEntry GetActive() =>
        Keys.First(k => string.Equals(k.Id, ActiveKeyId, StringComparison.Ordinal));

    public EncryptionKeyEntry? Find(string keyId) =>
        Keys.FirstOrDefault(k => string.Equals(k.Id, keyId, StringComparison.Ordinal));

    /// <summary>
    /// Loads the registry from configuration and validates it, throwing
    /// <see cref="InvalidOperationException"/> with an operator-actionable message on any
    /// violation — this must fail startup, not fail lazily on first use. Precedence:
    /// <c>Security:EncryptionKeyRegistry</c> (JSON) &gt; <c>Security:EncryptionKey</c> (single
    /// key, wrapped) &gt; fail.
    /// </summary>
    public static EncryptionKeyRegistry Load(IConfiguration configuration)
    {
        var registryJson = configuration["Security:EncryptionKeyRegistry"];
        if (!string.IsNullOrWhiteSpace(registryJson))
        {
            return LoadFromJson(registryJson);
        }

        var singleKey = configuration["Security:EncryptionKey"]
            ?? throw new InvalidOperationException(
                "Neither Security:EncryptionKeyRegistry nor Security:EncryptionKey is configured. " +
                "Set SECURITY__ENCRYPTIONKEY (single key) or SECURITY__ENCRYPTIONKEYREGISTRY " +
                "(multi-key, enables rotation) via environment variable.");

        if (string.IsNullOrWhiteSpace(singleKey))
        {
            throw new InvalidOperationException(
                "Security:EncryptionKey is empty or whitespace. " +
                "Set a secure random value via the SECURITY__ENCRYPTIONKEY environment variable " +
                "or Azure App Service Application Settings.");
        }

        return new EncryptionKeyRegistry
        {
            ActiveKeyId = LegacyKeyId,
            IsMultiKey = false,
            Keys = [new EncryptionKeyEntry { Id = LegacyKeyId, Material = singleKey, Status = EncryptionKeyStatus.Active }],
        };
    }

    private static EncryptionKeyRegistry LoadFromJson(string json)
    {
        EncryptionKeyRegistryWireFormat wire;
        try
        {
            wire = JsonSerializer.Deserialize<EncryptionKeyRegistryWireFormat>(json, JsonOptions)
                ?? throw new InvalidOperationException("Security:EncryptionKeyRegistry deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Security:EncryptionKeyRegistry is not valid JSON: {ex.Message}", ex);
        }

        if (string.IsNullOrWhiteSpace(wire.ActiveKeyId))
        {
            throw new InvalidOperationException(
                "Security:EncryptionKeyRegistry.ActiveKeyId is required.");
        }

        if (wire.Keys is null || wire.Keys.Count == 0)
        {
            throw new InvalidOperationException(
                "Security:EncryptionKeyRegistry.Keys must contain at least one key.");
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<EncryptionKeyEntry>(wire.Keys.Count);

        foreach (var key in wire.Keys)
        {
            if (string.IsNullOrWhiteSpace(key.Id) || !KeyIdPattern.IsMatch(key.Id))
            {
                throw new InvalidOperationException(
                    $"Security:EncryptionKeyRegistry key ID '{key.Id}' is invalid — must be 1-64 " +
                    "alphanumeric characters or hyphens.");
            }

            if (!seenIds.Add(key.Id))
            {
                throw new InvalidOperationException(
                    $"Security:EncryptionKeyRegistry contains duplicate key ID '{key.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(key.Material))
            {
                throw new InvalidOperationException(
                    $"Security:EncryptionKeyRegistry key '{key.Id}' has empty key material.");
            }

            if (!Enum.TryParse<EncryptionKeyStatus>(key.Status, ignoreCase: true, out var status))
            {
                throw new InvalidOperationException(
                    $"Security:EncryptionKeyRegistry key '{key.Id}' has unrecognized status " +
                    $"'{key.Status}' — expected one of: active, retired, compromised.");
            }

            entries.Add(new EncryptionKeyEntry
            {
                Id = key.Id,
                Material = key.Material,
                Status = status,
                CreatedAt = key.CreatedAt,
            });
        }

        if (!seenIds.Contains(wire.ActiveKeyId))
        {
            throw new InvalidOperationException(
                $"Security:EncryptionKeyRegistry.ActiveKeyId '{wire.ActiveKeyId}' does not match any " +
                "key in Keys.");
        }

        return new EncryptionKeyRegistry
        {
            ActiveKeyId = wire.ActiveKeyId,
            IsMultiKey = true,
            Keys = entries,
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class EncryptionKeyRegistryWireFormat
    {
        public string? ActiveKeyId { get; set; }
        public List<EncryptionKeyEntryWireFormat>? Keys { get; set; }
    }

    private sealed class EncryptionKeyEntryWireFormat
    {
        public string? Id { get; set; }
        public string? Material { get; set; }
        public string Status { get; set; } = "active";
        public DateTimeOffset? CreatedAt { get; set; }
    }
}
