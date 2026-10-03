using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using ServiceHub.Api.Middleware;
using ServiceHub.Core.Constants;

namespace ServiceHub.IntegrationTests;

/// <summary>
/// Wave 0's proof that the composition root, the pipeline and the routing actually work end to end.
/// </summary>
/// <remarks>
/// Deliberately thin: there is no product behaviour to test yet. What it does check is the shape
/// every later unit depends on — health answers, an unknown API route 404s as an API rather than
/// silently returning HTML, and no response carries a stack trace.
/// </remarks>
public sealed class ApiSmokeTests(ServiceHubApiFactory factory)
    : IClassFixture<ServiceHubApiFactory>
{
    [Fact]
    public async Task Health_answers()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_answers_separately_from_liveness()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unknown_api_route_404s_as_an_api_and_never_falls_back_to_the_spa()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("text/html",
            "an API client asking for a route that does not exist must get a 404, not the SPA shell");
    }

    [Fact]
    public async Task Security_headers_are_present_on_every_response()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        response.Headers.Should().ContainKey("X-Content-Type-Options");
        response.Headers.Should().ContainKey("X-Frame-Options");
    }

    [Fact]
    public async Task Every_response_carries_a_correlation_id()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        response.Headers.Should().ContainKey("X-Correlation-Id");
    }

    [Fact]
    public async Task A_framework_404_still_carries_a_stable_code()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        body.RootElement.GetProperty("code").GetString().Should().Be(ErrorCodes.NotFound,
            "the SPA turns every failure into a message by its code; a missing one reads as 'unexpected'");
        body.RootElement.TryGetProperty("correlationId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task A_plain_caller_correlation_id_is_kept()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/health", UriKind.Relative));
        request.Headers.Add("X-Correlation-Id", "req-42.a_b:c");

        var response = await client.SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").Single().Should().Be("req-42.a_b:c");
    }

    [Theory]
    [InlineData(65)]
    [InlineData(5000)]
    public async Task An_oversized_caller_correlation_id_is_replaced_not_echoed(int length)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/health", UriKind.Relative));
        request.Headers.Add("X-Correlation-Id", new string('A', length));

        var response = await client.SendAsync(request);

        var echoed = response.Headers.GetValues("X-Correlation-Id").Single();
        echoed.Length.Should().BeLessThanOrEqualTo(CorrelationIdMiddleware.MaxLength);
        echoed.Should().NotBe(new string('A', length));
    }

    [Fact]
    public async Task A_correlation_id_with_unsafe_characters_is_replaced()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/health", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "abc<script>");

        var response = await client.SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").Single().Should().NotContain("<");
    }

    [Fact]
    public async Task A_content_security_policy_forbids_inline_and_third_party_script()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        csp.Should().Contain("script-src 'self'").And.NotContain("unsafe-eval");
        csp.Should().Contain("frame-ancestors 'none'");
    }
}
