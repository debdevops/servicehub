using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Gcp;
using ServiceHub.Shared.Results;
using SHNamespace = ServiceHub.Core.Entities.Namespace;

namespace ServiceHub.UnitTests.Infrastructure.Gcp;

/// <summary>
/// Regression pack for <see cref="GcpHealthCheck"/> reachability semantics.
/// The gRPC list-probe happy path needs a live channel, so these tests pin the
/// decision logic around it: empty estates stay healthy, lookup failures
/// degrade, and unreachable projects surface as unhealthy.
/// </summary>
public sealed class GcpHealthCheckTests
{
    private static SHNamespace BuildGcpNamespace(string name = "gcp-ns") =>
        SHNamespace.Create(name, "{\"type\":\"service_account\"}",
            provider: CloudProviderType.Gcp, gcpProjectId: "my-project").Value;

    private static GcpHealthCheck BuildSut(
        Result<IReadOnlyList<SHNamespace>> namespaces,
        Mock<IGcpClientFactory>? factory = null)
    {
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(namespaces);
        return new GcpHealthCheck(
            (factory ?? new Mock<IGcpClientFactory>()).Object,
            repo.Object,
            NullLogger<GcpHealthCheck>.Instance);
    }

    [Fact]
    public async Task NoGcpNamespaces_ReportsHealthy()
    {
        var sut = BuildSut(Result<IReadOnlyList<SHNamespace>>.Success(Array.Empty<SHNamespace>()));

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["GcpNamespaces"].Should().Be(0);
    }

    [Fact]
    public async Task NamespaceLookupFailure_ReportsDegraded()
    {
        var sut = BuildSut(Result<IReadOnlyList<SHNamespace>>.Failure(Error.Internal("Repo.Failed", "db down")));

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task ClientFactoryThrows_ReportsUnhealthy()
    {
        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetSubscriberClientAsync(
                It.IsAny<SHNamespace>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("credentials invalid"));

        var sut = BuildSut(
            Result<IReadOnlyList<SHNamespace>>.Success(new List<SHNamespace> { BuildGcpNamespace() }),
            factory);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Data["UnhealthyGcpNamespaces"].Should().Be(1);
    }

    [Fact]
    public async Task AzureAndAwsNamespaces_AreIgnored()
    {
        var azureNs = SHNamespace.Create("azure-ns",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=P;SharedAccessKey=abc=").Value;
        var awsNs = SHNamespace.Create("aws-ns", "akid:secret",
            provider: CloudProviderType.Aws, awsRegion: "us-east-1").Value;
        var sut = BuildSut(Result<IReadOnlyList<SHNamespace>>.Success(new List<SHNamespace> { azureNs, awsNs }));

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["GcpNamespaces"].Should().Be(0);
    }
}
