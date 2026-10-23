using System.Net;
using FluentAssertions;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Providers.Azure.Components;

namespace ServiceHub.UnitTests.Components.Sources;

/// <summary>The Event Grid source against a fake Azure: the conformance kit plus what is particular to this service.</summary>
public sealed class EventGridSourceTests : ComponentSourceConformance
{
    private const string Sub = "11111111-1111-1111-1111-111111111111";
    private const string Account = "stgevents";
    private const string Container = "deadletter";
    private const string Place = $"blob://{Account}/{Container}";
    private const string Item = $"{Place}/orders/2026/10/01/abc.json";
    private const string AccountId = $"/subscriptions/{Sub}/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/{Account}";

    private readonly FakeAzure _azure = new();
    private readonly EventGridSource _source;
    private readonly Namespace _ns = Namespace.CreateWithManagedIdentity("grid-test", ConnectionAuthType.ManagedIdentity, provider: CloudProviderType.Azure).Value;

    public EventGridSourceTests()
    {
        _source = new EventGridSource(_azure);
        _azure.Subscriptions = [Sub];
        _azure.Containers[Sub] = [new DeadLetterContainer(Account, Container, AccountId)];
    }

    protected override IComponentSource Source => _source;

    protected override Namespace Connection => _ns;

    protected override string PlaceId => Place;

    protected override string ItemId => Item;

    protected override IReadOnlyList<string> CloudCalls => _azure.Calls;

    protected override void ArrangeFailures(int count)
    {
        var baseTime = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _azure.Blobs[(Account, Container)] = [.. Enumerable.Range(0, count).Select(i => new DeadLetterBlob(Account, Container, $"orders/2026/10/01/e{i}.json", baseTime.AddMinutes(i), 512))];
        _azure.Detail = new DeadLetterBlobRead(new DeadLetterBlob(Account, Container, "orders/2026/10/01/abc.json", null, null), "MaxDeliveryAttemptsExceeded", "Probation", 3, 500, baseTime);
    }

    protected override void ArrangeAccessDenied() => _azure.Throw = new AzureRestException(HttpStatusCode.Forbidden, "denied");

    protected override void ArrangeCloudFailure(string message) => _azure.Throw = new AzureRestException(HttpStatusCode.ServiceUnavailable, message);

    protected override void ArrangeSecretInFailureText(string secret)
    {
        var at = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _azure.Blobs[(Account, Container)] = [new DeadLetterBlob(Account, Container, $"orders/{secret}", at, 1)];
        _azure.Detail = new DeadLetterBlobRead(new DeadLetterBlob(Account, Container, "x", null, null), $"reason {secret}", $"outcome {secret}", 1, 500, at);
    }

    protected override void ArrangeMissingItem() => _azure.Detail = null;

    protected override void ArrangeCancellationAware() => _azure.HonourCancellation = true;

    [Fact]
    public async Task Places_are_the_dead_letter_containers_found_in_every_subscription_sorted_and_without_duplicates()
    {
        const string Other = "22222222-2222-2222-2222-222222222222";
        _azure.Subscriptions = [Sub, Other];
        _azure.Containers[Sub] = [new DeadLetterContainer("zeta", "dl", null), new DeadLetterContainer(Account, Container, AccountId)];
        _azure.Containers[Other] = [new DeadLetterContainer(Account, Container, AccountId)];

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;

        places.Items.Select(p => p.Id).Should().Equal(Place, "blob://zeta/dl");
        places.Items[0].Name.Should().Be($"{Container} ({Account})");
        places.Items[0].Kind.Should().Be("dead-letter container");
        places.Items[0].ConsoleUrl.Should().Be($"https://portal.azure.com/#@/resource{AccountId}/overview");
        places.Items[1].ConsoleUrl.Should().BeNull("no resource id was given for that account");
        places.Incomplete.Should().BeFalse();
    }

    [Fact]
    public async Task A_subscription_that_cannot_be_read_leaves_the_places_incomplete_and_keeps_the_rest()
    {
        const string Other = "22222222-2222-2222-2222-222222222222";
        _azure.Subscriptions = [Sub, Other];
        _azure.FailSubscription = Other;

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;

        places.Items.Should().ContainSingle();
        places.Incomplete.Should().BeTrue();
    }

    [Fact]
    public async Task A_reader_that_lost_part_of_its_read_marks_the_places_incomplete()
    {
        _azure.ContainersIncomplete = true;

        (await _source.ListPlacesAsync(_ns, default)).Value.Incomplete.Should().BeTrue();
    }

    [Fact]
    public async Task When_every_subscription_fails_the_places_are_an_error()
    {
        _azure.FailSubscription = Sub;

        (await _source.ListPlacesAsync(_ns, default)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task More_places_than_the_cap_say_so()
    {
        _azure.Containers[Sub] = [.. Enumerable.Range(0, ComponentSourceLimits.MaxPlaces + 5).Select(i => new DeadLetterContainer($"acct{i:000}", "dl", null))];

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;

        places.Items.Should().HaveCount(ComponentSourceLimits.MaxPlaces);
        places.ReachedLimit.Should().BeTrue();
    }

    [Fact]
    public async Task A_blob_is_listed_as_a_dead_lettered_event_without_claiming_to_hold_a_payload()
    {
        ArrangeFailures(1);

        var item = (await _source.ListFailedAsync(_ns, Place, default)).Value.Items.Single();

        item.Id.Should().Be($"{Place}/orders/2026/10/01/e0.json");
        item.Status.Should().Be("DEAD_LETTERED");
        item.Summary.Should().Be("Dead-lettered event: e0.json");
        item.Place.Should().Be(Place);
        item.Copy.Should().Be(CopyKind.None);
        item.ReasonCode.Should().BeNull();
    }

    [Fact]
    public async Task Newest_blobs_come_first_and_a_blob_with_no_time_comes_last()
    {
        var t = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _azure.Blobs[(Account, Container)] =
        [
            new DeadLetterBlob(Account, Container, "undated", null, 1),
            new DeadLetterBlob(Account, Container, "old", t, 1),
            new DeadLetterBlob(Account, Container, "new", t.AddHours(1), 1),
        ];

        var items = (await _source.ListFailedAsync(_ns, Place, default)).Value.Items;

        items.Select(i => i.Summary.Split(": ")[1]).Should().Equal("new", "old", "undated");
    }

    [Fact]
    public async Task Without_a_place_every_container_is_listed_and_at_most_twenty_are_read()
    {
        _azure.Containers[Sub] = [.. Enumerable.Range(0, EventGridSource.MaxContainersPerListing + 5).Select(i => new DeadLetterContainer($"acct{i:000}", "dl", null))];

        var page = (await _source.ListFailedAsync(_ns, null, default)).Value;

        _azure.Calls.Count(c => c == "ListBlobs").Should().Be(EventGridSource.MaxContainersPerListing);
        page.ReachedLimit.Should().BeTrue();
    }

    [Fact]
    public async Task One_container_that_cannot_be_read_leaves_the_page_incomplete_and_keeps_the_others()
    {
        ArrangeFailures(2);
        _azure.Containers[Sub] = [new DeadLetterContainer(Account, Container, null), new DeadLetterContainer("broken", "dl", null)];
        _azure.FailAccount = "broken";

        var page = (await _source.ListFailedAsync(_ns, null, default)).Value;

        page.Items.Should().HaveCount(2);
        page.Incomplete.Should().BeTrue();
    }

    [Fact]
    public async Task Access_is_refused_when_the_identity_sees_no_subscription()
    {
        _azure.Subscriptions = [];

        var access = await _source.ValidateAccessAsync(_ns, default);

        access.IsFailure.Should().BeTrue();
        access.Error.Message.Should().StartWith("Not enabled").And.Contain("Reader");
    }

    [Fact]
    public async Task Access_also_proves_a_blob_listing_when_a_container_exists_because_reader_does_not_grant_it()
    {
        var access = await _source.ValidateAccessAsync(_ns, default);

        access.IsSuccess.Should().BeTrue();
        _azure.Calls.Should().Equal("ListSubscriptions", "ListDeadLetterContainers", "ListBlobs");
    }

    [Fact]
    public async Task Access_is_not_enabled_with_the_data_role_named_when_the_blobs_are_refused()
    {
        _azure.FailAccount = Account;
        _azure.FailAccountWith = new AzureRestException(HttpStatusCode.Forbidden, "no data role");

        var access = await _source.ValidateAccessAsync(_ns, default);

        access.IsFailure.Should().BeTrue();
        access.Error.Message.Should().StartWith("Not enabled").And.Contain("Storage Blob Data Reader");
    }

    [Fact]
    public async Task Access_succeeds_without_a_blob_listing_when_no_event_subscription_dead_letters()
    {
        _azure.Containers[Sub] = [];

        var access = await _source.ValidateAccessAsync(_ns, default);

        access.IsSuccess.Should().BeTrue();
        _azure.Calls.Should().NotContain("ListBlobs");
    }

    [Fact]
    public async Task A_connection_that_does_not_sign_in_with_entra_is_not_enabled()
    {
        _azure.Throw = new AzureNotEntraException(ConnectionAuthType.ConnectionString);

        var access = await _source.ValidateAccessAsync(_ns, default);

        access.Error.Message.Should().StartWith("Not enabled").And.Contain("Entra");
    }

    [Fact]
    public async Task The_detail_names_the_reason_and_puts_attempts_outcome_and_status_in_the_cause()
    {
        ArrangeFailures(1);

        var detail = (await _source.GetFailedAsync(_ns, Item, default)).Value!;

        detail.Reason.Should().Be("MaxDeliveryAttemptsExceeded");
        detail.Cause.Should().Be("3 delivery attempts; last outcome Probation; last HTTP status 500");
        detail.StoppedUtc.Should().Be(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        detail.Item.Summary.Should().Be("Dead-lettered: MaxDeliveryAttemptsExceeded");
        detail.Item.Id.Should().Be(Item);
    }

    [Fact]
    public async Task One_attempt_is_singular_and_missing_parts_are_left_out()
    {
        ArrangeFailures(1);
        _azure.Detail = _azure.Detail! with { Attempts = 1, Outcome = null, HttpStatus = null };

        var detail = (await _source.GetFailedAsync(_ns, Item, default)).Value!;

        detail.Cause.Should().Be("1 delivery attempt");
    }

    [Fact]
    public async Task A_blob_with_no_recorded_reason_says_so_rather_than_inventing_one()
    {
        ArrangeFailures(1);
        _azure.Detail = new DeadLetterBlobRead(_azure.Detail!.Blob, null, null, null, null, null);

        var detail = (await _source.GetFailedAsync(_ns, Item, default)).Value!;

        detail.Reason.Should().BeNull();
        detail.Cause.Should().BeNull();
        detail.Item.Summary.Should().Be("Dead-lettered: reason not recorded");
    }

    [Theory]
    [InlineData("blob://ab/deadletter")]
    [InlineData("blob://STGEVENTS/deadletter")]
    [InlineData("blob://stgevents/-bad")]
    [InlineData("blob://stgevents/bad-")]
    [InlineData("blob://stgevents/UPPER")]
    [InlineData("blob://stgevents/deadletter/extra")]
    [InlineData("https://stgevents.blob.core.windows.net/deadletter")]
    [InlineData("blob://stgevents")]
    [InlineData("")]
    public async Task A_place_that_is_not_a_dead_letter_container_is_refused_before_any_call(string place)
    {
        var page = await _source.ListFailedAsync(_ns, place, default);

        page.IsFailure.Should().BeTrue();
        _azure.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("blob://stgevents/deadletter")]
    [InlineData("blob://stgevents/deadletter/")]
    [InlineData("blob://stgevents/deadletter/../x")]
    [InlineData("blob://stgevents/deadletter/a/../../x")]
    [InlineData("blob://stgevents/deadletter/a b")]
    [InlineData("blob://stgevents/deadletter/a?sas=1")]
    [InlineData("blob://stgevents/deadletter/a#frag")]
    [InlineData("blob://stgevents/deadletter/a\\b")]
    [InlineData("https://stgevents.blob.core.windows.net/deadletter/a")]
    public async Task An_item_that_is_not_a_dead_letter_blob_is_refused_before_any_call(string item)
    {
        var detail = await _source.GetFailedAsync(_ns, item, default);

        detail.IsFailure.Should().BeTrue();
        _azure.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task An_oversized_item_id_is_refused_before_any_call()
    {
        var detail = await _source.GetFailedAsync(_ns, $"{Place}/{new string('a', 1300)}", default);

        detail.IsFailure.Should().BeTrue();
        _azure.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task The_blob_is_read_from_the_account_and_container_in_the_id_and_nowhere_else()
    {
        ArrangeFailures(1);

        await _source.GetFailedAsync(_ns, Item, default);

        _azure.BlobsAsked.Should().Equal((Account, Container, "orders/2026/10/01/abc.json"));
    }

    [Fact]
    public async Task A_read_that_runs_out_of_time_is_a_failure_that_says_so_not_a_cancellation()
    {
        _azure.Throw = new OperationCanceledException();

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
        _azure.Throw = new OperationCanceledException();

        var failed = await _source.ListFailedAsync(_ns, Place, default);

        failed.IsFailure.Should().BeTrue();
        failed.Error.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Access_and_detail_reads_pass_a_cancellation_on_rather_than_hiding_it()
    {
        _azure.Throw = new OperationCanceledException();

        await ((Func<Task>)(() => _source.ValidateAccessAsync(_ns, default))).Should().ThrowAsync<OperationCanceledException>();
        await ((Func<Task>)(() => _source.GetFailedAsync(_ns, ItemId, default))).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void It_names_the_azure_adapter_and_declares_it_reads_no_payload()
    {
        _source.Provider.Should().Be(CloudProviderType.Azure);
        _source.ComponentKey.Should().Be("event-grid");
        _source.Capabilities.ReadsPayload.Should().BeFalse();
        _source.Capabilities.ReasonInList.Should().BeFalse();
    }

    /// <summary>Azure as the source sees it: records the operation names and answers from what a test arranged.</summary>
    private sealed class FakeAzure : IAzureEventGridReader
    {
        private readonly List<string> _calls = [];

        public IReadOnlyList<string> Calls => _calls;

        public IReadOnlyList<string> Subscriptions { get; set; } = [];

        public Dictionary<string, IReadOnlyList<DeadLetterContainer>> Containers { get; } = [];

        public bool ContainersIncomplete { get; set; }

        public Dictionary<(string Account, string Container), IReadOnlyList<DeadLetterBlob>> Blobs { get; } = [];

        public DeadLetterBlobRead? Detail { get; set; }

        public Exception? Throw { get; set; }

        public bool HonourCancellation { get; set; }

        public string? FailSubscription { get; set; }

        public string? FailAccount { get; set; }

        public Exception FailAccountWith { get; set; } = new AzureRestException(HttpStatusCode.ServiceUnavailable, "account down");

        public List<(string Account, string Container, string Name)> BlobsAsked { get; } = [];

        public Task<Listing<string>> ListSubscriptionsAsync(Namespace ns, CancellationToken ct) =>
            Run("ListSubscriptions", ct, () => new Listing<string>(Subscriptions, false));

        public Task<Listing<DeadLetterContainer>> ListDeadLetterContainersAsync(Namespace ns, string subscriptionId, int max, CancellationToken ct) =>
            Run("ListDeadLetterContainers", ct, () =>
            {
                if (subscriptionId == FailSubscription)
                {
                    throw new AzureRestException(HttpStatusCode.ServiceUnavailable, "subscription down");
                }

                var all = Containers.TryGetValue(subscriptionId, out var c) ? c : [];
                return new Listing<DeadLetterContainer>([.. all.Take(max)], all.Count > max, ContainersIncomplete);
            });

        public Task<Listing<DeadLetterBlob>> ListBlobsAsync(Namespace ns, string account, string container, int max, CancellationToken ct) =>
            Run("ListBlobs", ct, () =>
            {
                if (account == FailAccount)
                {
                    throw FailAccountWith;
                }

                var all = (Blobs.TryGetValue((account, container), out var b) ? b : []).OrderByDescending(x => x.ModifiedUtc).ToList();
                return new Listing<DeadLetterBlob>([.. all.Take(max)], all.Count > max);
            });

        public Task<DeadLetterBlobRead?> GetBlobAsync(Namespace ns, string account, string container, string blobName, CancellationToken ct)
        {
            lock (BlobsAsked)
            {
                BlobsAsked.Add((account, container, blobName));
            }

            return Run("GetBlob", ct, () => Detail);
        }

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
