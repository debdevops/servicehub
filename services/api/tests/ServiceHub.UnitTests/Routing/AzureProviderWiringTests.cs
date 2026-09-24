using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Routing;
using ServiceHub.Providers.Azure;

namespace ServiceHub.UnitTests.Routing;

/// <summary>
/// Unit 1.3's seam, through the real container: registering one provider is enough for the router
/// to resolve an Azure namespace to it, and registering it twice does not register it twice.
/// </summary>
public sealed class AzureProviderWiringTests
{
    private static ServiceProvider Build()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:EncryptionKey"] = "DEV_KEY_NOT_FOR_PRODUCTION_12345678901234567890",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(Mock.Of<IHostEnvironment>(e => e.EnvironmentName == Environments.Development));
        services.AddLogging();
        services.AddServiceHubPersistence();
        services.AddCloudProviderRouting();
        services.AddAzureProvider();
        services.AddAzureProvider();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
    }

    [Fact]
    public async Task TheRouter_ResolvesAnAzureNamespaceToTheAzureAdapter()
    {
        await using var provider = Build();
        await using var scope = provider.CreateAsyncScope();

        var router = scope.ServiceProvider.GetRequiredService<ICloudProviderRouter>();
        var azure = router.Resolve(CloudProviderType.Azure);

        azure.Should().BeOfType<AzureMessagingProvider>();
        azure.ProviderType.Should().Be(CloudProviderType.Azure);
        azure.Capabilities.CanProveDlqAbsence.Should().BeTrue();
    }

    [Fact]
    public async Task RegisteringAzureTwice_StillGivesTheRouterOneAzureProvider()
    {
        await using var provider = Build();
        await using var scope = provider.CreateAsyncScope();

        scope.ServiceProvider.GetServices<ICloudMessagingProvider>()
            .Should().ContainSingle(p => p.ProviderType == CloudProviderType.Azure);
    }

    [Fact]
    public async Task AProviderThatWasNeverRegistered_IsAnHonestFailure_NotAFallback()
    {
        await using var provider = Build();
        await using var scope = provider.CreateAsyncScope();

        var router = scope.ServiceProvider.GetRequiredService<ICloudProviderRouter>();

        var act = () => router.Resolve(CloudProviderType.Aws);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Aws*");
        router.IsRegistered(CloudProviderType.Aws).Should().BeFalse();
    }
}
