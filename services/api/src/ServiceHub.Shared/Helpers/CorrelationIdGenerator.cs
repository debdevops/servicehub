using System.Security.Cryptography;

namespace ServiceHub.Shared.Helpers;

/// <summary>
/// Provides functionality for generating and validating correlation IDs.
/// Correlation IDs are used to track requests across service boundaries.
/// </summary>
public static class CorrelationIdGenerator
{
    /// <summary>
    /// The prefix used for generated correlation IDs.
    /// </summary>
    public const string Prefix = "sh-";

    /// <summary>
    /// The default HTTP header name for correlation ID.
    /// Can be overridden via configuration.
    /// </summary>
    public const string DefaultHeaderName = "X-Correlation-Id";

    /// <summary>
    /// The length of the random portion of the correlation ID.
    /// </summary>
    private const int RandomPartLength = 24;

    /// <summary>
    /// Generates a new correlation ID.
    /// Format: sh-{timestamp}-{random}
    /// </summary>
    /// <returns>A unique correlation ID.</returns>
    public static string Generate()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x");
        var randomPart = GenerateRandomString(RandomPartLength);
        return $"{Prefix}{timestamp}-{randomPart}";
    }

    /// <summary>
    /// Generates a new correlation ID with a custom prefix.
    /// </summary>
    /// <param name="customPrefix">The custom prefix to use.</param>
    /// <returns>A unique correlation ID with the custom prefix.</returns>
    public static string Generate(string customPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customPrefix);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x");
        var randomPart = GenerateRandomString(RandomPartLength);
        return $"{customPrefix}-{timestamp}-{randomPart}";
    }

    /// <summary>
    /// Validates whether the provided string is a valid correlation ID format.
    /// </summary>
    /// <param name="correlationId">The correlation ID to validate.</param>
    /// <returns>True if the correlation ID is valid; otherwise, false.</returns>
    public static bool IsValid(string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return false;
        }

        // Must have at least the prefix and some content
        if (correlationId.Length < 10)
        {
            return false;
        }

        // Must contain only valid characters (alphanumeric and hyphens)
        return correlationId.All(c => char.IsLetterOrDigit(c) || c == '-');
    }

    /// <summary>
    /// Gets a valid correlation ID, either from the provided value or by generating a new one.
    /// </summary>
    /// <param name="existingCorrelationId">An existing correlation ID to validate and use.</param>
    /// <returns>A valid correlation ID.</returns>
    public static string GetOrGenerate(string? existingCorrelationId)
    {
        return IsValid(existingCorrelationId) ? existingCorrelationId! : Generate();
    }

    /// <summary>
    /// Generates a cryptographically secure random string.
    /// </summary>
    /// <param name="length">The length of the random string.</param>
    /// <returns>A random string of the specified length.</returns>
    private static string GenerateRandomString(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        Span<byte> randomBytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(randomBytes);

        return string.Create(length, (randomBytes.ToArray(), chars), static (span, state) =>
        {
            var (bytes, charSet) = state;
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = charSet[bytes[i] % charSet.Length];
            }
        });
    }
}
