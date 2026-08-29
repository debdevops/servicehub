using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public sealed class ExternalSignalsControllerTests
{
    private readonly Mock<IExternalSignalRepository> _externalSignalRepository = new();
    private readonly Mock<IAnomalyDetectionService> _anomalyDetectionService = new();
    private readonly Mock<IExternalSignalCorrelationService> _correlationService = new();
    private readonly Mock<IExternalSignalCorrelationCache> _correlationCache = new();
    private readonly Mock<INamespaceRepository> _namespaceRepository = new();
    private readonly Mock<ILogger<ExternalSignalsController>> _logger = new();
    private readonly ExternalSignalsController _controller;

    public ExternalSignalsControllerTests()
    {
        _controller = new ExternalSignalsController(
            _externalSignalRepository.Object,
            _anomalyDetectionService.Object,
            _correlationService.Object,
            _correlationCache.Object,
            _namespaceRepository.Object,
            _logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
    }

    private static Namespace CreateTestNamespace(string name = "test-namespace", string? ownerId = null) =>
        Namespace.Create(
            name,
            $"Endpoint=sb://{name}.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS",
            ownerId: ownerId).Value;

    private static ExternalSignalEvent CreateSignal(string ownerId, Guid? namespaceId = null) => new()
    {
        OwnerId = ownerId,
        NamespaceId = namespaceId,
        SignalType = ExternalSignalType.Deploy,
        OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        Source = "manual",
        IngestedAt = DateTimeOffset.UtcNow,
    };

    private static ExternalSignalCorrelation CreateCorrelation(string ownerId, Guid namespaceId, ExternalSignalEvent signal) =>
        ExternalSignalCorrelation.Create(
            ownerId, namespaceId, "queue-1", AnomalyType.HighMessageVolume, 80, CloudProviderType.Azure,
            signal, TimeSpan.FromMinutes(10), "spike after deploy");

    #region Constructor Tests

    [Fact]
    public void Constructor_NullExternalSignalRepository_Throws()
    {
        var act = () => new ExternalSignalsController(
            null!, _anomalyDetectionService.Object, _correlationService.Object, _correlationCache.Object, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("externalSignalRepository");
    }

    [Fact]
    public void Constructor_NullAnomalyDetectionService_Throws()
    {
        var act = () => new ExternalSignalsController(
            _externalSignalRepository.Object, null!, _correlationService.Object, _correlationCache.Object, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("anomalyDetectionService");
    }

    [Fact]
    public void Constructor_NullCorrelationService_Throws()
    {
        var act = () => new ExternalSignalsController(
            _externalSignalRepository.Object, _anomalyDetectionService.Object, null!, _correlationCache.Object, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("correlationService");
    }

    [Fact]
    public void Constructor_NullCorrelationCache_Throws()
    {
        var act = () => new ExternalSignalsController(
            _externalSignalRepository.Object, _anomalyDetectionService.Object, _correlationService.Object, null!, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("correlationCache");
    }

    [Fact]
    public void Constructor_NullNamespaceRepository_Throws()
    {
        var act = () => new ExternalSignalsController(
            _externalSignalRepository.Object, _anomalyDetectionService.Object, _correlationService.Object, _correlationCache.Object, null!, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("namespaceRepository");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new ExternalSignalsController(
            _externalSignalRepository.Object, _anomalyDetectionService.Object, _correlationService.Object, _correlationCache.Object, _namespaceRepository.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    #endregion

    #region RecordSignal Tests

    [Fact]
    public async Task RecordSignal_Success_ReturnsOk()
    {
        var signal = CreateSignal(Namespace.SpaOwnerId);
        _externalSignalRepository
            .Setup(r => r.RecordAsync(It.IsAny<RecordExternalSignalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExternalSignalEvent>.Success(signal));

        var result = await _controller.RecordSignal(new RecordExternalSignalHttpRequest(
            null, ExternalSignalType.Deploy, DateTimeOffset.UtcNow, "manual", null));

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ExternalSignalEventResponse>().Subject;
        response.Id.Should().Be(signal.Id);
    }

    [Fact]
    public async Task RecordSignal_ValidationFailure_ReturnsBadRequest()
    {
        _externalSignalRepository
            .Setup(r => r.RecordAsync(It.IsAny<RecordExternalSignalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExternalSignalEvent>.Failure(Error.Validation("ExternalSignal.SourceRequired", "blank")));

        var result = await _controller.RecordSignal(new RecordExternalSignalHttpRequest(
            null, ExternalSignalType.Deploy, DateTimeOffset.UtcNow, " ", null));

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RecordSignal_NamespaceIdNotAccessibleToCaller_ReturnsBadRequestAndDoesNotRecord()
    {
        var foreignNamespaceId = Guid.NewGuid();
        _namespaceRepository
            .Setup(r => r.GetByIdAsync(foreignNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("Namespace.NotFound", "not found")));

        var result = await _controller.RecordSignal(new RecordExternalSignalHttpRequest(
            foreignNamespaceId, ExternalSignalType.Deploy, DateTimeOffset.UtcNow, "manual", null));

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _externalSignalRepository.Verify(
            r => r.RecordAsync(It.IsAny<RecordExternalSignalRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordSignal_NamespaceIdAccessibleToCaller_Records()
    {
        var ns = CreateTestNamespace(ownerId: Namespace.SpaOwnerId);
        _namespaceRepository
            .Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var signal = CreateSignal(Namespace.SpaOwnerId, ns.Id);
        _externalSignalRepository
            .Setup(r => r.RecordAsync(It.IsAny<RecordExternalSignalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExternalSignalEvent>.Success(signal));

        var result = await _controller.RecordSignal(new RecordExternalSignalHttpRequest(
            ns.Id, ExternalSignalType.Deploy, DateTimeOffset.UtcNow, "manual", null));

        result.Result.Should().BeOfType<OkObjectResult>();
        _externalSignalRepository.Verify(
            r => r.RecordAsync(It.Is<RecordExternalSignalRequest>(req => req.NamespaceId == ns.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetSignals Tests

    [Fact]
    public async Task GetSignals_ReturnsSignalsFromRepository()
    {
        var signal = CreateSignal(Namespace.SpaOwnerId);
        _externalSignalRepository
            .Setup(r => r.QueryAsync(Namespace.SpaOwnerId, null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { signal });

        var result = await _controller.GetSignals();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeAssignableTo<IReadOnlyList<ExternalSignalEventResponse>>().Subject;
        response.Should().ContainSingle(s => s.Id == signal.Id);
    }

    #endregion

    #region DetectCorrelations Tests

    [Fact]
    public async Task DetectCorrelations_Success_ReturnsOk()
    {
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByOwnerAsync(Namespace.SpaOwnerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        _anomalyDetectionService.Setup(a => a.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(Array.Empty<Anomaly>()));

        _externalSignalRepository
            .Setup(r => r.QueryAsync(Namespace.SpaOwnerId, null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ExternalSignalEvent>());

        var signal = CreateSignal(Namespace.SpaOwnerId, ns.Id);
        var correlation = CreateCorrelation(Namespace.SpaOwnerId, ns.Id, signal);
        _correlationService
            .Setup(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>(), It.IsAny<IReadOnlyList<ExternalSignalEvent>>(), It.IsAny<TimeSpan>()))
            .Returns(new[] { correlation });

        var result = await _controller.DetectCorrelations();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ExternalSignalCorrelationDetectionResponse>().Subject;
        response.Correlations.Should().ContainSingle(c => c.Id == correlation.Id);
        _correlationCache.Verify(c => c.Store(It.Is<IEnumerable<ExternalSignalCorrelation>>(list => list.Contains(correlation))), Times.Once);
    }

    [Fact]
    public async Task DetectCorrelations_EndTimeNotAfterStartTime_ReturnsBadRequest_WithoutQueryingNamespaces()
    {
        var now = DateTimeOffset.UtcNow;

        var result = await _controller.DetectCorrelations(now, now);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _namespaceRepository.Verify(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DetectCorrelations_NamespaceLookupFails_ReturnsError()
    {
        _namespaceRepository.Setup(r => r.GetByOwnerAsync(Namespace.SpaOwnerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Failure(Error.Internal("DB_ERR", "unavailable")));

        var result = await _controller.DetectCorrelations();

        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    #endregion

    #region GetCorrelationById Tests

    [Fact]
    public async Task GetCorrelationById_Success_ReturnsOk()
    {
        var ns = CreateTestNamespace(ownerId: Namespace.SpaOwnerId);
        var signal = CreateSignal(Namespace.SpaOwnerId, ns.Id);
        var correlation = CreateCorrelation(Namespace.SpaOwnerId, ns.Id, signal);

        _correlationCache.Setup(c => c.TryGet(correlation.Id)).Returns(correlation);
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var result = await _controller.GetCorrelationById(correlation.Id);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ExternalSignalCorrelationInfo>().Subject;
        response.Id.Should().Be(correlation.Id);
    }

    [Fact]
    public async Task GetCorrelationById_NotFound_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _correlationCache.Setup(c => c.TryGet(id)).Returns((ExternalSignalCorrelation?)null);

        var result = await _controller.GetCorrelationById(id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetCorrelationById_NamespaceNotAccessible_ReturnsNotFound()
    {
        var namespaceId = Guid.NewGuid();
        var signal = CreateSignal("key_trueowner", namespaceId);
        var correlation = CreateCorrelation("key_trueowner", namespaceId, signal);

        _correlationCache.Setup(c => c.TryGet(correlation.Id)).Returns(correlation);
        _namespaceRepository.Setup(r => r.GetByIdAsync(namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateTestNamespace(ownerId: "key_someoneelse")));

        var result = await _controller.GetCorrelationById(correlation.Id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    #endregion
}
