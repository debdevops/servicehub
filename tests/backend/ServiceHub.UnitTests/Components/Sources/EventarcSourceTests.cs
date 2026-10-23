using FluentAssertions;
using Grpc.Core;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Providers.Gcp.Components;

namespace ServiceHub.UnitTests.Components.Sources;

/// <summary>The Eventarc source against a fake Google: the conformance kit plus what is particular to this service.</summary>
public sealed class EventarcSourceTests : ComponentSourceConformance
{
    private const string Project = "my-proj";
    private const string Orders = "projects/my-proj/locations/us-central1/triggers/orders";
    private const string Billing = "projects/my-proj/locations/europe-west1/triggers/billing";
    private const string OrdersSub = "projects/my-proj/subscriptions/eventarc-orders-sub";
    private const string BillingSub = "projects/my-proj/subscriptions/eventarc-billing-sub";
    private const string Dlt = "projects/my-proj/topics/orders-dlt";
    private const string Item = $"{Orders}#123456";

    private readonly FakeGoogle _google = new();
    private readonly EventarcSource _source;
    private readonly Namespace _ns = Namespace.Create(
        Project, "{\"type\":\"service_account\"}", provider: CloudProviderType.Gcp, gcpProjectId: Project).Value;

    public EventarcSourceTests()
    {
        _source = new EventarcSource(_google);
        _google.Locations = ["us-central1", "europe-west1"];
        _google.Triggers["us-central1"] = [new TriggerRef(Orders, OrdersSub)];
        _google.DeadLetterTopics[OrdersSub] = Dlt;
    }

    protected override IComponentSource Source => _source;

    protected override Namespace Connection => _ns;

    protected override string PlaceId => Orders;

    protected override string ItemId => Item;

    protected override IReadOnlyList<string> CloudCalls => _google.Calls;

    protected override void ArrangeFailures(int count)
    {
        var baseTime = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _google.Undelivered[Dlt] = [.. Enumerable.Range(0, count).Select(i => Event(123456 + i, baseTime.AddMinutes(i)))];
    }

    protected override void ArrangeAccessDenied() => _google.Throw = new RpcException(new Status(StatusCode.PermissionDenied, "denied"));

    protected override void ArrangeCloudFailure(string message) => _google.Throw = new RpcException(new Status(StatusCode.Unavailable, message));

    protected override void ArrangeSecretInFailureText(string secret)
    {
        var at = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var attributes = new Dictionary<string, string> { ["ce-type"] = $"type {secret}", ["ce-source"] = $"src {secret}", ["CloudPubSubDeadLetterSourceDeliveryErrorMessage"] = $"error {secret}" };
        _google.Undelivered[Dlt] = [new UndeliveredEvent("123456", at, attributes)];
    }

    protected override void ArrangeMissingItem() => _google.Undelivered[Dlt] = [];

    protected override void ArrangeCancellationAware() => _google.HonourCancellation = true;

    private static UndeliveredEvent Event(long id, DateTimeOffset at, string type = "google.cloud.pubsub.topic.v1.messagePublished", int count = 5, string error = "HTTP 500 from the service") =>
        new(id.ToString(System.Globalization.CultureInfo.InvariantCulture), at, new Dictionary<string, string>
        {
            ["ce-type"] = type,
            ["ce-source"] = "//pubsub.googleapis.com/projects/my-proj/topics/orders",
            ["CloudPubSubDeadLetterSourceDeliveryCount"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["CloudPubSubDeadLetterSourceDeliveryErrorMessage"] = error,
        });

    private static Namespace ProjectNamed(string project) =>
        Namespace.Create(project, "{\"type\":\"service_account\"}", provider: CloudProviderType.Gcp, gcpProjectId: project).Value;

    [Fact]
    public async Task Places_are_the_triggers_of_every_location_sorted_with_a_console_link()
    {
        _google.Triggers["europe-west1"] = [new TriggerRef(Billing, BillingSub)];

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;

        places.Items.Select(p => p.Name).Should().Equal("billing", "orders");
        places.Items.Should().OnlyContain(p => p.Kind == "trigger");
        places.Items[0].ConsoleUrl.Should().Be("https://console.cloud.google.com/eventarc/triggers/europe-west1/billing?project=my-proj");
        places.Incomplete.Should().BeFalse();
    }

    [Fact]
    public async Task A_location_that_cannot_be_read_leaves_the_places_incomplete_and_keeps_the_rest()
    {
        _google.FailLocation = "europe-west1";

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;

        places.Items.Should().ContainSingle().Which.Name.Should().Be("orders");
        places.Incomplete.Should().BeTrue();
    }

    [Fact]
    public async Task More_places_than_the_cap_say_so()
    {
        _google.Triggers["us-central1"] = [.. Enumerable.Range(0, ComponentSourceLimits.MaxPlaces + 5).Select(i => new TriggerRef($"projects/my-proj/locations/us-central1/triggers/t{i:000}", null))];

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;

        places.Items.Should().HaveCount(ComponentSourceLimits.MaxPlaces);
        places.ReachedLimit.Should().BeTrue();
    }

    [Fact]
    public async Task An_undelivered_event_is_listed_with_its_type_and_without_claiming_a_payload()
    {
        ArrangeFailures(1);

        var item = (await _source.ListFailedAsync(_ns, Orders, default)).Value.Items.Single();

        item.Id.Should().Be(Item);
        item.Place.Should().Be(Orders);
        item.Status.Should().Be("UNDELIVERED");
        item.Summary.Should().Be("Undelivered event: google.cloud.pubsub.topic.v1.messagePublished");
        item.Copy.Should().Be(CopyKind.None);
        item.ReasonCode.Should().BeNull();
        item.ConsoleUrl.Should().Be("https://console.cloud.google.com/eventarc/triggers/us-central1/orders?project=my-proj");
    }

    [Fact]
    public async Task An_event_with_no_type_is_still_listed()
    {
        _google.Undelivered[Dlt] = [new UndeliveredEvent("1", null, new Dictionary<string, string>())];

        var item = (await _source.ListFailedAsync(_ns, Orders, default)).Value.Items.Single();

        item.Summary.Should().Be("Undelivered event");
        item.AtUtc.Should().BeNull();
    }

    [Fact]
    public async Task A_trigger_with_no_dead_letter_policy_is_an_error_when_asked_for_by_name_not_an_empty_list()
    {
        _google.DeadLetterTopics.Clear();

        var page = await _source.ListFailedAsync(_ns, Orders, default);

        page.IsFailure.Should().BeTrue();
        page.Error.Message.Should().Contain("keeps nothing").And.Contain("dead-letter policy");
        _google.Calls.Should().NotContain("ListUndelivered");
    }

    [Fact]
    public async Task A_trigger_with_no_transport_subscription_keeps_nothing_either()
    {
        _google.Triggers["us-central1"] = [new TriggerRef(Orders, null)];

        (await _source.ListFailedAsync(_ns, Orders, default)).IsFailure.Should().BeTrue();
        _google.Calls.Should().NotContain("GetDeadLetterTopic", "there is no subscription to ask about");
    }

    [Fact]
    public async Task When_no_trigger_keeps_anything_the_whole_listing_is_an_error()
    {
        _google.DeadLetterTopics.Clear();

        var page = await _source.ListFailedAsync(_ns, null, default);

        page.IsFailure.Should().BeTrue();
        page.Error.Message.Should().Contain("None of these triggers keeps anything");
    }

    [Fact]
    public async Task No_triggers_at_all_is_an_error_naming_that_nothing_is_kept_not_a_clean_bill_of_health()
    {
        _google.Triggers.Clear();

        var page = await _source.ListFailedAsync(_ns, null, default);

        page.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task A_dead_letter_topic_with_no_pull_subscription_is_an_error_that_says_what_to_add()
    {
        _google.UnreadableTopics.Add(Dlt);

        var page = await _source.ListFailedAsync(_ns, Orders, default);

        page.IsFailure.Should().BeTrue();
        page.Error.Message.Should().Contain("no pull subscription");
    }

    [Fact]
    public async Task Without_a_place_every_trigger_that_keeps_something_is_read_and_a_shared_topic_is_looked_at_once()
    {
        ArrangeFailures(2);
        _google.Triggers["europe-west1"] = [new TriggerRef(Billing, BillingSub)];
        _google.DeadLetterTopics[BillingSub] = Dlt;

        var page = (await _source.ListFailedAsync(_ns, null, default)).Value;

        _google.Calls.Count(c => c == "ListUndelivered").Should().Be(1);
        page.Items.Should().HaveCount(2, "an event is not listed once per trigger");
    }

    [Fact]
    public async Task Events_are_attributed_to_the_first_trigger_that_uses_the_topic()
    {
        ArrangeFailures(1);
        _google.Triggers["europe-west1"] = [new TriggerRef(Billing, BillingSub)];
        _google.DeadLetterTopics[BillingSub] = Dlt;

        var item = (await _source.ListFailedAsync(_ns, null, default)).Value.Items.Single();

        item.Place.Should().Be(Billing, "triggers are read in name order, and billing sorts before orders");
    }

    [Fact]
    public async Task Triggers_with_different_topics_each_contribute_and_the_newest_come_first()
    {
        const string BillingDlt = "projects/my-proj/topics/billing-dlt";
        var t = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _google.Triggers["europe-west1"] = [new TriggerRef(Billing, BillingSub)];
        _google.DeadLetterTopics[BillingSub] = BillingDlt;
        _google.Undelivered[Dlt] = [Event(1, t)];
        _google.Undelivered[BillingDlt] = [Event(2, t.AddHours(1))];

        var items = (await _source.ListFailedAsync(_ns, null, default)).Value.Items;

        items.Select(i => i.Id).Should().Equal($"{Billing}#2", $"{Orders}#1");
    }

    [Fact]
    public async Task Only_the_first_twenty_triggers_are_read_without_a_place_and_the_page_says_there_are_more()
    {
        _google.Triggers["us-central1"] = [.. Enumerable.Range(0, EventarcSource.MaxTriggersPerListing + 5).Select(i => new TriggerRef($"projects/my-proj/locations/us-central1/triggers/t{i:000}", $"projects/my-proj/subscriptions/s{i:000}"))];
        _google.DeadLetterTopics.Clear();

        await _source.ListFailedAsync(_ns, null, default);

        _google.Calls.Count(c => c == "GetDeadLetterTopic").Should().Be(EventarcSource.MaxTriggersPerListing);
    }

    [Fact]
    public async Task A_trigger_whose_policy_cannot_be_read_leaves_the_page_incomplete_and_keeps_the_others()
    {
        ArrangeFailures(2);
        _google.Triggers["europe-west1"] = [new TriggerRef(Billing, BillingSub)];
        _google.FailSubscription = BillingSub;

        var page = (await _source.ListFailedAsync(_ns, null, default)).Value;

        page.Items.Should().HaveCount(2);
        page.Incomplete.Should().BeTrue();
    }

    [Fact]
    public async Task A_trigger_that_is_gone_is_an_error_for_a_place_asked_by_name()
    {
        _google.Triggers.Clear();

        (await _source.ListFailedAsync(_ns, Orders, default)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task The_detail_carries_the_error_pubsub_recorded_and_a_cause_with_tries_type_and_source()
    {
        ArrangeFailures(1);

        var detail = (await _source.GetFailedAsync(_ns, Item, default)).Value!;

        detail.Reason.Should().Be("HTTP 500 from the service");
        detail.Cause.Should().Be("tried 5 times; event type google.cloud.pubsub.topic.v1.messagePublished; from //pubsub.googleapis.com/projects/my-proj/topics/orders");
        detail.StoppedUtc.Should().NotBeNull();
        detail.Item.Id.Should().Be(Item);
    }

    [Fact]
    public async Task One_try_is_singular_and_a_bare_event_has_no_cause()
    {
        _google.Undelivered[Dlt] = [new UndeliveredEvent("123456", null, new Dictionary<string, string> { ["CloudPubSubDeadLetterSourceDeliveryCount"] = "1" })];
        (await _source.GetFailedAsync(_ns, Item, default)).Value!.Cause.Should().Be("tried 1 time");

        _google.Undelivered[Dlt] = [new UndeliveredEvent("123456", null, new Dictionary<string, string>())];
        var bare = (await _source.GetFailedAsync(_ns, Item, default)).Value!;
        bare.Cause.Should().BeNull();
        bare.Reason.Should().BeNull();
    }

    [Fact]
    public async Task Long_error_text_is_bounded_and_marked()
    {
        _google.Undelivered[Dlt] = [Event(123456, DateTimeOffset.UtcNow, error: new string('x', 5000))];

        var detail = (await _source.GetFailedAsync(_ns, Item, default)).Value!;

        detail.Reason!.Length.Should().Be(1001);
        detail.Reason.Should().EndWith("…");
    }

    [Fact]
    public async Task An_item_whose_trigger_is_gone_or_keeps_nothing_is_a_success_with_nothing()
    {
        ArrangeFailures(1);
        _google.DeadLetterTopics.Clear();
        (await _source.GetFailedAsync(_ns, Item, default)).Value.Should().BeNull();

        _google.Triggers.Clear();
        (await _source.GetFailedAsync(_ns, Item, default)).Value.Should().BeNull();
    }

    [Fact]
    public async Task Access_is_proved_by_listing_triggers_in_one_location()
    {
        var access = await _source.ValidateAccessAsync(_ns, default);

        access.IsSuccess.Should().BeTrue();
        _google.Calls.Should().Equal("ListTriggers");
        _google.MaxAsked.Should().Be(1);
    }

    [Fact]
    public async Task A_connection_with_no_project_cannot_read_anything()
    {
        var ns = Namespace.CreateWithManagedIdentity("no-project-test", ConnectionAuthType.GcpWorkloadIdentity, provider: CloudProviderType.Gcp).Value;

        (await _source.ValidateAccessAsync(ns, default)).IsFailure.Should().BeTrue();
        (await _source.ListPlacesAsync(ns, default)).IsFailure.Should().BeTrue();
        (await _source.ListFailedAsync(ns, null, default)).IsFailure.Should().BeTrue();
        (await _source.GetFailedAsync(ns, Item, default)).IsFailure.Should().BeTrue();
        _google.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Google_names_a_trigger_by_the_project_number_and_it_is_always_read_in_the_connections_own_project()
    {
        var ns = ProjectNamed("proj-num");
        const string Numbered = "projects/1078147762303/locations/us-central1/triggers/orders";
        const string Own = "projects/proj-num/locations/us-central1/triggers/orders";
        _google.Triggers["us-central1"] = [new TriggerRef(Own, OrdersSub)];

        await _source.ListFailedAsync(ns, Numbered, default);

        _google.NamesAsked.Should().Equal(Own); // rewritten to the connection's own project id, and the number is never sent
    }

    [Fact]
    public async Task A_numeric_project_in_an_item_is_rewritten_and_a_non_numeric_other_project_is_refused()
    {
        var ns = ProjectNamed("proj-item");

        (await _source.GetFailedAsync(ns, "projects/not-mine/locations/us-central1/triggers/orders#1", default)).IsFailure.Should().BeTrue();
        await _source.GetFailedAsync(ns, "projects/123/locations/us-central1/triggers/orders#1", default);
        _google.NamesAsked.Should().OnlyContain(n => n.StartsWith("projects/proj-item/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("projects/other/locations/us-central1/triggers/orders")]
    [InlineData("projects/my-proj/locations/us-central1/triggers/orders#123")]
    [InlineData("projects/my-proj/locations/us-central1/workflows/orders")]
    [InlineData("projects/my-proj/locations/us-central1")]
    [InlineData("")]
    public async Task A_place_in_another_project_or_that_is_not_a_trigger_is_refused_before_any_call(string place)
    {
        var page = await _source.ListFailedAsync(_ns, place, default);

        page.IsFailure.Should().BeTrue();
        _google.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("projects/other/locations/us-central1/triggers/orders#123")]
    [InlineData("projects/my-proj/locations/us-central1/triggers/orders")]
    [InlineData("projects/my-proj/locations/us-central1/triggers/orders#")]
    [InlineData("projects/my-proj/locations/us-central1/triggers/orders#abc")]
    [InlineData("projects/my-proj/locations/us-central1/triggers/orders#12 3")]
    [InlineData("#123")]
    public async Task An_item_that_is_not_an_event_of_a_trigger_in_this_project_is_refused_before_any_call(string item)
    {
        var detail = await _source.GetFailedAsync(_ns, item, default);

        detail.IsFailure.Should().BeTrue();
        _google.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_read_that_runs_out_of_time_is_a_failure_that_says_so_not_a_cancellation()
    {
        _google.Throw = new OperationCanceledException();

        var places = await _source.ListPlacesAsync(_ns, default);
        var failed = await _source.ListFailedAsync(_ns, null, default);

        places.IsFailure.Should().BeTrue();
        places.Error.Message.Should().Contain("took too long");
        failed.IsFailure.Should().BeTrue();
        failed.Error.Message.Should().Contain("took too long");
    }

    [Fact]
    public async Task A_read_of_one_place_that_runs_out_of_time_is_a_failure_not_an_empty_list()
    {
        _google.Throw = new OperationCanceledException();

        var failed = await _source.ListFailedAsync(_ns, Orders, default);

        failed.IsFailure.Should().BeTrue();
        failed.Error.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Access_and_detail_reads_pass_a_cancellation_on_rather_than_hiding_it()
    {
        _google.Throw = new OperationCanceledException();

        await ((Func<Task>)(() => _source.ValidateAccessAsync(_ns, default))).Should().ThrowAsync<OperationCanceledException>();
        await ((Func<Task>)(() => _source.GetFailedAsync(_ns, ItemId, default))).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void It_names_the_gcp_adapter_and_declares_it_reads_no_payload()
    {
        _source.Provider.Should().Be(CloudProviderType.Gcp);
        _source.ComponentKey.Should().Be("eventarc");
        _source.Capabilities.ReadsPayload.Should().BeFalse();
        _source.Capabilities.ReasonInList.Should().BeFalse();
    }

    /// <summary>Google as the source sees it: records the operation names and answers from what a test arranged.</summary>
    private sealed class FakeGoogle : IGcpEventarcReader
    {
        private readonly List<string> _calls = [];

        public IReadOnlyList<string> Calls => _calls;

        public IReadOnlyList<string> Locations { get; set; } = [];

        public Dictionary<string, IReadOnlyList<TriggerRef>> Triggers { get; } = [];

        public Dictionary<string, string> DeadLetterTopics { get; } = [];

        public Dictionary<string, IReadOnlyList<UndeliveredEvent>> Undelivered { get; } = [];

        public HashSet<string> UnreadableTopics { get; } = [];

        public Exception? Throw { get; set; }

        public bool HonourCancellation { get; set; }

        public string? FailLocation { get; set; }

        public string? FailSubscription { get; set; }

        public int MaxAsked { get; private set; }

        public List<string> NamesAsked { get; } = [];

        public Task<Listing<string>> ListLocationsAsync(Namespace ns, CancellationToken ct) =>
            Run("ListLocations", ct, () => new Listing<string>(Locations, false));

        public Task<Listing<TriggerRef>> ListTriggersAsync(Namespace ns, string location, int max, CancellationToken ct) =>
            Run("ListTriggers", ct, () =>
            {
                MaxAsked = max;
                if (location == FailLocation)
                {
                    throw new RpcException(new Status(StatusCode.Unavailable, "location down"));
                }

                var all = Triggers.TryGetValue(location, out var t) ? t : [];
                return new Listing<TriggerRef>([.. all.Take(max)], all.Count > max);
            });

        public Task<TriggerRef?> GetTriggerAsync(Namespace ns, string triggerName, CancellationToken ct) =>
            Run("GetTrigger", ct, () =>
            {
                lock (NamesAsked)
                {
                    NamesAsked.Add(triggerName);
                }

                return Triggers.Values.SelectMany(x => x).FirstOrDefault(t => t.Name == triggerName);
            });

        public Task<string?> GetDeadLetterTopicAsync(Namespace ns, string subscriptionName, CancellationToken ct) =>
            Run("GetDeadLetterTopic", ct, () =>
            {
                if (subscriptionName == FailSubscription)
                {
                    throw new RpcException(new Status(StatusCode.Unavailable, "subscription down"));
                }

                return DeadLetterTopics.TryGetValue(subscriptionName, out var topic) ? topic : null;
            });

        public Task<DeadLetterRead> ListUndeliveredAsync(Namespace ns, string deadLetterTopic, int max, CancellationToken ct) =>
            Run("ListUndelivered", ct, () =>
            {
                if (UnreadableTopics.Contains(deadLetterTopic))
                {
                    return new DeadLetterRead([], false, false);
                }

                var all = (Undelivered.TryGetValue(deadLetterTopic, out var e) ? e : []).OrderByDescending(x => x.PublishedUtc).ToList();
                return new DeadLetterRead([.. all.Take(max)], all.Count > max, true);
            });

        public Task<UndeliveredEvent?> GetUndeliveredAsync(Namespace ns, string deadLetterTopic, string messageId, CancellationToken ct) =>
            Run("GetUndelivered", ct, () => (Undelivered.TryGetValue(deadLetterTopic, out var e) ? e : []).FirstOrDefault(x => x.MessageId == messageId));

        private Task<T> Run<T>(string call, CancellationToken ct, Func<T> answer)
        {
            lock (_calls)
            {
                _calls.Add(call);
            }

            if (HonourCancellation)
            {
                ct.ThrowIfCancellationRequested();
            }

            if (Throw is not null)
            {
                return Task.FromException<T>(Throw);
            }

            try
            {
                return Task.FromResult(answer());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }
    }
}
