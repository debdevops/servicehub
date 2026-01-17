using System.Net;
using FluentAssertions;
using ServiceHub.IntegrationTests.Infrastructure;

namespace ServiceHub.IntegrationTests.Api.Middleware;

public sealed class SecurityHeadersTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SecurityHeadersTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Request_ShouldIncludeSecurityHeaders()
    {
        var response = await _client.GetAsync("/health");

        response.Headers.Should().ContainKey("X-Content-Type-Options");
        response.Headers.Should().ContainKey("X-Frame-Options");
        response.Headers.Should().ContainKey("Referrer-Policy");
    }

    [Fact]
    public async Task Request_ShouldHaveCorrectXContentTypeOptions()
    {
        var response = await _client.GetAsync("/health");

        var header = response.Headers.GetValues("X-Content-Type-Options").FirstOrDefault();
        header.Should().Be("nosniff");
    }

    [Fact]
    public async Task Request_ShouldHaveCorrectXFrameOptions()
    {
        var response = await _client.GetAsync("/health");

        var header = response.Headers.GetValues("X-Frame-Options").FirstOrDefault();
        header.Should().Be("DENY");
    }

    [Fact]
    public async Task Request_ShouldIncludeCorrelationId()
    {
        var response = await _client.GetAsync("/health");

        response.Headers.Should().ContainKey("X-Correlation-ID");
        var correlationId = response.Headers.GetValues("X-Correlation-ID").FirstOrDefault();
        correlationId.Should().NotBeNullOrEmpty();
        Guid.TryParse(correlationId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Request_WithProvidedCorrelationId_ShouldReturnSameId()
    {
        var providedCorrelationId = Guid.NewGuid().ToString();
        _client.DefaultRequestHeaders.Add("X-Correlation-ID", providedCorrelationId);

        var response = await _client.GetAsync("/health");

        var returnedCorrelationId = response.Headers.GetValues("X-Correlation-ID").FirstOrDefault();
        returnedCorrelationId.Should().Be(providedCorrelationId);
    }
}
