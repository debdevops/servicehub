using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using ServiceHub.IntegrationTests.Infrastructure;
using ServiceHub.Simulator;

namespace ServiceHub.IntegrationTests.Api.Simulator;

/// <summary>
/// Integration tests verifying that simulated Azure Service Bus entities
/// are reachable through the standard MessagesController endpoints.
/// </summary>
[Trait("Category", "Simulator")]
public sealed class SimulatedAzureMessagesTests : IClassFixture<SimulatorWebApplicationFactory>
{
    private readonly SimulatorWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SimulatedAzureMessagesTests(SimulatorWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PeekMessages_AzureOrdersQueue_ReturnsActiveMessages()
    {
        _factory.ResetSimulator();
        var nsId = SimulatorDataSeeder.AzureNamespaceId;

        var response = await _client.GetAsync(
            $"/api/v1/messages/queue/orders?namespaceId={nsId}&maxMessages=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var messages = JsonDocument.Parse(json).RootElement;
        messages.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PeekDeadLetterMessages_AzureOrdersQueue_ReturnsDlqMessages()
    {
        _factory.ResetSimulator();
        var nsId = SimulatorDataSeeder.AzureNamespaceId;

        var response = await _client.GetAsync(
            $"/api/v1/messages/queue/orders/deadletter?namespaceId={nsId}&maxMessages=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var messages = JsonDocument.Parse(json).RootElement;
        messages.GetArrayLength().Should().BeGreaterThan(0);

        // All returned messages should be from DLQ
        foreach (var msg in messages.EnumerateArray())
        {
            msg.GetProperty("isFromDeadLetter").GetBoolean().Should().BeTrue();
        }
    }

    [Fact]
    public async Task DlqMessages_ContainMaxDeliveryCountPattern_ForClassification()
    {
        _factory.ResetSimulator();
        var nsId = SimulatorDataSeeder.AzureNamespaceId;

        var response = await _client.GetAsync(
            $"/api/v1/messages/queue/orders/deadletter?namespaceId={nsId}&maxMessages=20");

        var json = await response.Content.ReadAsStringAsync();
        var messages = JsonDocument.Parse(json).RootElement;

        var maxDelivery = messages.EnumerateArray()
            .FirstOrDefault(m =>
                m.TryGetProperty("deadLetterReason", out var r) &&
                r.GetString()?.Contains("MaxDeliveryCountExceeded") == true);

        maxDelivery.ValueKind.Should().Be(JsonValueKind.Object,
            because: "orders queue should have a MaxDeliveryCountExceeded DLQ message");
    }

    [Fact]
    public async Task NetworkTimeout_Fault_CausesPeekToFail()
    {
        _factory.ResetSimulator();
        var nsId = SimulatorDataSeeder.AzureNamespaceId;

        // Inject NetworkTimeout fault on orders
        var faultPayload = new
        {
            faultType = "NetworkTimeout",
            namespaceId = nsId,
            targetEntity = "orders",
            durationSeconds = 60,
        };
        await _client.PostAsJsonAsync("/api/v1/simulator/faults", faultPayload);

        // Peek should now fail with 502
        var response = await _client.GetAsync(
            $"/api/v1/messages/queue/orders?namespaceId={nsId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }
}
