using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using ServiceHub.IntegrationTests.Infrastructure;

namespace ServiceHub.IntegrationTests.Api.Middleware;

public sealed class ErrorHandlingTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ErrorHandlingTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task NotFoundEndpoint_ShouldReturn404()
    {
        var response = await _client.GetAsync("/api/v1/nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BadRequest_ShouldReturnValidationErrors()
    {
        var invalidRequest = new { };

        var response = await _client.PostAsJsonAsync("/api/v1/namespaces", invalidRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("errors");
    }

    [Fact]
    public async Task ErrorResponse_ShouldHaveStandardProblemDetailsFormat()
    {
        var response = await _client.GetAsync("/api/v1/namespaces/" + Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        
        var problemDetails = await response.Content.ReadFromJsonAsync<JsonDocument>();
        problemDetails.Should().NotBeNull();
        
        var root = problemDetails!.RootElement;
        root.TryGetProperty("type", out _).Should().BeTrue();
        root.TryGetProperty("title", out _).Should().BeTrue();
        root.TryGetProperty("status", out _).Should().BeTrue();
        root.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ErrorResponse_ShouldIncludeCorrelationIdInHeaders()
    {
        var response = await _client.GetAsync("/api/v1/namespaces/" + Guid.NewGuid());

        response.Headers.Should().ContainKey("X-Correlation-Id");
        var headerCorrelationId = response.Headers.GetValues("X-Correlation-Id").FirstOrDefault();
        headerCorrelationId.Should().NotBeNullOrEmpty();
    }
}
