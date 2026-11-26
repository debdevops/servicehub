using System.Net;
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
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.IntegrationTests;

/// <summary>Build-plan unit B1 through the real host: the two component actions, their wire shape, their errors and their scoping.</summary>
public sealed class FleetComponentsApiTests
{
    /// <summary>A cloud that lists the entities it is given and does nothing else.</summary>
    private sealed class ListingProvider(CloudProviderType type, ProviderCapabilities caps, CloudEntity[] entities) : ICloudMessagingProvider
    {
        public CloudProviderType ProviderType => type;
        public ProviderCapabilities Capabilities => caps;
        public IMessageReceiver GetMessageReceiver() => throw new NotSupportedException();
        public IMessageSender GetMessageSender() => throw new NotSupportedException();
        public Task<Result> ValidateConnectionAsync(Namespace ns, CancellationToken ct) => Task.FromResult(Result.Success());
        public Task<Result<IReadOnlyList<CloudEntity>>> ListEntitiesAsync(Guid namespaceId, CancellationToken ct) =>
            Task.FromResult(Result.Success<IReadOnlyList<CloudEntity>>(entities));
    }

    private static ICloudMessagingProvider Cloud(CloudProviderType type, ProviderCapabilities caps, params CloudEntity[] entities) =>
        new ListingProvider(type, caps, entities);

    private static (DeadLettersApiTests.Handle Host, WebApplicationFactory<Program> Factory) Host()
    {
        var root = new ServiceHubApiFactory();
        var factory = root.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(ICloudMessagingProvider)).ToList())
            {
                services.Remove(d);
            }

            services.AddSingleton<ICloudMessagingProvider>(Cloud(CloudProviderType.Azure, ProviderCapabilities.Azure,
                new CloudEntity { Name = "orders", EntityType = "Queue", Provider = CloudProviderType.Azure, DeadLetterCount = 3, ActiveMessageCount = 9 }));
            services.AddSingleton<ICloudMessagingProvider>(Cloud(CloudProviderType.Aws, ProviderCapabilities.Aws,
                new CloudEntity { Name = "orders", EntityType = "Queue", Provider = CloudProviderType.Aws, DeadLetterCount = 5, DeadLetterTargetName = "orders-dlq" },
                new CloudEntity { Name = "orders-dlq", EntityType = "Queue", Provider = CloudProviderType.Aws },
                new CloudEntity { Name = "fanout/subscriptions/billing", EntityType = "Subscription", Provider = CloudProviderType.Aws, DeadLetterCount = 1 }));
        }));
        return (new DeadLettersApiTests.Handle(root, factory), factory);
    }

    private static async Task<JsonElement> Get(HttpClient client, string url, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(expected);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task The_list_returns_each_connection_as_its_components_in_the_wire_words()
    {
        var (host, _) = Host();
        using var _h = host;
        await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Connect(host.Client, "aws");

        var all = (await Get(host.Client, "/api/v1/fleet/components")).GetProperty("components").EnumerateArray().ToList();

        all.Select(c => c.GetProperty("componentKey").GetString()).Should().BeEquivalentTo("service-bus", "sqs", "sns");
        var sqs = all.Single(c => c.GetProperty("componentKey").GetString() == "sqs");
        sqs.GetProperty("cloud").GetString().Should().Be("aws");
        sqs.GetProperty("deadLetters").GetProperty("kind").GetString().Should().Be("approximate");
        sqs.GetProperty("deadLetters").GetProperty("value").GetInt64().Should().Be(5);
        sqs.GetProperty("class").GetString().Should().NotBeNullOrEmpty();
        all.Single(c => c.GetProperty("componentKey").GetString() == "service-bus")
            .GetProperty("deadLetters").GetProperty("kind").GetString().Should().Be("exact");
    }

    [Fact]
    public async Task A_cloud_narrows_the_list_and_anything_else_is_a_400()
    {
        var (host, _) = Host();
        using var _h = host;
        await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Connect(host.Client, "aws");

        var aws = (await Get(host.Client, "/api/v1/fleet/components?cloud=aws")).GetProperty("components").EnumerateArray().ToList();
        aws.Should().OnlyContain(c => c.GetProperty("cloud").GetString() == "aws");

        // An empty value binds to "not given", so it means all clouds.
        foreach (var bad in new[] { "oracle", "7", " aws" })
        {
            await Get(host.Client, "/api/v1/fleet/components?cloud=" + Uri.EscapeDataString(bad), HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task One_component_is_found_by_connection_and_key_and_a_wrong_one_is_a_404()
    {
        var (host, _) = Host();
        using var _h = host;
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");

        var sns = await Get(host.Client, $"/api/v1/fleet/components/{aws}/sns");
        sns.GetProperty("entities").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("parent").GetString().Should().Be("fanout");

        await Get(host.Client, $"/api/v1/fleet/components/{aws}/nope", HttpStatusCode.NotFound);
        await Get(host.Client, $"/api/v1/fleet/components/{Guid.NewGuid()}/sqs", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_restricted_caller_neither_sees_nor_counts_a_namespace_outside_its_allow_list()
    {
        var (host, _) = Host();
        using var _h = host;
        var azure = await DeadLettersApiTests.Connect(host.Client, "azure");
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");

        using var scope = host.Services.CreateScope();
        var owner = (await scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>().Namespaces.FindAsync(azure))!.OwnerId;
        var service = scope.ServiceProvider.GetRequiredService<IComponentOverviewService>();
        var allowed = new HashSet<Guid> { azure };

        (await service.GetAsync(owner, allowed, null, default)).Components.Should().OnlyContain(c => c.NamespaceId == azure);
        (await service.GetOneAsync(owner, allowed, aws, "sqs", default)).Should().BeNull();
    }
}
