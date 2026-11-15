using System.Net;
using FluentAssertions;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Providers.Azure.Components;

namespace ServiceHub.UnitTests.Components.Sources;

/// <summary>The Storage Queues source against a fake Azure: the conformance kit plus what is particular to this service.</summary>
public sealed class StorageQueuesSourceTests : ComponentSourceConformance
{
    private const string Sub = "11111111-1111-1111-1111-111111111111";
    private const string Account = "stgqueues";
    private const string Queue = "orders-poison";
    private const string Place = $"queue://{Account}/{Queue}";
    private const string AccountId = $"/subscriptions/{Sub}/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/{Account}";

    private static readonly string Item = $"{Place}/{Id(0)}";

    private readonly FakeAzure _azure = new();
    private readonly StorageQueuesSource _source;
    private readonly Namespace _ns = Namespace.CreateWithManagedIdentity("queues-test", ConnectionAuthType.ManagedIdentity, provider: CloudProviderType.Azure).Value;

    public StorageQueuesSourceTests()
    {
        _source = new StorageQueuesSource(_azure);
        _azure.Subscriptions = [Sub];
        _azure.Queues[Sub] = [new PoisonQueue(Account, Queue, AccountId)];
    }

    protected override IComponentSource Source => _source;

    protected override Namespace Connection => _ns;

    protected override string PlaceId => Place;

    protected override string ItemId => Item;

    protected override IReadOnlyList<string> CloudCalls => _azure.Calls;

    protected override void ArrangeFailures(int count)
    {
        var at = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _azure.Messages[(Account, Queue)] = [.. Enumerable.Range(0, count).Select(i => new PoisonMessage(Account, Queue, Id(i), at.AddMinutes(i), 5))];
    }

    protected override void ArrangeAccessDenied() => _azure.Throw = new AzureRestException(HttpStatusCode.Forbidden, "denied");

    protected override void ArrangeCloudFailure(string message) => _azure.Throw = new AzureRestException(HttpStatusCode.ServiceUnavailable, message);

    // The message text is never read, so there is nowhere for a workload's secret to reach the screen; the kit still checks what is shown.
    protected override void ArrangeSecretInFailureText(string secret)
    {
    }

    protected override void ArrangeMissingItem() => _azure.Messages.Clear();

    protected override void ArrangeCancellationAware() => _azure.HonourCancellation = true;

    private static readonly string Link =
        $"https://portal.azure.com/#view/Microsoft_Azure_Storage/QueueMenuBlade/~/overview/storageAccountId/{Uri.EscapeDataString(AccountId)}/queueName/{Queue}";

    private static string Id(int i) => $"00000000-0000-0000-0000-{i:D12}";

    [Fact]
    public async Task Places_are_the_poison_queues_found_in_every_subscription_sorted_and_without_duplicates()
    {
        const string Other = "22222222-2222-2222-2222-222222222222";
        _azure.Subscriptions = [Sub, Other];
        _azure.Queues[Sub] = [new PoisonQueue("zeta", "jobs-poison", null), new PoisonQueue(Account, Queue, AccountId)];
        _azure.Queues[Other] = [new PoisonQueue(Account, Queue, AccountId)];

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;

        places.Items.Select(p => p.Id).Should().Equal(Place, "queue://zeta/jobs-poison");
        places.Items[0].Name.Should().Be($"{Queue} ({Account})");
        places.Items[0].Kind.Should().Be("poison queue");
    }

    [Fact]
    public async Task A_queue_that_is_not_a_poison_queue_is_refused_so_nothing_else_is_read_on_a_callers_say_so()
    {
        var before = _azure.Calls.Count;

        var list = await _source.ListFailedAsync(_ns, $"queue://{Account}/orders", default);
        var item = await _source.GetFailedAsync(_ns, $"queue://{Account}/orders/{Id(0)}", default);

        list.IsFailure.Should().BeTrue();
        item.IsFailure.Should().BeTrue();
        _azure.Calls.Count.Should().Be(before);
    }

    [Fact]
    public async Task A_listed_message_names_no_text_and_offers_no_copy_or_second_place_to_retry()
    {
        ArrangeFailures(1);

        var item = (await _source.ListFailedAsync(_ns, null, default)).Value.Items.Single();

        item.ConsoleUrl.Should().Be(Link);
        item.Copy.Should().Be(CopyKind.None);
        item.HeldIn.Should().BeNull();
        item.Status.Should().Be("POISONED");
    }

    [Fact]
    public async Task A_message_listed_by_queue_name_still_links_to_that_queue()
    {
        ArrangeFailures(1);

        var item = (await _source.ListFailedAsync(_ns, Place, default)).Value.Items.Single();

        item.ConsoleUrl.Should().Be(Link);
    }

    [Fact]
    public async Task A_listing_by_queue_name_still_shows_the_messages_when_the_link_cannot_be_found()
    {
        ArrangeFailures(2);
        _azure.Queues[Sub] = [];

        var items = (await _source.ListFailedAsync(_ns, Place, default)).Value.Items;

        items.Should().HaveCount(2);
        items.Should().OnlyContain(i => i.ConsoleUrl == null);
    }

    [Fact]
    public async Task A_place_says_how_many_messages_wait_in_it_and_how_often_they_were_tried()
    {
        ArrangeFailures(3);

        var place = (await _source.ListPlacesAsync(_ns, default)).Value.Items.Single();

        place.Detail.Should().Be("3 messages, tried up to 5 times, newest 2026-10-01 00:02 UTC");
    }

    [Fact]
    public async Task An_empty_queue_is_listed_as_empty()
    {
        var place = (await _source.ListPlacesAsync(_ns, default)).Value.Items.Single();

        place.Detail.Should().Be("Empty");
    }

    [Fact]
    public async Task A_queue_that_cannot_be_peeked_is_still_listed_without_a_line_and_the_list_says_it_is_incomplete()
    {
        ArrangeFailures(1);
        _azure.ThrowOnMessages = new AzureRestException(HttpStatusCode.Forbidden, "denied");

        var page = (await _source.ListPlacesAsync(_ns, default)).Value;

        page.Items.Single().Detail.Should().BeNull();
        page.Incomplete.Should().BeTrue();
    }

    [Fact]
    public async Task A_detail_says_how_often_the_message_was_tried()
    {
        ArrangeFailures(1);

        var detail = (await _source.GetFailedAsync(_ns, Item, default)).Value!;

        detail.Cause.Should().Contain("5 times");
    }

    private sealed class FakeAzure : IAzureStorageQueuesReader
    {
        private readonly List<string> _calls = [];

        public IReadOnlyList<string> Calls => _calls;

        public IReadOnlyList<string> Subscriptions { get; set; } = [];

        public Dictionary<string, IReadOnlyList<PoisonQueue>> Queues { get; } = [];

        public Dictionary<(string Account, string Queue), IReadOnlyList<PoisonMessage>> Messages { get; } = [];

        public Exception? Throw { get; set; }

        public Exception? ThrowOnMessages { get; set; }

        public bool HonourCancellation { get; set; }

        public Task<Listing<string>> ListSubscriptionsAsync(Namespace ns, CancellationToken ct) =>
            Run("ListSubscriptions", ct, () => new Listing<string>(Subscriptions, false));

        public Task<Listing<PoisonQueue>> ListPoisonQueuesAsync(Namespace ns, string subscriptionId, int max, CancellationToken ct) =>
            Run("ListPoisonQueues", ct, () =>
            {
                var all = Queues.TryGetValue(subscriptionId, out var q) ? q : [];
                return new Listing<PoisonQueue>([.. all.Take(max)], all.Count > max);
            });

        public Task<Listing<PoisonMessage>> ListMessagesAsync(Namespace ns, string account, string queue, int max, CancellationToken ct) =>
            Run("ListMessages", ct, () =>
            {
                if (ThrowOnMessages is not null)
                {
                    throw ThrowOnMessages;
                }

                var all = Messages.TryGetValue((account, queue), out var m) ? m : [];
                return new Listing<PoisonMessage>([.. all.Take(max)], all.Count > max);
            });

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

            return Throw is not null ? Task.FromException<T>(Throw) : Task.FromResult(answer());
        }
    }
}
