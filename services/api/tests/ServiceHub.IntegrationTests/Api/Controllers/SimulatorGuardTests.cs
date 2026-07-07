using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceHub.IntegrationTests.Infrastructure;

namespace ServiceHub.IntegrationTests.Api.Controllers;

/// <summary>
/// Verifies that the Simulator management endpoints are unreachable when the app
/// is not running in the Simulator environment. <see cref="TestWebApplicationFactory"/>
/// boots the app under the Development environment.
/// </summary>
public sealed class SimulatorGuardTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SimulatorGuardTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetStatus_OutsideSimulatorEnvironment_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/v1/simulator/status");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reset_OutsideSimulatorEnvironment_ReturnsNotFound()
    {
        var response = await _client.PostAsync("/api/v1/simulator/reset", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InjectDlqFlood_OutsideSimulatorEnvironment_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/simulator/inject-dlq-flood",
            new { NamespaceId = Guid.NewGuid(), EntityName = "q", Count = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
