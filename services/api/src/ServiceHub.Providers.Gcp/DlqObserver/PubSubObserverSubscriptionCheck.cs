using Google.Cloud.PubSub.V1;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Providers.Gcp.DlqObserver;

/// <summary>
/// Whether a replayed message came back, answered from a subscription of ServiceHub's OWN on the dead-letter topic
/// (ADR-0018). Every subscription on a topic gets its own copy of every message, so ServiceHub sees every dead letter
/// without competing with the customer's own reader — and Pub/Sub holds them while ServiceHub is down.
/// </summary>
/// <remarks>
/// A replay stamps the message with <c>x-servicehub-recovery-id</c>; Pub/Sub keeps attributes when it dead-letters. A
/// dead letter carrying that marker is the replay coming back, and it is recorded the moment it is read. A message is
/// acknowledged only AFTER that is saved: if saving fails, Pub/Sub delivers it again.
/// </remarks>
public sealed class PubSubObserverSubscriptionCheck : IDeadLetterReturnCheck
{
    /// <summary>
    /// ServiceHub's own subscription must end with this. Everything else that looks for "the subscription on the dead-letter
    /// topic" skips names ending so — otherwise replay could read from a subscription ServiceHub itself empties.
    /// </summary>
    public const string ObserverSuffix = "-servicehub-observer";

    internal const string MarkerAttribute = "x-servicehub-recovery-id";
    internal const int PullSize = 100;
    internal const int EmptyPullsToStop = 3;
    internal const int MaxPerDrain = 5000;
    internal static readonly TimeSpan LongestFloor = TimeSpan.FromHours(24);

    private static readonly RecoveryActor Actor = new("System:DeadLetterViewAgent", RecoveryActorKind.System);

    private readonly IGcpClientFactory _clients;
    private readonly IRecoveryLedger _ledger;
    private readonly ILogger<PubSubObserverSubscriptionCheck> _logger;
    private readonly TimeProvider _time;

    /// <summary>Creates the check.</summary>
    public PubSubObserverSubscriptionCheck(IGcpClientFactory clients, IRecoveryLedger ledger, ILogger<PubSubObserverSubscriptionCheck> logger, TimeProvider? time = null)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public CloudProviderType Provider => CloudProviderType.Gcp;

    /// <inheritdoc />
    public bool NeedsObserverReference => true;

    /// <inheritdoc />
    public string? ValidateObserverReference(string? observerReference) =>
        string.IsNullOrWhiteSpace(observerReference) || !observerReference.EndsWith(ObserverSuffix, StringComparison.Ordinal) || observerReference.Length == ObserverSuffix.Length
            ? $"Name the subscription ServiceHub reads for itself. Its name must end with '{ObserverSuffix}', so nothing else mistakes it for your own dead-letter subscription."
            : null;

    /// <inheritdoc />
    public async Task<DeadLetterViewHealth> CheckHealthAsync(Namespace ns, DlqObserverAttestation attestation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentNullException.ThrowIfNull(attestation);
        if (ValidateObserverReference(attestation.ObserverReference) is not null || string.IsNullOrWhiteSpace(attestation.DlqEntityName))
        {
            return new DeadLetterViewHealth(false, "NOT_SET_UP", Lost: true);
        }

        try
        {
            var subscriber = await _clients.GetSubscriberClientAsync(ns, attestation.ObserverReference!, cancellationToken).ConfigureAwait(false);
            var subscription = await subscriber.GetSubscriptionAsync(new GetSubscriptionRequest { Subscription = NameOf(ns, attestation.ObserverReference!) }, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(subscription.TopicAsTopicName?.TopicId, attestation.DlqEntityName, StringComparison.Ordinal))
            {
                return new DeadLetterViewHealth(false, "WRONG_TOPIC", Lost: true);
            }

            await DrainAsync(ns, subscriber, attestation.ObserverReference!, cancellationToken).ConfigureAwait(false);
            return new DeadLetterViewHealth(true);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return new DeadLetterViewHealth(false, "SUBSCRIPTION_MISSING", Lost: true); // deleted: whatever it held is gone
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "The observer subscription for namespace {NamespaceId} could not be read", ns.Id);
            return new DeadLetterViewHealth(false, ex.StatusCode == StatusCode.PermissionDenied ? "PERMISSION_DENIED" : "SUBSCRIPTION_UNREADABLE");
        }
    }

    /// <inheritdoc />
    public async Task<DeadLetterReturnVerdict> CheckAsync(
        Namespace ns, DlqObserverAttestation attestation, RecoveryLedgerEntry entry, DateTimeOffset? liveSince, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentNullException.ThrowIfNull(attestation);
        ArgumentNullException.ThrowIfNull(entry);

        // Only dead letters that arrived while this subscription existed, and was being read, were seen.
        if (liveSince is null || liveSince > entry.BegunAt)
        {
            return DeadLetterReturnVerdict.CannotTell("VIEW_NOT_LIVE_AT_REPLAY");
        }

        if (!entry.MarkerApplied || string.IsNullOrEmpty(entry.RecoveryMarker))
        {
            return DeadLetterReturnVerdict.CannotTell("NO_WAY_TO_RECOGNISE_IT");
        }

        var sourceId = SubscriptionIdOf(entry);
        if (sourceId is null || ValidateObserverReference(attestation.ObserverReference) is not null)
        {
            return DeadLetterReturnVerdict.CannotTell("SUBSCRIPTION_UNKNOWN");
        }

        try
        {
            var sourceClient = await _clients.GetSubscriberClientAsync(ns, sourceId, cancellationToken).ConfigureAwait(false);
            var source = await sourceClient.GetSubscriptionAsync(new GetSubscriptionRequest { Subscription = NameOf(ns, sourceId) }, cancellationToken).ConfigureAwait(false);
            var policy = source.DeadLetterPolicy;
            if (policy is null || string.IsNullOrEmpty(policy.DeadLetterTopic))
            {
                return DeadLetterReturnVerdict.CannotTell("NO_DEAD_LETTER_TOPIC");
            }

            // This view watches one dead-letter topic. A subscription that dead-letters somewhere else was never seen by it.
            if (!string.Equals(TopicName.Parse(policy.DeadLetterTopic).TopicId, attestation.DlqEntityName, StringComparison.Ordinal))
            {
                return DeadLetterReturnVerdict.CannotTell("OTHER_DEAD_LETTER_TOPIC");
            }

            // "Did not arrive" only means "fixed" once the message has had time to use up its delivery attempts.
            var backoff = source.RetryPolicy?.MaximumBackoff?.ToTimeSpan() ?? TimeSpan.Zero;
            var floor = TimeSpan.FromSeconds((policy.MaxDeliveryAttempts * (source.AckDeadlineSeconds + backoff.TotalSeconds)) + 60);
            if (floor > LongestFloor)
            {
                return DeadLetterReturnVerdict.CannotTell("WINDOW_TOO_SHORT");
            }

            if (_time.GetUtcNow() < entry.BegunAt + floor)
            {
                return DeadLetterReturnVerdict.TooEarly;
            }

            var observer = await _clients.GetSubscriberClientAsync(ns, attestation.ObserverReference!, cancellationToken).ConfigureAwait(false);
            var (seen, complete) = await DrainWithCompletenessAsync(ns, observer, attestation.ObserverReference!, cancellationToken).ConfigureAwait(false);
            if (seen.Contains(entry.RecoveryMarker))
            {
                return DeadLetterReturnVerdict.Returned;
            }

            // The drain stopped at its cap with more waiting: this replay may be in the part not yet read. "Not seen" is
            // not "did not come back" then, so it is never answered as fixed. The next look reads the rest.
            return complete ? DeadLetterReturnVerdict.NotReturned : DeadLetterReturnVerdict.CannotTell("OBSERVER_BACKLOG_NOT_FULLY_READ");
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "The observer subscription could not answer for entry {EntryId}", entry.Id);
            return DeadLetterReturnVerdict.CannotTell(ex.StatusCode == StatusCode.NotFound ? "SUBSCRIPTION_MISSING" : "SUBSCRIPTION_UNREADABLE");
        }
    }

    /// <summary>
    /// Reads everything waiting on ServiceHub's own subscription. A dead letter carrying a replay's marker is recorded as
    /// that replay coming back; only then is it acknowledged. Returns the markers recorded in this drain.
    /// </summary>
    internal async Task<IReadOnlySet<string>> DrainAsync(Namespace ns, SubscriberServiceApiClient subscriber, string observerId, CancellationToken ct)
        => (await DrainWithCompletenessAsync(ns, subscriber, observerId, ct).ConfigureAwait(false)).Returned;

    /// <summary>
    /// <see cref="DrainAsync"/>, plus whether the subscription was read until it ran dry. False when the per-drain cap was
    /// reached first, so what was not returned may simply not have been read yet.
    /// </summary>
    internal async Task<(IReadOnlySet<string> Returned, bool Complete)> DrainWithCompletenessAsync(Namespace ns, SubscriberServiceApiClient subscriber, string observerId, CancellationToken ct)
    {
        var name = NameOf(ns, observerId);
        var returned = new HashSet<string>(StringComparer.Ordinal);
        var read = 0;
        var empty = 0;
        while (empty < EmptyPullsToStop && read < MaxPerDrain)
        {
#pragma warning disable CS0612, CS0618 // ReturnImmediately is how "is anything waiting right now?" is asked; a blocking pull cannot answer it.
            var response = await subscriber.PullAsync(new PullRequest { Subscription = name, MaxMessages = PullSize, ReturnImmediately = true }, ct).ConfigureAwait(false);
#pragma warning restore CS0612, CS0618
            if (response.ReceivedMessages.Count == 0)
            {
                empty++;
                continue;
            }

            empty = 0;
            var acknowledge = new List<string>(response.ReceivedMessages.Count);
            foreach (var received in response.ReceivedMessages)
            {
                read++;
                if (!received.Message.Attributes.TryGetValue(MarkerAttribute, out var marker) || string.IsNullOrEmpty(marker))
                {
                    acknowledge.Add(received.AckId); // not a replay: nothing to record
                    continue;
                }

                if (await RecordReturnAsync(ns.OwnerId, marker, ct).ConfigureAwait(false))
                {
                    returned.Add(marker);
                    acknowledge.Add(received.AckId);
                }
                // Not saved: left unacknowledged, so Pub/Sub hands it over again.
            }

            if (acknowledge.Count > 0)
            {
                await subscriber.AcknowledgeAsync(new AcknowledgeRequest { Subscription = name, AckIds = { acknowledge } }, ct).ConfigureAwait(false);
            }
        }

        return (returned, empty >= EmptyPullsToStop);
    }

    /// <summary>Records that the replay stamped with <paramref name="marker"/> came back. True when the message may be acknowledged.</summary>
    private async Task<bool> RecordReturnAsync(string ownerId, string marker, CancellationToken ct)
    {
        try
        {
            var entry = await _ledger.FindByMarkerAsync(ownerId, marker, ct).ConfigureAwait(false);
            if (entry is null || entry.State != RecoveryEntryState.Observing)
            {
                return true; // not one of ours, or its window already closed: there is nothing left to record
            }

            // A refusal here is a lost race with something that closed the entry first — not a failure to save.
            await _ledger.RecordObservationAsync(new RecordObservationRequest
            {
                EntryId = entry.Id, OwnerId = ownerId, Actor = Actor, Outcome = RecoveryObservationOutcome.RecurrenceObserved, Confidence = VerificationConfidence.Exact,
            }, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "A returned replay could not be recorded; it will be read again");
            return false;
        }
    }

    private static string NameOf(Namespace ns, string subscriptionId) =>
        SubscriptionName.FromProjectSubscription(ns.GcpProjectId ?? "unknown-project", subscriptionId).ToString();

    /// <summary>The subscription a replay was made on. It is stored as <c>topic/subscriptions/name</c> or <c>topic/name</c>.</summary>
    internal static string? SubscriptionIdOf(RecoveryLedgerEntry entry)
    {
        var name = entry.EntityNameSnapshot;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var slash = name.LastIndexOf('/');
        return slash >= 0 ? name[(slash + 1)..] : name;
    }

    /// <summary>Whether a subscription is ServiceHub's own — never the customer's dead-letter subscription, never a queue to show.</summary>
    public static bool IsObserver(string? subscriptionId) => subscriptionId is not null && subscriptionId.EndsWith(ObserverSuffix, StringComparison.Ordinal);
}
