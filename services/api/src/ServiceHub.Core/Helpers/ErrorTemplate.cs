using System.Text.RegularExpressions;

namespace ServiceHub.Core.Helpers;

/// <summary>
/// Reduces a dead-letter error message to its <i>shape</i>: the same sentence with the parts that change from message to
/// message (ids, times, numbers, quoted values, URL paths) replaced by fixed placeholders.
/// </summary>
/// <remarks>
/// Two messages that fail for the same cause must give the same template; two that fail for different causes must not.
/// Pure and deterministic — no clock, no state — so a template can be hashed into a failure signature (design
/// <c>10-SPLIT-SIGNATURES-DESIGN.md</c> §5). Changing any rule here changes signature identity for every signature that
/// uses it, so treat an edit as a versioned algorithm change, like <see cref="DeliveryCountBucket"/>.
/// </remarks>
public static partial class ErrorTemplate
{
    /// <summary>Longest template returned. Stack traces add no identity and vary by line.</summary>
    public const int MaxLength = 200;

    /// <summary>Input is cut here before any pattern runs, so a huge message cannot make matching slow.</summary>
    private const int MaxInput = 2000;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>Returns the template for <paramref name="errorText"/>, or an empty string when there is no text.</summary>
    public static string Normalize(string? errorText)
    {
        if (string.IsNullOrWhiteSpace(errorText))
        {
            return string.Empty;
        }

        var text = errorText.Length > MaxInput ? errorText[..MaxInput] : errorText;
        text = text.ToLowerInvariant();

        try
        {
            text = Url().Replace(text, "${scheme}${host}");
            text = Guid().Replace(text, "<id>");
            text = Time().Replace(text, "<time>");
            text = Hex().Replace(text, "<hex>");
            text = SingleQuoted().Replace(text, "<q>");
            text = DoubleQuoted().Replace(text, "<q>");
            text = Digits().Replace(text, "<n>");
            text = Whitespace().Replace(text, " ").Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            // Pathological input: fall back to the coarsest honest template rather than throw from a hashing path.
            return "<unreadable>";
        }

        return text.Length > MaxLength ? text[..MaxLength].TrimEnd() : text;
    }

    [GeneratedRegex(@"(?<scheme>[a-z][a-z0-9+.\-]*://)(?<host>[^/\s?#:]+)(?::\d+)?[^\s]*", RegexOptions.None, 250)]
    private static partial Regex Url();

    [GeneratedRegex(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.None, 250)]
    private static partial Regex Guid();

    // ISO-8601 / RFC 3339 date-times and bare dates or times of day.
    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}(?:[t ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:z|[+\-]\d{2}:?\d{2})?)?\b|\b\d{1,2}:\d{2}:\d{2}(?:\.\d+)?\b", RegexOptions.None, 250)]
    private static partial Regex Time();

    // Eight or more hex digits that include at least one digit, so ordinary words such as "feedface" survive.
    [GeneratedRegex(@"\b(?=[0-9a-f]*\d)[0-9a-f]{8,}\b", RegexOptions.None, 250)]
    private static partial Regex Hex();

    // A quote is a quote only when it is not an apostrophe inside a word ("can't").
    [GeneratedRegex(@"(?<![\p{L}\p{N}])'[^'\r\n]{1,100}'(?![\p{L}\p{N}])", RegexOptions.None, 250)]
    private static partial Regex SingleQuoted();

    [GeneratedRegex("\"[^\"\\r\\n]{1,100}\"", RegexOptions.None, 250)]
    private static partial Regex DoubleQuoted();

    [GeneratedRegex(@"\d+", RegexOptions.None, 250)]
    private static partial Regex Digits();

    [GeneratedRegex(@"\s+", RegexOptions.None, 250)]
    private static partial Regex Whitespace();
}
