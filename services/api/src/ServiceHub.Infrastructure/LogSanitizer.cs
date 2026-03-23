namespace ServiceHub.Infrastructure;

/// <summary>
/// Sanitizes user-supplied and externally-sourced strings before they are written
/// to log entries. Strips CR and LF characters to prevent log-injection (CWE-117).
/// </summary>
internal static class LogSanitizer
{
    /// <summary>
    /// Removes carriage-return and newline characters from a user-supplied value.
    /// Returns "[null]" for null input so that log entries remain readable.
    /// </summary>
    internal static string Sanitize(string? value)
        => value is null
            ? "[null]"
            : value.Replace("\r", string.Empty, StringComparison.Ordinal)
                   .Replace("\n", string.Empty, StringComparison.Ordinal);
}
