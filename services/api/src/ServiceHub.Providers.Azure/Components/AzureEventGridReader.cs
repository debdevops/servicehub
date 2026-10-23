using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;

namespace ServiceHub.Providers.Azure.Components;

/// <summary>
/// Reads Event Grid event subscriptions through Azure Resource Manager and their dead-letter blobs through Blob storage.
/// <c>GET</c> only, by construction: <see cref="IAzureRestClient"/> has no other verb.
/// </summary>
internal sealed partial class AzureEventGridReader : IAzureEventGridReader
{
    private const string ApiVersion = "2022-06-15";

    /// <summary>How many topics (and, separately, system topics) of one subscription are looked into.</summary>
    internal const int MaxTopicsPerSubscription = 20;

    /// <summary>How much of a blob is read to find its reason: the head of the file is enough, and a large event is never pulled whole.</summary>
    internal const int MaxBlobBytes = 256 * 1024;

    private readonly IAzureRestClient _rest;

    public AzureEventGridReader(IAzureRestClient rest) => _rest = rest ?? throw new ArgumentNullException(nameof(rest));

    public Task<Listing<string>> ListSubscriptionsAsync(Namespace ns, CancellationToken ct) => AzureArm.ListSubscriptionsAsync(_rest, ns, ct);

    public async Task<Listing<DeadLetterContainer>> ListDeadLetterContainersAsync(Namespace ns, string subscriptionId, int max, CancellationToken ct)
    {
        var sub = $"/subscriptions/{Uri.EscapeDataString(subscriptionId)}";
        var listPaths = new List<string> { $"{sub}/providers/Microsoft.EventGrid/eventSubscriptions" };
        var more = false;
        var incomplete = false;

        // Event subscriptions on a topic or a system topic are listed under that topic, not under the subscription.
        foreach (var kind in new[] { "topics", "systemTopics" })
        {
            try
            {
                using var doc = AzureArm.Parse(await _rest.GetTextAsync(ns, AzureArm.Url($"{sub}/providers/Microsoft.EventGrid/{kind}", ApiVersion), AzureAudience.Management, null, ct).ConfigureAwait(false));
                var ids = AzureArm.Values(doc).Select(v => AzureArm.Text(v, "id")).Where(id => id is not null && id.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase)).Select(id => id!).ToList();
                // A topic's event subscriptions are listed under the provider path; a system topic's are its child resource.
                listPaths.AddRange(ids.Take(MaxTopicsPerSubscription).Select(id => kind == "topics" ? $"{id}/providers/Microsoft.EventGrid/eventSubscriptions" : $"{id}/eventSubscriptions"));
                more |= ids.Count > MaxTopicsPerSubscription || AzureArm.HasNext(doc);
            }
            catch (AzureRestException)
            {
                // One kind of topic that cannot be listed leaves the result incomplete; the others are still read.
                incomplete = true;
            }
        }

        var found = new Dictionary<string, DeadLetterContainer>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in listPaths)
        {
            try
            {
                using var doc = AzureArm.Parse(await _rest.GetTextAsync(ns, AzureArm.Url(path, ApiVersion), AzureAudience.Management, null, ct).ConfigureAwait(false));
                foreach (var container in AzureArm.Values(doc).Select(ToContainer).Where(c => c is not null))
                {
                    found.TryAdd($"{container!.Account}/{container.Container}", container);
                }

                more |= AzureArm.HasNext(doc);
            }
            catch (AzureRestException)
            {
                incomplete = true;
            }
        }

        var ordered = found.Values.OrderBy(c => c.Account, StringComparer.Ordinal).ThenBy(c => c.Container, StringComparer.Ordinal).ToList();
        return new Listing<DeadLetterContainer>([.. ordered.Take(max)], more || ordered.Count > max, incomplete);
    }

    public async Task<Listing<DeadLetterBlob>> ListBlobsAsync(Namespace ns, string account, string container, int max, CancellationToken ct)
    {
        var url = new Uri($"https://{account}.blob.core.windows.net/{container}?restype=container&comp=list&maxresults={max.ToString(CultureInfo.InvariantCulture)}");
        var text = await _rest.GetTextAsync(ns, url, AzureAudience.Storage, null, ct).ConfigureAwait(false);
        if (text is null)
        {
            return new Listing<DeadLetterBlob>([], false);
        }

        var doc = ParseXml(text);
        var blobs = doc.Descendants("Blob").Select(b => ToBlob(account, container, b)).Where(b => b is not null).Select(b => b!).ToList();
        var next = doc.Descendants("NextMarker").FirstOrDefault()?.Value;
        return new Listing<DeadLetterBlob>([.. blobs.Take(max)], blobs.Count > max || !string.IsNullOrEmpty(next));
    }

    public async Task<DeadLetterBlobRead?> GetBlobAsync(Namespace ns, string account, string container, string blobName, CancellationToken ct)
    {
        var url = new Uri($"https://{account}.blob.core.windows.net/{container}/{string.Join('/', blobName.Split('/').Select(Uri.EscapeDataString))}");
        var text = await _rest.GetTextAsync(ns, url, AzureAudience.Storage, MaxBlobBytes, ct).ConfigureAwait(false);
        if (text is null)
        {
            return null;
        }

        // Only these few fields are picked out of the text. The event's data is never parsed, kept or shown, and a blob cut short by the
        // size cap still gives its reason (the fields sit at the start of each event record).
        return new DeadLetterBlobRead(
            new DeadLetterBlob(account, container, blobName, null, null),
            Field("deadLetterReason", text), Field("lastDeliveryOutcome", text),
            int.TryParse(Field("deliveryAttempts", text, quoted: false), CultureInfo.InvariantCulture, out var attempts) ? attempts : null,
            int.TryParse(Field("lastHttpStatusCode", text, quoted: false), CultureInfo.InvariantCulture, out var status) ? status : null,
            DateTimeOffset.TryParse(Field("lastDeliveryAttemptTime", text) ?? Field("publishTime", text), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at.ToUniversalTime() : null);
    }

    private static DeadLetterContainer? ToContainer(JsonElement subscription)
    {
        var props = AzureArm.Child(subscription, "properties");
        var destination = AzureArm.Child(props, "deadLetterDestination");
        if (destination.ValueKind != JsonValueKind.Object)
        {
            destination = AzureArm.Child(AzureArm.Child(props, "deadLetterWithResourceIdentity"), "deadLetterDestination");
        }

        if (!string.Equals(AzureArm.Text(destination, "endpointType"), "StorageBlob", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var inner = AzureArm.Child(destination, "properties");
        var resourceId = AzureArm.Text(inner, "resourceId");
        var container = AzureArm.Text(inner, "blobContainerName");
        var match = resourceId is null ? Match.Empty : AccountRegex().Match(resourceId);
        return match.Success && container is not null && ContainerRegex().IsMatch(container)
            ? new DeadLetterContainer(match.Groups["a"].Value.ToLowerInvariant(), container, resourceId)
            : null;
    }

    private static DeadLetterBlob? ToBlob(string account, string container, XElement blob)
    {
        var name = blob.Element("Name")?.Value;
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var props = blob.Element("Properties");
        return new DeadLetterBlob(
            account, container, name,
            DateTimeOffset.TryParse(props?.Element("Last-Modified")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t.ToUniversalTime() : null,
            long.TryParse(props?.Element("Content-Length")?.Value, CultureInfo.InvariantCulture, out var length) ? length : null);
    }

    // DTD processing is off so a hostile response cannot pull in an external entity.
    private static XElement ParseXml(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader).Root ?? new XElement("Empty");
    }

    private static string? Field(string name, string text, bool quoted = true)
    {
        var pattern = quoted ? $"\"{name}\"\\s*:\\s*\"(?<v>[^\"]{{1,200}})\"" : $"\"{name}\"\\s*:\\s*(?<v>\\d{{1,9}})";
        var m = Regex.Match(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return m.Success ? m.Groups["v"].Value : null;
    }

    [GeneratedRegex(@"/providers/Microsoft\.Storage/storageAccounts/(?<a>[A-Za-z0-9]{3,24})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex AccountRegex();

    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]{1,61})[a-z0-9]$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex ContainerRegex();
}
