using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public class AnomaliesControllerTests
{
    private readonly Mock<IAnomalyDetectionService> _detectionService;
    private readonly Mock<IAnomalyResultCache> _resultCache;
    private readonly Mock<INamespaceRepository> _namespaceRepository;
    private readonly Mock<ILogger<AnomaliesController>> _logger;
    private readonly AnomaliesController _controller;

    public AnomaliesControllerTests()
    {
        _detectionService = new Mock<IAnomalyDetectionService>();
        _resultCache = new Mock<IAnomalyResultCache>();
        _namespaceRepository = new Mock<INamespaceRepository>();
        _logger = new Mock<ILogger<AnomaliesController>>();

        _controller = new AnomaliesController(
            _detectionService.Object,
            _resultCache.Object,
            _namespaceRepository.Object,
            _logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static Namespace CreateTestNamespace() =>
        Namespace.Create(
            "test-namespace",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS").Value;

    private static Anomaly CreateTestAnomaly(Guid namespaceId) =>
        Anomaly.Create(
            namespaceId,
            "test-queue",
            AnomalyType.HighFailureRate,
            75,
            "Unusual spike in dead letter messages",
            new Dictionary<string, double> { ["dlq_count"] = 150 },
            new List<string> { "Check consumer health" });

    #region Constructor Tests

    [Fact]
    public void Constructor_NullDetectionService_ShouldThrow()
    {
        var act = () => new AnomaliesController(null!, _resultCache.Object, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullResultCache_ShouldThrow()
    {
        var act = () => new AnomaliesController(_detectionService.Object, null!, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ShouldThrow()
    {
        var act = () => new AnomaliesController(_detectionService.Object, _resultCache.Object, _namespaceRepository.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region DetectAnomalies Tests

    [Fact]
    public async Task DetectAnomalies_Success_ShouldReturnOk()
    {
        var ns = CreateTestNamespace();
        var anomaly = CreateTestAnomaly(ns.Id);

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _detectionService.Setup(a => a.DetectAnomaliesAsync(
            ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new List<Anomaly> { anomaly }));

        var result = await _controller.DetectAnomalies(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<AnomalyDetectionResponse>().Subject;
        response.Anomalies.Should().HaveCount(1);
        response.NamespaceId.Should().Be(ns.Id);
        _resultCache.Verify(c => c.Store(It.Is<IEnumerable<Anomaly>>(a => a.Contains(anomaly))), Times.Once);
    }

    [Fact]
    public async Task DetectAnomalies_NamespaceNotFound_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        _namespaceRepository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("NOT_FOUND", "Not found")));

        var result = await _controller.DetectAnomalies(id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DetectAnomalies_WithTimeWindow_ShouldPassParameters()
    {
        var ns = CreateTestNamespace();
        var start = DateTimeOffset.UtcNow.AddHours(-2);
        var end = DateTimeOffset.UtcNow;

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _detectionService.Setup(a => a.DetectAnomaliesAsync(
            ns.Id, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new List<Anomaly>()));

        var result = await _controller.DetectAnomalies(ns.Id, start, end);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<AnomalyDetectionResponse>().Subject;
        response.StartTime.Should().Be(start);
        response.EndTime.Should().Be(end);
    }

    [Fact]
    public async Task DetectAnomalies_DetectionFails_ShouldReturnError()
    {
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _detectionService.Setup(a => a.DetectAnomaliesAsync(
            ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Failure(Error.Validation("BAD_REQUEST", "Detection failed")));

        var result = await _controller.DetectAnomalies(ns.Id);

        result.Result.Should().NotBeOfType<OkObjectResult>();
        _resultCache.Verify(c => c.Store(It.IsAny<IEnumerable<Anomaly>>()), Times.Never);
    }

    #endregion

    #region GetById Tests

    [Fact]
    public async Task GetById_Success_ShouldReturnOk()
    {
        var anomaly = Anomaly.Create(
            Guid.NewGuid(),
            "test-queue",
            AnomalyType.HighMessageVolume,
            50,
            "Message volume anomaly");

        _resultCache.Setup(c => c.TryGet(anomaly.Id)).Returns(anomaly);
        _namespaceRepository.Setup(r => r.GetByIdAsync(anomaly.NamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateTestNamespace()));

        var result = await _controller.GetById(anomaly.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<AnomalyInfo>().Subject;
        response.Id.Should().Be(anomaly.Id);
    }

    [Fact]
    public async Task GetById_NamespaceOwnedByAnotherTenant_ShouldReturnNotFound()
    {
        var anomaly = Anomaly.Create(
            Guid.NewGuid(),
            "test-queue",
            AnomalyType.HighMessageVolume,
            50,
            "Message volume anomaly");

        var foreignNamespace = Namespace.Create(
            "other-namespace",
            "Endpoint=sb://other.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            ownerId: "key_othertenant").Value;

        _resultCache.Setup(c => c.TryGet(anomaly.Id)).Returns(anomaly);
        _namespaceRepository.Setup(r => r.GetByIdAsync(anomaly.NamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(foreignNamespace));

        var result = await _controller.GetById(anomaly.Id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetById_NamespaceNotFound_ShouldReturnNotFound()
    {
        var anomaly = Anomaly.Create(
            Guid.NewGuid(),
            "test-queue",
            AnomalyType.HighMessageVolume,
            50,
            "Message volume anomaly");

        _resultCache.Setup(c => c.TryGet(anomaly.Id)).Returns(anomaly);
        _namespaceRepository.Setup(r => r.GetByIdAsync(anomaly.NamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("NOT_FOUND", "Namespace not found")));

        var result = await _controller.GetById(anomaly.Id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetById_NamespaceLookupFails_ShouldPropagateError()
    {
        var anomaly = Anomaly.Create(
            Guid.NewGuid(),
            "test-queue",
            AnomalyType.HighMessageVolume,
            50,
            "Message volume anomaly");

        _resultCache.Setup(c => c.TryGet(anomaly.Id)).Returns(anomaly);
        _namespaceRepository.Setup(r => r.GetByIdAsync(anomaly.NamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.Internal("DB_ERR", "Database unavailable")));

        var result = await _controller.GetById(anomaly.Id);

        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task GetById_NotFound_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        _resultCache.Setup(c => c.TryGet(id)).Returns((Anomaly?)null);

        var result = await _controller.GetById(id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    #endregion
}
