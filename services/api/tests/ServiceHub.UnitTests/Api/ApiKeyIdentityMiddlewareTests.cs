using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Api.Middleware;
using ServiceHub.Api.Security;

namespace ServiceHub.UnitTests.Api;

/// <summary>The API-key identity source: it names callers, and never quietly downgrades a wrong key.</summary>
public sealed class ApiKeyIdentityMiddlewareTests
{
    private static (ApiKeyIdentityMiddleware Middleware, Func<bool> NextCalled) Build(params (string Key, string? Name)[] keys)
    {
        var settings = new Dictionary<string, string?>();
        for (var i = 0; i < keys.Length; i++)
        {
            settings[$"Security:Authentication:ApiKeys:{i}:Key"] = keys[i].Key;
            if (keys[i].Name is not null)
            {
                settings[$"Security:Authentication:ApiKeys:{i}:Description"] = keys[i].Name;
            }
        }

        var called = false;
        var middleware = new ApiKeyIdentityMiddleware(
            _ => { called = true; return Task.CompletedTask; },
            NullLogger<ApiKeyIdentityMiddleware>.Instance,
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return (middleware, () => called);
    }

    private static DefaultHttpContext Request(string? key = null)
    {
        var context = new DefaultHttpContext
        {
            // Results.Problem serialises through the request's services.
            RequestServices = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider(),
        };
        context.Response.Body = new MemoryStream();
        if (key is not null)
        {
            context.Request.Headers[ApiKeyIdentityMiddleware.HeaderName] = key;
        }

        return context;
    }

    [Fact]
    public async Task A_configured_key_names_the_caller()
    {
        var (middleware, nextCalled) = Build(("real-key-value", "ops-bot"));
        var context = Request("real-key-value");

        await middleware.InvokeAsync(context);

        nextCalled().Should().BeTrue();
        context.Items["ApiKeyName"].Should().Be("ops-bot");
        context.Items["AuthMethod"].Should().Be("ApiKey");
        ActorContextFactory.From(context).ApiKeyName.Should().Be("ops-bot");
    }

    [Fact]
    public async Task A_key_that_is_not_configured_is_refused_never_treated_as_anonymous()
    {
        var (middleware, nextCalled) = Build(("real-key-value", "ops-bot"));
        var context = Request("guessed-key");

        await middleware.InvokeAsync(context);

        nextCalled().Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        context.Items.Should().NotContainKey("ApiKeyName");
    }

    [Fact]
    public async Task No_header_means_a_browser_session_as_before()
    {
        var (middleware, nextCalled) = Build(("real-key-value", "ops-bot"));
        var context = Request();

        await middleware.InvokeAsync(context);

        nextCalled().Should().BeTrue();
        context.Items.Should().NotContainKey("ApiKeyName");
    }

    [Fact]
    public async Task With_no_keys_configured_a_stray_header_changes_nothing()
    {
        var (middleware, nextCalled) = Build();
        var context = Request("whatever");

        await middleware.InvokeAsync(context);

        nextCalled().Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Placeholder_keys_are_ignored_so_a_template_value_cannot_authenticate()
    {
        var (middleware, nextCalled) = Build(("CHANGE_THIS_IN_PRODUCTION", "template"));
        var context = Request("CHANGE_THIS_IN_PRODUCTION");

        await middleware.InvokeAsync(context);

        nextCalled().Should().BeTrue();
        context.Items.Should().NotContainKey("ApiKeyName");
    }

    [Fact]
    public async Task A_key_with_no_description_is_still_named_something_honest()
    {
        var (middleware, _) = Build(("real-key-value", null));
        var context = Request("real-key-value");

        await middleware.InvokeAsync(context);

        context.Items["ApiKeyName"].Should().Be("unnamed key");
    }

    [Fact]
    public async Task An_identity_another_source_already_established_is_never_overwritten()
    {
        var (middleware, nextCalled) = Build(("real-key-value", "ops-bot"));
        var context = Request("real-key-value");
        context.Items["OwnerId"] = "oidc:alice";

        await middleware.InvokeAsync(context);

        nextCalled().Should().BeTrue();
        context.Items.Should().NotContainKey("ApiKeyName");
    }

    [Theory]
    [InlineData("3f2b9c1e-7a64-4d0a", true)]
    [InlineData("short", false)]
    [InlineData("has spaces in it here", false)]
    [InlineData("<script>alert(1)</script>", false)]
    public void A_session_id_is_accepted_only_when_it_looks_like_one(string header, bool accepted)
    {
        var context = Request();
        context.Request.Headers[ActorContextFactory.SessionHeaderName] = header;

        ActorContextFactory.From(context).SessionId.Should().Be(accepted ? header : null);
    }
}
