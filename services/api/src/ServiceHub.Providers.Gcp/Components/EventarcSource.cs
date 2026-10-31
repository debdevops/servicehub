using System.Text.RegularExpressions;
using Grpc.Core;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;
using ServiceHub.Core.Security;

namespace ServiceHub.Providers.Gcp.Components;

/// <summary>
/// Eventarc undelivered events (build-plan unit PP4b). <b>Read-only</b>. Eventarc keeps no dead-letter store of its own: an event it cannot
/// deliver survives only if the trigger's Pub/Sub transport subscription has a dead-letter policy. This source lists triggers, follows each
/// trigger's subscription to its dead-letter topic, and looks at what that topic holds. An event's data is never read into a result.
/// </summary>
/// <remarks>
/// A trigger with no dead-letter policy keeps nothing, and the source says so as an error naming the reason, never as an empty list that
/// reads as "nothing is wrong". A dead-letter topic with no pull subscription cannot be looked at either. Looking at a topic is a pull
/// followed by an immediate release: nothing is acknowledged, but it counts as a delivery attempt on the subscription that is looked at.
/// Needs <c>eventarc.triggers.list</c> and <c>eventarc.triggers.get</c> (Eventarc Viewer), <c>pubsub.subscriptions.get</c>,
/// <c>pubsub.topics.attachSubscription</c>-free listing (<c>pubsub.topics.get</c>), and <c>pubsub.subscriptions.consume</c> on the
/// dead-letter subscription.
/// </remarks>
public sealed partial class EventarcSource : IComponentSource
{
    /// <summary>The catalogue component this serves.</summary>
    public const string Key = "eventarc";

    /// <summary>The catalogue component that holds an Eventarc event as a dead letter and can put it back.</summary>
    internal const string PubSubKey = "pubsub";

    /// <summary>Without a chosen place, at most this many triggers are read, so one listing stays a bounded number of calls.</summary>
    internal const int MaxTriggersPerListing = 20;

    private const int MaxTextLength = 1000;
    private const string Status = "UNDELIVERED";
    private const string ErrorAttribute = "CloudPubSubDeadLetterSourceDeliveryErrorMessage";
    private const string CountAttribute = "CloudPubSubDeadLetterSourceDeliveryCount";

    private readonly IGcpEventarcReader _reader;

    // The reader is this module's own seam to Google, so a test can stand in for it; only the public constructor is for the host.
    internal EventarcSource(IGcpEventarcReader reader) =>
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <summary>Creates the source over Google.</summary>
    public EventarcSource(IGcpClientFactory clients)
        : this(new GcpEventarcReader(clients))
    {
    }

    /// <inheritdoc />
    public string ComponentKey => Key;

    /// <inheritdoc />
    public CloudProviderType Provider => CloudProviderType.Gcp;

    /// <inheritdoc />
    public ComponentSourceCapabilities Capabilities { get; } = new(ReasonInList: false, ReadsPayload: false);

    /// <inheritdoc />
    public async Task<Result> ValidateAccessAsync(Namespace ns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        if (ProjectOf(ns) is null)
        {
            return Result.Failure(NoProject());
        }

        try
        {
            // Listing is permitted per project, not per location, so one location answers for all of them.
            await _reader.ListTriggersAsync(ns, "us-central1", 1, ct).ConfigureAwait(false);
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
        if (ProjectOf(ns) is null)
        {
            return Result.Failure<SourcePlacesPage>(NoProject());
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(ComponentSourceLimits.ListingBudget);
        try
        {
            var found = await FindTriggersAsync(ns, ComponentSourceLimits.MaxPlaces, budget.Token, ct).ConfigureAwait(false);
            if (found.Error is not null)
            {
                return Result.Failure<SourcePlacesPage>(found.Error);
            }

            return Result.Success(new SourcePlacesPage(
                [.. found.Items.Take(ComponentSourceLimits.MaxPlaces).Select(t => new SourcePlace(t.Name, ShortName(t.Name), "trigger", TriggerLink(ns, t.Name)))],
                found.More || found.Items.Count > ComponentSourceLimits.MaxPlaces, found.Incomplete));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<SourcePlacesPage>(Error.ExternalService("GCP.Eventarc.ReadTimedOut", "Reading Eventarc took too long."));
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
        if (ProjectOf(ns) is null)
        {
            return Result.Failure<FailedItemsPage>(NoProject());
        }

        var place = placeId is null ? null : InOwnProject(ns, placeId);
        if (placeId is not null && place is null)
        {
            return Result.Failure<FailedItemsPage>(Error.Validation("GCP.Eventarc.InvalidPlace", "That is not an Eventarc trigger in this connection's project."));
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(ComponentSourceLimits.ListingBudget);
        try
        {
            var reached = false;
            var incomplete = false;
            List<TriggerRef> triggers;
            if (place is null)
            {
                var found = await FindTriggersAsync(ns, MaxTriggersPerListing + 1, budget.Token, ct).ConfigureAwait(false);
                if (found.Error is not null)
                {
                    return Result.Failure<FailedItemsPage>(found.Error);
                }

                triggers = [.. found.Items.Take(MaxTriggersPerListing)];
                reached = found.More || found.Items.Count > MaxTriggersPerListing;
                incomplete = found.Incomplete;
            }
            else
            {
                var one = await _reader.GetTriggerAsync(ns, place, budget.Token).ConfigureAwait(false);
                if (one is null)
                {
                    return Result.Failure<FailedItemsPage>(Error.Validation("GCP.Eventarc.InvalidPlace", "That trigger no longer exists."));
                }

                triggers = [one];
            }

            // Which topic each trigger dead-letters into. A trigger that keeps nothing is not an error in a wide listing, but it is when asked for by name.
            var topics = await SourceReads.RunAsync(
                triggers, (t, token) => TopicOfAsync(ns, t, token), Fail, (string?)null, budget.Token, ct).ConfigureAwait(false);
            incomplete |= topics.Any(r => r.Failed);
            if (triggers.Count > 0 && topics.All(r => r.Failed))
            {
                return Result.Failure<FailedItemsPage>(topics.Select(r => r.Error).FirstOrDefault(e => e is not null) ?? Error.ExternalService("GCP.Eventarc.ReadFailed", "Eventarc could not be read."));
            }

            var owners = new Dictionary<string, TriggerRef>(StringComparer.Ordinal);
            for (var i = 0; i < triggers.Count; i++)
            {
                if (topics[i].Value is { } topic)
                {
                    owners.TryAdd(topic, triggers[i]);
                }
            }

            if (owners.Count == 0 && !incomplete)
            {
                return Result.Failure<FailedItemsPage>(NothingKept(place is not null));
            }

            var reads = await SourceReads.RunAsync(
                owners.Keys.ToList(), (topic, token) => _reader.ListUndeliveredAsync(ns, topic, ComponentSourceLimits.MaxFailedItems, token),
                Fail, new DeadLetterRead([], false, true), budget.Token, ct).ConfigureAwait(false);
            incomplete |= reads.Any(r => r.Failed);

            var keys = owners.Keys.ToList();
            if (reads.Count > 0 && reads.All(r => !r.Failed && !r.Value.Readable))
            {
                return Result.Failure<FailedItemsPage>(Error.Validation("GCP.Eventarc.NotReadable",
                    "The dead-letter topic has no pull subscription, so Pub/Sub keeps nothing that ServiceHub can look at. Add a pull subscription to it."));
            }

            if (reads.Count > 0 && reads.All(r => r.Failed))
            {
                return Result.Failure<FailedItemsPage>(reads.Select(r => r.Error).FirstOrDefault(e => e is not null) ?? Error.ExternalService("GCP.Eventarc.ReadFailed", "Eventarc could not be read."));
            }

            var items = new List<FailedItem>();
            for (var i = 0; i < keys.Count; i++)
            {
                reached |= reads[i].Value.More;
                items.AddRange(reads[i].Value.Events.Select(e => ToItem(ns, owners[keys[i]].Name, e, owners[keys[i]].TransportSubscription)));
            }

            var ordered = items.OrderByDescending(i => i.AtUtc ?? DateTimeOffset.MinValue).ToList();
            reached |= ordered.Count > ComponentSourceLimits.MaxFailedItems;
            return Result.Success(new FailedItemsPage([.. ordered.Take(ComponentSourceLimits.MaxFailedItems)], reached, incomplete));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<FailedItemsPage>(Error.ExternalService("GCP.Eventarc.ReadTimedOut", "Reading Eventarc took too long. Try one trigger."));
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
        if (ProjectOf(ns) is null)
        {
            return Result.Failure<FailedItemDetail?>(NoProject());
        }

        var at = itemId.LastIndexOf('#');
        var trigger = at > 0 && itemId.Length <= 1200 ? InOwnProject(ns, itemId[..at]) : null;
        var messageId = at > 0 ? itemId[(at + 1)..] : string.Empty;
        if (trigger is null || !MessageIdRegex().IsMatch(messageId))
        {
            return Result.Failure<FailedItemDetail?>(Error.Validation("GCP.Eventarc.InvalidItem", "That is not an undelivered event in this connection's project."));
        }

        try
        {
            var found = await _reader.GetTriggerAsync(ns, trigger, ct).ConfigureAwait(false);
            var topic = found is null ? null : await TopicOfAsync(ns, found, ct).ConfigureAwait(false);
            if (topic is null)
            {
                return Result.Success<FailedItemDetail?>(null);
            }

            var read = await _reader.GetUndeliveredAsync(ns, topic, messageId, ct).ConfigureAwait(false);
            if (read is null)
            {
                return Result.Success<FailedItemDetail?>(null);
            }

            // The error text is what Pub/Sub recorded about a delivery: free text, redacted and bounded before it leaves this source.
            return Result.Success<FailedItemDetail?>(new FailedItemDetail(
                ToItem(ns, trigger, read, found!.TransportSubscription), Clean(Attribute(read, ErrorAttribute)), Clean(Cause(read)), null, read.PublishedUtc));
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

    private sealed record Found(IReadOnlyList<TriggerRef> Items, bool More, bool Incomplete, Error? Error);

    /// <summary>Every trigger in every location Eventarc is offered in, sorted by name. A location that cannot be read leaves the result incomplete.</summary>
    private async Task<Found> FindTriggersAsync(Namespace ns, int max, CancellationToken budget, CancellationToken caller)
    {
        // Eventarc lists across every location with "-" in place of a location, in one call. Asking each of its ~50 locations (4 at a
        // time) took 16 s of the 20 s budget on the first live run, so a listing that also read a dead-letter topic ran out of time and
        // failed. The per-location walk stays as the fallback if Google refuses the wildcard.
#pragma warning disable CA1031
        try
        {
            var wide = await _reader.ListTriggersAsync(ns, "-", max, budget).ConfigureAwait(false);
            return new Found([.. wide.Items.OrderBy(t => t.Name, StringComparer.Ordinal)], wide.More, false, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // fall through to the per-location walk
        }
#pragma warning restore CA1031

        var locations = await _reader.ListLocationsAsync(ns, budget).ConfigureAwait(false);
        var results = await SourceReads.RunAsync(
            locations.Items, (location, token) => _reader.ListTriggersAsync(ns, location, max, token), Fail, new Listing<TriggerRef>([], false), budget, caller).ConfigureAwait(false);

        if (results.Count > 0 && results.All(r => r.Failed))
        {
            return new Found([], false, true, results.Select(r => r.Error).FirstOrDefault(e => e is not null) ?? Error.ExternalService("GCP.Eventarc.ReadFailed", "Eventarc could not be read."));
        }

        var all = results.SelectMany(r => r.Value.Items).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        return new Found(all, locations.More || results.Any(r => r.Value.More), results.Any(r => r.Failed), null);
    }

    /// <summary>The dead-letter topic a trigger's events end up in, or null when the trigger keeps nothing.</summary>
    private async Task<string?> TopicOfAsync(Namespace ns, TriggerRef trigger, CancellationToken ct) =>
        trigger.TransportSubscription is null ? null : await _reader.GetDeadLetterTopicAsync(ns, trigger.TransportSubscription, ct).ConfigureAwait(false);

    private static Error NothingKept(bool named) => Error.Validation(
        "GCP.Eventarc.NothingKept",
        named
            ? "This trigger keeps nothing: its Pub/Sub subscription has no dead-letter policy, so an event Eventarc cannot deliver is dropped. Add a dead-letter policy to see them here."
            : "None of these triggers keeps anything: no Pub/Sub subscription has a dead-letter policy, so an event Eventarc cannot deliver is dropped.");

    private static FailedItem ToItem(Namespace ns, string triggerName, UndeliveredEvent e, string? transportSubscription)
    {
        var type = Clean(Attribute(e, "ce-type"));
        return new FailedItem(
            $"{triggerName}#{e.MessageId}", triggerName, e.PublishedUtc, Status, ReasonCode: null,
            type is null ? "Undelivered event" : $"Undelivered event: {type}", CopyKind.None, TriggerLink(ns, triggerName), HeldInPubSub(transportSubscription));
    }

    // The event is held in the dead-letter topic of the trigger's Pub/Sub transport subscription, and the Pub/Sub component lists that
    // subscription and replays to its own source topic: the one trigger's transport topic, so a retry there reaches only this trigger.
    // The entity named is the subscription's id, which is the text that finds it among the Pub/Sub component's dead letters.
    private static HeldIn? HeldInPubSub(string? transportSubscription)
    {
        var id = transportSubscription is null ? null : transportSubscription[(transportSubscription.LastIndexOf('/') + 1)..];
        return string.IsNullOrWhiteSpace(id) ? null : new HeldIn(PubSubKey, id);
    }

    private static string? Cause(UndeliveredEvent e)
    {
        var parts = new List<string>();
        if (int.TryParse(Attribute(e, CountAttribute), System.Globalization.CultureInfo.InvariantCulture, out var count))
        {
            parts.Add($"tried {count} time{(count == 1 ? string.Empty : "s")}");
        }

        if (Attribute(e, "ce-type") is { } type)
        {
            parts.Add($"event type {type}");
        }

        if (Attribute(e, "ce-source") is { } source)
        {
            parts.Add($"from {source}");
        }

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private static string? Attribute(UndeliveredEvent e, string name) => e.Attributes.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    private static string? ProjectOf(Namespace ns) => string.IsNullOrWhiteSpace(ns.GcpProjectId) ? null : ns.GcpProjectId.Trim();

    private static Error NoProject() =>
        Error.Validation("GCP.Eventarc.NoProject", "This connection has no Google project id, so Eventarc cannot be read.");

    // projects/{project}/locations/{location}/triggers/{trigger}, in this connection's own project only.
    [GeneratedRegex(@"^projects/(?<p>[^/]+)/locations/(?<l>[^/]+)/triggers/(?<t>[^/#]+)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"^[0-9]{1,30}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex MessageIdRegex();

    // Google may name a trigger by the project NUMBER while a connection stores the id. A name is accepted when its project segment is this
    // connection's id or any number, and it is then REWRITTEN to the connection's own project id before anything is read, so the read can
    // only ever happen in this connection's project. Any other id is refused.
    private static string? InOwnProject(Namespace ns, string name)
    {
        var m = name.Length <= 1024 ? NameRegex().Match(name) : Match.Empty;
        var project = ProjectOf(ns);
        if (!m.Success || project is null)
        {
            return null;
        }

        var segment = m.Groups["p"].Value;
        if (string.Equals(segment, project, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        return segment.All(char.IsAsciiDigit) ? $"projects/{project}{name[name.IndexOf('/', "projects/".Length)..]}" : null;
    }

    private static string ShortName(string name) => name[(name.LastIndexOf('/') + 1)..];

    private static string TriggerLink(Namespace ns, string triggerName)
    {
        var m = NameRegex().Match(triggerName);
        return m.Success
            ? $"https://console.cloud.google.com/eventarc/triggers/{m.Groups["l"].Value}/{m.Groups["t"].Value}?project={ProjectOf(ns)}"
            : "https://console.cloud.google.com/eventarc";
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var safe = LogRedactor.SanitiseForLog(LogRedactor.Redact(text));
        return safe.Length <= MaxTextLength ? safe : safe[..MaxTextLength] + "…";
    }

    private static Error Fail(Exception ex) => ex is RpcException { StatusCode: StatusCode.PermissionDenied or StatusCode.Unauthenticated }
        ? Error.Forbidden("GCP.Eventarc.NotEnabled",
            "Not enabled for this connection: its service account may not read Eventarc (needs the Eventarc Viewer role and the Eventarc API turned on, plus Pub/Sub Viewer and Subscriber on the dead-letter subscription).")
        : Error.ExternalService("GCP.Eventarc.ReadFailed", LogRedactor.SanitiseForLog(LogRedactor.Redact(ex.Message)));
}
