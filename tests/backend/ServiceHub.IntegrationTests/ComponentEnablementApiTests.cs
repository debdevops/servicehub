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
/// The Add a component endpoints through the real host: what is listed, the read-only check, ticking and unticking with the intent
/// header, and that a failed check saves nothing. The cloud is a fake source, so nothing here reaches AWS.
/// </summary>
public sealed class ComponentEnablementApiTests
{
    private sealed class FakeSource : IComponentSource
    {
        public Result Access { get; set; } = Result.Success();

        public string ComponentKey => "step-functions";

        public CloudProviderType Provider => CloudProviderType.Aws;

        public ComponentSourceCapabilities Capabilities => new(ReasonInList: false, ReadsPayload: false);

        public Task<Result> ValidateAccessAsync(Namespace ns, CancellationToken ct) => Task.FromResult(Access);

        public Task<Result<SourcePlacesPage>> ListPlacesAsync(Namespace ns, CancellationToken ct) => Task.FromResult(Result.Success(new SourcePlacesPage([], false)));

        public Task<Result<FailedItemsPage>> ListFailedAsync(Namespace ns, string? placeId, CancellationToken ct) => Task.FromResult(Result.Success(new FailedItemsPage([], false)));

        public Task<Result<FailedItemDetail?>> GetFailedAsync(Namespace ns, string itemId, CancellationToken ct) => Task.FromResult(Result.Success<FailedItemDetail?>(null));
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

    private static (DeadLettersApiTests.Handle Host, FakeSource Source) Host()
    {
        var source = new FakeSource();
        var root = new ServiceHubApiFactory();
        var factory = root.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(ICloudMessagingProvider) || d.ServiceType == typeof(IComponentSource)).ToList())
            {
                services.Remove(d);
            }

            services.AddSingleton<ICloudMessagingProvider>(new NoAwsMessaging());
            services.AddSingleton<IComponentSource>(source);
        }));
        return (new DeadLettersApiTests.Handle(root, factory), source);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.Should().Be(expected);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, Guid id, string key, bool enabled, string? intent = "manage-components")
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/namespaces/{id}/enabled-components/{key}") { Content = JsonContent.Create(new { enabled }) };
        if (intent is not null)
        {
            request.Headers.Add("X-ServiceHub-Intent", intent);
        }

        return client.SendAsync(request);
    }

    private static JsonElement Entry(JsonElement list, string key) =>
        list.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("componentKey").GetString() == key);

    [Fact]
    public async Task The_list_says_how_each_component_is_reached_and_what_is_already_on()
    {
        var (host, _) = Host();
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");

        var list = await Json(await host.Client.GetAsync($"/api/v1/namespaces/{aws}/components"), HttpStatusCode.OK);

        list.GetProperty("cloud").GetString().Should().Be("AWS");
        var sqs = Entry(list, "sqs");
        sqs.GetProperty("state").GetString().Should().Be("alwaysOn");
        sqs.GetProperty("canToggle").GetBoolean().Should().BeFalse();
        var sfn = Entry(list, "step-functions");
        sfn.GetProperty("access").GetString().Should().Be("sameConnection");
        sfn.GetProperty("state").GetString().Should().Be("off");
        sfn.GetProperty("canToggle").GetBoolean().Should().BeTrue();
        sfn.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Should().Contain("states:ListExecutions");
        sfn.GetProperty("snippets").EnumerateArray().Should().NotBeEmpty("the policy is copy-ready");
        Entry(list, "lambda-eventbridge").GetProperty("state").GetString().Should().Be("included");
        Entry(list, "aws-streams").GetProperty("state").GetString().Should().Be("unavailable");
    }

    [Fact]
    public async Task Ticking_needs_the_intent_header()
    {
        var (host, _) = Host();
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");

        var problem = await Json(await Put(host.Client, aws, "step-functions", true, intent: null), HttpStatusCode.PreconditionRequired);

        problem.GetProperty("detail").GetString().Should().Contain("manage-components");
    }

    [Fact]
    public async Task A_failed_check_saves_nothing_and_names_what_is_missing()
    {
        var (host, source) = Host();
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");
        source.Access = Result.Failure(Error.Forbidden("AWS.StepFunctions.NotEnabled", "Not enabled for this connection: needs states:ListExecutions."));

        var problem = await Json(await Put(host.Client, aws, "step-functions", true), HttpStatusCode.Conflict);

        problem.GetProperty("detail").GetString().Should().Contain("Not added").And.Contain("states:ListExecutions");
        var list = await Json(await host.Client.GetAsync($"/api/v1/namespaces/{aws}/components"), HttpStatusCode.OK);
        Entry(list, "step-functions").GetProperty("state").GetString().Should().Be("off");
        var enabled = await Json(await host.Client.GetAsync("/api/v1/namespaces/components/enabled"), HttpStatusCode.OK);
        enabled.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Ticking_then_unticking_is_remembered_and_repeatable_and_unticking_deletes_nothing()
    {
        var (host, _) = Host();
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");

        var on = await Json(await Put(host.Client, aws, "step-functions", true), HttpStatusCode.OK);
        on.GetProperty("state").GetString().Should().Be("on");
        (await Put(host.Client, aws, "step-functions", true)).StatusCode.Should().Be(HttpStatusCode.OK, "ticking twice ends in the same state");
        var enabled = await Json(await host.Client.GetAsync("/api/v1/namespaces/components/enabled"), HttpStatusCode.OK);
        enabled.EnumerateArray().Should().ContainSingle().Which.GetProperty("componentKey").GetString().Should().Be("step-functions");

        var off = await Json(await Put(host.Client, aws, "step-functions", false), HttpStatusCode.OK);
        off.GetProperty("state").GetString().Should().Be("off");
        (await Json(await host.Client.GetAsync("/api/v1/namespaces/components/enabled"), HttpStatusCode.OK)).EnumerateArray().Should().BeEmpty();

        using var scope = host.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<INamespaceComponentService>().ListAsync(Namespace.SpaOwnerId, aws);
        stored.Should().ContainSingle().Which.Enabled.Should().BeFalse("the row stays, so the history it produced is not orphaned");
    }

    [Fact]
    public async Task A_message_store_an_unknown_key_and_an_unbuilt_component_cannot_be_ticked()
    {
        var (host, _) = Host();
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");

        (await Put(host.Client, aws, "sqs", false)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Put(host.Client, aws, "aws-streams", true)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Put(host.Client, aws, "no-such-component", true)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Check_access_reports_the_reason_and_never_saves()
    {
        var (host, source) = Host();
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");
        source.Access = Result.Failure(Error.Forbidden("AWS.StepFunctions.NotEnabled", "Not enabled for this connection: needs states:ListStateMachines."));

        var check = await Json(await host.Client.PostAsync($"/api/v1/namespaces/{aws}/enabled-components/step-functions/check", null), HttpStatusCode.OK);

        check.GetProperty("ok").GetBoolean().Should().BeFalse();
        check.GetProperty("reason").GetString().Should().Contain("states:ListStateMachines");
        check.GetProperty("permissions").EnumerateArray().Should().HaveCount(3);
        (await Json(await host.Client.GetAsync("/api/v1/namespaces/components/enabled"), HttpStatusCode.OK)).EnumerateArray().Should().BeEmpty();
    }
}
