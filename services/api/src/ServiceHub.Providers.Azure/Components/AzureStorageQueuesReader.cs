using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ServiceHub.Core.Entities;

namespace ServiceHub.Providers.Azure.Components;

/// <summary>
/// Finds storage accounts through Azure Resource Manager and reads their poison queues through Queue storage. <c>GET</c> only, by
/// construction: <see cref="IAzureRestClient"/> has no other verb. Peeking (<c>peekonly=true</c>) leaves every message as it was.
/// </summary>
internal sealed partial class AzureStorageQueuesReader : IAzureStorageQueuesReader
{
    private const string ApiVersion = "2023-01-01";

    /// <summary>The suffix that marks a poison queue.</summary>
    internal const string PoisonSuffix = "-poison";

    /// <summary>How many storage accounts of one subscription are looked into.</summary>
    internal const int MaxAccountsPerSubscription = 20;

    /// <summary>Queue storage peeks at most this many messages in one call.</summary>
    internal const int MaxPeek = 32;

    private readonly IAzureRestClient _rest;

    public AzureStorageQueuesReader(IAzureRestClient rest) => _rest = rest ?? throw new ArgumentNullException(nameof(rest));

    public Task<Listing<string>> ListSubscriptionsAsync(Namespace ns, CancellationToken ct) => AzureArm.ListSubscriptionsAsync(_rest, ns, ct);

    public async Task<Listing<PoisonQueue>> ListPoisonQueuesAsync(Namespace ns, string subscriptionId, int max, CancellationToken ct)
    {
        var sub = $"/subscriptions/{Uri.EscapeDataString(subscriptionId)}";
        using var accounts = AzureArm.Parse(await _rest.GetTextAsync(ns, AzureArm.Url($"{sub}/providers/Microsoft.Storage/storageAccounts", ApiVersion), AzureAudience.Management, null, ct).ConfigureAwait(false));
        var found = AzureArm.Values(accounts)
            .Select(v => (Id: AzureArm.Text(v, "id"), Name: AzureArm.Text(v, "name")))
            .Where(a => a.Name is not null && AccountRegex().IsMatch(a.Name))
            .ToList();
        var more = found.Count > MaxAccountsPerSubscription || AzureArm.HasNext(accounts);
        var incomplete = false;
        var queues = new List<PoisonQueue>();
        foreach (var account in found.Take(MaxAccountsPerSubscription))
        {
            try
            {
                var text = await _rest.GetTextAsync(ns, new Uri($"https://{account.Name}.queue.core.windows.net/?comp=list&maxresults=500"), AzureAudience.Queue, null, ct).ConfigureAwait(false);
                if (text is null)
                {
                    continue;
                }

                var doc = ParseXml(text);
                queues.AddRange(doc.Descendants("Queue").Select(q => q.Element("Name")?.Value)
                    .Where(n => n is not null && n.EndsWith(PoisonSuffix, StringComparison.Ordinal) && QueueRegex().IsMatch(n))
                    .Select(n => new PoisonQueue(account.Name!, n!, account.Id)));
                more |= !string.IsNullOrEmpty(doc.Descendants("NextMarker").FirstOrDefault()?.Value);
            }
            catch (AzureRestException)
            {
                // An account whose queues this identity may not read leaves the result incomplete; the others are still read.
                incomplete = true;
            }
        }

        var ordered = queues.OrderBy(q => q.Account, StringComparer.Ordinal).ThenBy(q => q.Queue, StringComparer.Ordinal).ToList();
        return new Listing<PoisonQueue>([.. ordered.Take(max)], more || ordered.Count > max, incomplete);
    }

    public async Task<Listing<PoisonMessage>> ListMessagesAsync(Namespace ns, string account, string queue, int max, CancellationToken ct)
    {
        var count = Math.Clamp(max, 1, MaxPeek);
        var url = new Uri($"https://{account}.queue.core.windows.net/{queue}/messages?peekonly=true&numofmessages={count.ToString(CultureInfo.InvariantCulture)}");
        var text = await _rest.GetTextAsync(ns, url, AzureAudience.Queue, null, ct).ConfigureAwait(false);
        if (text is null)
        {
            return new Listing<PoisonMessage>([], false);
        }

        // Only the id, the time and the dequeue count are read. MessageText is never read, kept or shown.
        var items = ParseXml(text).Descendants("QueueMessage").Select(m => new PoisonMessage(
            account, queue, m.Element("MessageId")?.Value ?? string.Empty,
            DateTimeOffset.TryParse(m.Element("InsertionTime")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at.ToUniversalTime() : null,
            int.TryParse(m.Element("DequeueCount")?.Value, CultureInfo.InvariantCulture, out var n) ? n : null))
            .Where(m => m.MessageId.Length > 0).ToList();
        return new Listing<PoisonMessage>(items, items.Count >= count);
    }

    // DTD processing is off so a hostile response cannot pull in an external entity.
    private static XElement ParseXml(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader).Root ?? new XElement("Empty");
    }

    [GeneratedRegex(@"^[a-z0-9]{3,24}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex AccountRegex();

    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]{1,61})[a-z0-9]$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex QueueRegex();
}
