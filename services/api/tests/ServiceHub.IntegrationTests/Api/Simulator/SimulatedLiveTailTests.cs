using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using ServiceHub.IntegrationTests.Infrastructure;
using ServiceHub.Simulator;

namespace ServiceHub.IntegrationTests.Api.Simulator;

/// <summary>
/// Integration tests for <c>GET /api/v1/messages/live-tail</c> against the Simulator's
/// seeded Azure/AWS/GCP entities — verifies the SSE stream opens, honours the
/// <c>SupportsRepeatablePeek</c> capability gate (AWS is rejected), and surfaces newly
/// sent messages as data frames.
/// </summary>
[Trait("Category", "Simulator")]
public sealed class SimulatedLiveTailTests : IClassFixture<SimulatorWebApplicationFactory>
{
    private static readonly TimeSpan StreamReadTimeout = TimeSpan.FromSeconds(20);

    private readonly SimulatorWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SimulatedLiveTailTests(SimulatorWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task LiveTail_MissingEntityName_ReturnsBadRequest()
    {
        _factory.ResetSimulator();
        var nsId = SimulatorDataSeeder.AzureNamespaceId;

        var response = await _client.GetAsync($"/api/v1/messages/live-tail?namespaceId={nsId}&entityName=");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LiveTail_UnknownNamespace_ReturnsNotFound()
    {
        _factory.ResetSimulator();

        var response = await _client.GetAsync(
            $"/api/v1/messages/live-tail?namespaceId={Guid.NewGuid()}&entityName=orders");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LiveTail_AwsNamespace_ReturnsConflict_BecauseSqsHasNoRepeatablePeek()
    {
        _factory.ResetSimulator();
        var nsId = SimulatorDataSeeder.AwsNamespaceId;

        var response = await _client.GetAsync(
            $"/api/v1/messages/live-tail?namespaceId={nsId}&entityName=checkout-queue");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task LiveTail_AzureQueue_OpensStreamAndReturnsConnectedFrame()
    {
        _factory.ResetSimulator();
        var nsId = SimulatorDataSeeder.AzureNamespaceId;

        using var cts = new CancellationTokenSource(StreamReadTimeout);
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/messages/live-tail?namespaceId={nsId}&entityName=orders");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var hello = await reader.ReadLineAsync(cts.Token);

        hello.Should().Be(": connected");
    }

    [Fact]
    public async Task LiveTail_GcpSubscription_OpensStreamAndReturnsConnectedFrame()
    {
        _factory.ResetSimulator();
        var nsId = SimulatorDataSeeder.GcpNamespaceId;

        using var cts = new CancellationTokenSource(StreamReadTimeout);
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/messages/live-tail?namespaceId={nsId}&entityName=fulfillment-sub");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var hello = await reader.ReadLineAsync(cts.Token);

        hello.Should().Be(": connected");
    }

    [Fact]
    public async Task LiveTail_AzureQueue_NewlySentMessage_AppearsAsDataFrame()
    {
        _factory.ResetSimulator();
        var nsId = SimulatorDataSeeder.AzureNamespaceId;

        using var cts = new CancellationTokenSource(StreamReadTimeout);
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/messages/live-tail?namespaceId={nsId}&entityName=orders");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        // First frame is the ": connected" comment — the session's first poll (which seeds
        // its "seen" set without emitting) happens after this, so a send issued now cannot
        // race the seed poll into being silently absorbed as pre-existing backlog.
        var hello = await reader.ReadLineAsync(cts.Token);
        hello.Should().Be(": connected");

        using var sendRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/namespaces/{nsId}/queues/orders/messages")
        {
            Content = JsonContent.Create(new
            {
                body = "{\"eventType\":\"live-tail-test\"}",
                contentType = "application/json",
            }),
        };
        sendRequest.Headers.Add("X-ServiceHub-Intent", "messages:send");
        sendRequest.Headers.Add("X-ServiceHub-Confirm", "true");
        var sendResponse = await _client.SendAsync(sendRequest, cts.Token);
        sendResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var dataLine = await ReadNextDataLineAsync(reader, cts.Token);

        dataLine.Should().NotBeNull("the newly sent message should appear on the live tail stream");
        using var frame = JsonDocument.Parse(dataLine!["data: ".Length..]);
        frame.RootElement.GetProperty("body").GetString().Should().Contain("live-tail-test");
    }

    private static async Task<string?> ReadNextDataLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return null;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                return line;
            }
        }

        return null;
    }
}
