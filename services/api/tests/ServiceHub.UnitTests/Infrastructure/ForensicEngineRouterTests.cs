using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Routing;

namespace ServiceHub.UnitTests.Infrastructure;

/// <summary>
/// Direct unit tests for <see cref="ForensicEngineRouter"/>. Uses a real, minimal DI container
/// (rather than a mocked <see cref="IKeyedServiceProvider"/>) so these tests exercise the actual
/// keyed-service resolution the router depends on in production, not just its own dispatch logic.
/// </summary>
public sealed class ForensicEngineRouterTests
{
    private static DlqMessage CreateMessage(CloudProviderType provider) => new()
    {
        MessageId = "msg-1",
        SequenceNumber = 1,
        BodyHash = "hash-1",
        NamespaceId = Guid.NewGuid(),
        OwnerId = "owner-1",
        EntityName = "queue-1",
        EntityType = ServiceBusEntityType.Queue,
        CloudProvider = provider,
        EnqueuedTimeUtc = DateTimeOffset.UtcNow,
        DetectedAtUtc = DateTimeOffset.UtcNow,
    };

    private static readonly ForensicEngineResult AzureResult =
        new(FailureCategory.Transient, 0.9, "azure", "Safe", "Deterministic");

    private static readonly ForensicEngineResult AwsResult =
        new(FailureCategory.MaxDelivery, 0.95, "aws", "RequiresReview", "AWS-Deterministic");

    private static readonly ForensicEngineResult GcpResult =
        new(FailureCategory.ProcessingError, 0.85, "gcp", "RequiresReview", "GCP-Deterministic");

    private static IKeyedServiceProvider BuildProvider(
        IForensicEngine? azure = null, IForensicEngine? aws = null, IForensicEngine? gcp = null)
    {
        var services = new ServiceCollection();
        if (azure is not null)
            services.AddKeyedSingleton(CloudProviderType.Azure, (_, _) => azure);
        if (aws is not null)
            services.AddKeyedSingleton(CloudProviderType.Aws, (_, _) => aws);
        if (gcp is not null)
            services.AddKeyedSingleton(CloudProviderType.Gcp, (_, _) => gcp);

        return (IKeyedServiceProvider)services.BuildServiceProvider();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullKeyedServices_Throws()
    {
        var act = () => new ForensicEngineRouter(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("keyedServices");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Analyse — dispatch
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyse_NullMessage_Throws()
    {
        var azureEngine = new Mock<IForensicEngine>();
        var router = new ForensicEngineRouter(BuildProvider(azure: azureEngine.Object));

        var act = () => router.Analyse(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("message");
    }

    [Fact]
    public void Analyse_AzureMessage_DispatchesToAzureEngine()
    {
        var azureEngine = new Mock<IForensicEngine>();
        azureEngine.Setup(e => e.Analyse(It.IsAny<DlqMessage>())).Returns(AzureResult);
        var router = new ForensicEngineRouter(BuildProvider(azure: azureEngine.Object));

        var result = router.Analyse(CreateMessage(CloudProviderType.Azure));

        result.Should().Be(AzureResult);
    }

    [Fact]
    public void Analyse_AwsMessage_WhenAwsEngineRegistered_DispatchesToAwsEngine()
    {
        var azureEngine = new Mock<IForensicEngine>();
        var awsEngine = new Mock<IForensicEngine>();
        awsEngine.Setup(e => e.Analyse(It.IsAny<DlqMessage>())).Returns(AwsResult);
        var router = new ForensicEngineRouter(BuildProvider(azure: azureEngine.Object, aws: awsEngine.Object));

        var result = router.Analyse(CreateMessage(CloudProviderType.Aws));

        result.Should().Be(AwsResult);
        azureEngine.Verify(e => e.Analyse(It.IsAny<DlqMessage>()), Times.Never);
    }

    [Fact]
    public void Analyse_GcpMessage_WhenGcpEngineRegistered_DispatchesToGcpEngine()
    {
        var azureEngine = new Mock<IForensicEngine>();
        var gcpEngine = new Mock<IForensicEngine>();
        gcpEngine.Setup(e => e.Analyse(It.IsAny<DlqMessage>())).Returns(GcpResult);
        var router = new ForensicEngineRouter(BuildProvider(azure: azureEngine.Object, gcp: gcpEngine.Object));

        var result = router.Analyse(CreateMessage(CloudProviderType.Gcp));

        result.Should().Be(GcpResult);
        azureEngine.Verify(e => e.Analyse(It.IsAny<DlqMessage>()), Times.Never);
    }

    [Fact]
    public void Analyse_AwsMessage_WhenAwsEngineNotRegistered_FallsBackToAzureEngine()
    {
        var azureEngine = new Mock<IForensicEngine>();
        azureEngine.Setup(e => e.Analyse(It.IsAny<DlqMessage>())).Returns(AzureResult);
        // Only Azure is registered — mirrors CloudProviders:Aws:Enabled=false in Simulator-off,
        // non-flagged environments.
        var router = new ForensicEngineRouter(BuildProvider(azure: azureEngine.Object));

        var result = router.Analyse(CreateMessage(CloudProviderType.Aws));

        result.Should().Be(AzureResult);
    }

    [Fact]
    public void Analyse_GcpMessage_WhenGcpEngineNotRegistered_FallsBackToAzureEngine()
    {
        var azureEngine = new Mock<IForensicEngine>();
        azureEngine.Setup(e => e.Analyse(It.IsAny<DlqMessage>())).Returns(AzureResult);
        var router = new ForensicEngineRouter(BuildProvider(azure: azureEngine.Object));

        var result = router.Analyse(CreateMessage(CloudProviderType.Gcp));

        result.Should().Be(AzureResult);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IForensicEngineRouter conformance
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ForensicEngineRouter_IsAssignableToIForensicEngineRouter()
    {
        var azureEngine = new Mock<IForensicEngine>();
        var router = new ForensicEngineRouter(BuildProvider(azure: azureEngine.Object));

        router.Should().BeAssignableTo<IForensicEngineRouter>();
    }
}
