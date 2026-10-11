using FluentAssertions;
using Google.Cloud.PubSub.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Providers.Gcp;
using ServiceHub.Providers.Gcp.DlqObserver;

namespace ServiceHub.UnitTests.Providers.Gcp;

/// <summary>
/// ADR-0018: ServiceHub's own subscription on the dead-letter topic. A returned replay is recorded the moment it is read, and
/// acknowledged only once that is saved; "did not come back" is only said when the subscription was being read when the
/// replay was made.
/// </summary>
public sealed class PubSubObserverSubscriptionCheckTests
{
    private const string Owner = "owner1";
    private const string Observer = "orders-dlq-servicehub-observer";
    private const string DlqTopic = "orders-topic-dlq";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-04T12:00:00Z");

    private static Namespace Ns() =>
        Namespace.Create("gcp-ns", "Endpoint=sb://x.servicebus.windows.net/;SharedAccessKeyName=P;SharedAccessKey=abc=", provider: CloudProviderType.Gcp, gcpProjectId: "my-project", ownerId: Owner).Value;

    private static DlqObserverAttestation Attestation(Namespace ns, string observer = Observer, string topic = DlqTopic) =>
        new() { OwnerId = Owner, NamespaceId = ns.Id, Enabled = true, ObserverReference = observer, DlqEntityName = topic, StalenessBoundMinutes = 30, LastConfirmedAt = Now };

    private static RecoveryLedgerEntry Entry(TimeSpan ago, string? marker = "entry-7") => new()
    {
        OperationId = Guid.NewGuid(), OwnerId = Owner, BodyHash = "h", TargetEntity = "orders-topic", BegunAt = Now - ago,
        EntityNameSnapshot = "orders-topic/subscriptions/orders-subscription", RecoveryMarker = marker, MarkerApplied = marker is not null, State = RecoveryEntryState.Observing,
    };

    /// <summary>Pub/Sub as the check sees it: a source subscription with a dead-letter policy, and ServiceHub's own subscription holding messages.</summary>
    private sealed class Fake
    {
        public readonly Mock<SubscriberServiceApiClient> Subscriber = new();
        public readonly Mock<IRecoveryLedger> Ledger = new();
        public readonly Queue<ReceivedMessage> Waiting = new();
        public readonly List<string> Acknowledged = [];
        public bool SavingFails;
        public string DeadLetterTopic = $"projects/my-project/topics/{DlqTopic}";
        public string ObserverTopic = DlqTopic;
        public bool ObserverMissing;

        public Fake(RecoveryLedgerEntry? watched = null)
        {
            Subscriber.Setup(s => s.GetSubscriptionAsync(It.IsAny<GetSubscriptionRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((GetSubscriptionRequest r, CancellationToken _) =>
                {
                    if (r.Subscription.EndsWith(Observer, StringComparison.Ordinal))
                    {
                        if (ObserverMissing) throw new RpcException(new Status(StatusCode.NotFound, "gone"));
                        return new Subscription { Name = r.Subscription, Topic = $"projects/my-project/topics/{ObserverTopic}" };
                    }

                    return new Subscription
                    {
                        Name = r.Subscription, Topic = "projects/my-project/topics/orders-topic", AckDeadlineSeconds = 10,
                        RetryPolicy = new RetryPolicy { MaximumBackoff = Duration.FromTimeSpan(TimeSpan.FromSeconds(600)) },
                        DeadLetterPolicy = new DeadLetterPolicy { DeadLetterTopic = DeadLetterTopic, MaxDeliveryAttempts = 5 },
                    };
                });
            Subscriber.Setup(s => s.PullAsync(It.IsAny<PullRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    var response = new PullResponse();
                    while (Waiting.Count > 0 && response.ReceivedMessages.Count < 100) response.ReceivedMessages.Add(Waiting.Dequeue());
                    return response;
                });
            Subscriber.Setup(s => s.AcknowledgeAsync(It.IsAny<AcknowledgeRequest>(), It.IsAny<CancellationToken>()))
                .Returns((AcknowledgeRequest r, CancellationToken _) => { Acknowledged.AddRange(r.AckIds); return Task.CompletedTask; });

            Ledger.Setup(l => l.FindByMarkerAsync(Owner, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string marker, CancellationToken _) => watched is not null && watched.RecoveryMarker == marker ? watched : null);
            Ledger.Setup(l => l.RecordObservationAsync(It.IsAny<RecordObservationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RecordObservationRequest _, CancellationToken _) => SavingFails ? throw new InvalidOperationException("database is busy") : Result<RecoveryLedgerEntry>.Success(watched!));
        }

        public void Arrives(string ackId, string? marker = null)
        {
            var message = new PubsubMessage { MessageId = ackId };
            if (marker is not null) message.Attributes["x-servicehub-recovery-id"] = marker;
            Waiting.Enqueue(new ReceivedMessage { AckId = ackId, Message = message });
        }

        public PubSubObserverSubscriptionCheck Check()
        {
            var clients = Mock.Of<IGcpClientFactory>(f => f.GetSubscriberClientAsync(It.IsAny<Namespace>(), It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult(Subscriber.Object));
            return new PubSubObserverSubscriptionCheck(clients, Ledger.Object, NullLogger<PubSubObserverSubscriptionCheck>.Instance, new FakeTimeProvider(Now));
        }
    }

    private static readonly TimeSpan LongAgo = TimeSpan.FromHours(3);

    [Fact]
    public async Task A_replay_whose_marker_never_arrived_did_not_come_back()
    {
        var ns = Ns();
        var entry = Entry(LongAgo);
        var fake = new Fake(entry);
        fake.Arrives("a1");
        fake.Arrives("a2", marker: "someone-elses");

        var verdict = await fake.Check().CheckAsync(ns, Attestation(ns), entry, liveSince: Now.AddDays(-1));

        verdict.Should().Be(DeadLetterReturnVerdict.NotReturned);
        fake.Acknowledged.Should().BeEquivalentTo(["a1", "a2"], "everything read is acknowledged so it is not read twice");
        fake.Ledger.Verify(l => l.RecordObservationAsync(It.IsAny<RecordObservationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_replay_already_recorded_as_returned_by_another_drain_is_still_returned()
    {
        var ns = Ns();
        var entry = Entry(LongAgo);
        var fake = new Fake(entry); // nothing waiting: a health drain already took it
        fake.Ledger.Setup(l => l.GetEntryAsync(entry.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { entry.State = RecoveryEntryState.Returned; return entry; });

        var verdict = await fake.Check().CheckAsync(ns, Attestation(ns), entry, liveSince: Now.AddDays(-1));

        verdict.Should().Be(DeadLetterReturnVerdict.Returned);
    }

    [Fact]
    public async Task Drains_of_one_subscription_never_overlap()
    {
        var ns = Ns();
        var fake = new Fake(Entry(LongAgo));
        var inside = 0;
        var overlapped = false;
        fake.Subscriber.Setup(s => s.PullAsync(It.IsAny<PullRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (Interlocked.Increment(ref inside) > 1) overlapped = true;
                await Task.Delay(20);
                Interlocked.Decrement(ref inside);
                return new PullResponse();
            });
        var check = fake.Check();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => check.DrainAsync(ns, fake.Subscriber.Object, "observer-sub", CancellationToken.None)));

        overlapped.Should().BeFalse();
    }

    [Fact]
    public async Task A_backlog_larger_than_one_drain_is_never_answered_as_not_returned()
    {
        var ns = Ns();
        var entry = Entry(LongAgo);
        var fake = new Fake(entry);
        for (var i = 0; i < PubSubObserverSubscriptionCheck.MaxPerDrain + 500; i++) fake.Arrives($"a{i}");

        var verdict = await fake.Check().CheckAsync(ns, Attestation(ns), entry, liveSince: Now.AddDays(-1));

        verdict.Should().NotBe(DeadLetterReturnVerdict.NotReturned, "the replay may be in the part that was not read yet");
        verdict.Should().NotBe(DeadLetterReturnVerdict.Returned);
    }

    [Fact]
    public async Task A_dead_letter_carrying_the_replays_marker_is_recorded_as_came_back_before_it_is_acknowledged()
    {
        var ns = Ns();
        var entry = Entry(LongAgo);
        var fake = new Fake(entry);
        fake.Arrives("a1", marker: "entry-7");

        var verdict = await fake.Check().CheckAsync(ns, Attestation(ns), entry, liveSince: Now.AddDays(-1));

        verdict.Should().Be(DeadLetterReturnVerdict.Returned);
        fake.Ledger.Verify(l => l.RecordObservationAsync(
            It.Is<RecordObservationRequest>(r => r.EntryId == entry.Id && r.Outcome == RecoveryObservationOutcome.RecurrenceObserved && r.Confidence == VerificationConfidence.Exact),
            It.IsAny<CancellationToken>()), Times.Once);
        fake.Acknowledged.Should().Equal("a1");
    }

    [Fact]
    public async Task A_return_that_could_not_be_saved_is_not_acknowledged_so_PubSub_hands_it_over_again()
    {
        var ns = Ns();
        var entry = Entry(LongAgo);
        var fake = new Fake(entry) { SavingFails = true };
        fake.Arrives("a1", marker: "entry-7");
        fake.Arrives("a2");

        await fake.Check().CheckHealthAsync(ns, Attestation(ns));

        fake.Acknowledged.Should().Equal("a2");
    }

    [Fact]
    public async Task A_replay_made_before_the_subscription_was_being_read_cannot_be_answered()
    {
        var ns = Ns();
        var entry = Entry(LongAgo);
        var check = new Fake(entry).Check();

        (await check.CheckAsync(ns, Attestation(ns), entry, liveSince: null)).Should().Be(DeadLetterReturnVerdict.CannotTell("VIEW_NOT_LIVE_AT_REPLAY"));
        (await check.CheckAsync(ns, Attestation(ns), entry, liveSince: Now.AddHours(-1))).Should().Be(DeadLetterReturnVerdict.CannotTell("VIEW_NOT_LIVE_AT_REPLAY"));
    }

    [Fact]
    public async Task It_waits_until_the_message_has_used_up_its_delivery_attempts()
    {
        // 5 attempts × (10 s deadline + 600 s back-off) + 60 s = 3,110 s before "did not arrive" can mean "fixed".
        var ns = Ns();
        var early = Entry(TimeSpan.FromSeconds(3000));
        (await new Fake(early).Check().CheckAsync(ns, Attestation(ns), early, liveSince: Now.AddDays(-1))).Should().Be(DeadLetterReturnVerdict.TooEarly);
        var later = Entry(TimeSpan.FromSeconds(3200));
        (await new Fake(later).Check().CheckAsync(ns, Attestation(ns), later, liveSince: Now.AddDays(-1))).Should().Be(DeadLetterReturnVerdict.NotReturned);
    }

    [Fact]
    public async Task What_it_cannot_see_is_cannot_tell()
    {
        var ns = Ns();
        var unmarked = Entry(LongAgo, marker: null);
        (await new Fake(unmarked).Check().CheckAsync(ns, Attestation(ns), unmarked, Now.AddDays(-1))).Should().Be(DeadLetterReturnVerdict.CannotTell("NO_WAY_TO_RECOGNISE_IT"));

        var entry = Entry(LongAgo);
        var elsewhere = new Fake(entry) { DeadLetterTopic = "projects/my-project/topics/another-dlq" };
        (await elsewhere.Check().CheckAsync(ns, Attestation(ns), entry, Now.AddDays(-1))).Should().Be(DeadLetterReturnVerdict.CannotTell("OTHER_DEAD_LETTER_TOPIC"));
    }

    [Fact]
    public async Task Health_says_when_the_subscription_is_gone_or_reads_another_topic_and_that_what_it_saw_is_lost()
    {
        var ns = Ns();
        (await new Fake().Check().CheckHealthAsync(ns, Attestation(ns))).Should().Be(new DeadLetterViewHealth(true));
        (await new Fake { ObserverMissing = true }.Check().CheckHealthAsync(ns, Attestation(ns))).Should().Be(new DeadLetterViewHealth(false, "SUBSCRIPTION_MISSING", Lost: true));
        (await new Fake { ObserverTopic = "something-else" }.Check().CheckHealthAsync(ns, Attestation(ns))).Should().Be(new DeadLetterViewHealth(false, "WRONG_TOPIC", Lost: true));
        (await new Fake().Check().CheckHealthAsync(ns, Attestation(ns, observer: "customers-own-dlq-subscription"))).Healthy.Should().BeFalse();
    }

    [Fact]
    public void Its_own_subscription_must_be_named_so_nothing_mistakes_it_for_the_customers()
    {
        var check = new Fake().Check();
        check.NeedsObserverReference.Should().BeTrue();
        check.ValidateObserverReference(Observer).Should().BeNull();
        check.ValidateObserverReference("orders-topic-dlq-subscription").Should().NotBeNull();
        check.ValidateObserverReference(PubSubObserverSubscriptionCheck.ObserverSuffix).Should().NotBeNull("the suffix alone is not a name");
        check.ValidateObserverReference(null).Should().NotBeNull();
        PubSubObserverSubscriptionCheck.IsObserver(Observer).Should().BeTrue();
        PubSubObserverSubscriptionCheck.IsObserver("orders-topic-dlq-subscription").Should().BeFalse();
        PubSubObserverSubscriptionCheck.SubscriptionIdOf(Entry(TimeSpan.Zero)).Should().Be("orders-subscription");
    }
}
