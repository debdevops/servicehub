using System.Net;
using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Providers.Azure.Components;

namespace ServiceHub.UnitTests.Components.Sources;

/// <summary>What the Logic Apps reader asks Azure Resource Manager for, and how it reads the answers.</summary>
public sealed class AzureLogicAppsReaderTests
{
    private const string Sub = "11111111-1111-1111-1111-111111111111";
    private const string Wf = $"/subscriptions/{Sub}/resourceGroups/rg/providers/Microsoft.Logic/workflows/orders";
    private const string SubscriptionsUrl = "/subscriptions?api-version=2022-12-01";

    private readonly FakeAzureRest _rest = new();
    private readonly AzureLogicAppsReader _reader;
    private readonly Namespace _ns = Namespace.CreateWithManagedIdentity("logic-reader", ConnectionAuthType.ManagedIdentity, provider: CloudProviderType.Azure).Value;

    public AzureLogicAppsReaderTests() => _reader = new AzureLogicAppsReader(_rest);

    // Answers are written with @WF@ and @SUB@ so the JSON stays readable.
    private static string J(string template) => template.Replace("@WF@", Wf, StringComparison.Ordinal).Replace("@SUB@", Sub, StringComparison.Ordinal);

    [Fact]
    public async Task Subscriptions_are_read_from_the_subscriptions_list()
    {
        _rest.Answers[SubscriptionsUrl] = J("""{"value":[{"subscriptionId":"@SUB@","displayName":"Prod"},{"subscriptionId":"other"}]}""");

        var list = await _reader.ListSubscriptionsAsync(_ns, default);

        list.Items.Should().Equal(Sub, "other");
        list.More.Should().BeFalse();
        _rest.Asked.Should().ContainSingle().Which.Audience.Should().Be(AzureAudience.Management);
    }

    [Fact]
    public async Task A_next_link_means_there_are_more_subscriptions()
    {
        _rest.Answers[SubscriptionsUrl] = J("""{"value":[{"subscriptionId":"@SUB@"}],"nextLink":"https://management.azure.com/subscriptions?x=1"}""");

        (await _reader.ListSubscriptionsAsync(_ns, default)).More.Should().BeTrue();
    }

    [Fact]
    public async Task Only_the_first_twenty_subscriptions_are_used_and_more_is_reported()
    {
        var many = string.Join(',', Enumerable.Range(0, 25).Select(i => "{\"subscriptionId\":\"s" + i + "\"}"));
        _rest.Answers[SubscriptionsUrl] = "{\"value\":[" + many + "]}";

        var list = await _reader.ListSubscriptionsAsync(_ns, default);

        list.Items.Should().HaveCount(AzureArm.MaxSubscriptions);
        list.More.Should().BeTrue();
    }

    [Fact]
    public async Task An_entry_without_a_subscription_id_is_skipped()
    {
        _rest.Answers[SubscriptionsUrl] = """{"value":[{"displayName":"x"},{"subscriptionId":"ok"}]}""";

        (await _reader.ListSubscriptionsAsync(_ns, default)).Items.Should().Equal("ok");
    }

    [Fact]
    public async Task A_missing_or_unexpected_answer_reads_as_no_subscriptions()
    {
        (await _reader.ListSubscriptionsAsync(_ns, default)).Items.Should().BeEmpty();
        _rest.Answers[SubscriptionsUrl] = """{"unexpected":true}""";
        (await _reader.ListSubscriptionsAsync(_ns, default)).Items.Should().BeEmpty();
        _rest.Answers[SubscriptionsUrl] = "[]";
        (await _reader.ListSubscriptionsAsync(_ns, default)).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Workflows_are_listed_under_the_logic_provider_with_a_top_cap()
    {
        _rest.Answers[$"/subscriptions/{Sub}/providers/Microsoft.Logic/workflows?api-version=2016-06-01&$top=5"] = J("""{"value":[{"id":"@WF@","name":"orders"},{"name":"no-id"}]}""");

        var list = await _reader.ListWorkflowsAsync(_ns, Sub, 5, default);

        list.Items.Should().ContainSingle().Which.Id.Should().Be(Wf);
        list.More.Should().BeFalse();
    }

    [Fact]
    public async Task More_workflows_than_the_cap_are_cut_and_reported()
    {
        var many = string.Join(',', Enumerable.Range(0, 4).Select(i => "{\"id\":\"" + Wf + i + "\"}"));
        _rest.Answers[$"/subscriptions/{Sub}/providers/Microsoft.Logic/workflows?api-version=2016-06-01&$top=3"] = "{\"value\":[" + many + "]}";

        var list = await _reader.ListWorkflowsAsync(_ns, Sub, 3, default);

        list.Items.Should().HaveCount(3);
        list.More.Should().BeTrue();
    }

    [Fact]
    public async Task Runs_are_filtered_by_status_in_the_request_and_parsed()
    {
        _rest.Answers[$"{Wf}/runs?api-version=2016-06-01&$filter=status%20eq%20%27Failed%27&$top=10"] = J("""
            {"value":[
              {"id":"@WF@/runs/r1","properties":{"status":"Failed","startTime":"2026-10-01T10:00:00Z","endTime":"2026-10-01T10:05:00Z"}},
              {"id":"@WF@/runs/r2","properties":{"startTime":"not a date"}},
              {"properties":{"status":"Failed"}}
            ]}
            """);

        var list = await _reader.ListRunsAsync(_ns, Wf, "Failed", 10, default);

        list.Items.Should().HaveCount(2, "a run with no id is skipped");
        list.Items[0].Should().Be(new LogicRunRef($"{Wf}/runs/r1", "Failed", new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 1, 10, 5, 0, TimeSpan.Zero)));
        list.Items[1].Status.Should().Be("Unknown");
        list.Items[1].StartedUtc.Should().BeNull();
        _rest.Urls.Should().ContainSingle().Which.Should().Contain("$filter=status%20eq%20%27Failed%27");
    }

    [Fact]
    public async Task Runs_report_more_when_azure_gives_a_next_link()
    {
        _rest.Answers[$"{Wf}/runs?api-version=2016-06-01&$filter=status%20eq%20%27TimedOut%27&$top=10"] = """{"value":[],"nextLink":"https://management.azure.com/next"}""";

        (await _reader.ListRunsAsync(_ns, Wf, "TimedOut", 10, default)).More.Should().BeTrue();
    }

    [Fact]
    public async Task One_run_gives_its_error_code_and_message_and_nothing_else()
    {
        _rest.Answers[$"{Wf}/runs/r1?api-version=2016-06-01"] = J("""
            {"id":"@WF@/runs/r1","properties":{"status":"Failed","startTime":"2026-10-01T10:00:00Z","code":"ActionFailed",
             "error":{"code":"BadGateway","message":"The upstream failed."},
             "trigger":{"outputs":{"body":"customer data"}},"outputs":{"x":"secret"}}}
            """);

        var read = await _reader.GetRunAsync(_ns, $"{Wf}/runs/r1", default);

        read!.ErrorCode.Should().Be("BadGateway");
        read.ErrorMessage.Should().Be("The upstream failed.");
        read.Run.Status.Should().Be("Failed");
        read.ToString().Should().NotContain("customer data").And.NotContain("secret");
    }

    [Fact]
    public async Task A_run_with_only_a_code_falls_back_to_the_code()
    {
        _rest.Answers[$"{Wf}/runs/r1?api-version=2016-06-01"] = J("""{"id":"@WF@/runs/r1","properties":{"status":"TimedOut","code":"Timeout"}}""");

        var read = await _reader.GetRunAsync(_ns, $"{Wf}/runs/r1", default);

        read!.ErrorCode.Should().Be("Timeout");
        read.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task A_run_azure_no_longer_has_is_null()
    {
        (await _reader.GetRunAsync(_ns, $"{Wf}/runs/gone", default)).Should().BeNull();
    }

    [Fact]
    public async Task A_run_answer_with_no_id_is_null()
    {
        _rest.Answers[$"{Wf}/runs/r1?api-version=2016-06-01"] = """{"properties":{"status":"Failed"}}""";

        (await _reader.GetRunAsync(_ns, $"{Wf}/runs/r1", default)).Should().BeNull();
    }

    [Fact]
    public async Task An_error_from_azure_is_not_swallowed()
    {
        _rest.Fail = _ => new AzureRestException(HttpStatusCode.Forbidden, "no");

        await ((Func<Task>)(() => _reader.ListSubscriptionsAsync(_ns, default))).Should().ThrowAsync<AzureRestException>();
        await ((Func<Task>)(() => _reader.ListWorkflowsAsync(_ns, Sub, 1, default))).Should().ThrowAsync<AzureRestException>();
        await ((Func<Task>)(() => _reader.ListRunsAsync(_ns, Wf, "Failed", 1, default))).Should().ThrowAsync<AzureRestException>();
        await ((Func<Task>)(() => _reader.GetRunAsync(_ns, $"{Wf}/runs/r1", default))).Should().ThrowAsync<AzureRestException>();
    }

    [Fact]
    public async Task Every_request_goes_to_resource_manager_and_never_to_storage()
    {
        _rest.Answers[SubscriptionsUrl] = J("""{"value":[{"subscriptionId":"@SUB@"}]}""");
        await _reader.ListSubscriptionsAsync(_ns, default);
        await _reader.ListWorkflowsAsync(_ns, Sub, 1, default);
        await _reader.ListRunsAsync(_ns, Wf, "Failed", 1, default);
        await _reader.GetRunAsync(_ns, $"{Wf}/runs/r1", default);

        _rest.Asked.Should().OnlyContain(a => a.Audience == AzureAudience.Management && a.Url.Host == "management.azure.com" && a.Url.Scheme == "https" && a.MaxBytes == null);
    }
}
