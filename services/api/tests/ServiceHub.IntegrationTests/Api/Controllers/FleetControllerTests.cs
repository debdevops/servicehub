using System.Net;
using System.Text.Json;
using FluentAssertions;
using ServiceHub.IntegrationTests.Infrastructure;

namespace ServiceHub.IntegrationTests.Api.Controllers;

/// <summary>
/// Integration tests for the cross-namespace fleet operations overview endpoint.
/// </summary>
public sealed class FleetControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public FleetControllerTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetOverview_ReturnsOk_WithWellFormedShape()
    {
        var response = await _client.GetAsync("/api/v1/fleet/overview");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        root.GetProperty("windowHours").GetInt32().Should().Be(24);
        root.GetProperty("namespaces").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("dailyTrend").GetArrayLength().Should().Be(7);
        root.TryGetProperty("totalActive", out _).Should().BeTrue();
        root.TryGetProperty("topCategories", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(168)]
    public async Task GetOverview_ClampsAndEchoesWindowHours(int windowHours)
    {
        var response = await _client.GetAsync($"/api/v1/fleet/overview?windowHours={windowHours}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("windowHours").GetInt32().Should().Be(windowHours);
    }

    [Fact]
    public async Task GetOverview_OutOfRangeWindow_IsClamped()
    {
        var response = await _client.GetAsync("/api/v1/fleet/overview?windowHours=99999");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("windowHours").GetInt32().Should().Be(720);
    }
}
