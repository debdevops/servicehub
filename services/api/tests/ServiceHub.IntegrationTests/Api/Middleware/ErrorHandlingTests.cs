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
    public async Task NotFoundEndpoint_ShouldReturn404WithProblemDetails()
    {
        var response = await _client.GetAsync("/api/v1/nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("status");
        content.Should().Contain("404");
    }

    [Fact]
    public async Task BadRequest_ShouldReturnProblemDetailsWithCorrelationId()
    {
        var invalidRequest = new { };

        var response = await _client.PostAsJsonAsync("/api/v1/namespaces", invalidRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("correlationId");
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
        root.TryGetProperty("correlationId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ErrorResponse_ShouldIncludeCorrelationIdInHeaders()
    {
        var response = await _client.GetAsync("/api/v1/namespaces/" + Guid.NewGuid());

        response.Headers.Should().ContainKey("X-Correlation-ID");
        var headerCorrelationId = response.Headers.GetValues("X-Correlation-ID").FirstOrDefault();
        headerCorrelationId.Should().NotBeNullOrEmpty();
    }
}
