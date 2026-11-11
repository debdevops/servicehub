using FluentAssertions;
using Microsoft.AspNetCore.Http;
using ServiceHub.Api.Middleware;

namespace ServiceHub.UnitTests.Api;

/// <summary>
/// ServiceHub does not log the browser in, so a request that changes something must not be one another website made the browser send.
/// Found in the 5.0.0 security review: four endpoints that take no body and no intent header (pause an agent, cancel a bulk run, test a
/// connection, check a component) were reachable that way.
/// </summary>
public sealed class CrossSiteRequestTests
{
    [Theory]
    // The browser's own label, when it sends one.
    [InlineData("POST", "same-origin", null, false)]
    [InlineData("POST", "none", null, false)]
    [InlineData("POST", "cross-site", null, true)]
    [InlineData("POST", "same-site", null, true)]          // evil.example.com beside servicehub.example.com, or another port on localhost
    [InlineData("DELETE", "cross-site", null, true)]
    [InlineData("PUT", "CROSS-SITE", null, true)]
    [InlineData("PATCH", "cross-site", null, true)]
    // The label wins over Origin: a dev proxy changes the port, never the label.
    [InlineData("POST", "same-origin", "http://localhost:3000", false)]
    // An older browser: Origin only.
    [InlineData("POST", null, "http://localhost:5153", false)]
    [InlineData("POST", null, "http://LOCALHOST:3000", false)]
    [InlineData("POST", null, "https://evil.example", true)]
    [InlineData("POST", null, "null", true)]               // a sandboxed frame or a file: page
    [InlineData("POST", "", "https://evil.example", true)]
    // Not a web page at all: curl, a script, the MCP tool.
    [InlineData("POST", null, null, false)]
    [InlineData("POST", "", "", false)]
    // A read changes nothing and its answer cannot be seen cross-site.
    [InlineData("GET", "cross-site", "https://evil.example", false)]
    [InlineData("HEAD", "cross-site", null, false)]
    [InlineData("OPTIONS", "cross-site", null, false)]
    public void A_changing_request_is_refused_only_when_a_browser_says_another_site_sent_it(string method, string? fetchSite, string? origin, bool refused) =>
        CrossSiteRequestMiddleware.IsCrossSite(method, fetchSite, origin, "localhost").Should().Be(refused);

    [Fact]
    public void An_ipv6_loopback_origin_matches_its_host() =>
        CrossSiteRequestMiddleware.IsCrossSite("POST", null, "http://[::1]:5153", "[::1]").Should().BeFalse();

    [Fact]
    public async Task A_refused_request_gets_403_with_a_code_and_never_reaches_the_endpoint()
    {
        var reached = false;
        var middleware = new CrossSiteRequestMiddleware(_ => { reached = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext { RequestServices = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(new Microsoft.Extensions.DependencyInjection.ServiceCollection())) };
        context.Request.Method = "POST";
        context.Request.Host = new HostString("localhost", 5153);
        context.Request.Path = "/api/v1/agents/auto-replay/pause";
        context.Request.Headers["Sec-Fetch-Site"] = "cross-site";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        reached.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.Body.Position = 0;
        (await new StreamReader(context.Response.Body).ReadToEndAsync()).Should().Contain("cross_site_request");
    }

    [Fact]
    public async Task ServiceHubs_own_page_goes_through()
    {
        var reached = false;
        var middleware = new CrossSiteRequestMiddleware(_ => { reached = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Host = new HostString("localhost", 5153);
        context.Request.Headers["Sec-Fetch-Site"] = "same-origin";

        await middleware.InvokeAsync(context);

        reached.Should().BeTrue();
    }
}
