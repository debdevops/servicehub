using System.Globalization;
using System.Text.Json;
using ServiceHub.Core.Entities;

namespace ServiceHub.Providers.Azure.Components;

/// <summary>The few Azure Resource Manager reads every Azure component source shares, and the small JSON helpers they use.</summary>
internal static class AzureArm
{
    private const string Host = "https://management.azure.com";

    /// <summary>The most subscriptions one listing looks in, so a large tenant is a bounded number of calls.</summary>
    public const int MaxSubscriptions = 20;

    /// <summary>The subscription ids the identity can see (the first page; <c>More</c> says there are more).</summary>
    public static async Task<Listing<string>> ListSubscriptionsAsync(IAzureRestClient rest, Namespace ns, CancellationToken ct)
    {
        var text = await rest.GetTextAsync(ns, new Uri($"{Host}/subscriptions?api-version=2022-12-01"), AzureAudience.Management, null, ct).ConfigureAwait(false);
        using var doc = Parse(text);
        var ids = Values(doc).Select(v => Text(v, "subscriptionId")).Where(id => id is not null).Select(id => id!).ToList();
        return new Listing<string>([.. ids.Take(MaxSubscriptions)], ids.Count > MaxSubscriptions || HasNext(doc));
    }

    /// <summary>An ARM address for a path that starts with <c>/subscriptions</c>, with its query.</summary>
    public static Uri Url(string path, string apiVersion, params (string Key, string Value)[] query)
    {
        var parts = new List<string> { $"api-version={apiVersion}" };
        parts.AddRange(query.Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value)}"));
        return new Uri($"{Host}{path}?{string.Join('&', parts)}");
    }

    /// <summary>Parses a body; a null body (404) reads as an empty document.</summary>
    public static JsonDocument Parse(string? text) => JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);

    /// <summary>The items of a standard ARM list (<c>value</c>), or none.</summary>
    public static IEnumerable<JsonElement> Values(JsonDocument doc) =>
        doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray()
            : [];

    /// <summary>Whether the list says another page follows.</summary>
    public static bool HasNext(JsonDocument doc) =>
        doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("nextLink", out var n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(n.GetString());

    /// <summary>A string property, or null.</summary>
    public static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    /// <summary>A nested object property, or an undefined element when it is missing.</summary>
    public static JsonElement Child(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var p) ? p : default;

    /// <summary>A date-time string property, or null.</summary>
    public static DateTimeOffset? Time(JsonElement element, string name) =>
        DateTimeOffset.TryParse(Text(element, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t.ToUniversalTime() : null;
}
