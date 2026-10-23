using System.Net;
using FluentAssertions;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Providers.Azure.Components;

namespace ServiceHub.UnitTests.Components.Sources;

/// <summary>The Logic Apps source against a fake Azure: the conformance kit plus what is particular to this service.</summary>
public sealed class LogicAppsSourceTests : ComponentSourceConformance
{
    private const string Sub = "11111111-1111-1111-1111-111111111111";
    private const string Orders = $"/subscriptions/{Sub}/resourceGroups/rg-orders/providers/Microsoft.Logic/workflows/orders";
    private const string Run = $"{Orders}/runs/08584-run-1";

    private readonly FakeAzure _azure = new();
    private readonly LogicAppsSource _source;
    private readonly Namespace _ns = Entra();

    public LogicAppsSourceTests()
    {
        _source = new LogicAppsSource(_azure);
        _azure.Subscriptions = [Sub];
        _azure.Workflows[Sub] = [new LogicAppRef(Orders)];
    }

    protected override IComponentSource Source => _source;

    protected override Namespace Connection => _ns;

    protected override string PlaceId => Orders;

    protected override string ItemId => Run;

    protected override IReadOnlyList<string> CloudCalls => _azure.Calls;

    protected override void ArrangeFailures(int count)
    {
        var baseTime = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _azure.Runs[(Orders, "Failed")] = [.. Enumerable.Range(0, count).Select(i => new LogicRunRef($"{Orders}/runs/run-{i}", "Failed", baseTime.AddMinutes(i - 1), baseTime.AddMinutes(i)))];
        _azure.Detail = new LogicRunRead(new LogicRunRef(Run, "Failed", baseTime, baseTime.AddMinutes(1)), "ActionFailed", "The action 'Send_email' failed.");
    }

    protected override void ArrangeAccessDenied() => _azure.Throw = new AzureRestException(HttpStatusCode.Forbidden, "denied");

    protected override void ArrangeCloudFailure(string message) => _azure.Throw = new AzureRestException(HttpStatusCode.ServiceUnavailable, message);

    protected override void ArrangeSecretInFailureText(string secret)
    {
        var at = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var withSecret = new LogicRunRef($"{Orders}/runs/{secret}", "Failed", at, at);
        _azure.Runs[(Orders, "Failed")] = [withSecret];
        _azure.Detail = new LogicRunRead(withSecret, $"code {secret}", $"message {secret}");
    }

    protected override void ArrangeMissingItem() => _azure.Detail = null;

    protected override void ArrangeCancellationAware() => _azure.HonourCancellation = true;

    private static Namespace Entra(ConnectionAuthType type = ConnectionAuthType.DefaultAzureCredential) =>
        Namespace.CreateWithManagedIdentity("logic-test", type, provider: CloudProviderType.Azure).Value;

    [Fact]
    public async Task Places_are_found_in_every_subscription_and_sorted()
    {
        const string Other = "22222222-2222-2222-2222-222222222222";
        _azure.Subscriptions = [Sub, Other];
        _azure.Workflows[Other] = [new LogicAppRef($"/subscriptions/{Other}/resourceGroups/rg/providers/Microsoft.Logic/workflows/billing")];

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;

        places.Items.Select(p => p.Name).Should().Equal("orders", "billing");
        places.Items.Should().OnlyContain(p => p.Kind == "logic app");
        places.Items[0].ConsoleUrl.Should().Be($"https://portal.azure.com/#@/resource{Orders}/logicApp");
        places.ReachedLimit.Should().BeFalse();
        places.Incomplete.Should().BeFalse();
    }

    [Fact]
    public async Task A_subscription_that_cannot_be_read_leaves_the_places_incomplete_and_keeps_the_rest()
    {
        const string Other = "22222222-2222-2222-2222-222222222222";
        _azure.Subscriptions = [Sub, Other];
        _azure.FailSubscription = Other;

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;

        places.Items.Should().ContainSingle().Which.Name.Should().Be("orders");
        places.Incomplete.Should().BeTrue("a subscription failed, so a Logic App that exists there may be missing");
    }

    [Fact]
    public async Task When_every_subscription_fails_the_places_are_an_error_not_an_empty_list()
    {
        _azure.FailSubscription = Sub;

        var places = await _source.ListPlacesAsync(_ns, default);

        places.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task More_places_than_the_cap_say_so()
    {
        _azure.Workflows[Sub] = [.. Enumerable.Range(0, ComponentSourceLimits.MaxPlaces + 5).Select(i => new LogicAppRef($"/subscriptions/{Sub}/resourceGroups/rg/providers/Microsoft.Logic/workflows/w{i:000}"))];

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;

        places.Items.Should().HaveCount(ComponentSourceLimits.MaxPlaces);
        places.ReachedLimit.Should().BeTrue();
    }

    [Fact]
    public async Task More_subscriptions_than_it_looks_in_say_so()
    {
        _azure.SubscriptionsMore = true;

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;

        places.ReachedLimit.Should().BeTrue();
    }

    [Fact]
    public async Task Without_a_place_it_reads_every_logic_app_for_all_three_statuses()
    {
        ArrangeFailures(2);
        const string Other = "22222222-2222-2222-2222-222222222222";
        _azure.Subscriptions = [Sub, Other];
        _azure.Workflows[Other] = [new LogicAppRef($"/subscriptions/{Other}/resourceGroups/rg/providers/Microsoft.Logic/workflows/billing")];

        await _source.ListFailedAsync(_ns, null, default);

        _azure.Calls.Count(c => c == "ListRuns").Should().Be(6, "Failed, TimedOut and Cancelled for each of the two Logic Apps");
        _azure.StatusesAsked.Distinct().Should().BeEquivalentTo("Failed", "TimedOut", "Cancelled");
    }

    [Fact]
    public async Task Only_the_first_twenty_logic_apps_are_read_without_a_place_and_the_page_says_there_are_more()
    {
        _azure.Workflows[Sub] = [.. Enumerable.Range(0, LogicAppsSource.MaxWorkflowsPerListing + 5).Select(i => new LogicAppRef($"/subscriptions/{Sub}/resourceGroups/rg/providers/Microsoft.Logic/workflows/w{i:000}"))];

        var page = (await _source.ListFailedAsync(_ns, null, default)).Value;

        _azure.Calls.Count(c => c == "ListRuns").Should().Be(LogicAppsSource.MaxWorkflowsPerListing * 3);
        page.ReachedLimit.Should().BeTrue();
    }

    [Theory]
    [InlineData("Failed", "Failed")]
    [InlineData("TimedOut", "Timed out")]
    [InlineData("Cancelled", "Cancelled")]
    public async Task Each_ending_is_shown_with_azures_own_status_and_a_plain_summary(string status, string words)
    {
        var at = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _azure.Runs[(Orders, status)] = [new LogicRunRef($"{Orders}/runs/run-9", status, at, at)];

        var item = (await _source.ListFailedAsync(_ns, Orders, default)).Value.Items.Single();

        item.Status.Should().Be(status);
        item.Summary.Should().Be($"{words}: run-9");
        item.Place.Should().Be(Orders);
        item.ConsoleUrl.Should().Be($"https://portal.azure.com/#@/resource{Orders}/logicApp");
        item.Copy.Should().Be(CopyKind.None);
        item.ReasonCode.Should().BeNull("the list does not carry a reason");
    }

    [Fact]
    public async Task One_status_read_that_fails_leaves_the_page_incomplete_and_keeps_the_others()
    {
        ArrangeFailures(2);
        _azure.FailStatus = "Cancelled";

        var page = (await _source.ListFailedAsync(_ns, Orders, default)).Value;

        page.Items.Should().HaveCount(2);
        page.Incomplete.Should().BeTrue();
    }

    [Fact]
    public async Task Runs_from_every_status_are_merged_newest_first()
    {
        var t = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _azure.Runs[(Orders, "Failed")] = [new LogicRunRef($"{Orders}/runs/a", "Failed", t, t.AddMinutes(1))];
        _azure.Runs[(Orders, "TimedOut")] = [new LogicRunRef($"{Orders}/runs/b", "TimedOut", t, t.AddMinutes(5))];
        _azure.Runs[(Orders, "Cancelled")] = [new LogicRunRef($"{Orders}/runs/c", "Cancelled", t, t.AddMinutes(3))];

        var items = (await _source.ListFailedAsync(_ns, Orders, default)).Value.Items;

        items.Select(i => i.Summary.Split(": ")[1]).Should().Equal("b", "c", "a");
    }

    [Fact]
    public async Task A_run_with_no_end_time_is_ordered_by_its_start_time()
    {
        var t = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _azure.Runs[(Orders, "Failed")] =
        [
            new LogicRunRef($"{Orders}/runs/old", "Failed", t, t.AddMinutes(1)),
            new LogicRunRef($"{Orders}/runs/open", "Failed", t.AddHours(1), null),
        ];

        var items = (await _source.ListFailedAsync(_ns, Orders, default)).Value.Items;

        items[0].AtUtc.Should().Be(t.AddHours(1));
    }

    [Fact]
    public async Task Access_is_refused_with_a_reason_when_the_identity_sees_no_subscription()
    {
        _azure.Subscriptions = [];

        var access = await _source.ValidateAccessAsync(_ns, default);

        access.IsFailure.Should().BeTrue();
        access.Error.Message.Should().StartWith("Not enabled").And.Contain("Reader");
    }

    [Fact]
    public async Task Access_proves_the_logic_apps_themselves_can_be_listed_not_only_the_subscriptions()
    {
        var access = await _source.ValidateAccessAsync(_ns, default);

        access.IsSuccess.Should().BeTrue();
        _azure.Calls.Should().Equal("ListSubscriptions", "ListWorkflows");
        _azure.MaxAsked.Should().Be(1, "one Logic App is enough to prove the permission");
    }

    [Fact]
    public async Task A_connection_that_does_not_sign_in_with_entra_is_not_enabled_and_says_how_to_fix_it()
    {
        _azure.Throw = new AzureNotEntraException(ConnectionAuthType.ConnectionString);

        var access = await _source.ValidateAccessAsync(_ns, default);

        access.IsFailure.Should().BeTrue();
        access.Error.Message.Should().StartWith("Not enabled").And.Contain("Entra");
    }

    [Fact]
    public async Task A_missing_login_is_not_enabled_not_a_crash()
    {
        _azure.Throw = new Azure.Identity.CredentialUnavailableException("no identity");

        var access = await _source.ValidateAccessAsync(_ns, default);

        access.IsFailure.Should().BeTrue();
        access.Error.Message.Should().StartWith("Not enabled");
    }

    [Fact]
    public async Task The_detail_carries_the_code_the_message_and_the_times()
    {
        ArrangeFailures(1);

        var detail = (await _source.GetFailedAsync(_ns, Run, default)).Value!;

        detail.Reason.Should().Be("ActionFailed");
        detail.Cause.Should().Be("The action 'Send_email' failed.");
        detail.StartedUtc.Should().NotBeNull();
        detail.StoppedUtc.Should().NotBeNull();
        detail.Item.Id.Should().Be(Run);
        detail.Item.Place.Should().Be(Orders);
    }

    [Fact]
    public async Task Long_error_text_is_bounded_and_marked()
    {
        ArrangeFailures(1);
        _azure.Detail = _azure.Detail! with { ErrorMessage = new string('x', 5000) };

        var detail = (await _source.GetFailedAsync(_ns, Run, default)).Value!;

        detail.Cause!.Length.Should().Be(1001);
        detail.Cause.Should().EndWith("…");
    }

    [Fact]
    public async Task A_run_with_no_error_text_has_no_reason_or_cause()
    {
        ArrangeFailures(1);
        _azure.Detail = _azure.Detail! with { ErrorCode = null, ErrorMessage = "   " };

        var detail = (await _source.GetFailedAsync(_ns, Run, default)).Value!;

        detail.Reason.Should().BeNull();
        detail.Cause.Should().BeNull();
    }

    [Theory]
    [InlineData("/subscriptions/not-a-guid/resourceGroups/rg/providers/Microsoft.Logic/workflows/orders")]
    [InlineData($"/subscriptions/{Sub}/resourceGroups/rg/providers/Microsoft.Web/sites/orders")]
    [InlineData($"/subscriptions/{Sub}/resourceGroups/rg/providers/Microsoft.Logic/workflows/orders/runs/r1")]
    [InlineData($"/subscriptions/{Sub}/resourceGroups/rg/providers/Microsoft.Logic/workflows/orders?api-version=1")]
    [InlineData($"/subscriptions/{Sub}/resourceGroups/rg/providers/Microsoft.Logic/workflows/or ders")]
    [InlineData($"/subscriptions/{Sub}/resourceGroups/rg/providers/Microsoft.Logic/workflows/orders#frag")]
    [InlineData("https://management.azure.com/subscriptions")]
    [InlineData("")]
    public async Task A_place_that_is_not_a_logic_app_is_refused_before_any_call(string place)
    {
        var page = await _source.ListFailedAsync(_ns, place, default);

        page.IsFailure.Should().BeTrue();
        _azure.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData($"/subscriptions/{Sub}/resourceGroups/rg/providers/Microsoft.Logic/workflows/orders")]
    [InlineData($"/subscriptions/{Sub}/resourceGroups/rg/providers/Microsoft.Logic/workflows/orders/runs/")]
    [InlineData($"/subscriptions/{Sub}/resourceGroups/rg/providers/Microsoft.Logic/workflows/orders/runs/a/b")]
    [InlineData($"/subscriptions/{Sub}/resourceGroups/rg/providers/Microsoft.Logic/workflows/orders/runs/a?x=1")]
    [InlineData("../../etc/passwd")]
    public async Task An_item_that_is_not_a_run_is_refused_before_any_call(string item)
    {
        var detail = await _source.GetFailedAsync(_ns, item, default);

        detail.IsFailure.Should().BeTrue();
        _azure.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_resource_group_with_dots_and_brackets_is_accepted_because_azure_allows_them()
    {
        const string Place = $"/subscriptions/{Sub}/resourceGroups/rg.prod_(eu)/providers/Microsoft.Logic/workflows/orders-v2";

        var page = await _source.ListFailedAsync(_ns, Place, default);

        page.IsSuccess.Should().BeTrue();
        _azure.WorkflowsAsked.Should().OnlyContain(w => w == Place);
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

        var failed = await _source.ListFailedAsync(_ns, Orders, default);

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
    public async Task It_names_the_azure_adapter_and_is_read_only_by_declaration()
    {
        _source.Provider.Should().Be(CloudProviderType.Azure);
        _source.ComponentKey.Should().Be("logic-apps");
        _source.Capabilities.ReasonInList.Should().BeFalse();
        _source.Capabilities.ReadsPayload.Should().BeFalse();
        await Task.CompletedTask;
    }

    /// <summary>Azure as the source sees it: records the operation names and answers from what a test arranged.</summary>
    private sealed class FakeAzure : IAzureLogicAppsReader
    {
        private readonly List<string> _calls = [];

        public IReadOnlyList<string> Calls => _calls;

        public IReadOnlyList<string> Subscriptions { get; set; } = [];

        public bool SubscriptionsMore { get; set; }

        public Dictionary<string, IReadOnlyList<LogicAppRef>> Workflows { get; } = [];

        public Dictionary<(string Workflow, string Status), IReadOnlyList<LogicRunRef>> Runs { get; } = [];

        public LogicRunRead? Detail { get; set; }

        public Exception? Throw { get; set; }

        public bool HonourCancellation { get; set; }

        public string? FailSubscription { get; set; }

        public string? FailStatus { get; set; }

        public int MaxAsked { get; private set; }

        public List<string> StatusesAsked { get; } = [];

        public List<string> WorkflowsAsked { get; } = [];

        public Task<Listing<string>> ListSubscriptionsAsync(Namespace ns, CancellationToken ct) =>
            Run("ListSubscriptions", ct, () => new Listing<string>(Subscriptions, SubscriptionsMore));

        public Task<Listing<LogicAppRef>> ListWorkflowsAsync(Namespace ns, string subscriptionId, int max, CancellationToken ct) =>
            Run("ListWorkflows", ct, () =>
            {
                MaxAsked = max;
                if (subscriptionId == FailSubscription)
                {
                    throw new AzureRestException(HttpStatusCode.ServiceUnavailable, "subscription down");
                }

                var all = Workflows.TryGetValue(subscriptionId, out var w) ? w : [];
                return new Listing<LogicAppRef>([.. all.Take(max)], all.Count > max);
            });

        public Task<Listing<LogicRunRef>> ListRunsAsync(Namespace ns, string workflowId, string status, int max, CancellationToken ct) =>
            Run("ListRuns", ct, () =>
            {
                lock (StatusesAsked)
                {
                    StatusesAsked.Add(status);
                    WorkflowsAsked.Add(workflowId);
                }

                if (status == FailStatus)
                {
                    throw new AzureRestException(HttpStatusCode.TooManyRequests, "throttled");
                }

                var all = (Runs.TryGetValue((workflowId, status), out var r) ? r : []).OrderByDescending(x => x.EndedUtc).ToList();
                return new Listing<LogicRunRef>([.. all.Take(max)], all.Count > max);
            });

        public Task<LogicRunRead?> GetRunAsync(Namespace ns, string runId, CancellationToken ct) =>
            Run("GetRun", ct, () => Detail);

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
