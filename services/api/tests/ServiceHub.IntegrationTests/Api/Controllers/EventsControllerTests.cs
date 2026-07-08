using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Events;
using ServiceHub.Core.Interfaces;
using ServiceHub.IntegrationTests.Infrastructure;

namespace ServiceHub.IntegrationTests.Api.Controllers;

public sealed class EventsControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly TimeSpan StreamReadTimeout = TimeSpan.FromSeconds(15);

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public EventsControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Stream_ShouldRespondWithEventStreamContentType()
    {
        using var cts = new CancellationTokenSource(StreamReadTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/events/stream");

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
        response.Headers.CacheControl?.NoCache.Should().BeTrue();
    }

    [Fact]
    public async Task Stream_PublishedEvent_IsDeliveredAsDataFrame_AndUnstreamedTypesAreFiltered()
    {
        using var cts = new CancellationTokenSource(StreamReadTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/events/stream");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        // First frame is the ": connected" comment — the subscription is registered
        // before it is written, so publishing after this point cannot race Register.
        var hello = await reader.ReadLineAsync(cts.Token);
        hello.Should().Be(": connected");

        var namespaceId = Guid.NewGuid();
        var bus = _factory.Services.GetRequiredService<IPlatformEventBus>();

        // Unstreamed event type published first: if the filter leaked it, this frame
        // would arrive before the spike event below.
        await bus.PublishAsync(new PlatformEvent
        {
            Source = "IntegrationTest",
            Category = "test",
            EventType = "servicehub.test.unstreamed.v1",
            Severity = EventSeverity.Info,
            Actor = Namespace.SpaOwnerId,
        }, cts.Token);

        await bus.PublishAsync(new PlatformEvent
        {
            Source = "IntegrationTest",
            Category = EventCategories.Dlq,
            EventType = EventTypes.DlqSpikeDetected,
            Severity = EventSeverity.Warning,
            NamespaceId = namespaceId,
            NamespaceName = "integration-test-ns",
            Actor = Namespace.SpaOwnerId,
        }, cts.Token);

        var dataLine = await ReadNextDataLineAsync(reader, cts.Token);

        dataLine.Should().NotBeNull("the published DlqSpikeDetected event should reach the stream");
        using var frame = JsonDocument.Parse(dataLine!["data: ".Length..]);
        frame.RootElement.GetProperty("eventType").GetString().Should().Be(EventTypes.DlqSpikeDetected);
        frame.RootElement.GetProperty("namespaceId").GetGuid().Should().Be(namespaceId);
        frame.RootElement.GetProperty("severity").GetString().Should().Be("warning");
        frame.RootElement.TryGetProperty("payload", out _).Should().BeFalse("raw payloads must not leave the server");
        frame.RootElement.TryGetProperty("actor", out _).Should().BeFalse("actor identity must not leave the server");
    }

    private static async Task<string?> ReadNextDataLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
                return null;
            if (line.StartsWith("data: ", StringComparison.Ordinal))
                return line;
        }

        return null;
    }
}
