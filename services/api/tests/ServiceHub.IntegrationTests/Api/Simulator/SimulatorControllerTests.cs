using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using ServiceHub.IntegrationTests.Infrastructure;
using ServiceHub.Simulator;

namespace ServiceHub.IntegrationTests.Api.Simulator;

/// <summary>
/// Integration tests that verify the SimulatorController endpoints.
/// </summary>
[Trait("Category", "Simulator")]
public sealed class SimulatorControllerTests : IClassFixture<SimulatorWebApplicationFactory>
{
    private readonly SimulatorWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SimulatorControllerTests(SimulatorWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetStatus_ReturnsOk_WithAllThreeNamespaces()
    {
        _factory.ResetSimulator();

        var response = await _client.GetAsync("/api/v1/simulator/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("environment").GetString().Should().Be("Simulator");
        var namespaces = doc.RootElement.GetProperty("namespaces");
        namespaces.GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task GetStatus_ActiveMessageCounts_AreNonZero()
    {
        _factory.ResetSimulator();

        var response = await _client.GetAsync("/api/v1/simulator/status");
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var namespaces = doc.RootElement.GetProperty("namespaces");
        foreach (var ns in namespaces.EnumerateArray())
        {
            var activeCount = ns.GetProperty("activeMessageCount").GetInt64();
            activeCount.Should().BeGreaterThan(0,
                because: $"namespace {ns.GetProperty("name").GetString()} should have active messages");
        }
    }

    [Fact]
    public async Task InjectFault_ThenGetStatus_ShowsFaultInResponse()
    {
        _factory.ResetSimulator();

        var payload = new
        {
            faultType = "NetworkTimeout",
            namespaceId = SimulatorDataSeeder.AzureNamespaceId,
            targetEntity = "orders",
            severity = 1,
            durationSeconds = 60,
        };

        var injectResponse = await _client.PostAsJsonAsync("/api/v1/simulator/faults", payload);
        injectResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var statusResponse = await _client.GetAsync("/api/v1/simulator/status");
        var json = await statusResponse.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("activeFaultCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task InjectFault_InvalidType_ReturnsBadRequest()
    {
        var payload = new
        {
            faultType = "NotAValidFaultType",
            namespaceId = SimulatorDataSeeder.AzureNamespaceId,
            durationSeconds = 30,
        };

        var response = await _client.PostAsJsonAsync("/api/v1/simulator/faults", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ClearFaults_RemovesAllActiveFaults()
    {
        _factory.ResetSimulator();

        // Inject a fault first
        var payload = new
        {
            faultType = "KmsError",
            namespaceId = SimulatorDataSeeder.AwsNamespaceId,
            durationSeconds = 60,
        };
        await _client.PostAsJsonAsync("/api/v1/simulator/faults", payload);

        var clearResponse = await _client.DeleteAsync("/api/v1/simulator/faults");
        clearResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var statusResponse = await _client.GetAsync("/api/v1/simulator/status");
        var json = await statusResponse.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("activeFaultCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Reset_RestoresDefaultMessageCounts()
    {
        _factory.ResetSimulator();

        var resetResponse = await _client.PostAsync("/api/v1/simulator/reset", null);
        resetResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var statusResponse = await _client.GetAsync("/api/v1/simulator/status");
        var json = await statusResponse.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        // After reset, active messages should be back to seeded values
        var totalActive = doc.RootElement.GetProperty("namespaces")
            .EnumerateArray()
            .Sum(ns => ns.GetProperty("activeMessageCount").GetInt64());
        totalActive.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AdvanceTime_ByPositiveSeconds_ReturnsNewTime()
    {
        _factory.ResetSimulator();

        var payload = new { seconds = 300 };
        var response = await _client.PostAsJsonAsync("/api/v1/simulator/advance-time", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("newUtcNow", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AdvanceTime_ByZeroSeconds_ReturnsBadRequest()
    {
        var payload = new { seconds = 0 };
        var response = await _client.PostAsJsonAsync("/api/v1/simulator/advance-time", payload);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InjectDlqFlood_AddsMessagesToEntityDlq()
    {
        _factory.ResetSimulator();

        var payload = new
        {
            namespaceId = SimulatorDataSeeder.AzureNamespaceId,
            entityName = "orders",
            count = 5,
            reason = "MaxDeliveryCountExceeded",
        };

        var response = await _client.PostAsJsonAsync("/api/v1/simulator/inject-dlq-flood", payload);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task InjectDlqFlood_UnknownEntity_Returns404()
    {
        var payload = new
        {
            namespaceId = SimulatorDataSeeder.AzureNamespaceId,
            entityName = "no-such-queue",
            count = 5,
        };

        var response = await _client.PostAsJsonAsync("/api/v1/simulator/inject-dlq-flood", payload);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
