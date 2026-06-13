using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using ServiceHub.IntegrationTests.Infrastructure;
using ServiceHub.Simulator;

namespace ServiceHub.IntegrationTests.Api.Simulator;

/// <summary>
/// Integration tests verifying that simulated AWS SQS entities are reachable
/// through the standard endpoints and that SQS-specific visibility-window
/// status is available via the CloudBridge endpoint.
/// </summary>
[Trait("Category", "Simulator")]
public sealed class SimulatedAwsMessagesTests : IClassFixture<SimulatorWebApplicationFactory>
{
    private readonly SimulatorWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SimulatedAwsMessagesTests(SimulatorWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PeekMessages_AwsCheckoutQueue_ReturnsActiveMessages()
    {
        _factory.ResetSimulator();
        var nsId = SimulatorDataSeeder.AwsNamespaceId;

        var response = await _client.GetAsync(
            $"/api/v1/messages/queue/checkout-queue?namespaceId={nsId}&maxMessages=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var messages = JsonDocument.Parse(json).RootElement;
        messages.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PeekDlq_AwsCheckoutQueue_HasMaxReceiveCountMessage()
    {
        _factory.ResetSimulator();
        var nsId = SimulatorDataSeeder.AwsNamespaceId;

        var response = await _client.GetAsync(
            $"/api/v1/messages/queue/checkout-queue/deadletter?namespaceId={nsId}&maxMessages=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var messages = JsonDocument.Parse(json).RootElement;
        messages.GetArrayLength().Should().BeGreaterThan(0);

        // AWS DLQ should have MaxReceiveCount reason
        var maxReceive = messages.EnumerateArray()
            .Any(m =>
                m.TryGetProperty("deadLetterReason", out var r) &&
                r.GetString()?.Contains("MaxReceiveCount") == true);
        maxReceive.Should().BeTrue("AWS checkout-queue DLQ should contain MaxReceiveCount messages");
    }

    [Fact]
    public async Task KmsError_Fault_CausesCheckoutQueuePeekToFail()
    {
        _factory.ResetSimulator();
        var nsId = SimulatorDataSeeder.AwsNamespaceId;

        var faultPayload = new
        {
            faultType = "KmsError",
            namespaceId = nsId,
            targetEntity = "checkout-queue",
            durationSeconds = 60,
        };
        await _client.PostAsJsonAsync("/api/v1/simulator/faults", faultPayload);

        var response = await _client.GetAsync(
            $"/api/v1/messages/queue/checkout-queue?namespaceId={nsId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task GetVisibilityStatus_AwsCheckoutQueue_ReturnsVisibilityInfo()
    {
        _factory.ResetSimulator();
        var nsId = SimulatorDataSeeder.AwsNamespaceId;

        var response = await _client.GetAsync(
            $"/api/v1/cloud-bridge/namespaces/{nsId}/visibility/checkout-queue?provider=Aws");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("visibilityTimeoutSeconds", out _).Should().BeTrue();
    }
}
