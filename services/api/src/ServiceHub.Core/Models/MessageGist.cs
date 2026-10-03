using System.Text.Json;

namespace ServiceHub.Core.Models;

/// <summary>
/// What a person needs to tell one message from the next in a grid: a one-line snippet of the body, how it is
/// labelled, and a few of its own properties. All of it comes from what was recorded for the message; none of it is guessed.
/// </summary>
/// <param name="Preview">The start of the body, whitespace collapsed to one line. Null when the message had no body.</param>
/// <param name="ContentType">The body's content type, as recorded.</param>
/// <param name="CorrelationId">The message's correlation id, when it had one.</param>
/// <param name="SessionId">The message's session id, when it had one.</param>
/// <param name="Properties">Up to <see cref="MaxProperties"/> of the application properties, as short text. Null when there are none.</param>
public sealed record MessageGist(
    string? Preview,
    string? ContentType,
    string? CorrelationId,
    string? SessionId,
    IReadOnlyDictionary<string, string>? Properties)
{
    /// <summary>How many application properties a grid row carries.</summary>
    public const int MaxProperties = 3;

    private const int MaxPreview = 140;
    private const int MaxValue = 48;

    /// <summary>Builds the gist from a stored body preview and the stored properties JSON. Null when there is nothing to show.</summary>
    public static MessageGist? From(string? bodyPreview, string? contentType, string? correlationId, string? sessionId, string? propertiesJson)
    {
        var preview = OneLine(bodyPreview, MaxPreview);
        var props = ReadProperties(propertiesJson);
        if (preview is null && contentType is null && correlationId is null && sessionId is null && props is null)
        {
            return null;
        }

        return new MessageGist(preview, contentType, correlationId, sessionId, props);
    }

    /// <summary>Properties already shown as their own tag, or set by the cloud's plumbing rather than the sender.</summary>
    private static bool IsNoise(string name) =>
        name.Equals("correlationId", StringComparison.OrdinalIgnoreCase)
        || name.Equals("sessionId", StringComparison.OrdinalIgnoreCase)
        || name.Equals("messageId", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("CloudPubSub", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("googclient_", StringComparison.OrdinalIgnoreCase);

    private static string? OneLine(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= max ? collapsed : collapsed[..max] + "…";
    }

    private static Dictionary<string, string>? ReadProperties(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var found = new Dictionary<string, string>();
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (IsNoise(p.Name))
                {
                    continue;
                }

                // Only plain values read well in a cell; nested objects are for the details panel.
                var value = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => p.Value.GetRawText(),
                    _ => null,
                };
                var shown = OneLine(value, MaxValue);
                if (shown is null)
                {
                    continue;
                }

                found[p.Name] = shown;
                if (found.Count == MaxProperties)
                {
                    break;
                }
            }

            return found.Count == 0 ? null : found;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
