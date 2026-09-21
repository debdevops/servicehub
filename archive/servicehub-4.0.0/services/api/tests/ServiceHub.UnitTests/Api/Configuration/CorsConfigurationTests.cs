using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Api.Configuration;

namespace ServiceHub.UnitTests.Api.Configuration;

public sealed class CorsConfigurationTests
{
    [Fact]
    public void AddCorsConfiguration_WithAllowedOrigins_AddsService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "https://example.com",
                ["Cors:AllowedOrigins:1"] = "https://other.com",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddCorsConfiguration(config);

        var sp = services.BuildServiceProvider();
        var corsOptions = sp.GetService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>>();

        corsOptions.Should().NotBeNull();
    }

    [Fact]
    public void AddCorsConfiguration_NoOrigins_UsesDevDefaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:DevelopmentDefaults:0"] = "http://localhost:3000",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddCorsConfiguration(config);

        var sp = services.BuildServiceProvider();
        var corsOptions = sp.GetService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>>();

        corsOptions.Should().NotBeNull();
        var policy = corsOptions!.Value.GetPolicy("DevelopmentPolicy");
        policy.Should().NotBeNull();
    }

    [Fact]
    public void AddCorsConfiguration_NoOriginsConfigured_ServiceHubPolicyHasNoOrigins()
    {
        // SECURITY REGRESSION: the production/staging policy (ServiceHubPolicy, selected by
        // UseCorsConfiguration whenever the environment is not Development) must never fall back
        // to the "http://localhost:*" DevelopmentDefaults when an operator forgets to configure
        // Cors:AllowedOrigins. Falling back would let any page a victim's browser loads from its
        // own localhost obtain credentialed cross-origin access to a real production deployment.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:DevelopmentDefaults:0"] = "http://localhost:3000",
                ["Cors:DevelopmentDefaults:1"] = "http://localhost:5173",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddCorsConfiguration(config);

        var sp = services.BuildServiceProvider();
        var corsOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>>();

        var policy = corsOptions.Value.GetPolicy(CorsConfiguration.PolicyName);
        policy.Should().NotBeNull();
        policy!.Origins.Should().BeEmpty();
    }

    [Fact]
    public void AddCorsConfiguration_EmptyConfig_StillAddsService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddCorsConfiguration(config);

        services.Should().NotBeEmpty();
    }

    [Fact]
    public void AddCorsConfiguration_WithHttpHeaders_ConfiguresExposedHeaders()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "https://example.com",
                ["HttpHeaders:CorrelationIdHeader"] = "X-Correlation-Id",
                ["HttpHeaders:RequestIdHeader"] = "X-Request-Id",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddCorsConfiguration(config);

        var sp = services.BuildServiceProvider();
        var corsOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>>();

        var policy = corsOptions.Value.GetPolicy(CorsConfiguration.PolicyName);
        policy.Should().NotBeNull();
    }

    [Fact]
    public void AddCorsConfiguration_ReturnsSameServiceCollection()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        var result = services.AddCorsConfiguration(config);

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void PolicyName_IsExpectedValue()
    {
        CorsConfiguration.PolicyName.Should().Be("ServiceHubPolicy");
    }
}
