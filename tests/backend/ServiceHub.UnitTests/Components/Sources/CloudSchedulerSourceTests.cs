using FluentAssertions;
using Grpc.Core;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Providers.Gcp.Components;

namespace ServiceHub.UnitTests.Components.Sources;

/// <summary>The Cloud Scheduler source against a fake Google: the conformance kit plus what is particular to a state-based reader.</summary>
public sealed class CloudSchedulerSourceTests : ComponentSourceConformance
{
    private const string Project = "my-proj";
    private const string Place = "projects/my-proj/locations/us-central1";
    private const string Nightly = "projects/my-proj/locations/us-central1/jobs/nightly";
    private static readonly DateTimeOffset At = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly FakeGoogle _google = new();
    private readonly CloudSchedulerSource _source;
    private readonly Namespace _ns = Namespace.Create(
        Project, "{\"type\":\"service_account\"}", provider: CloudProviderType.Gcp, gcpProjectId: Project).Value;

    public CloudSchedulerSourceTests()
    {
        _source = new CloudSchedulerSource(_google);
        _google.Locations = ["us-central1", "europe-west1"];
    }

    protected override IComponentSource Source => _source;

    protected override Namespace Connection => _ns;

    protected override string PlaceId => Place;

    protected override string ItemId => Nightly;

    protected override IReadOnlyList<string> CloudCalls => _google.Calls;

    private static SchedulerJobRef Job(string name, string state = "ENABLED", int? code = 14, int minutesAgo = 0, string? message = "target unavailable") =>
        new($"projects/my-proj/locations/us-central1/jobs/{name}", state, At.AddMinutes(-minutesAgo), code, message);

    // The fake holds jobs keyed by name, so one job is the place (Nightly) and `count` jobs are the failures a listing returns.
    protected override void ArrangeFailures(int count)
    {
        _google.Jobs.Clear();
        _google.Jobs["us-central1"] = [.. Enumerable.Range(0, count).Select(i => i == 0 ? Job("nightly", minutesAgo: 0) with { LastAttemptUtc = At } : Job($"job-{i:000}", minutesAgo: i))];
        _google.Detail = Job("nightly");
    }

    protected override void ArrangeAccessDenied() => _google.Throw = new RpcException(new Status(StatusCode.PermissionDenied, "denied"));

    protected override void ArrangeCloudFailure(string message) => _google.Throw = new RpcException(new Status(StatusCode.Unavailable, message));

    protected override void ArrangeSecretInFailureText(string secret)
    {
        _google.Jobs["us-central1"] = [Job("nightly", message: $"call to https://x?key={secret} failed {secret}")];
        _google.Detail = Job("nightly", message: $"call to https://x?key={secret} failed {secret}");
    }

    protected override void ArrangeMissingItem() => _google.Detail = null;

    protected override void ArrangeCancellationAware() => _google.HonourCancellation = true;

    [Fact]
    public async Task Only_a_job_whose_last_attempt_failed_is_a_failure()
    {
        _google.Jobs["us-central1"] =
        [
            Job("ok", code: 0), Job("never-ran", code: null), Job("broken", code: 4, minutesAgo: 5), Job("flaky", code: 14, minutesAgo: 1),
        ];

        var page = (await _source.ListFailedAsync(_ns, null, default)).Value;

        page.Items.Select(i => i.Summary).Should().Equal("Last attempt failed: Unavailable (flaky)", "Last attempt failed: Deadline exceeded (broken)");
        page.Items.Select(i => i.Status).Should().Equal("UNAVAILABLE", "DEADLINE_EXCEEDED");
    }

    [Fact]
    public async Task A_paused_job_is_never_a_failure_and_a_place_is_a_location_that_holds_jobs()
    {
        _google.Jobs["us-central1"] = [Job("paused-one", state: "PAUSED", code: 14), Job("live", code: 0)];
        _google.Jobs["europe-west1"] = [new SchedulerJobRef("projects/my-proj/locations/europe-west1/jobs/eu", "ENABLED", At, 0, null)];

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;
        var failed = (await _source.ListFailedAsync(_ns, null, default)).Value;

        places.Items.Select(p => (p.Name, p.Kind)).Should().Equal(("europe-west1", "location"), ("us-central1", "location"));
        failed.Items.Should().BeEmpty("a paused job's last attempt is stale");
    }

    [Fact]
    public async Task Jobs_are_listed_with_one_wildcard_call_and_the_per_location_walk_is_the_fallback()
    {
        _google.Jobs["us-central1"] = [Job("a")];

        (await _source.ListPlacesAsync(_ns, default)).Value.Items.Should().ContainSingle();
        _google.Calls.Should().Equal("ListJobs");

        _google.Calls.Clear();
        _google.NoWildcard = true;
        (await _source.ListPlacesAsync(_ns, default)).Value.Items.Should().ContainSingle();
        _google.Calls.Should().Contain("ListLocations");
    }

    [Fact]
    public async Task A_location_that_cannot_be_read_leaves_the_result_incomplete_and_keeps_the_rest()
    {
        _google.NoWildcard = true;
        _google.FailLocation = "europe-west1";
        _google.Jobs["us-central1"] = [Job("a")];

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;

        places.Items.Should().ContainSingle();
        places.Incomplete.Should().BeTrue("a location failed, so a job that exists there may be missing");
    }

    [Fact]
    public async Task One_place_returns_only_the_failures_in_that_location()
    {
        _google.Jobs["us-central1"] = [Job("nightly")];
        _google.Jobs["europe-west1"] = [new SchedulerJobRef("projects/my-proj/locations/europe-west1/jobs/eu", "ENABLED", At, 14, null)];

        var page = (await _source.ListFailedAsync(_ns, Place, default)).Value;

        page.Items.Should().ContainSingle().Which.Id.Should().Be(Nightly);
        page.Items[0].ConsoleUrl.Should().Be("https://console.cloud.google.com/cloudscheduler/jobs/edit/us-central1/nightly?project=my-proj");
    }

    [Fact]
    public async Task The_detail_names_the_status_and_a_job_that_has_succeeded_since_is_nothing()
    {
        _google.Detail = Job("nightly", code: 4, message: "deadline");

        var detail = (await _source.GetFailedAsync(_ns, Nightly, default)).Value!;

        detail.Reason.Should().Be("Deadline exceeded");
        detail.Cause.Should().Be("deadline");

        _google.Detail = Job("nightly", code: 0);
        (await _source.GetFailedAsync(_ns, Nightly, default)).Value.Should().BeNull("the last attempt now succeeded, so nothing is failed any more");
    }

    [Fact]
    public async Task A_status_code_with_no_name_is_shown_as_its_number_never_guessed()
    {
        _google.Jobs["us-central1"] = [Job("odd", code: 99)];

        (await _source.ListFailedAsync(_ns, null, default)).Value.Items.Single().Summary.Should().Be("Last attempt failed: Status code 99 (odd)");
    }

    [Theory]
    [InlineData("projects/other/locations/us-central1")]
    [InlineData("projects/my-proj/locations/us-central1/jobs/nightly")]
    [InlineData("projects/my-proj")]
    public async Task A_place_in_another_project_or_that_is_not_a_location_is_refused_before_any_call(string place)
    {
        (await _source.ListFailedAsync(_ns, place, default)).IsFailure.Should().BeTrue();
        _google.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("projects/other/locations/us-central1/jobs/nightly")]
    [InlineData("projects/my-proj/locations/us-central1")]
    [InlineData("projects/my-proj/locations/us-central1/jobs/nightly/extra")]
    public async Task An_item_in_another_project_or_that_is_not_a_job_is_refused_before_any_call(string item)
    {
        (await _source.GetFailedAsync(_ns, item, default)).IsFailure.Should().BeTrue();
        _google.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task The_message_is_bounded()
    {
        _google.Detail = Job("nightly", message: new string('x', 5000));

        (await _source.GetFailedAsync(_ns, Nightly, default)).Value!.Cause!.Length.Should().BeLessThanOrEqualTo(1001);
    }

    private sealed class FakeGoogle : IGcpSchedulerReader
    {
        public List<string> Calls { get; } = [];

        public IReadOnlyList<string> Locations { get; set; } = [];

        public Dictionary<string, IReadOnlyList<SchedulerJobRef>> Jobs { get; } = [];

        public SchedulerJobRef? Detail { get; set; }

        public Exception? Throw { get; set; }

        public bool HonourCancellation { get; set; }

        public string? FailLocation { get; set; }

        public bool NoWildcard { get; set; }

        public Task<Listing<string>> ListLocationsAsync(Namespace ns, CancellationToken ct) =>
            Run("ListLocations", ct, () => new Listing<string>(Locations, false));

        public Task<Listing<SchedulerJobRef>> ListJobsAsync(Namespace ns, string location, int max, CancellationToken ct) =>
            Run("ListJobs", ct, () =>
            {
                if (location == FailLocation)
                {
                    throw new RpcException(new Status(StatusCode.Unavailable, "location down"));
                }

                var all = location == "-"
                    ? NoWildcard ? throw new RpcException(new Status(StatusCode.InvalidArgument, "wildcard not supported")) : Jobs.Values.SelectMany(x => x).ToList()
                    : (Jobs.TryGetValue(location, out var j) ? j : []).ToList();
                return new Listing<SchedulerJobRef>([.. all.Take(max)], all.Count > max);
            });

        public Task<SchedulerJobRef?> GetJobAsync(Namespace ns, string jobName, CancellationToken ct) =>
            Run("GetJob", ct, () => Detail);

        private Task<T> Run<T>(string call, CancellationToken ct, Func<T> answer)
        {
            lock (Calls)
            {
                Calls.Add(call);
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
            catch (RpcException ex)
            {
                return Task.FromException<T>(ex);
            }
        }
    }
}
