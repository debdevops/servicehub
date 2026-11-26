using FluentAssertions;
using Grpc.Core;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Providers.Gcp.Components;

namespace ServiceHub.UnitTests.Components.Sources;

/// <summary>The Workflows source against a fake Google: the conformance kit plus what is particular to this service.</summary>
public sealed class WorkflowsSourceTests : ComponentSourceConformance
{
    private const string Project = "my-proj";
    private const string Orders = "projects/my-proj/locations/us-central1/workflows/orders";
    private const string Run = "projects/my-proj/locations/us-central1/workflows/orders/executions/run-1";

    private readonly FakeGoogle _google = new();
    private readonly WorkflowsSource _source;
    private readonly Namespace _ns = Namespace.Create(
        Project, "{\"type\":\"service_account\"}", provider: CloudProviderType.Gcp, gcpProjectId: Project).Value;

    public WorkflowsSourceTests()
    {
        _source = new WorkflowsSource(_google);
        _google.Locations = ["us-central1", "europe-west1"];
        _google.Workflows["us-central1"] = [new WorkflowRef(Orders)];
    }

    protected override IComponentSource Source => _source;

    protected override Namespace Connection => _ns;

    protected override string PlaceId => Orders;

    protected override string ItemId => Run;

    protected override IReadOnlyList<string> CloudCalls => _google.Calls;

    protected override void ArrangeFailures(int count)
    {
        var baseTime = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _google.Executions[(Orders, "FAILED")] = [.. Enumerable.Range(0, count).Select(i => new ExecutionRef($"{Orders}/executions/run-{i}", "FAILED", baseTime.AddMinutes(i - 1), baseTime.AddMinutes(i)))];
        _google.Detail = new ExecutionRead(new ExecutionRef(Run, "FAILED", baseTime, baseTime.AddMinutes(1)), "{\"message\":\"boom\"}", "step main");
    }

    protected override void ArrangeAccessDenied() => _google.Throw = new RpcException(new Status(StatusCode.PermissionDenied, "denied"));

    protected override void ArrangeCloudFailure(string message) => _google.Throw = new RpcException(new Status(StatusCode.Unavailable, message));

    protected override void ArrangeSecretInFailureText(string secret)
    {
        var at = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _google.Executions[(Orders, "FAILED")] = [new ExecutionRef($"{Orders}/executions/{secret}", "FAILED", at, at)];
        _google.Detail = new ExecutionRead(new ExecutionRef($"{Orders}/executions/{secret}", "FAILED", at, at), $"payload {secret}", $"context {secret}");
    }

    protected override void ArrangeMissingItem() => _google.Detail = null;

    protected override void ArrangeCancellationAware() => _google.HonourCancellation = true;

    [Fact]
    public async Task Places_are_found_in_every_location_and_sorted()
    {
        _google.Workflows["europe-west1"] = [new WorkflowRef("projects/my-proj/locations/europe-west1/workflows/billing")];

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;

        places.Items.Select(p => p.Name).Should().Equal("billing", "orders");
        places.Items.Should().OnlyContain(p => p.Kind == "workflow");
        places.Items[0].ConsoleUrl.Should().Be("https://console.cloud.google.com/workflows/workflow/europe-west1/billing/executions?project=my-proj");
        places.ReachedLimit.Should().BeFalse();
        places.Incomplete.Should().BeFalse();
    }

    [Fact]
    public async Task A_location_that_cannot_be_read_leaves_the_places_incomplete_and_keeps_the_rest()
    {
        _google.NoWildcard = true;
        _google.FailLocation = "europe-west1";

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;

        places.Items.Should().ContainSingle().Which.Name.Should().Be("orders");
        places.Incomplete.Should().BeTrue("a location failed, so a workflow that exists there may be missing");
    }

    [Fact]
    public async Task Workflows_are_listed_with_one_wildcard_call_and_the_per_location_walk_is_the_fallback()
    {
        (await _source.ListPlacesAsync(_ns, default)).Value.Items.Should().NotBeEmpty();
        _google.Calls.Should().Equal("ListWorkflows");

        _google.Calls.Clear();
        _google.NoWildcard = true;
        (await _source.ListPlacesAsync(_ns, default)).Value.Items.Should().NotBeEmpty();
        _google.Calls.Should().Contain("ListLocations");
    }

    [Fact]
    public async Task More_places_than_the_cap_say_so()
    {
        _google.Workflows["us-central1"] = [.. Enumerable.Range(0, ComponentSourceLimits.MaxPlaces + 5).Select(i => new WorkflowRef($"projects/my-proj/locations/us-central1/workflows/w{i:000}"))];

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;

        places.Items.Should().HaveCount(ComponentSourceLimits.MaxPlaces);
        places.ReachedLimit.Should().BeTrue();
    }

    [Fact]
    public async Task Without_a_place_it_reads_every_workflow_for_both_states()
    {
        ArrangeFailures(2);
        _google.Workflows["europe-west1"] = [new WorkflowRef("projects/my-proj/locations/europe-west1/workflows/billing")];

        await _source.ListFailedAsync(_ns, null, default);

        _google.Calls.Count(c => c == "ListExecutions").Should().Be(4, "FAILED and CANCELLED for each of the two workflows");
    }

    [Fact]
    public async Task A_cancelled_run_is_shown_as_cancelled_with_google_s_own_state_word()
    {
        var at = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _google.Executions[(Orders, "CANCELLED")] = [new ExecutionRef($"{Orders}/executions/run-9", "CANCELLED", at, at)];

        var item = (await _source.ListFailedAsync(_ns, Orders, default)).Value.Items.Single();

        item.Status.Should().Be("CANCELLED");
        item.Summary.Should().Be("Cancelled: run-9");
        item.ConsoleUrl.Should().Be("https://console.cloud.google.com/workflows/workflow/us-central1/orders/execution/run-9?project=my-proj");
    }

    [Fact]
    public async Task One_failed_state_read_leaves_the_page_incomplete_and_keeps_the_others()
    {
        ArrangeFailures(2);
        _google.FailExecutionsState = "CANCELLED";

        var page = (await _source.ListFailedAsync(_ns, Orders, default)).Value;

        page.Items.Should().HaveCount(2);
        page.Incomplete.Should().BeTrue();
    }

    private static Namespace ProjectNamed(string project) =>
        Namespace.Create(project, "{\"type\":\"service_account\"}", provider: CloudProviderType.Gcp, gcpProjectId: project).Value;

    [Fact]
    public async Task Google_names_an_execution_by_the_project_number_and_it_is_always_read_in_the_connections_own_project()
    {
        // Found on a real project: workflows are listed as projects/{id}/..., but an execution comes back as projects/{number}/....
        var ns = ProjectNamed("proj-num");
        const string Numbered = "projects/1078147762303/locations/us-central1/workflows/orders/executions/run-1";
        const string Own = "projects/proj-num/locations/us-central1/workflows/orders/executions/run-1";
        var at = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _google.Detail = new ExecutionRead(new ExecutionRef(Numbered, "FAILED", at, at), "{\"message\":\"boom\"}", "step main");

        var detail = (await _source.GetFailedAsync(ns, Numbered, default)).Value;

        detail.Should().NotBeNull();
        _google.NamesAsked.Should().Equal(Own); // the read is rewritten to the connection's own project id, and the number is never sent
        detail!.Item.ConsoleUrl.Should().Contain("project=proj-num");
    }

    [Fact]
    public async Task A_numeric_project_in_a_place_is_also_rewritten_and_a_non_numeric_other_project_is_still_refused()
    {
        var ns = ProjectNamed("proj-place");

        (await _source.ListFailedAsync(ns, "projects/123/locations/us-central1/workflows/orders", default)).IsSuccess.Should().BeTrue();
        _google.NamesAsked.Should().OnlyContain(n => n.StartsWith("projects/proj-place/", StringComparison.Ordinal), "a number is rewritten to the connection's own project id");
        (await _source.ListFailedAsync(ns, "projects/not-mine/locations/us-central1/workflows/orders", default)).IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData("projects/other/locations/us-central1/workflows/orders")]
    [InlineData("projects/my-proj/locations/us-central1/workflows/orders/executions/run-1")]
    [InlineData("projects/my-proj/locations/us-central1")]
    public async Task A_place_in_another_project_or_that_is_not_a_workflow_is_refused_before_any_call(string place)
    {
        var page = await _source.ListFailedAsync(_ns, place, default);

        page.IsFailure.Should().BeTrue();
        _google.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("projects/other/locations/us-central1/workflows/orders/executions/run-1")]
    [InlineData("projects/my-proj/locations/us-central1/workflows/orders")]
    public async Task An_item_in_another_project_or_that_is_not_an_execution_is_refused_before_any_call(string item)
    {
        var detail = await _source.GetFailedAsync(_ns, item, default);

        detail.IsFailure.Should().BeTrue();
        _google.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task The_error_is_returned_as_text_and_long_text_is_bounded()
    {
        ArrangeFailures(1);
        _google.Detail = _google.Detail! with { ErrorPayload = new string('x', 5000) };

        var detail = (await _source.GetFailedAsync(_ns, Run, default)).Value!;

        detail.Reason!.Length.Should().BeLessThanOrEqualTo(1001);
        detail.Cause.Should().Be("step main");
        detail.StartedUtc.Should().NotBeNull();
    }

    /// <summary>Google as the source sees it: records the operation names and answers from what a test arranged.</summary>
    private sealed class FakeGoogle : IGcpWorkflowsReader
    {
        private readonly List<string> _calls = [];

        public List<string> Calls => _calls;

        public IReadOnlyList<string> Locations { get; set; } = [];

        public Dictionary<string, IReadOnlyList<WorkflowRef>> Workflows { get; } = [];

        public Dictionary<(string Workflow, string State), IReadOnlyList<ExecutionRef>> Executions { get; } = [];

        public ExecutionRead? Detail { get; set; }

        public Exception? Throw { get; set; }

        public bool HonourCancellation { get; set; }

        public string? FailLocation { get; set; }

        /// <summary>When set, Google refuses the "-" (every location) wildcard, as the source must survive.</summary>
        public bool NoWildcard { get; set; }

        public string? FailExecutionsState { get; set; }

        public Task<Listing<string>> ListLocationsAsync(Namespace ns, CancellationToken ct) =>
            Run("ListLocations", ct, () => new Listing<string>(Locations, false));

        public Task<Listing<WorkflowRef>> ListWorkflowsAsync(Namespace ns, string location, int max, CancellationToken ct) =>
            Run("ListWorkflows", ct, () =>
            {
                if (location == FailLocation)
                {
                    throw new RpcException(new Status(StatusCode.Unavailable, "location down"));
                }

                if (location == "-")
                {
                    if (NoWildcard)
                    {
                        throw new RpcException(new Status(StatusCode.InvalidArgument, "wildcard not supported"));
                    }

                    var every = Workflows.Values.SelectMany(x => x).ToList();
                    return new Listing<WorkflowRef>([.. every.Take(max)], every.Count > max);
                }

                var all = Workflows.TryGetValue(location, out var w) ? w : [];
                return new Listing<WorkflowRef>([.. all.Take(max)], all.Count > max);
            });

        public Task<Listing<ExecutionRef>> ListExecutionsAsync(Namespace ns, string workflowName, string state, int max, CancellationToken ct) =>
            Run("ListExecutions", ct, () =>
            {
                NamesAsked.Add(workflowName);
                if (state == FailExecutionsState)
                {
                    throw new RpcException(new Status(StatusCode.Unavailable, "throttled"));
                }

                var all = (Executions.TryGetValue((workflowName, state), out var e) ? e : []).OrderByDescending(x => x.EndedUtc).ToList();
                return new Listing<ExecutionRef>([.. all.Take(max)], all.Count > max);
            });

        /// <summary>The names Google was actually asked about, so a test can see what was sent.</summary>
        public List<string> NamesAsked { get; } = [];

        public Task<ExecutionRead?> GetExecutionAsync(Namespace ns, string executionName, CancellationToken ct)
        {
            NamesAsked.Add(executionName);
            return Run("GetExecution", ct, () => Detail);
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

            return Throw is not null ? Task.FromException<T>(Throw) : Task.FromResult(answer());
        }
    }
}
