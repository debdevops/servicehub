using System.Text.RegularExpressions;

namespace ServiceHub.Infrastructure.Security;

/// <summary>
/// Provides log redaction capabilities to prevent sensitive data from being logged.
/// Uses pattern matching to identify and mask secrets like connection strings, keys, and tokens.
/// </summary>
public static partial class LogRedactor
{
    private const string MaskedValue = "***REDACTED***";
    private const string PartialMaskedValue = "***...***";

    /// <summary>
    /// Redacts sensitive information from a string value.
    /// </summary>
    /// <param name="value">The value to redact.</param>
    /// <returns>The redacted value with sensitive data masked.</returns>
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var result = value;

        // Redact SharedAccessKey values
        result = SharedAccessKeyRegex().Replace(result, $"SharedAccessKey={MaskedValue}");

        // Redact SharedAccessSignature values
        result = SharedAccessSignatureRegex().Replace(result, $"SharedAccessSignature={MaskedValue}");

        // Redact AccountKey values (Azure Storage)
        result = AccountKeyRegex().Replace(result, $"AccountKey={MaskedValue}");

        // Redact password patterns
        result = PasswordRegex().Replace(result, $"$1={MaskedValue}");

        // Redact connection string endpoints (partial - keep domain)
        result = EndpointRegex().Replace(result, m =>
        {
            var endpoint = m.Groups[1].Value;
            // Keep the domain but mask the protocol/port details
            if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            {
                return $"Endpoint={uri.Scheme}://{uri.Host}/***";
            }
            return $"Endpoint={PartialMaskedValue}";
        });

        // Redact API keys (common patterns)
        result = ApiKeyRegex().Replace(result, $"$1{MaskedValue}");

        // Redact Bearer tokens
        result = BearerTokenRegex().Replace(result, $"Bearer {MaskedValue}");

        // Redact encrypted values (our own format)
        result = EncryptedValueRegex().Replace(result, "[ENCRYPTED]");

        // Redact Base64-encoded protected values (legacy format)
        result = LegacyProtectedRegex().Replace(result, "[PROTECTED]");

        return result;
    }

    /// <summary>
    /// Redacts sensitive information from an object for logging.
    /// Handles common types including strings, exceptions, and dictionaries.
    /// </summary>
    /// <param name="value">The value to redact.</param>
    /// <returns>A redacted representation suitable for logging.</returns>
    public static object? RedactForLogging(object? value)
    {
        return value switch
        {
            null => null,
            string s => Redact(s),
            Exception ex => RedactException(ex),
            IDictionary<string, object> dict => RedactDictionary(dict),
            _ => value
        };
    }

    /// <summary>
    /// Creates a redacted version of an exception message.
    /// </summary>
    private static string RedactException(Exception ex)
    {
        var message = Redact(ex.Message);
        if (ex.InnerException != null)
        {
            message += $" -> {Redact(ex.InnerException.Message)}";
        }
        return message;
    }

    /// <summary>
    /// Creates a redacted copy of a dictionary.
    /// </summary>
    private static IDictionary<string, object> RedactDictionary(IDictionary<string, object> dict)
    {
        var result = new Dictionary<string, object>(dict.Count);
        foreach (var kvp in dict)
        {
            // Check if key suggests sensitive data
            var key = kvp.Key.ToLowerInvariant();
            if (IsSensitiveKey(key))
            {
                result[kvp.Key] = MaskedValue;
            }
            else if (kvp.Value is string s)
            {
                result[kvp.Key] = Redact(s);
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        return result;
    }

    /// <summary>
    /// Determines if a key name suggests sensitive data.
    /// </summary>
    private static bool IsSensitiveKey(string key)
    {
        return key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("key", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("connectionstring", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("apikey", StringComparison.OrdinalIgnoreCase);
    }

    // Regex patterns for sensitive data detection

    [GeneratedRegex(@"SharedAccessKey=[^;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SharedAccessKeyRegex();

    [GeneratedRegex(@"SharedAccessSignature=[^;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SharedAccessSignatureRegex();

    [GeneratedRegex(@"AccountKey=[^;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AccountKeyRegex();

    [GeneratedRegex(@"(password|pwd|passwd)=[^;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PasswordRegex();

    [GeneratedRegex(@"Endpoint=([^;]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EndpointRegex();

    [GeneratedRegex(@"(api[_-]?key|apikey|x-api-key)[=:\s]+\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ApiKeyRegex();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+", RegexOptions.Compiled)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"ENC:V2:[A-Za-z0-9+/=]+", RegexOptions.Compiled)]
    private static partial Regex EncryptedValueRegex();

    [GeneratedRegex(@"PROTECTED:[A-Za-z0-9+/=]+", RegexOptions.Compiled)]
    private static partial Regex LegacyProtectedRegex();
}
