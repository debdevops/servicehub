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
        public FakeSource(string key = "step-functions", CloudProviderType provider = CloudProviderType.Aws)
        {
            ComponentKey = key;
            Provider = provider;
        }

        public Result Access { get; set; } = Result.Success();

        public string ComponentKey { get; }

        public CloudProviderType Provider { get; }

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

    private static (DeadLettersApiTests.Handle Host, FakeSource Source) Host(params FakeSource[] more)
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
            foreach (var extra in more)
            {
                services.AddSingleton<IComponentSource>(extra);
            }
        }));
        return (new DeadLettersApiTests.Handle(root, factory), source);
    }

    /// <summary>The real providers, but no component sources: what an install that registered none would show.</summary>
    private static DeadLettersApiTests.Handle HostWithoutSources()
    {
        var root = new ServiceHubApiFactory();
        var factory = root.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(IComponentSource)).ToList())
            {
                services.Remove(d);
            }
        }));
        return new DeadLettersApiTests.Handle(root, factory);
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
        var streams = Entry(list, "aws-streams");
        streams.GetProperty("state").GetString().Should().Be("off", "stream failures are built now, so the component can be ticked");
        streams.GetProperty("access").GetString().Should().Be("sameConnection");
        streams.GetProperty("canToggle").GetBoolean().Should().BeTrue();
        streams.GetProperty("status").GetString().Should().Be("beingValidated");
        streams.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Should().Contain(["lambda:ListEventSourceMappings", "sqs:ReceiveMessage"]);
        streams.GetProperty("steps").EnumerateArray().Select(p => p.GetString()).Should().Contain(x => x!.Contains("Check access"));
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

    /// <summary>
    /// 2026-10-23: PUT enabled-components is Admin-gated (adding a component widens what ServiceHub reads from a cloud) and no test proved a
    /// lower role is refused; the architecture test only reads the source for the words. A refused tick must also change nothing.
    /// </summary>
    [Fact]
    public async Task Ticking_a_component_is_an_Admin_act_a_viewer_and_an_operator_are_refused_and_nothing_is_saved()
    {
        var source = new FakeSource();
        var root = new ServiceHubApiFactory();
        var factory = root.WithWebHostBuilder(b =>
        {
            b.UseSetting("Security:Authentication:ApiKeys:0:Key", "viewer-key-value");
            b.UseSetting("Security:Authentication:ApiKeys:0:Description", "viewer");
            b.UseSetting("Security:Authentication:ApiKeys:1:Key", "operator-key-value");
            b.UseSetting("Security:Authentication:ApiKeys:1:Description", "operator");
            b.UseSetting("Security:Authentication:ApiKeys:2:Key", "lead-key-value");
            b.UseSetting("Security:Authentication:ApiKeys:2:Description", "lead");
            b.ConfigureServices(services =>
            {
                foreach (var d in services.Where(d => d.ServiceType == typeof(ICloudMessagingProvider) || d.ServiceType == typeof(IComponentSource)).ToList())
                {
                    services.Remove(d);
                }

                services.AddSingleton<ICloudMessagingProvider>(new NoAwsMessaging());
                services.AddSingleton<IComponentSource>(source);
            });
        });
        using var host = new DeadLettersApiTests.Handle(root, factory);
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");

        async Task Grant(string who, string role) =>
            (await host.Client.SendAsync(Keyed(HttpMethod.Post, "/api/v1/governance/grants", null, "grant-role", new { granteeIdentity = who, granteeKind = "ApiKey", role })))
                .StatusCode.Should().Be(HttpStatusCode.Created);
        await Grant("lead", "Admin");
        await Grant("viewer", "Viewer");
        await Grant("operator", "Operator");

        foreach (var who in new[] { "viewer-key-value", "operator-key-value" })
        {
            var refused = await host.Client.SendAsync(Keyed(HttpMethod.Put, $"/api/v1/namespaces/{aws}/enabled-components/step-functions", who, "manage-components", new { enabled = true }));
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{who} may not add a component");
        }

        (await Json(await host.Client.GetAsync("/api/v1/namespaces/components/enabled"), HttpStatusCode.OK)).EnumerateArray().Should().BeEmpty("a refused tick saves nothing");

        var allowed = await host.Client.SendAsync(Keyed(HttpMethod.Put, $"/api/v1/namespaces/{aws}/enabled-components/step-functions", "lead-key-value", "manage-components", new { enabled = true }));
        allowed.StatusCode.Should().Be(HttpStatusCode.OK, "an Admin may");
    }

    private static HttpRequestMessage Keyed(HttpMethod method, string url, string? key, string intent, object body)
    {
        var r = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        if (key is not null) r.Headers.Add("X-API-KEY", key);
        r.Headers.Add("X-ServiceHub-Intent", intent);
        return r;
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
        (await Put(host.Client, aws, "aws-streams", true)).StatusCode.Should().Be(HttpStatusCode.Conflict, "no reader is registered for it in this host");
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

    [Fact]
    public async Task A_failure_record_component_is_ticked_checked_and_listed_in_the_menu_like_a_run_history()
    {
        var streams = new FakeSource("aws-streams");
        var (host, _) = Host(streams);
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");

        var on = await Json(await Put(host.Client, aws, "aws-streams", true), HttpStatusCode.OK);

        on.GetProperty("state").GetString().Should().Be("on");
        var enabled = await Json(await host.Client.GetAsync("/api/v1/namespaces/components/enabled"), HttpStatusCode.OK);
        enabled.EnumerateArray().Should().ContainSingle().Which.GetProperty("componentKey").GetString().Should().Be("aws-streams");
        var check = await Json(await host.Client.PostAsync($"/api/v1/namespaces/{aws}/enabled-components/aws-streams/check", null), HttpStatusCode.OK);
        check.GetProperty("ok").GetBoolean().Should().BeTrue();
        check.GetProperty("permissions").EnumerateArray().Should().HaveCount(3);
    }

    [Fact]
    public async Task A_failure_record_component_whose_check_fails_is_not_added_and_says_why()
    {
        var streams = new FakeSource("aws-streams") { Access = Result.Failure(Error.Forbidden("AWS.Streams.NotEnabled", "Not enabled for this connection: needs sqs:ReceiveMessage.")) };
        var (host, _) = Host(streams);
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");

        var problem = await Json(await Put(host.Client, aws, "aws-streams", true), HttpStatusCode.Conflict);

        problem.GetProperty("detail").GetString().Should().Contain("Not added").And.Contain("sqs:ReceiveMessage");
        (await Json(await host.Client.GetAsync("/api/v1/namespaces/components/enabled"), HttpStatusCode.OK)).EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task The_azure_components_are_listed_as_off_and_tickable_with_their_entra_prerequisites()
    {
        using var host = HostWithoutSources();
        var azure = await DeadLettersApiTests.Connect(host.Client, "azure");

        var list = await Json(await host.Client.GetAsync($"/api/v1/namespaces/{azure}/components"), HttpStatusCode.OK);

        list.GetProperty("cloud").GetString().Should().Be("Azure");
        foreach (var key in new[] { "logic-apps", "event-grid" })
        {
            var entry = Entry(list, key);
            entry.GetProperty("access").GetString().Should().Be("sameConnection");
            entry.GetProperty("state").GetString().Should().Be("off");
            entry.GetProperty("canToggle").GetBoolean().Should().BeTrue();
            // Logic Apps has a recorded live run (RECOVERY-EVIDENCE.md section 8); Event Grid is still being validated.
            entry.GetProperty("status").GetString().Should().Be(key == "logic-apps" ? "supported" : "beingValidated");
            entry.GetProperty("steps").EnumerateArray().Select(p => p.GetString()).Should().Contain(x => x!.Contains("Entra"));
            entry.GetProperty("steps").EnumerateArray().Select(p => p.GetString()).Should().Contain(x => x!.Contains("Check access"));
            entry.GetProperty("permissions").EnumerateArray().Should().NotBeEmpty();
        }

        Entry(list, "service-bus").GetProperty("state").GetString().Should().Be("alwaysOn");
        (await Put(host.Client, azure, "logic-apps", true)).StatusCode.Should().Be(HttpStatusCode.Conflict, "no reader is registered in this host");
    }

    [Fact]
    public async Task A_service_bus_connection_string_connection_cannot_pass_the_azure_check_with_the_real_sources()
    {
        var root = new ServiceHubApiFactory();
        using var host = new DeadLettersApiTests.Handle(root, root);
        var azure = await DeadLettersApiTests.Connect(host.Client, "azure");

        var check = await Json(await host.Client.PostAsync($"/api/v1/namespaces/{azure}/enabled-components/logic-apps/check", null), HttpStatusCode.OK);

        check.GetProperty("ok").GetBoolean().Should().BeFalse();
        check.GetProperty("reason").GetString().Should().StartWith("Not enabled").And.Contain("Entra");
        var tick = await Json(await Put(host.Client, azure, "event-grid", true), HttpStatusCode.Conflict);
        tick.GetProperty("detail").GetString().Should().Contain("Not added").And.Contain("Entra");
        (await Json(await host.Client.GetAsync("/api/v1/namespaces/components/enabled"), HttpStatusCode.OK)).EnumerateArray().Should().BeEmpty();
    }
}
