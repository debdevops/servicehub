using System.Net;
using FluentAssertions;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;
using ServiceHub.Providers.Azure.Components;
using ServiceHub.Providers.Azure.MoveOn;

namespace ServiceHub.UnitTests.Components.MoveOn;

/// <summary>
/// The Logic Apps mover against a fake Azure: it checks the run ended badly right now, builds exactly one resubmit address from the run's
/// own trigger, never reads inputs or outputs, and tells "did nothing" from "may have" by the error's type.
/// </summary>
public sealed class LogicAppsMoveOnTests
{
    private const string Sub = "11111111-1111-1111-1111-111111111111";
    private const string Orders = $"/subscriptions/{Sub}/resourceGroups/rg-orders/providers/Microsoft.Logic/workflows/orders";
    private const string Run = $"{Orders}/runs/08584-run-1";

    private readonly FakeReader _reader = new();
    private readonly FakeDoor _door = new();
    private readonly LogicAppsMoveOn _mover;
    private readonly Namespace _ns = Namespace.CreateWithManagedIdentity("logic-test", ConnectionAuthType.ManagedIdentity, provider: CloudProviderType.Azure).Value;

    public LogicAppsMoveOnTests()
    {
        _mover = new LogicAppsMoveOn(_reader, _door);
        _reader.Detail = Read("Failed", "manual");
    }

    private static LogicRunRead Read(string status, string? trigger) =>
        new(new LogicRunRef(Run, status, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(-4)), "ActionFailed", "failed", trigger);

    [Fact]
    public void It_serves_logic_apps_on_azure_and_calls_its_action_a_resubmit_not_a_redrive()
    {
        _mover.ComponentKey.Should().Be("logic-apps");
        _mover.Provider.Should().Be(CloudProviderType.Azure);
        _mover.Verb.Should().Be("resubmit");
    }

    [Theory]
    [InlineData(Run, true)]
    [InlineData(Orders, false)]
    [InlineData($"{Orders}/runs/", false)]
    [InlineData("arn:aws:states:us-east-1:123456789012:execution:orders:run-1", false)]
    [InlineData($"/subscriptions/{Sub}/resourceGroups/rg/providers/Microsoft.Web/sites/x/runs/1", false)]
    public void It_owns_only_a_logic_apps_run_by_its_resource_id(string id, bool owns)
    {
        _mover.Owns(_ns, id).Should().Be(owns);
    }

    [Fact]
    public void The_place_is_the_logic_app_the_run_belongs_to()
    {
        _mover.PlaceOf(Run).Should().Be(Orders);
    }

    [Fact]
    public async Task A_failed_run_is_resubmitted_with_one_post_to_its_own_triggers_history()
    {
        var result = await _mover.RedriveAsync(_ns, Run, "token", default);

        result.IsSuccess.Should().BeTrue();
        _door.Posted.Should().ContainSingle().Which.AbsoluteUri
            .Should().Be($"https://management.azure.com{Orders}/triggers/manual/histories/08584-run-1/resubmit?api-version=2016-06-01");
        _reader.Reads.Should().Equal(Run);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("TimedOut")]
    [InlineData("Cancelled")]
    public async Task A_run_that_ended_badly_may_be_resubmitted(string status)
    {
        _reader.Detail = Read(status, "manual");

        (await _mover.RedriveAsync(_ns, Run, "token", default)).IsSuccess.Should().BeTrue();
        _door.Posted.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("Succeeded")]
    [InlineData("Running")]
    [InlineData("Waiting")]
    public async Task A_run_that_did_not_end_badly_is_never_resubmitted(string status)
    {
        _reader.Detail = Read(status, "manual");

        var result = await _mover.RedriveAsync(_ns, Run, "token", default);

        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("Azure.LogicApps.NotResubmittable");
        _door.Posted.Should().BeEmpty();
    }

    [Fact]
    public async Task A_run_azure_no_longer_has_is_not_found_and_nothing_is_posted()
    {
        _reader.Detail = null;

        var result = await _mover.RedriveAsync(_ns, Run, "token", default);

        result.Error.Type.Should().Be(ErrorType.NotFound);
        _door.Posted.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("manual?x=1")]
    [InlineData("../other")]
    public async Task A_run_with_no_usable_trigger_name_is_never_posted_and_never_put_in_an_address(string? trigger)
    {
        _reader.Detail = Read("Failed", trigger);

        var result = await _mover.RedriveAsync(_ns, Run, "token", default);

        result.Error.Code.Should().Be("Azure.LogicApps.NoTrigger");
        _door.Posted.Should().BeEmpty();
    }

    [Fact]
    public async Task Something_that_is_not_a_run_is_refused_before_azure_is_asked_anything()
    {
        var result = await _mover.RedriveAsync(_ns, Orders, "token", default);

        result.Error.Type.Should().Be(ErrorType.Validation);
        _reader.Reads.Should().BeEmpty();
        _door.Posted.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, ErrorType.Forbidden, "Azure.LogicApps.ResubmitNotAllowed")]
    [InlineData(HttpStatusCode.Unauthorized, ErrorType.Forbidden, "Azure.LogicApps.ResubmitNotAllowed")]
    [InlineData(HttpStatusCode.NotFound, ErrorType.NotFound, "Azure.LogicApps.ResubmitGone")]
    [InlineData(HttpStatusCode.Conflict, ErrorType.Conflict, "Azure.LogicApps.ResubmitConflict")]
    [InlineData(HttpStatusCode.BadRequest, ErrorType.Validation, "Azure.LogicApps.ResubmitInvalid")]
    [InlineData(HttpStatusCode.TooManyRequests, ErrorType.RateLimited, "Azure.LogicApps.ResubmitLimit")]
    [InlineData(HttpStatusCode.ServiceUnavailable, ErrorType.ExternalService, "Azure.LogicApps.ResubmitFailed")]
    [InlineData(HttpStatusCode.InternalServerError, ErrorType.ExternalService, "Azure.LogicApps.ResubmitFailed")]
    public async Task What_azure_answers_is_told_apart_as_did_nothing_or_may_have(HttpStatusCode status, ErrorType type, string code)
    {
        _door.Throw = new AzureRestException(status, "answer");

        var result = await _mover.RedriveAsync(_ns, Run, "token", default);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(type);
        result.Error.Code.Should().Be(code);
    }

    [Fact]
    public async Task A_refused_identity_is_told_which_permission_it_lacks_and_that_reader_does_not_have_it()
    {
        _door.Throw = new AzureRestException(HttpStatusCode.Forbidden, "denied");

        var result = await _mover.RedriveAsync(_ns, Run, "token", default);

        result.Error.Message.Should().Contain("Microsoft.Logic/workflows/triggers/histories/resubmit/action").And.Contain("Reader");
    }

    [Fact]
    public async Task A_connection_that_does_not_sign_in_with_entra_is_refused_and_says_so()
    {
        _door.Throw = new AzureNotEntraException(ConnectionAuthType.ConnectionString);

        var result = await _mover.RedriveAsync(_ns, Run, "token", default);

        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Message.Should().Contain("Entra");
    }

    [Fact]
    public async Task A_failure_while_reading_the_run_posts_nothing()
    {
        _reader.Throw = new AzureRestException(HttpStatusCode.ServiceUnavailable, "down");

        var result = await _mover.RedriveAsync(_ns, Run, "token", default);

        result.IsFailure.Should().BeTrue();
        _door.Posted.Should().BeEmpty();
    }

    [Fact]
    public async Task The_callers_cancellation_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _door.HonourCancellation = true;

        var act = () => _mover.RedriveAsync(_ns, Run, "token", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class FakeReader : IAzureLogicAppsReader
    {
        public LogicRunRead? Detail { get; set; }

        public Exception? Throw { get; set; }

        public List<string> Reads { get; } = [];

        public Task<LogicRunRead?> GetRunAsync(Namespace ns, string runId, CancellationToken ct)
        {
            Reads.Add(runId);
            return Throw is not null ? throw Throw : Task.FromResult(Detail);
        }

        public Task<Listing<string>> ListSubscriptionsAsync(Namespace ns, CancellationToken ct) => throw new NotSupportedException("a mover never lists");

        public Task<Listing<LogicAppRef>> ListWorkflowsAsync(Namespace ns, string subscriptionId, int max, CancellationToken ct) => throw new NotSupportedException("a mover never lists");

        public Task<Listing<LogicRunRef>> ListRunsAsync(Namespace ns, string workflowId, string status, int max, CancellationToken ct) => throw new NotSupportedException("a mover never lists");
    }

    private sealed class FakeDoor : IAzureResubmitClient
    {
        public List<Uri> Posted { get; } = [];

        public Exception? Throw { get; set; }

        public bool HonourCancellation { get; set; }

        public Task PostAsync(Namespace ns, Uri url, CancellationToken ct)
        {
            if (HonourCancellation)
            {
                ct.ThrowIfCancellationRequested();
            }

            Posted.Add(url);
            return Throw is not null ? throw Throw : Task.CompletedTask;
        }
    }
}
