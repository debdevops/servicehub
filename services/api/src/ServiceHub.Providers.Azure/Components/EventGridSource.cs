using System.Text.RegularExpressions;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;
using ServiceHub.Core.Security;

namespace ServiceHub.Providers.Azure.Components;

/// <summary>
/// Azure Event Grid dead-lettered events (build-plan unit PP4a). <b>Read-only</b>: it finds the event subscriptions that dead-letter
/// into a blob container, lists the blobs there and reads the start of one to find its reason. The event's data is never parsed,
/// kept or shown.
/// </summary>
/// <remarks>
/// The places are the dead-letter containers, found by asking every subscription the connection's Entra identity can see. A list of blobs
/// does not carry a reason, so <see cref="ComponentSourceCapabilities.ReasonInList"/> is false. Needs the Reader role (to find the
/// event subscriptions) and Storage Blob Data Reader on the storage account.
/// </remarks>
public sealed partial class EventGridSource : IComponentSource
{
    /// <summary>The catalogue component this serves.</summary>
    public const string Key = "event-grid";

    /// <summary>Without a chosen place, at most this many containers are read, so one listing stays a bounded number of calls.</summary>
    internal const int MaxContainersPerListing = 20;

    private const int MaxTextLength = 1000;
    private const string Needs = "the Reader role on the subscription and Storage Blob Data Reader on the dead-letter storage account";

    private readonly IAzureEventGridReader _reader;

    // The reader is this module's own seam to Azure, so a test can stand in for it; only the public constructor is for the host.
    internal EventGridSource(IAzureEventGridReader reader) =>
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <summary>Creates the source over Azure Resource Manager and Blob storage.</summary>
    public EventGridSource(IAzureRestClient rest)
        : this(new AzureEventGridReader(rest))
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
                return Result.Failure(Error.Forbidden("Azure.EventGrid.NotEnabled", "Not enabled for this connection: its identity cannot see any Azure subscription. Give it the Reader role on the subscription that holds the event subscriptions."));
            }

            var containers = await _reader.ListDeadLetterContainersAsync(ns, subscriptions.Items[0], 1, ct).ConfigureAwait(false);
            if (containers.Items.Count > 0)
            {
                // The Reader role finds the container; reading what is in it needs a data role, which only a blob listing proves.
                var first = containers.Items[0];
                await _reader.ListBlobsAsync(ns, first.Account, first.Container, 1, ct).ConfigureAwait(false);
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
            var found = await FindContainersAsync(ns, ComponentSourceLimits.MaxPlaces, budget.Token, ct).ConfigureAwait(false);
            if (found.Error is not null)
            {
                return Result.Failure<SourcePlacesPage>(found.Error);
            }

            return Result.Success(new SourcePlacesPage(
                [.. found.Items.Take(ComponentSourceLimits.MaxPlaces).Select(c => new SourcePlace(PlaceId(c), $"{c.Container} ({c.Account})", "dead-letter container", AccountLink(c)))],
                found.More || found.Items.Count > ComponentSourceLimits.MaxPlaces, found.Incomplete));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<SourcePlacesPage>(Error.ExternalService("Azure.EventGrid.ReadTimedOut", "Reading Event Grid took too long."));
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
            return Result.Failure<FailedItemsPage>(Error.Validation("Azure.EventGrid.InvalidPlace", "That is not a dead-letter container."));
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(ComponentSourceLimits.ListingBudget);
        try
        {
            var reached = false;
            var incomplete = false;
            List<(string Account, string Container)> containers;
            if (place is null)
            {
                var found = await FindContainersAsync(ns, MaxContainersPerListing + 1, budget.Token, ct).ConfigureAwait(false);
                if (found.Error is not null)
                {
                    return Result.Failure<FailedItemsPage>(found.Error);
                }

                containers = [.. found.Items.Take(MaxContainersPerListing).Select(c => (c.Account, c.Container))];
                reached = found.More || found.Items.Count > MaxContainersPerListing;
                incomplete = found.Incomplete;
            }
            else
            {
                containers = [(place.Groups["a"].Value, place.Groups["c"].Value)];
            }

            var results = await SourceReads.RunAsync(
                containers, (c, token) => _reader.ListBlobsAsync(ns, c.Account, c.Container, ComponentSourceLimits.MaxFailedItems, token),
                Fail, new Listing<DeadLetterBlob>([], false), budget.Token, ct).ConfigureAwait(false);

            incomplete |= results.Any(r => r.Failed);
            if (results.Count > 0 && results.All(r => r.Failed))
            {
                return Result.Failure<FailedItemsPage>(results.Select(r => r.Error).FirstOrDefault(e => e is not null) ?? Error.ExternalService("Azure.EventGrid.ReadFailed", "Event Grid could not be read."));
            }

            reached |= results.Any(r => r.Value.More);
            var ordered = results.SelectMany(r => r.Value.Items).OrderByDescending(b => b.ModifiedUtc ?? DateTimeOffset.MinValue).ToList();
            reached |= ordered.Count > ComponentSourceLimits.MaxFailedItems;
            return Result.Success(new FailedItemsPage([.. ordered.Take(ComponentSourceLimits.MaxFailedItems).Select(ToItem)], reached, incomplete));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<FailedItemsPage>(Error.ExternalService("Azure.EventGrid.ReadTimedOut", "Reading Event Grid took too long. Try one container."));
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
        var m = itemId.Length <= 1200 ? ItemRegex().Match(itemId) : Match.Empty;
        if (!m.Success || m.Groups["b"].Value.Split('/').Contains(".."))
        {
            return Result.Failure<FailedItemDetail?>(Error.Validation("Azure.EventGrid.InvalidItem", "That is not a dead-lettered event."));
        }

        try
        {
            var read = await _reader.GetBlobAsync(ns, m.Groups["a"].Value, m.Groups["c"].Value, m.Groups["b"].Value, ct).ConfigureAwait(false);
            if (read is null)
            {
                return Result.Success<FailedItemDetail?>(null);
            }

            var cause = Cause(read);
            var item = new FailedItem(
                itemId, PlaceId(read.Blob.Account, read.Blob.Container), read.LastAttemptUtc ?? read.Blob.ModifiedUtc, Status, ReasonCode: null,
                $"Dead-lettered: {Clean(read.Reason) ?? "reason not recorded"}", CopyKind.None, ConsoleUrl: null);
            return Result.Success<FailedItemDetail?>(new FailedItemDetail(item, Clean(read.Reason), Clean(cause), null, read.LastAttemptUtc));
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

    private const string Status = "DEAD_LETTERED";

    private sealed record Found(IReadOnlyList<DeadLetterContainer> Items, bool More, bool Incomplete, Error? Error);

    /// <summary>Every dead-letter container in every subscription the identity can see, sorted. A subscription that cannot be read leaves the result incomplete.</summary>
    private async Task<Found> FindContainersAsync(Namespace ns, int max, CancellationToken budget, CancellationToken caller)
    {
        var subscriptions = await _reader.ListSubscriptionsAsync(ns, budget).ConfigureAwait(false);
        var results = await SourceReads.RunAsync(
            subscriptions.Items, (sub, token) => _reader.ListDeadLetterContainersAsync(ns, sub, max, token), Fail, new Listing<DeadLetterContainer>([], false), budget, caller).ConfigureAwait(false);

        if (results.Count > 0 && results.All(r => r.Failed))
        {
            return new Found([], false, true, results.Select(r => r.Error).FirstOrDefault(e => e is not null) ?? Error.ExternalService("Azure.EventGrid.ReadFailed", "Event Grid could not be read."));
        }

        var all = results.SelectMany(r => r.Value.Items)
            .GroupBy(c => $"{c.Account}/{c.Container}", StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(c => c.Account, StringComparer.Ordinal).ThenBy(c => c.Container, StringComparer.Ordinal).ToList();
        return new Found(all, subscriptions.More || results.Any(r => r.Value.More), results.Any(r => r.Failed || r.Value.Incomplete), null);
    }

    private static FailedItem ToItem(DeadLetterBlob blob) =>
        new($"blob://{blob.Account}/{blob.Container}/{blob.Name}", PlaceId(blob.Account, blob.Container), blob.ModifiedUtc, Status, ReasonCode: null,
            $"Dead-lettered event: {Clean(blob.Name[(blob.Name.LastIndexOf('/') + 1)..])}", CopyKind.None, ConsoleUrl: null);

    private static string? Cause(DeadLetterBlobRead read)
    {
        var parts = new List<string>();
        if (read.Attempts is { } attempts)
        {
            parts.Add($"{attempts} delivery attempt{(attempts == 1 ? string.Empty : "s")}");
        }

        if (!string.IsNullOrWhiteSpace(read.Outcome))
        {
            parts.Add($"last outcome {read.Outcome}");
        }

        if (read.HttpStatus is { } status)
        {
            parts.Add($"last HTTP status {status}");
        }

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private static string PlaceId(DeadLetterContainer c) => PlaceId(c.Account, c.Container);

    private static string PlaceId(string account, string container) => $"blob://{account}/{container}";

    // blob://{storage account}/{container}[/{blob name}], names as Azure Storage defines them.
    [GeneratedRegex(@"^blob://(?<a>[a-z0-9]{3,24})/(?<c>[a-z0-9][a-z0-9-]{1,61}[a-z0-9])$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex PlaceRegex();

    [GeneratedRegex(@"^blob://(?<a>[a-z0-9]{3,24})/(?<c>[a-z0-9][a-z0-9-]{1,61}[a-z0-9])/(?<b>[^\s?#\\]{1,1024})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex ItemRegex();

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var safe = LogRedactor.SanitiseForLog(LogRedactor.Redact(text));
        return safe.Length <= MaxTextLength ? safe : safe[..MaxTextLength] + "…";
    }

    private static string? AccountLink(DeadLetterContainer c) =>
        c.AccountResourceId is null ? null : $"https://portal.azure.com/#@/resource{c.AccountResourceId}/overview";

    private static Error Fail(Exception ex) => AzureFailures.ToError(ex, "Azure.EventGrid", Needs);
}
