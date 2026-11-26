using System.Net;
using System.Xml;
using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Providers.Azure.Components;

namespace ServiceHub.UnitTests.Components.Sources;

/// <summary>What the Event Grid reader asks Azure Resource Manager and Blob storage for, and how it reads the answers.</summary>
public sealed class AzureEventGridReaderTests
{
    private const string Sub = "11111111-1111-1111-1111-111111111111";
    private const string Base = $"/subscriptions/{Sub}/providers/Microsoft.EventGrid";
    private const string Global = $"{Base}/eventSubscriptions?api-version=2022-06-15";
    private const string Topics = $"{Base}/topics?api-version=2022-06-15";
    private const string SystemTopics = $"{Base}/systemTopics?api-version=2022-06-15";
    private const string AccountId = $"/subscriptions/{Sub}/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/StgEvents";

    private readonly FakeAzureRest _rest = new();
    private readonly AzureEventGridReader _reader;
    private readonly Namespace _ns = Namespace.CreateWithManagedIdentity("grid-reader", ConnectionAuthType.DefaultAzureCredential, provider: CloudProviderType.Azure).Value;

    public AzureEventGridReaderTests() => _reader = new AzureEventGridReader(_rest);

    private static string Subscription(string container = "deadletter", string resourceId = AccountId, string endpoint = "StorageBlob", bool withIdentity = false)
    {
        var destination = "{\"endpointType\":\"" + endpoint + "\",\"properties\":{\"resourceId\":\"" + resourceId + "\",\"blobContainerName\":\"" + container + "\"}}";
        return withIdentity
            ? "{\"properties\":{\"deadLetterWithResourceIdentity\":{\"deadLetterDestination\":" + destination + "}}}"
            : "{\"properties\":{\"deadLetterDestination\":" + destination + "}}";
    }

    private static string List(params string[] items) => "{\"value\":[" + string.Join(',', items) + "]}";

    [Fact]
    public async Task A_subscription_scope_event_subscription_with_a_blob_destination_gives_its_container_and_lowercases_the_account()
    {
        _rest.Answers[Global] = List(Subscription());

        var found = await _reader.ListDeadLetterContainersAsync(_ns, Sub, 10, default);

        found.Items.Should().ContainSingle().Which.Should().Be(new DeadLetterContainer("stgevents", "deadletter", AccountId));
        found.More.Should().BeFalse();
        found.Incomplete.Should().BeFalse();
    }

    [Fact]
    public async Task A_destination_given_with_a_resource_identity_is_read_too()
    {
        _rest.Answers[Global] = List(Subscription(withIdentity: true));

        (await _reader.ListDeadLetterContainersAsync(_ns, Sub, 10, default)).Items.Should().ContainSingle();
    }

    [Theory]
    [InlineData("WebHook")]
    [InlineData("EventHub")]
    [InlineData("")]
    public async Task A_dead_letter_destination_that_is_not_a_blob_container_is_ignored(string endpoint)
    {
        _rest.Answers[Global] = List(Subscription(endpoint: endpoint));

        (await _reader.ListDeadLetterContainersAsync(_ns, Sub, 10, default)).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task An_event_subscription_with_no_dead_letter_destination_is_ignored()
    {
        _rest.Answers[Global] = List("{\"properties\":{\"destination\":{\"endpointType\":\"WebHook\"}}}", "{}");

        (await _reader.ListDeadLetterContainersAsync(_ns, Sub, 10, default)).Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/subscriptions/x/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/ab", "deadletter")]
    [InlineData("/subscriptions/x/resourceGroups/rg/providers/Microsoft.Web/sites/stgevents", "deadletter")]
    [InlineData("https://evil.example/storageAccounts/stgevents", "deadletter")]
    [InlineData(AccountId, "UPPER")]
    [InlineData(AccountId, "ab")]
    [InlineData(AccountId, "../x")]
    [InlineData(AccountId, "bad name")]
    public async Task A_storage_account_or_container_name_that_azure_would_not_allow_is_ignored(string resourceId, string container)
    {
        _rest.Answers[Global] = List(Subscription(container, resourceId));

        (await _reader.ListDeadLetterContainersAsync(_ns, Sub, 10, default)).Items.Should().BeEmpty("a name from an answer is never turned into an address unless it is a valid one");
    }

    [Fact]
    public async Task Topics_and_system_topics_are_searched_too_each_under_its_own_path()
    {
        const string Topic = $"/subscriptions/{Sub}/resourceGroups/rg/providers/Microsoft.EventGrid/topics/orders";
        const string System = $"/subscriptions/{Sub}/resourceGroups/rg/providers/Microsoft.EventGrid/systemTopics/storage";
        _rest.Answers[Topics] = List("{\"id\":\"" + Topic + "\"}");
        _rest.Answers[SystemTopics] = List("{\"id\":\"" + System + "\"}");
        _rest.Answers[$"{Topic}/providers/Microsoft.EventGrid/eventSubscriptions?api-version=2022-06-15"] = List(Subscription("topic-dl", AccountId.Replace("StgEvents", "acctone", StringComparison.Ordinal)));
        _rest.Answers[$"{System}/eventSubscriptions?api-version=2022-06-15"] = List(Subscription("system-dl", AccountId.Replace("StgEvents", "accttwo", StringComparison.Ordinal)));

        var found = await _reader.ListDeadLetterContainersAsync(_ns, Sub, 10, default);

        found.Items.Select(c => $"{c.Account}/{c.Container}").Should().Equal("acctone/topic-dl", "accttwo/system-dl");
    }

    [Fact]
    public async Task The_same_container_named_by_two_event_subscriptions_is_one_container()
    {
        _rest.Answers[Global] = List(Subscription(), Subscription());

        (await _reader.ListDeadLetterContainersAsync(_ns, Sub, 10, default)).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Containers_are_sorted_and_cut_at_the_cap_with_more_reported()
    {
        _rest.Answers[Global] = List(
            Subscription("ccc", AccountId.Replace("StgEvents", "bbb", StringComparison.Ordinal)),
            Subscription("aaa", AccountId.Replace("StgEvents", "ccc", StringComparison.Ordinal)),
            Subscription("bbb", AccountId.Replace("StgEvents", "aaa", StringComparison.Ordinal)));

        var found = await _reader.ListDeadLetterContainersAsync(_ns, Sub, 2, default);

        found.Items.Select(c => c.Account).Should().Equal("aaa", "bbb");
        found.More.Should().BeTrue();
    }

    [Fact]
    public async Task A_topic_list_that_fails_leaves_the_result_incomplete_and_the_rest_is_still_read()
    {
        _rest.Answers[Global] = List(Subscription());
        _rest.Fail = url => url.AbsolutePath.EndsWith("/topics", StringComparison.Ordinal) ? new AzureRestException(HttpStatusCode.Forbidden, "no topics") : null;

        var found = await _reader.ListDeadLetterContainersAsync(_ns, Sub, 10, default);

        found.Items.Should().ContainSingle();
        found.Incomplete.Should().BeTrue();
    }

    [Fact]
    public async Task An_event_subscription_list_that_fails_leaves_the_result_incomplete()
    {
        _rest.Fail = url => url.AbsolutePath.EndsWith("/eventSubscriptions", StringComparison.Ordinal) ? new AzureRestException(HttpStatusCode.TooManyRequests, "slow") : null;

        var found = await _reader.ListDeadLetterContainersAsync(_ns, Sub, 10, default);

        found.Items.Should().BeEmpty();
        found.Incomplete.Should().BeTrue();
    }

    [Fact]
    public async Task Only_twenty_topics_of_each_kind_are_looked_into_and_more_is_reported()
    {
        var topics = Enumerable.Range(0, AzureEventGridReader.MaxTopicsPerSubscription + 3).Select(i => "{\"id\":\"/subscriptions/" + Sub + "/resourceGroups/rg/providers/Microsoft.EventGrid/topics/t" + i + "\"}").ToArray();
        _rest.Answers[Topics] = List(topics);

        var found = await _reader.ListDeadLetterContainersAsync(_ns, Sub, 10, default);

        found.More.Should().BeTrue();
        _rest.Urls.Count(u => u.Contains("/topics/t", StringComparison.Ordinal)).Should().Be(AzureEventGridReader.MaxTopicsPerSubscription);
    }

    [Fact]
    public async Task A_topic_id_that_is_not_under_a_subscription_is_never_turned_into_a_request()
    {
        _rest.Answers[Topics] = List("{\"id\":\"https://evil.example/x\"}", "{\"id\":\"/other/path\"}");

        await _reader.ListDeadLetterContainersAsync(_ns, Sub, 10, default);

        _rest.Urls.Should().OnlyContain(u => u.StartsWith("https://management.azure.com/subscriptions/", StringComparison.Ordinal));
        _rest.Urls.Should().NotContain(u => u.Contains("evil", StringComparison.Ordinal) || u.Contains("/other/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Blobs_are_listed_from_the_container_with_their_names_times_and_sizes()
    {
        _rest.Answers["/deadletter?restype=container&comp=list&maxresults=100"] =
            "<?xml version=\"1.0\"?><EnumerationResults><Blobs>" +
            "<Blob><Name>orders/a.json</Name><Properties><Last-Modified>Thu, 01 Oct 2026 10:00:00 GMT</Last-Modified><Content-Length>512</Content-Length></Properties></Blob>" +
            "<Blob><Name></Name></Blob>" +
            "<Blob><Name>orders/b.json</Name><Properties><Last-Modified>garbage</Last-Modified></Properties></Blob>" +
            "</Blobs><NextMarker /></EnumerationResults>";

        var list = await _reader.ListBlobsAsync(_ns, "stgevents", "deadletter", 100, default);

        list.Items.Should().HaveCount(2, "a blob with no name is skipped");
        list.Items[0].Should().Be(new DeadLetterBlob("stgevents", "deadletter", "orders/a.json", new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero), 512));
        list.Items[1].ModifiedUtc.Should().BeNull();
        list.More.Should().BeFalse();
        _rest.Asked.Should().ContainSingle().Which.Audience.Should().Be(AzureAudience.Storage);
        _rest.Asked[0].Url.Host.Should().Be("stgevents.blob.core.windows.net");
    }

    [Fact]
    public async Task A_next_marker_means_there_are_more_blobs()
    {
        _rest.Answers["/dl?restype=container&comp=list&maxresults=5"] = "<EnumerationResults><Blobs><Blob><Name>a</Name></Blob></Blobs><NextMarker>abc</NextMarker></EnumerationResults>";

        (await _reader.ListBlobsAsync(_ns, "stg", "dl", 5, default)).More.Should().BeTrue();
    }

    [Fact]
    public async Task More_blobs_than_asked_for_are_cut_and_reported()
    {
        _rest.Answers["/dl?restype=container&comp=list&maxresults=2"] = "<EnumerationResults><Blobs><Blob><Name>a</Name></Blob><Blob><Name>b</Name></Blob><Blob><Name>c</Name></Blob></Blobs></EnumerationResults>";

        var list = await _reader.ListBlobsAsync(_ns, "stg", "dl", 2, default);

        list.Items.Should().HaveCount(2);
        list.More.Should().BeTrue();
    }

    [Fact]
    public async Task A_container_azure_does_not_have_lists_nothing()
    {
        var list = await _reader.ListBlobsAsync(_ns, "stg", "gone", 5, default);

        list.Items.Should().BeEmpty();
        list.More.Should().BeFalse();
    }

    [Fact]
    public async Task A_response_that_declares_a_dtd_is_refused_so_no_external_entity_is_ever_fetched()
    {
        _rest.Answers["/dl?restype=container&comp=list&maxresults=5"] =
            "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><EnumerationResults><Blobs><Blob><Name>&e;</Name></Blob></Blobs></EnumerationResults>";

        await ((Func<Task>)(() => _reader.ListBlobsAsync(_ns, "stg", "dl", 5, default))).Should().ThrowAsync<XmlException>();
    }

    [Fact]
    public async Task A_blob_gives_only_its_reason_fields_and_asks_for_no_more_than_the_size_cap()
    {
        _rest.Answers["/dl/orders/a.json"] =
            "[{\"id\":\"e1\",\"data\":{\"customer\":\"Alice\",\"card\":\"4111\"},\"deadLetterReason\":\"MaxDeliveryAttemptsExceeded\",\"deliveryAttempts\":4," +
            "\"lastDeliveryOutcome\":\"Probation\",\"lastHttpStatusCode\":503,\"publishTime\":\"2026-10-01T09:00:00Z\",\"lastDeliveryAttemptTime\":\"2026-10-01T10:00:00Z\"}]";

        var read = await _reader.GetBlobAsync(_ns, "stg", "dl", "orders/a.json", default);

        read!.Reason.Should().Be("MaxDeliveryAttemptsExceeded");
        read.Outcome.Should().Be("Probation");
        read.Attempts.Should().Be(4);
        read.HttpStatus.Should().Be(503);
        read.LastAttemptUtc.Should().Be(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));
        read.ToString().Should().NotContain("Alice").And.NotContain("4111");
        _rest.Asked.Should().ContainSingle().Which.MaxBytes.Should().Be(AzureEventGridReader.MaxBlobBytes);
    }

    [Fact]
    public async Task The_publish_time_is_used_when_there_is_no_last_attempt_time()
    {
        _rest.Answers["/dl/a"] = "[{\"deadLetterReason\":\"TTLExpired\",\"publishTime\":\"2026-10-01T09:00:00Z\"}]";

        var read = await _reader.GetBlobAsync(_ns, "stg", "dl", "a", default);

        read!.LastAttemptUtc.Should().Be(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        read.Attempts.Should().BeNull();
    }

    [Fact]
    public async Task A_blob_cut_short_by_the_size_cap_still_gives_its_reason()
    {
        _rest.Answers["/dl/a"] = "[{\"deadLetterReason\":\"MaxDeliveryAttemptsExceeded\",\"deliveryAttempts\":3,\"data\":{\"big\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        var read = await _reader.GetBlobAsync(_ns, "stg", "dl", "a", default);

        read!.Reason.Should().Be("MaxDeliveryAttemptsExceeded");
        read.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task A_blob_that_is_not_json_has_no_reason_but_is_still_a_blob()
    {
        _rest.Answers["/dl/a"] = "not json at all";

        var read = await _reader.GetBlobAsync(_ns, "stg", "dl", "a", default);

        read!.Reason.Should().BeNull();
        read.Attempts.Should().BeNull();
        read.LastAttemptUtc.Should().BeNull();
    }

    [Fact]
    public async Task A_blob_azure_no_longer_has_is_null()
    {
        (await _reader.GetBlobAsync(_ns, "stg", "dl", "gone", default)).Should().BeNull();
    }

    [Fact]
    public async Task A_blob_name_is_escaped_segment_by_segment_so_it_cannot_change_the_address()
    {
        await _reader.GetBlobAsync(_ns, "stg", "dl", "a b/c%d/e", default);

        _rest.Asked.Single().Url.AbsoluteUri.Should().Be("https://stg.blob.core.windows.net/dl/a%20b/c%25d/e");
    }

    [Fact]
    public async Task A_very_long_reason_is_not_taken_whole()
    {
        _rest.Answers["/dl/a"] = "[{\"deadLetterReason\":\"" + new string('x', 500) + "\"}]";

        (await _reader.GetBlobAsync(_ns, "stg", "dl", "a", default))!.Reason.Should().BeNull("a value longer than 200 characters is not a reason code");
    }

    [Fact]
    public async Task An_error_from_azure_is_not_swallowed_on_the_blob_reads()
    {
        _rest.Fail = _ => new AzureRestException(HttpStatusCode.Forbidden, "no data role");

        await ((Func<Task>)(() => _reader.ListBlobsAsync(_ns, "stg", "dl", 5, default))).Should().ThrowAsync<AzureRestException>();
        await ((Func<Task>)(() => _reader.GetBlobAsync(_ns, "stg", "dl", "a", default))).Should().ThrowAsync<AzureRestException>();
    }

    [Fact]
    public async Task Blob_requests_go_only_to_storage_and_arm_requests_only_to_resource_manager()
    {
        _rest.Answers[Global] = List(Subscription());
        await _reader.ListDeadLetterContainersAsync(_ns, Sub, 10, default);
        await _reader.ListBlobsAsync(_ns, "stg", "dl", 5, default);
        await _reader.GetBlobAsync(_ns, "stg", "dl", "a", default);

        _rest.Asked.Where(a => a.Audience == AzureAudience.Management).Should().OnlyContain(a => a.Url.Host == "management.azure.com");
        _rest.Asked.Where(a => a.Audience == AzureAudience.Storage).Should().OnlyContain(a => a.Url.Host.EndsWith(".blob.core.windows.net", StringComparison.Ordinal));
        _rest.Asked.Should().OnlyContain(a => a.Url.Scheme == "https");
    }
}
