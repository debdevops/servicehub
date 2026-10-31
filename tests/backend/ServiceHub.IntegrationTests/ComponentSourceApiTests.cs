using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;

namespace ServiceHub.IntegrationTests;

/// <summary>
/// The three read endpoints for a component that is not a message store, through the real host: wire shape, the "not enabled" state,
/// the errors, and that they call only the source. The cloud is a fake source, so nothing here reaches AWS.
/// </summary>
public sealed class ComponentSourceApiTests
{
    private sealed class FakeSource : IComponentSource
    {
        public Result Access { get; set; } = Result.Success();

        public List<string> Calls { get; } = [];

        public string ComponentKey => "step-functions";

        public CloudProviderType Provider => CloudProviderType.Aws;

        public ComponentSourceCapabilities Capabilities => new(ReasonInList: false, ReadsPayload: false);

        public Task<Result> ValidateAccessAsync(Namespace ns, CancellationToken ct) => Task.FromResult(Access);

        public Task<Result<SourcePlacesPage>> ListPlacesAsync(Namespace ns, CancellationToken ct)
        {
            Calls.Add("places");
            return Task.FromResult(Result.Success(new SourcePlacesPage([new SourcePlace("m1", "orders", "state machine", "https://console/m1")], ReachedLimit: true)));
        }

        public Task<Result<FailedItemsPage>> ListFailedAsync(Namespace ns, string? placeId, CancellationToken ct)
        {
            Calls.Add($"failed:{placeId}");
            return Task.FromResult(Result.Success(new FailedItemsPage(
                [new FailedItem("e1", "m1", DateTimeOffset.Parse("2026-10-01T00:00:00Z"), "FAILED", null, "Failed: run-1", CopyKind.None, "https://console/e1")],
                ReachedLimit: true, Incomplete: true)));
        }

        public Task<Result<FailedItemDetail?>> GetFailedAsync(Namespace ns, string itemId, CancellationToken ct)
        {
            Calls.Add($"detail:{itemId}");
            return Task.FromResult(Result.Success<FailedItemDetail?>(itemId == "gone" ? null : new FailedItemDetail(
                new FailedItem(itemId, "m1", null, "FAILED", "States.TaskFailed", "Failed: run-1", CopyKind.None, null), "States.TaskFailed", "boom", null, null)));
        }
    }

    private sealed class FakeMover : IComponentMoveOn
    {
        public List<string> Calls { get; } = [];

        public string ComponentKey => "step-functions";

        public CloudProviderType Provider => CloudProviderType.Aws;

        public string Verb => "redrive";

        public bool Owns(Namespace ns, string itemId) => itemId.StartsWith("arn:aws:states:", StringComparison.Ordinal);

        public string PlaceOf(string itemId) => "arn:aws:states:us-east-1:1:stateMachine:m";

        public Task<Result<MoveOnAcceptance>> RedriveAsync(Namespace ns, string itemId, string requestToken, CancellationToken ct)
        {
            Calls.Add(itemId);
            return Task.FromResult(Result.Success(new MoveOnAcceptance(null)));
        }
    }

    private sealed class NoAwsMessaging : ICloudMessagingProvider
    {
        public CloudProviderType ProviderType => CloudProviderType.Aws;
        public ProviderCapabilities Capabilities => ProviderCapabilities.Aws;
        public IMessageReceiver GetMessageReceiver() => throw new NotSupportedException();
        public IMessageSender GetMessageSender() => throw new NotSupportedException();
        public Task<Result> ValidateConnectionAsync(Namespace ns, CancellationToken ct) => Task.FromResult(Result.Success());
        public Task<Result<IReadOnlyList<CloudEntity>>> ListEntitiesAsync(Guid namespaceId, CancellationToken ct) =>
            Task.FromResult(Result.Success<IReadOnlyList<CloudEntity>>([]));
    }

    private static (DeadLettersApiTests.Handle Host, FakeSource Source) Host() => Host(new FakeMover());

    private static (DeadLettersApiTests.Handle Host, FakeSource Source) Host(FakeMover mover)
    {
        var source = new FakeSource();
        var root = new ServiceHubApiFactory();
        var factory = root.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(ICloudMessagingProvider) || d.ServiceType == typeof(IComponentSource) || d.ServiceType == typeof(IComponentMoveOn)).ToList())
            {
                services.Remove(d);
            }

            services.AddSingleton<ICloudMessagingProvider>(new NoAwsMessaging());
            services.AddSingleton<IComponentSource>(source);
            services.AddSingleton<IComponentMoveOn>(mover);
        }));
        return (new DeadLettersApiTests.Handle(root, factory), source);
    }

    private static async Task<JsonElement> Get(HttpClient client, string url, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(expected);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task A_connection_that_can_read_the_component_lists_its_places()
    {
        var (host, source) = Host();
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");

        var body = await Get(host.Client, $"/api/v1/namespaces/{aws}/components/step-functions/source");

        body.GetProperty("available").GetBoolean().Should().BeTrue();
        body.GetProperty("reasonInList").GetBoolean().Should().BeFalse();
        body.GetProperty("placesReachedLimit").GetBoolean().Should().BeTrue();
        body.GetProperty("placesIncomplete").GetBoolean().Should().BeFalse();
        body.GetProperty("places").EnumerateArray().Should().ContainSingle().Which.GetProperty("name").GetString().Should().Be("orders");
        source.Calls.Should().Contain("places");
    }

    [Fact]
    public async Task A_credential_that_cannot_read_it_is_a_state_with_the_reason_not_an_error()
    {
        var (host, source) = Host();
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");
        source.Access = Result.Failure(Error.Forbidden("AWS.StepFunctions.NotEnabled", "Not enabled for this connection: needs states:ListStateMachines."));

        var body = await Get(host.Client, $"/api/v1/namespaces/{aws}/components/step-functions/source");

        body.GetProperty("available").GetBoolean().Should().BeFalse();
        body.GetProperty("reason").GetString().Should().StartWith("Not enabled");
        body.GetProperty("places").EnumerateArray().Should().BeEmpty();
        source.Calls.Should().BeEmpty("nothing is read once access is refused");
    }

    [Fact]
    public async Task Failed_items_carry_the_cap_and_incomplete_flags_and_the_chosen_place()
    {
        var (host, source) = Host();
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");

        var body = await Get(host.Client, $"/api/v1/namespaces/{aws}/components/step-functions/failed?place=m1");

        body.GetProperty("reachedLimit").GetBoolean().Should().BeTrue();
        body.GetProperty("incomplete").GetBoolean().Should().BeTrue();
        var item = body.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("status").GetString().Should().Be("FAILED");
        item.GetProperty("copy").GetString().Should().Be("none");
        item.GetProperty("reasonCode").ValueKind.Should().Be(JsonValueKind.Null);
        source.Calls.Should().Contain("failed:m1");
    }

    [Fact]
    public async Task One_item_is_read_by_a_query_value_and_a_missing_one_is_a_200_with_nothing()
    {
        var (host, _) = Host();
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");

        var found = await Get(host.Client, $"/api/v1/namespaces/{aws}/components/step-functions/failed/detail?itemId={Uri.EscapeDataString("arn:aws:states:x:y:execution:a:b")}");
        found.GetProperty("reason").GetString().Should().Be("States.TaskFailed");

        var gone = await Get(host.Client, $"/api/v1/namespaces/{aws}/components/step-functions/failed/detail?itemId=gone");
        gone.GetProperty("item").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Errors_are_a_400_for_a_missing_item_id_and_a_404_for_an_unknown_component_or_connection()
    {
        var (host, _) = Host();
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");

        await Get(host.Client, $"/api/v1/namespaces/{aws}/components/step-functions/failed/detail", HttpStatusCode.BadRequest);
        await Get(host.Client, $"/api/v1/namespaces/{aws}/components/workflows/source", HttpStatusCode.NotFound);
        await Get(host.Client, $"/api/v1/namespaces/{Guid.NewGuid()}/components/step-functions/source", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_endpoints_accept_only_get()
    {
        var (host, _) = Host();
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");

        foreach (var path in new[] { "source", "failed", "failed/detail" })
        {
            var post = await host.Client.PostAsync($"/api/v1/namespaces/{aws}/components/step-functions/{path}", null);
            post.StatusCode.Should().BeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
        }
    }

    private const string RunArn = "arn:aws:states:us-east-1:123456789012:execution:orders:run-1";

    private static async Task<HttpResponseMessage> PostRedrive(DeadLettersApiTests.Handle host, Guid ns, bool withIntent = true, string item = RunArn)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/namespaces/{ns}/component-redrives/step-functions?itemId={Uri.EscapeDataString(item)}");
        if (withIntent)
        {
            request.Headers.Add("X-ServiceHub-Intent", "redrive-execution");
        }

        return await host.Client.SendAsync(request);
    }

    [Fact]
    public async Task A_redrive_without_the_intent_header_is_a_400_and_nothing_is_sent()
    {
        var mover = new FakeMover();
        var (host, _) = Host(mover);
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");

        (await PostRedrive(host, aws, withIntent: false)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        mover.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_redrive_is_accepted_once_for_step_functions_because_a_live_run_is_recorded()
    {
        var mover = new FakeMover();
        var (host, _) = Host(mover);
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");

        var check = await Get(host.Client, $"/api/v1/namespaces/{aws}/component-redrives/step-functions/check?itemId={Uri.EscapeDataString(RunArn)}");
        var response = await PostRedrive(host, aws);

        check.GetProperty("canRedrive").GetBoolean().Should().BeTrue();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Unverified", "acceptance never proves the run finished fixed");
        mover.Calls.Should().Equal(RunArn);
    }

    [Fact]
    public async Task A_redrive_is_refused_on_a_read_only_connection_and_nothing_is_sent()
    {
        var mover = new FakeMover();
        var (host, _) = Host(mover);
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");
        var readOnly = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/namespaces/{aws}/can-send") { Content = JsonContent.Create(new { canSend = false }) };
        readOnly.Headers.Add("X-ServiceHub-Intent", "declare-can-send");
        (await host.Client.SendAsync(readOnly)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await PostRedrive(host, aws);
        var check = await Get(host.Client, $"/api/v1/namespaces/{aws}/component-redrives/step-functions/check?itemId={Uri.EscapeDataString(RunArn)}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("CONNECTION_READ_ONLY");
        check.GetProperty("canRedrive").GetBoolean().Should().BeFalse();
        check.GetProperty("reasonCode").GetString().Should().Be("CONNECTION_READ_ONLY");
        mover.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task The_redrive_endpoints_need_an_item_id_and_a_known_connection()
    {
        var (host, _) = Host();
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");

        await Get(host.Client, $"/api/v1/namespaces/{aws}/component-redrives/step-functions/check", HttpStatusCode.BadRequest);
        await Get(host.Client, $"/api/v1/namespaces/{Guid.NewGuid()}/component-redrives/step-functions/check?itemId=x", HttpStatusCode.NotFound);
        (await PostRedrive(host, Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
