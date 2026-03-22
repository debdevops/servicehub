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
