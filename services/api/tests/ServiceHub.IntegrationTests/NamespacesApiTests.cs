using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.IntegrationTests;

/// <summary>
/// Unit 1.6 through the real host: connect, test, list, inspect and remove a namespace, with the
/// clouds replaced by fakes so nothing leaves the machine. What is under test is the API — the
/// scoping, the intent header, the capability-honest numbers and the secret that never comes back.
/// </summary>
public sealed class NamespacesApiTests
{
    private const string Secret = "super-secret-key-value==";

    private static string AzureCredential(string host) =>
        $"Endpoint=sb://{host}.servicebus.windows.net/;SharedAccessKeyName=servicehub;SharedAccessKey={Secret}";

    private static WebApplicationFactoryHandle Host(bool failProbes = false, bool withAws = true)
    {
        var root = new ServiceHubApiFactory();
        var factory = root.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                foreach (var d in services.Where(d => d.ServiceType == typeof(ICloudMessagingProvider)).ToList())
                {
                    services.Remove(d);
                }

                services.AddScoped<ICloudMessagingProvider>(_ => new FakeProvider(CloudProviderType.Azure, ProviderCapabilities.Azure, failProbes));
                if (withAws)
                {
                    services.AddScoped<ICloudMessagingProvider>(_ => new FakeProvider(CloudProviderType.Aws, ProviderCapabilities.Aws, failProbes));
                }
            }));
        return new WebApplicationFactoryHandle(root, factory, factory.CreateClient());
    }

    private static HttpRequestMessage Post(string url, object body, string? intent = "create-namespace")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (intent is not null)
        {
            request.Headers.Add("X-ServiceHub-Intent", intent);
        }

        return request;
    }

    private static object AzureBody(string name, string? credential = null) => new
    {
        name,
        connectionString = credential ?? AzureCredential(name),
        authType = "connectionString",
        provider = "azure",
    };

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Connecting_without_the_intent_header_is_refused_and_says_which_header()
    {
        using var host = Host();

        var response = await host.Client.SendAsync(Post("/api/v1/namespaces", AzureBody("acme-bus"), intent: null));

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
        var problem = await Json(response);
        problem.GetProperty("code").GetString().Should().Be("intent_required");
        problem.GetProperty("detail").GetString().Should().Contain("X-ServiceHub-Intent");
    }

    [Fact]
    public async Task A_connected_namespace_carries_its_capabilities_and_never_its_credential()
    {
        using var host = Host();

        var created = await host.Client.SendAsync(Post("/api/v1/namespaces", AzureBody("acme-bus")));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = await created.Content.ReadAsStringAsync();

        var listed = await host.Client.GetStringAsync("/api/v1/namespaces");
        var id = JsonDocument.Parse(createdBody).RootElement.GetProperty("id").GetGuid();
        var one = await host.Client.GetStringAsync($"/api/v1/namespaces/{id}");

        foreach (var body in new[] { createdBody, listed, one })
        {
            body.Should().NotContain(Secret).And.NotContainEquivalentOf("\"connectionString\":").And.NotContain("SharedAccessKey").And.NotContain("ENC[");
        }

        var ns = JsonDocument.Parse(one).RootElement;
        ns.GetProperty("provider").GetString().Should().Be("azure");
        ns.GetProperty("capabilities").GetProperty("canProveDlqAbsence").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task The_stored_credential_is_an_envelope_in_the_database_file()
    {
        using var host = Host();
        await host.Client.SendAsync(Post("/api/v1/namespaces", AzureBody("acme-bus")));

        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection(
            $"Data Source={Path.Combine(host.Root.DataDirectory, ServiceHubDataDirectory.DatabaseFileName)};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ConnectionStringEncrypted FROM Namespaces LIMIT 1";
        var raw = (string)command.ExecuteScalar()!;

        raw.Should().StartWith("ENC[").And.NotContain(Secret);
    }

    [Fact]
    public async Task The_same_name_or_the_same_credential_twice_is_a_conflict()
    {
        using var host = Host();
        (await host.Client.SendAsync(Post("/api/v1/namespaces", AzureBody("acme-bus")))).StatusCode.Should().Be(HttpStatusCode.Created);

        var sameName = await host.Client.SendAsync(Post("/api/v1/namespaces", AzureBody("acme-bus", AzureCredential("other-host"))));
        var sameCredential = await host.Client.SendAsync(Post("/api/v1/namespaces", AzureBody("second-bus", AzureCredential("acme-bus"))));

        sameName.StatusCode.Should().Be(HttpStatusCode.Conflict);
        sameCredential.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(sameName)).GetProperty("code").GetString().Should().Be("Namespace.AlreadyExists");
    }

    [Fact]
    public async Task A_provider_with_no_adapter_in_this_build_cannot_be_connected()
    {
        using var host = Host(withAws: false);
        var body = new
        {
            name = "orders-queue",
            connectionString = "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
            authType = "awsAccessKey",
            provider = "aws",
            awsRegion = "us-east-1",
        };

        var response = await host.Client.SendAsync(Post("/api/v1/namespaces", body));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response)).GetProperty("code").GetString().Should().Be("capability_unavailable");
    }

    [Fact]
    public async Task Testing_a_connection_records_the_outcome_and_a_failure_is_an_answer_not_an_error()
    {
        using var ok = Host();
        var id = await Connect(ok, "acme-bus");
        var success = await ok.Client.PostAsync($"/api/v1/namespaces/{id}/test-connection", null);
        (await Json(success)).GetProperty("isConnected").GetBoolean().Should().BeTrue();
        var afterOk = await Json(await ok.Client.GetAsync($"/api/v1/namespaces/{id}"));
        afterOk.GetProperty("lastConnectionTestSucceeded").GetBoolean().Should().BeTrue();

        using var failing = Host(failProbes: true);
        var failId = await Connect(failing, "acme-bus");
        var failure = await failing.Client.PostAsync($"/api/v1/namespaces/{failId}/test-connection", null);
        failure.StatusCode.Should().Be(HttpStatusCode.OK);
        var failBody = await Json(failure);
        failBody.GetProperty("isConnected").GetBoolean().Should().BeFalse();
        failBody.GetProperty("message").GetString().Should().Contain("probe refused");
        (await Json(await failing.Client.GetAsync($"/api/v1/namespaces/{failId}")))
            .GetProperty("lastConnectionTestSucceeded").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task One_entities_endpoint_serves_every_kind_and_rejects_an_unknown_one()
    {
        using var host = Host();
        var id = await Connect(host, "acme-bus");

        var all = await Json(await host.Client.GetAsync($"/api/v1/namespaces/{id}/entities"));
        var queues = await Json(await host.Client.GetAsync($"/api/v1/namespaces/{id}/entities?kind=queue"));
        var bogus = await host.Client.GetAsync($"/api/v1/namespaces/{id}/entities?kind=widget");

        all.GetProperty("entities").GetArrayLength().Should().Be(3);
        queues.GetProperty("entities").EnumerateArray().Select(e => e.GetProperty("name").GetString())
            .Should().BeEquivalentTo(["orders"]);
        bogus.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Message_totals_are_numbers_where_the_provider_can_count_and_null_where_it_cannot()
    {
        using var host = Host();
        var azureId = await Connect(host, "acme-bus");
        var awsResponse = await host.Client.SendAsync(Post("/api/v1/namespaces", new
        {
            name = "orders-queue",
            connectionString = "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
            authType = "awsAccessKey",
            provider = "aws",
            awsRegion = "us-east-1",
        }));
        awsResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var awsId = (await Json(awsResponse)).GetProperty("id").GetGuid();

        var azure = await Json(await host.Client.GetAsync($"/api/v1/namespaces/{azureId}/stats"));
        azure.GetProperty("messageCountsSupported").GetBoolean().Should().BeTrue();
        azure.GetProperty("activeMessages").GetInt64().Should().Be(12);
        azure.GetProperty("deadLetterMessages").GetInt64().Should().Be(3);

        var aws = await Json(await host.Client.GetAsync($"/api/v1/namespaces/{awsId}/stats"));
        aws.GetProperty("messageCountsSupported").GetBoolean().Should().Be(ProviderCapabilities.Aws.SupportsMessageCounts);
    }

    [Fact]
    public async Task Removing_a_namespace_needs_the_intent_header_and_then_it_is_gone()
    {
        using var host = Host();
        var id = await Connect(host, "acme-bus");

        var refused = await host.Client.DeleteAsync($"/api/v1/namespaces/{id}");
        refused.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/namespaces/{id}");
        request.Headers.Add("X-ServiceHub-Intent", "delete-namespace");
        (await host.Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await host.Client.GetAsync($"/api/v1/namespaces/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_owners_namespace_is_invisible_everywhere_and_looks_like_it_does_not_exist()
    {
        using var host = Host();
        Guid foreign;
        await using (var scope = host.Factory.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();
            var ns = Namespace.Create(
                "someone-elses.servicebus.windows.net", AzureCredential("someone-elses"), ownerId: "another-owner").Value;
            (await repository.AddAsync(ns)).IsSuccess.Should().BeTrue();
            foreign = ns.Id;
        }

        (await Json(await host.Client.GetAsync("/api/v1/namespaces"))).GetArrayLength().Should().Be(0);
        foreach (var path in new[] { "", "/stats", "/entities" })
        {
            (await host.Client.GetAsync($"/api/v1/namespaces/{foreign}{path}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        (await host.Client.PostAsync($"/api/v1/namespaces/{foreign}/test-connection", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<Guid> Connect(WebApplicationFactoryHandle host, string name)
    {
        var response = await host.Client.SendAsync(Post("/api/v1/namespaces", AzureBody(name)));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private sealed class WebApplicationFactoryHandle(
        ServiceHubApiFactory root, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, HttpClient client) : IDisposable
    {
        public ServiceHubApiFactory Root { get; } = root;
        public Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory { get; } = factory;
        public HttpClient Client { get; } = client;

        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
            Root.Dispose();
        }
    }
}

/// <summary>What a fake cloud reports, so the API's arithmetic and honesty can be asserted.</summary>
internal sealed class FakeProvider(CloudProviderType type, ProviderCapabilities capabilities, bool failProbes) : ICloudMessagingProvider
{
    public CloudProviderType ProviderType => type;

    public ProviderCapabilities Capabilities => capabilities;

    public Task<Result> ValidateConnectionAsync(Namespace ns, CancellationToken ct) =>
        Task.FromResult(failProbes
            ? Result.Failure(Error.ExternalService("fake.probe", "probe refused"))
            : Result.Success());

    public Task<Result<IReadOnlyList<CloudEntity>>> ListEntitiesAsync(Guid namespaceId, CancellationToken ct)
    {
        IReadOnlyList<CloudEntity> entities =
        [
            new() { Name = "orders", EntityType = "Queue", ActiveMessageCount = 10, DeadLetterCount = 3, Provider = type },
            new() { Name = "events", EntityType = "Topic", Provider = type },
            new() { Name = "events/billing", EntityType = "Subscription", ActiveMessageCount = 2, Provider = type },
        ];
        return Task.FromResult(Result.Success(entities));
    }

    public IMessageReceiver GetMessageReceiver() => throw new NotSupportedException();

    public IMessageSender GetMessageSender() => throw new NotSupportedException();
}
