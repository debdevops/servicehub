using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Api.Middleware;
using ServiceHub.Shared.Helpers;

namespace ServiceHub.UnitTests.Api.Middleware;

public sealed class CorrelationIdMiddlewareTests
{
    private static DefaultHttpContext CreateContext(string? correlationHeader = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        if (correlationHeader is not null)
        {
            context.Request.Headers[CorrelationIdGenerator.DefaultHeaderName] = correlationHeader;
        }

        return context;
    }

    private static CorrelationIdMiddleware CreateMiddleware(RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        return new CorrelationIdMiddleware(next, NullLogger<CorrelationIdMiddleware>.Instance);
    }

    // ── CorrelationId stored in Items ────────────────────────────────

    [Fact]
    public async Task InvokeAsync_NoHeader_StoresGeneratedIdInItems()
    {
        var context = CreateContext();
        var middleware = CreateMiddleware();

        await middleware.InvokeAsync(context);

        context.Items["CorrelationId"].Should().NotBeNull();
        var id = context.Items["CorrelationId"]!.ToString();
        CorrelationIdGenerator.IsValid(id).Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ValidHeader_ReusesHeaderValueInItems()
    {
        var existingId = CorrelationIdGenerator.Generate();
        var context = CreateContext(existingId);
        var middleware = CreateMiddleware();

        await middleware.InvokeAsync(context);

        context.Items["CorrelationId"].Should().Be(existingId);
    }

    [Fact]
    public async Task InvokeAsync_InvalidHeader_GeneratesNewIdInItems()
    {
        var context = CreateContext("bad-id!");
        var middleware = CreateMiddleware();

        await middleware.InvokeAsync(context);

        var id = context.Items["CorrelationId"]!.ToString();
        id.Should().NotBe("bad-id!");
        CorrelationIdGenerator.IsValid(id).Should().BeTrue();
    }

    // ── Next is called ───────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_CallsNextDelegate()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(CreateContext());

        nextCalled.Should().BeTrue();
    }

    // ── Correlation ID value consistency ────────────────────────────

    [Fact]
    public async Task InvokeAsync_EachRequest_GeneratesDifferentCorrelationId()
    {
        var ctx1 = CreateContext();
        var ctx2 = CreateContext();
        var middleware = CreateMiddleware();

        await middleware.InvokeAsync(ctx1);
        await middleware.InvokeAsync(ctx2);

        var id1 = ctx1.Items["CorrelationId"]!.ToString();
        var id2 = ctx2.Items["CorrelationId"]!.ToString();
        id1.Should().NotBe(id2);
    }

    [Fact]
    public async Task InvokeAsync_ValidHeader_StoredIdMatchesHeader()
    {
        var correlationId = CorrelationIdGenerator.Generate();
        var context = CreateContext(correlationId);
        var middleware = CreateMiddleware();

        await middleware.InvokeAsync(context);

        context.Items["CorrelationId"]!.ToString().Should().Be(correlationId);
    }
}
