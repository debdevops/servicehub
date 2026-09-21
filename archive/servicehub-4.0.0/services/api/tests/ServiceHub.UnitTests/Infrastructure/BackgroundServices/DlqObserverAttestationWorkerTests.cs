using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

/// <summary>
/// Coverage for <see cref="DlqObserverAttestationWorker"/>'s per-namespace sweep (ADR-004 item 4;
/// ADR-0011): checks the previous canary against the observer's log, then dispatches the next.
/// </summary>
public sealed class DlqObserverAttestationWorkerTests
{
    private const string OwnerId = "owner-a";
    private static readonly Guid NamespaceId = Guid.NewGuid();

    private static DlqObserverAttestationWorker CreateWorker()
    {
        var rootServices = new ServiceCollection();
        return new(
            rootServices.BuildServiceProvider(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
            NullLogger<DlqObserverAttestationWorker>.Instance);
    }

    private static Namespace BuildAwsNamespace() =>
        Namespace.Create(
            "aws-observer-test-ns", "AKID:secret", environment: EnvironmentType.Dev,
            provider: CloudProviderType.Aws, ownerId: OwnerId, awsRegion: "us-east-1").Value;

    private static DlqObserverAttestation BuildAttestation(string? lastCanaryMessageId = null) => new()
    {
        OwnerId = OwnerId,
        NamespaceId = NamespaceId,
        Enabled = true,
        ObserverReference = "dlq-observations",
        DlqEntityName = "orders-dlq",
        StalenessBoundMinutes = 60,
        LastCanaryMessageId = lastCanaryMessageId,
    };

    [Fact]
    public async Task SweepNamespaceAsync_MissingObserverReference_SkipsWithoutSending()
    {
        var attestation = BuildAttestation();
        attestation.ObserverReference = null;
        var messageOps = new Mock<IMessageOperationsService>();
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IDlqObserverAttestationService>());
        services.AddSingleton(messageOps.Object);
        services.AddSingleton(Mock.Of<INamespaceRepository>());

        await CreateWorker().SweepNamespaceAsync(services.BuildServiceProvider(), attestation, CancellationToken.None);

        messageOps.Verify(m => m.SendAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SweepNamespaceAsync_NamespaceUnresolvable_SkipsWithoutSending()
    {
        var attestation = BuildAttestation();
        var namespaceRepo = new Mock<INamespaceRepository>();
        namespaceRepo
            .Setup(r => r.GetByIdAsync(NamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("Namespace.NotFound", "gone")));
        var messageOps = new Mock<IMessageOperationsService>();

        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IDlqObserverAttestationService>());
        services.AddSingleton(messageOps.Object);
        services.AddSingleton(namespaceRepo.Object);

        await CreateWorker().SweepNamespaceAsync(services.BuildServiceProvider(), attestation, CancellationToken.None);

        messageOps.Verify(m => m.SendAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SweepNamespaceAsync_NoPendingCanary_SendsFreshCanaryAndRecordsIt()
    {
        var attestation = BuildAttestation(lastCanaryMessageId: null);
        var ns = BuildAwsNamespace();

        var namespaceRepo = new Mock<INamespaceRepository>();
        namespaceRepo.Setup(r => r.GetByIdAsync(NamespaceId, It.IsAny<CancellationToken>())).ReturnsAsync(Result<Namespace>.Success(ns));

        var messageOps = new Mock<IMessageOperationsService>();
        messageOps.Setup(m => m.SendAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        var attestationService = new Mock<IDlqObserverAttestationService>();

        var services = new ServiceCollection();
        services.AddSingleton(attestationService.Object);
        services.AddSingleton(messageOps.Object);
        services.AddSingleton(namespaceRepo.Object);
        services.AddSingleton(Mock.Of<IDlqObserverLogReader>()); // never consulted — no pending canary

        await CreateWorker().SweepNamespaceAsync(services.BuildServiceProvider(), attestation, CancellationToken.None);

        messageOps.Verify(m => m.SendAsync(
            It.Is<SendMessageRequest>(r => r.NamespaceId == NamespaceId && r.EntityName == "orders-dlq"),
            It.IsAny<CancellationToken>()), Times.Once);
        attestationService.Verify(s => s.RecordCanarySentAsync(OwnerId, NamespaceId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        attestationService.Verify(s => s.RecordCanaryConfirmedAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SweepNamespaceAsync_PendingCanaryConfirmedByReader_RecordsConfirmationThenSendsNext()
    {
        var attestation = BuildAttestation(lastCanaryMessageId: "canary-1");
        var ns = BuildAwsNamespace();

        var namespaceRepo = new Mock<INamespaceRepository>();
        namespaceRepo.Setup(r => r.GetByIdAsync(NamespaceId, It.IsAny<CancellationToken>())).ReturnsAsync(Result<Namespace>.Success(ns));

        var reader = new Mock<IDlqObserverLogReader>();
        reader.SetupGet(r => r.Provider).Returns(CloudProviderType.Aws);
        reader
            .Setup(r => r.HasRecordedArrivalAsync(ns, "dlq-observations", "canary-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var messageOps = new Mock<IMessageOperationsService>();
        messageOps.Setup(m => m.SendAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        var attestationService = new Mock<IDlqObserverAttestationService>();

        var services = new ServiceCollection();
        services.AddSingleton(attestationService.Object);
        services.AddSingleton(messageOps.Object);
        services.AddSingleton(namespaceRepo.Object);
        services.AddSingleton(reader.Object);

        await CreateWorker().SweepNamespaceAsync(services.BuildServiceProvider(), attestation, CancellationToken.None);

        attestationService.Verify(s => s.RecordCanaryConfirmedAsync(OwnerId, NamespaceId, It.IsAny<CancellationToken>()), Times.Once);
        attestationService.Verify(s => s.RecordCanarySentAsync(OwnerId, NamespaceId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SweepNamespaceAsync_PendingCanaryNotFoundByReader_DoesNotRecordConfirmation()
    {
        var attestation = BuildAttestation(lastCanaryMessageId: "canary-1");
        var ns = BuildAwsNamespace();

        var namespaceRepo = new Mock<INamespaceRepository>();
        namespaceRepo.Setup(r => r.GetByIdAsync(NamespaceId, It.IsAny<CancellationToken>())).ReturnsAsync(Result<Namespace>.Success(ns));

        var reader = new Mock<IDlqObserverLogReader>();
        reader.SetupGet(r => r.Provider).Returns(CloudProviderType.Aws);
        reader
            .Setup(r => r.HasRecordedArrivalAsync(ns, "dlq-observations", "canary-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var messageOps = new Mock<IMessageOperationsService>();
        messageOps.Setup(m => m.SendAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        var attestationService = new Mock<IDlqObserverAttestationService>();

        var services = new ServiceCollection();
        services.AddSingleton(attestationService.Object);
        services.AddSingleton(messageOps.Object);
        services.AddSingleton(namespaceRepo.Object);
        services.AddSingleton(reader.Object);

        await CreateWorker().SweepNamespaceAsync(services.BuildServiceProvider(), attestation, CancellationToken.None);

        attestationService.Verify(s => s.RecordCanaryConfirmedAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SweepNamespaceAsync_ReaderThrows_FailsClosedWithoutRecordingConfirmation()
    {
        var attestation = BuildAttestation(lastCanaryMessageId: "canary-1");
        var ns = BuildAwsNamespace();

        var namespaceRepo = new Mock<INamespaceRepository>();
        namespaceRepo.Setup(r => r.GetByIdAsync(NamespaceId, It.IsAny<CancellationToken>())).ReturnsAsync(Result<Namespace>.Success(ns));

        var reader = new Mock<IDlqObserverLogReader>();
        reader.SetupGet(r => r.Provider).Returns(CloudProviderType.Aws);
        reader
            .Setup(r => r.HasRecordedArrivalAsync(ns, "dlq-observations", "canary-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var messageOps = new Mock<IMessageOperationsService>();
        messageOps.Setup(m => m.SendAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        var attestationService = new Mock<IDlqObserverAttestationService>();

        var services = new ServiceCollection();
        services.AddSingleton(attestationService.Object);
        services.AddSingleton(messageOps.Object);
        services.AddSingleton(namespaceRepo.Object);
        services.AddSingleton(reader.Object);

        await CreateWorker().SweepNamespaceAsync(services.BuildServiceProvider(), attestation, CancellationToken.None);

        attestationService.Verify(s => s.RecordCanaryConfirmedAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SweepNamespaceAsync_SendFails_DoesNotRecordCanarySent()
    {
        var attestation = BuildAttestation(lastCanaryMessageId: null);
        var ns = BuildAwsNamespace();

        var namespaceRepo = new Mock<INamespaceRepository>();
        namespaceRepo.Setup(r => r.GetByIdAsync(NamespaceId, It.IsAny<CancellationToken>())).ReturnsAsync(Result<Namespace>.Success(ns));

        var messageOps = new Mock<IMessageOperationsService>();
        messageOps
            .Setup(m => m.SendAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.ExternalService("Send.Failed", "broker unreachable")));

        var attestationService = new Mock<IDlqObserverAttestationService>();

        var services = new ServiceCollection();
        services.AddSingleton(attestationService.Object);
        services.AddSingleton(messageOps.Object);
        services.AddSingleton(namespaceRepo.Object);
        services.AddSingleton(Mock.Of<IDlqObserverLogReader>());

        await CreateWorker().SweepNamespaceAsync(services.BuildServiceProvider(), attestation, CancellationToken.None);

        attestationService.Verify(s => s.RecordCanarySentAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
