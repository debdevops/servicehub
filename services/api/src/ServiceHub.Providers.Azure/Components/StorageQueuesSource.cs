using System.Text.RegularExpressions;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;

namespace ServiceHub.Providers.Azure.Components;

/// <summary>
/// Azure Storage Queues poison queues. <b>Read-only</b>: it finds the queues whose name ends in <c>-poison</c> (where Azure Functions and
/// WebJobs move a message that keeps failing) in every storage account the connection's Entra identity can see, and peeks at them. A peek
/// hides nothing, removes nothing and does not raise a message's dequeue count. The message text is never read, kept or shown.
/// </summary>
/// <remarks>Needs the Reader role (to find storage accounts) and Storage Queue Data Reader on the accounts, signed in with Entra.</remarks>
public sealed partial class StorageQueuesSource : IComponentSource
{
    /// <summary>The catalogue component this serves.</summary>
    public const string Key = "storage-queues";

    /// <summary>Without a chosen place, at most this many queues are peeked, so one listing stays a bounded number of calls.</summary>
    internal const int MaxQueuesPerListing = 20;

    private const string Status = "POISONED";
    private const string Needs = "the Reader role on the subscription and Storage Queue Data Reader on the storage account";

    private readonly IAzureStorageQueuesReader _reader;

    // The reader is this module's own seam to Azure, so a test can stand in for it; only the public constructor is for the host.
    internal StorageQueuesSource(IAzureStorageQueuesReader reader) =>
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <summary>Creates the source over Azure Resource Manager and Queue storage.</summary>
    public StorageQueuesSource(IAzureRestClient rest)
        : this(new AzureStorageQueuesReader(rest))
    {
    }

    /// <inheritdoc />
    public string ComponentKey => Key;

    /// <inheritdoc />
    public CloudProviderType Provider => CloudProviderType.Azure;

    /// <inheritdoc />
    public ComponentSourceCapabilities Capabilities { get; } = new(ReasonInList: false, ReadsPayload: false);

    /// <inheritdoc />
    public async Task<Result> ValidateAccessAsync(Namespace ns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        try
        {
            var subscriptions = await _reader.ListSubscriptionsAsync(ns, ct).ConfigureAwait(false);
            if (subscriptions.Items.Count == 0)
            {
                return Result.Failure(Error.Forbidden("Azure.StorageQueues.NotEnabled", "Not enabled for this connection: its identity cannot see any Azure subscription. Give it the Reader role on the subscription that holds the storage accounts."));
            }

            var queues = await _reader.ListPoisonQueuesAsync(ns, subscriptions.Items[0], 1, ct).ConfigureAwait(false);
            if (queues.Items.Count > 0)
            {
                // The Reader role finds the account; reading its queues needs a data role, which only a peek proves.
                await _reader.ListMessagesAsync(ns, queues.Items[0].Account, queues.Items[0].Queue, 1, ct).ConfigureAwait(false);
            }

            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // A cloud failure becomes a result the screen can show, never an exception.
        catch (Exception ex)
        {
            return Result.Failure(Fail(ex));
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public async Task<Result<SourcePlacesPage>> ListPlacesAsync(Namespace ns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(ComponentSourceLimits.ListingBudget);
        try
        {
            var found = await FindQueuesAsync(ns, ComponentSourceLimits.MaxPlaces, budget.Token, ct).ConfigureAwait(false);
            if (found.Error is not null)
            {
                return Result.Failure<SourcePlacesPage>(found.Error);
            }

            var queues = found.Items.Take(ComponentSourceLimits.MaxPlaces).ToList();

            // One peek per queue says how much waits in it. A queue that cannot be peeked is still listed, just without the line.
            var peeks = await SourceReads.RunAsync(
                queues.Take(MaxQueuesPerListing), (q, token) => _reader.ListMessagesAsync(ns, q.Account, q.Queue, AzureStorageQueuesReader.MaxPeek, token),
                Fail, new Listing<PoisonMessage>([], false), budget.Token, ct).ConfigureAwait(false);
            var details = queues.Take(MaxQueuesPerListing).Zip(peeks, (q, r) => (q, r)).ToDictionary(x => $"{x.q.Account}/{x.q.Queue}", x => x.r.Failed ? null : Describe(x.r.Value), StringComparer.Ordinal);

            return Result.Success(new SourcePlacesPage(
                [.. queues.Select(q => new SourcePlace(PlaceId(q.Account, q.Queue), $"{q.Queue} ({q.Account})", "poison queue", QueueLink(q), details.GetValueOrDefault($"{q.Account}/{q.Queue}")))],
                found.More || found.Items.Count > ComponentSourceLimits.MaxPlaces, found.Incomplete || peeks.Any(r => r.Failed)));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<SourcePlacesPage>(Error.ExternalService("Azure.StorageQueues.ReadTimedOut", "Reading Storage Queues took too long."));
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Failure<SourcePlacesPage>(Fail(ex));
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public async Task<Result<FailedItemsPage>> ListFailedAsync(Namespace ns, string? placeId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        var place = placeId is null ? null : PlaceRegex().Match(placeId.Length <= 256 ? placeId : string.Empty);
        if (placeId is not null && place is not { Success: true })
        {
            return Result.Failure<FailedItemsPage>(Error.Validation("Azure.StorageQueues.InvalidPlace", "That is not a poison queue."));
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(ComponentSourceLimits.ListingBudget);
        try
        {
            var reached = false;
            var incomplete = false;
            List<(string Account, string Queue)> queues;
            var links = new Dictionary<string, string?>(StringComparer.Ordinal);
            if (place is null)
            {
                var found = await FindQueuesAsync(ns, MaxQueuesPerListing + 1, budget.Token, ct).ConfigureAwait(false);
                if (found.Error is not null)
                {
                    return Result.Failure<FailedItemsPage>(found.Error);
                }

                queues = [.. found.Items.Take(MaxQueuesPerListing).Select(q => (q.Account, q.Queue))];
                foreach (var q in found.Items.Take(MaxQueuesPerListing))
                {
                    links[$"{q.Account}/{q.Queue}"] = QueueLink(q);
                }

                reached = found.More || found.Items.Count > MaxQueuesPerListing;
                incomplete = found.Incomplete;
            }
            else
            {
                queues = [(place.Groups["a"].Value, place.Groups["q"].Value)];
                await AddLinksAsync(ns, links, budget.Token, ct).ConfigureAwait(false);
            }

            var results = await SourceReads.RunAsync(
                queues, (q, token) => _reader.ListMessagesAsync(ns, q.Account, q.Queue, AzureStorageQueuesReader.MaxPeek, token),
                Fail, new Listing<PoisonMessage>([], false), budget.Token, ct).ConfigureAwait(false);

            incomplete |= results.Any(r => r.Failed);
            if (results.Count > 0 && results.All(r => r.Failed))
            {
                return Result.Failure<FailedItemsPage>(results.Select(r => r.Error).FirstOrDefault(e => e is not null) ?? Error.ExternalService("Azure.StorageQueues.ReadFailed", "Storage Queues could not be read."));
            }

            reached |= results.Any(r => r.Value.More);
            var ordered = results.SelectMany(r => r.Value.Items).OrderByDescending(m => m.InsertedUtc ?? DateTimeOffset.MinValue).ToList();
            reached |= ordered.Count > ComponentSourceLimits.MaxFailedItems;
            return Result.Success(new FailedItemsPage([.. ordered.Take(ComponentSourceLimits.MaxFailedItems).Select(m => ToItem(m, links.GetValueOrDefault($"{m.Account}/{m.Queue}")))], reached, incomplete));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<FailedItemsPage>(Error.ExternalService("Azure.StorageQueues.ReadTimedOut", "Reading Storage Queues took too long. Try one queue."));
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Failure<FailedItemsPage>(Fail(ex));
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public async Task<Result<FailedItemDetail?>> GetFailedAsync(Namespace ns, string itemId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        var m = itemId.Length <= 400 ? ItemRegex().Match(itemId) : Match.Empty;
        if (!m.Success)
        {
            return Result.Failure<FailedItemDetail?>(Error.Validation("Azure.StorageQueues.InvalidItem", "That is not a poison-queue message."));
        }

        try
        {
            var peek = await _reader.ListMessagesAsync(ns, m.Groups["a"].Value, m.Groups["q"].Value, AzureStorageQueuesReader.MaxPeek, ct).ConfigureAwait(false);
            var message = peek.Items.FirstOrDefault(x => string.Equals(x.MessageId, m.Groups["m"].Value, StringComparison.Ordinal));
            if (message is null)
            {
                return Result.Success<FailedItemDetail?>(null);
            }

            var item = ToItem(message, null);
            return Result.Success<FailedItemDetail?>(new FailedItemDetail(item, "Moved to the poison queue after too many failed attempts to process it", Cause(message), message.InsertedUtc, null));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            return Result.Failure<FailedItemDetail?>(Fail(ex));
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// A listing of one queue still links to it in the portal. Only a full discovery learns the account's resource id, so it is asked here;
    /// if that fails the rows simply have no link, the messages are still shown.
    /// </summary>
    private async Task AddLinksAsync(Namespace ns, Dictionary<string, string?> links, CancellationToken budget, CancellationToken caller)
    {
        try
        {
            var found = await FindQueuesAsync(ns, ComponentSourceLimits.MaxPlaces, budget, caller).ConfigureAwait(false);
            foreach (var q in found.Items)
            {
                links[$"{q.Account}/{q.Queue}"] = QueueLink(q);
            }
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        {
            // Only this read's own time budget ran out: show the messages without a link.
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A link is a convenience. Never fail the listing for it.
        }
#pragma warning restore CA1031
    }

    private sealed record Found(IReadOnlyList<PoisonQueue> Items, bool More, bool Incomplete, Error? Error);

    /// <summary>Every poison queue in every subscription the identity can see, sorted. A subscription that cannot be read leaves the result incomplete.</summary>
    private async Task<Found> FindQueuesAsync(Namespace ns, int max, CancellationToken budget, CancellationToken caller)
    {
        var subscriptions = await _reader.ListSubscriptionsAsync(ns, budget).ConfigureAwait(false);
        var results = await SourceReads.RunAsync(
            subscriptions.Items, (sub, token) => _reader.ListPoisonQueuesAsync(ns, sub, max, token), Fail, new Listing<PoisonQueue>([], false), budget, caller).ConfigureAwait(false);

        if (results.Count > 0 && results.All(r => r.Failed))
        {
            return new Found([], false, true, results.Select(r => r.Error).FirstOrDefault(e => e is not null) ?? Error.ExternalService("Azure.StorageQueues.ReadFailed", "Storage Queues could not be read."));
        }

        var all = results.SelectMany(r => r.Value.Items)
            .GroupBy(q => $"{q.Account}/{q.Queue}", StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(q => q.Account, StringComparer.Ordinal).ThenBy(q => q.Queue, StringComparer.Ordinal).ToList();
        return new Found(all, subscriptions.More || results.Any(r => r.Value.More), results.Any(r => r.Failed || r.Value.Incomplete), null);
    }

    private static FailedItem ToItem(PoisonMessage m, string? accountLink) =>
        new($"queue://{m.Account}/{m.Queue}/{m.MessageId}", PlaceId(m.Account, m.Queue), m.InsertedUtc, Status, ReasonCode: null,
            "Poison message: moved here after too many failed attempts", CopyKind.None, accountLink);

    // "3 messages, tried up to 5 times, newest 2026-11-15 15:09 UTC". More than one peek's worth reads "32+".
    private static string Describe(Listing<PoisonMessage> peek)
    {
        if (peek.Items.Count == 0)
        {
            return "Empty";
        }

        var count = peek.More ? $"{peek.Items.Count}+" : peek.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var parts = new List<string> { $"{count} message{(peek.Items.Count == 1 && !peek.More ? string.Empty : "s")}" };
        if (peek.Items.Max(m => m.DequeueCount) is { } tries)
        {
            parts.Add($"tried up to {tries} time{(tries == 1 ? string.Empty : "s")}");
        }

        if (peek.Items.Max(m => m.InsertedUtc) is { } newest)
        {
            parts.Add($"newest {newest.UtcDateTime:yyyy-MM-dd HH:mm} UTC");
        }

        return string.Join(", ", parts);
    }

    private static string? Cause(PoisonMessage m) =>
        m.DequeueCount is { } n ? $"dequeued {n} time{(n == 1 ? string.Empty : "s")} before it was moved here" : null;

    private static string PlaceId(string account, string queue) => $"queue://{account}/{queue}";

    // queue://{storage account}/{queue}[/{message id}]. Only a queue named *-poison can be named: nothing else is read on a caller's say-so.
    [GeneratedRegex(@"^queue://(?<a>[a-z0-9]{3,24})/(?<q>[a-z0-9][a-z0-9-]{0,54}-poison)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex PlaceRegex();

    [GeneratedRegex(@"^queue://(?<a>[a-z0-9]{3,24})/(?<q>[a-z0-9][a-z0-9-]{0,54}-poison)/(?<m>[0-9A-Fa-f-]{36})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex ItemRegex();

    // Opens the queue itself in the Azure portal (its Storage Queues blade), not just the storage account.
    private static string? QueueLink(PoisonQueue q) =>
        q.AccountResourceId is null ? null
            : $"https://portal.azure.com/#view/Microsoft_Azure_Storage/QueueMenuBlade/~/overview/storageAccountId/{Uri.EscapeDataString(q.AccountResourceId)}/queueName/{Uri.EscapeDataString(q.Queue)}";

    private static Error Fail(Exception ex) => AzureFailures.ToError(ex, "Azure.StorageQueues", Needs);
}
