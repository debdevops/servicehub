using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ServiceHub.Core.Entities;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// Extracts normalised signal tokens from a <see cref="DlqMessage"/>
/// for use by the deterministic and heuristic classifiers, and structured
/// <see cref="MessageFeatures"/> for clustering/baselining models.
/// </summary>
internal static partial class SignalExtractor
{
    /// <summary>
    /// Builds a combined, lower-cased text block from the reason, error description,
    /// body preview, and application properties of the message. Application properties
    /// are included because some providers (notably AWS SQS's native redrive-policy
    /// dead-lettering) never populate DeadLetterReason/DeadLetterErrorDescription — the
    /// only failure signal available is whatever the producer put in message attributes.
    /// </summary>
    internal static string CombinedText(DlqMessage msg)
    {
        return $"{msg.DeadLetterReason} {msg.DeadLetterErrorDescription} {msg.BodyPreview} {msg.ApplicationPropertiesJson}"
            .ToLowerInvariant();
    }

    /// <summary>
    /// Builds the versioned <see cref="MessageFeatures"/> record for a peeked message.
    /// Never throws — malformed body/property JSON degrades gracefully to empty values.
    /// </summary>
    internal static MessageFeatures ExtractFeatures(DlqMessage msg)
    {
        var enqueuedUtc = msg.EnqueuedTimeUtc.UtcDateTime;
        var secondsSinceEnqueued = Math.Max(0, (msg.DetectedAtUtc - msg.EnqueuedTimeUtc).TotalSeconds);
        var timeToDeadletterSeconds = msg.DeadLetterTimeUtc.HasValue
            ? Math.Max(0, (msg.DeadLetterTimeUtc.Value - msg.EnqueuedTimeUtc).TotalSeconds)
            : 0;

        var (payloadShape, topLevelProperties) = AnalyseBody(msg.BodyPreview);
        var exceptionType = ExtractExceptionType(msg.DeadLetterErrorDescription, msg.DeadLetterReason);

        return new MessageFeatures(
            DeliveryCount: msg.DeliveryCount,
            BodySizeBytes: msg.MessageSize,
            TimeToDeadletterSeconds: timeToDeadletterSeconds,
            SecondsSinceEnqueued: secondsSinceEnqueued,
            HourOfDay: enqueuedUtc.Hour,
            DayOfWeek: (int)enqueuedUtc.DayOfWeek,
            PropertyCount: CountProperties(msg.ApplicationPropertiesJson),
            Provider: msg.CloudProvider,
            EntityName: msg.EntityName,
            DeadletterReason: msg.DeadLetterReason ?? string.Empty,
            ExceptionType: exceptionType,
            ContentType: msg.ContentType ?? string.Empty,
            PayloadShape: payloadShape,
            ErrorTextNormalised: NormaliseErrorText(msg.DeadLetterReason, msg.DeadLetterErrorDescription),
            SchemaFingerprint: ComputeSchemaFingerprint(topLevelProperties),
            FeatureVersion: MessageFeatures.CurrentFeatureVersion);
    }

    private static int CountProperties(string? applicationPropertiesJson)
    {
        if (string.IsNullOrWhiteSpace(applicationPropertiesJson))
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(applicationPropertiesJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? doc.RootElement.EnumerateObject().Count()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static (string Shape, IReadOnlyList<string> TopLevelProperties) AnalyseBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return (MessageFeatures.ShapeText, []);

        if (ContainsBinaryIndicators(body))
            return (MessageFeatures.ShapeBinary, []);

        var trimmed = body.Trim();

        if (trimmed.StartsWith('{') && TryParseJson(trimmed, out var objDoc))
        {
            using (objDoc)
            {
                if (objDoc!.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var props = objDoc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
                    return (MessageFeatures.ShapeJsonObject, props);
                }
            }
        }

        if (trimmed.StartsWith('[') && TryParseJson(trimmed, out var arrDoc))
        {
            using (arrDoc)
            {
                if (arrDoc!.RootElement.ValueKind == JsonValueKind.Array)
                    return (MessageFeatures.ShapeJsonArray, []);
            }
        }

        if (trimmed.StartsWith('<'))
            return (MessageFeatures.ShapeXml, []);

        return (MessageFeatures.ShapeText, []);
    }

    private static bool TryParseJson(string text, out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }

    private static bool ContainsBinaryIndicators(string s)
    {
        foreach (var c in s)
        {
            if (c == '�')
                return true;
            if (c < 0x20 && c != '\t' && c != '\n' && c != '\r')
                return true;
        }
        return false;
    }

    private static string ExtractExceptionType(string? description, string? reason)
    {
        var haystack = $"{description} {reason}";
        var match = ExceptionTypePattern().Match(haystack);
        return match.Success ? match.Value : string.Empty;
    }

    private static string ComputeSchemaFingerprint(IReadOnlyList<string> topLevelProperties)
    {
        var normalised = string.Join('|', topLevelProperties.OrderBy(p => p, StringComparer.Ordinal));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalised));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string NormaliseErrorText(string? reason, string? description)
    {
        var text = $"{reason} {description}".Trim().ToLowerInvariant();

        text = EmailPattern().Replace(text, "<email>");
        text = UrlPattern().Replace(text, "<url>");
        text = IpAddressPattern().Replace(text, "<ip>");
        text = IsoTimestampPattern().Replace(text, "<timestamp>");
        text = GuidPattern().Replace(text, "<guid>");
        text = UnixPathPattern().Replace(text, "<path>");
        text = WindowsPathPattern().Replace(text, "<path>");
        text = LongHexPattern().Replace(text, "<hex>");
        text = LongIntegerPattern().Replace(text, "<num>");
        text = WhitespacePattern().Replace(text, " ").Trim();

        return text;
    }

    [GeneratedRegex(@"\b[A-Za-z_][A-Za-z0-9_.]*Exception\b", RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex ExceptionTypePattern();

    [GeneratedRegex(@"[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}", RegexOptions.IgnoreCase | RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex IpAddressPattern();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}[t ]\d{2}:\d{2}:\d{2}(\.\d+)?(z|[+-]\d{2}:?\d{2})?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex IsoTimestampPattern();

    [GeneratedRegex(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"(?<![\w])/(?:[\w.\-]+/)+[\w.\-]*", RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex UnixPathPattern();

    [GeneratedRegex(@"(?<![\w])[a-z]:\\(?:[^\s\\]+\\)*[^\s\\]*", RegexOptions.IgnoreCase | RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex WindowsPathPattern();

    [GeneratedRegex(@"\b[0-9a-f]{9,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex LongHexPattern();

    [GeneratedRegex(@"\b\d{4,}\b", RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex LongIntegerPattern();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex WhitespacePattern();
}
