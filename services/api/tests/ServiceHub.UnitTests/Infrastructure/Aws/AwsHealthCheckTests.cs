using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Aws;
using ServiceHub.Shared.Results;
using SHNamespace = ServiceHub.Core.Entities.Namespace;

namespace ServiceHub.UnitTests.Infrastructure.Aws;

/// <summary>
/// Regression pack for <see cref="AwsHealthCheck"/>: /health/ready must reflect
/// real SQS reachability without ever failing solely because no AWS namespace
/// has been connected yet.
/// </summary>
public sealed class AwsHealthCheckTests
{
    private static SHNamespace BuildAwsNamespace(string name = "aws-ns") =>
        SHNamespace.Create(name, "akid:secret", provider: CloudProviderType.Aws, awsRegion: "us-east-1").Value;

    private static AwsHealthCheck BuildSut(
        Result<IReadOnlyList<SHNamespace>> namespaces,
        Func<SHNamespace, IAmazonSQS>? clientForNamespace = null)
    {
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(namespaces);

        var factory = new Mock<IAwsClientFactory>();
        if (clientForNamespace is not null)
            factory.Setup(f => f.GetSqsClient(It.IsAny<SHNamespace>()))
                .Returns<SHNamespace>(clientForNamespace);

        return new AwsHealthCheck(factory.Object, repo.Object, NullLogger<AwsHealthCheck>.Instance);
    }

    private static IAmazonSQS ReachableSqs()
    {
        var sqs = new Mock<IAmazonSQS>();
        sqs.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse());
        return sqs.Object;
    }

    private static IAmazonSQS UnreachableSqs()
    {
        var sqs = new Mock<IAmazonSQS>();
        sqs.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSQSException("connection refused"));
        return sqs.Object;
    }

    [Fact]
    public async Task NoAwsNamespaces_ReportsHealthy()
    {
        var sut = BuildSut(Result<IReadOnlyList<SHNamespace>>.Success(Array.Empty<SHNamespace>()));

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        // A user who never connected AWS must not see a degraded system.
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["AwsNamespaces"].Should().Be(0);
    }

    [Fact]
    public async Task AllNamespacesReachable_ReportsHealthy()
    {
        var ns = BuildAwsNamespace();
        var sut = BuildSut(
            Result<IReadOnlyList<SHNamespace>>.Success(new List<SHNamespace> { ns }),
            _ => ReachableSqs());

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["HealthyAwsNamespaces"].Should().Be(1);
    }

    [Fact]
    public async Task AllNamespacesUnreachable_ReportsUnhealthy()
    {
        var ns = BuildAwsNamespace();
        var sut = BuildSut(
            Result<IReadOnlyList<SHNamespace>>.Success(new List<SHNamespace> { ns }),
            _ => UnreachableSqs());

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Data["UnhealthyAwsNamespaces"].Should().Be(1);
    }

    [Fact]
    public async Task SomeNamespacesUnreachable_ReportsDegraded()
    {
        var healthy = BuildAwsNamespace("aws-healthy");
        var broken = BuildAwsNamespace("aws-broken");
        var sut = BuildSut(
            Result<IReadOnlyList<SHNamespace>>.Success(new List<SHNamespace> { healthy, broken }),
            ns => ns.Name == "aws-healthy" ? ReachableSqs() : UnreachableSqs());

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data["HealthyAwsNamespaces"].Should().Be(1);
        result.Data["UnhealthyAwsNamespaces"].Should().Be(1);
        result.Data["UnhealthyAwsNamespaceNames"].Should().Be("aws-broken");
    }

    [Fact]
    public async Task NamespaceLookupFailure_ReportsDegraded()
    {
        var sut = BuildSut(Result<IReadOnlyList<SHNamespace>>.Failure(Error.Internal("Repo.Failed", "db down")));

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task NonAzureProvidersOnly_AzureNamespacesAreIgnored()
    {
        var azureNs = SHNamespace.Create("azure-ns",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=P;SharedAccessKey=abc=").Value;
        var sut = BuildSut(Result<IReadOnlyList<SHNamespace>>.Success(new List<SHNamespace> { azureNs }));

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        // Azure namespaces are the Azure health check's concern, not this one's.
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["AwsNamespaces"].Should().Be(0);
    }
}
