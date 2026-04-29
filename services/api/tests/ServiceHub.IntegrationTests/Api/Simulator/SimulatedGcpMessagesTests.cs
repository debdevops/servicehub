using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using ServiceHub.IntegrationTests.Infrastructure;
using ServiceHub.Simulator;

namespace ServiceHub.IntegrationTests.Api.Simulator;

/// <summary>
/// Integration tests verifying that simulated GCP Pub/Sub entities are reachable
/// through the standard endpoints and that GCP-specific ack-deadline status
/// is available via the CloudBridge endpoint.
/// </summary>
[Trait("Category", "Simulator")]
public sealed class SimulatedGcpMessagesTests : IClassFixture<SimulatorWebApplicationFactory>
{
    private readonly SimulatorWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SimulatedGcpMessagesTests(SimulatorWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PeekMessages_GcpFulfillmentSub_ReturnsActiveMessages()
    {
        _factory.ResetSimulator();
        var nsId = SimulatorDataSeeder.GcpNamespaceId;

        var response = await _client.GetAsync(
            $"/api/v1/messages/queue/fulfillment-sub?namespaceId={nsId}&maxMessages=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var messages = JsonDocument.Parse(json).RootElement;
        messages.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PeekDlq_GcpFulfillmentSub_HasGoogDeliveryAttemptMessage()
    {
        _factory.ResetSimulator();
        var nsId = SimulatorDataSeeder.GcpNamespaceId;

        var response = await _client.GetAsync(
            $"/api/v1/messages/queue/fulfillment-sub/deadletter?namespaceId={nsId}&maxMessages=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var messages = JsonDocument.Parse(json).RootElement;
        messages.GetArrayLength().Should().BeGreaterThan(0);

        // GCP DLQ should have googclient_deliveryattempt property
        var hasDeliveryAttempt = messages.EnumerateArray()
            .Any(m =>
                m.TryGetProperty("applicationProperties", out var props) &&
                props.TryGetProperty("googclient_deliveryattempt", out _));
        hasDeliveryAttempt.Should().BeTrue(
            "GCP fulfillment-sub DLQ should have messages with googclient_deliveryattempt");
    }

    [Fact]
    public async Task GetAckDeadlineStatus_GcpFulfillmentSub_ReturnsAckDeadlineInfo()
    {
        _factory.ResetSimulator();
        var nsId = SimulatorDataSeeder.GcpNamespaceId;

        var response = await _client.GetAsync(
            $"/api/v1/cloud-bridge/namespaces/{nsId}/visibility/fulfillment-sub?provider=Gcp");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("ackDeadlineSeconds", out _).Should().BeTrue();
        doc.RootElement.GetProperty("hasDeadLetterPolicy").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetAckDeadlineStatus_AnalyticsSub_HasOrderingEnabled()
    {
        _factory.ResetSimulator();
        var nsId = SimulatorDataSeeder.GcpNamespaceId;

        var response = await _client.GetAsync(
            $"/api/v1/cloud-bridge/namespaces/{nsId}/visibility/analytics-sub?provider=Gcp");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("messageOrderingEnabled").GetBoolean().Should().BeTrue();
    }
}
