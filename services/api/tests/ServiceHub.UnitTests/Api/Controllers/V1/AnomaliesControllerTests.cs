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
    private readonly Mock<IAIServiceClient> _aiServiceClient;
    private readonly Mock<INamespaceRepository> _namespaceRepository;
    private readonly Mock<ILogger<AnomaliesController>> _logger;
    private readonly AnomaliesController _controller;

    public AnomaliesControllerTests()
    {
        _aiServiceClient = new Mock<IAIServiceClient>();
        _namespaceRepository = new Mock<INamespaceRepository>();
        _logger = new Mock<ILogger<AnomaliesController>>();

        _controller = new AnomaliesController(
            _aiServiceClient.Object,
            _namespaceRepository.Object,
            _logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static Namespace CreateTestNamespace()
    {
        var result = Namespace.Create(
            "test-namespace",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS");
        return result.Value;
    }

    private static Anomaly CreateTestAnomaly(Guid namespaceId)
    {
        return Anomaly.Create(
            namespaceId,
            "test-queue",
            AnomalyType.HighFailureRate,
            75,
            "Unusual spike in dead letter messages",
            new Dictionary<string, double> { ["dlq_count"] = 150 },
            new List<string> { "Check consumer health" });
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullAIClient_ShouldThrow()
    {
        var act = () => new AnomaliesController(null!, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ShouldThrow()
    {
        var act = () => new AnomaliesController(_aiServiceClient.Object, _namespaceRepository.Object, null!);
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

        _aiServiceClient.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        _aiServiceClient.Setup(a => a.DetectAnomaliesAsync(
            ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(new List<Anomaly> { anomaly }));

        var result = await _controller.DetectAnomalies(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<AnomalyDetectionResponse>().Subject;
        response.Anomalies.Should().HaveCount(1);
        response.NamespaceId.Should().Be(ns.Id);
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
    public async Task DetectAnomalies_AIUnavailable_ShouldReturn503()
    {
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _aiServiceClient.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(false));

        var result = await _controller.DetectAnomalies(ns.Id);

        var statusResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task DetectAnomalies_WithTimeWindow_ShouldPassParameters()
    {
        var ns = CreateTestNamespace();
        var start = DateTimeOffset.UtcNow.AddHours(-2);
        var end = DateTimeOffset.UtcNow;

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _aiServiceClient.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        _aiServiceClient.Setup(a => a.DetectAnomaliesAsync(
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

        _aiServiceClient.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        _aiServiceClient.Setup(a => a.DetectAnomaliesAsync(
            ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Failure(Error.Internal("AI_ERR", "Detection failed")));

        var result = await _controller.DetectAnomalies(ns.Id);

        result.Result.Should().NotBeOfType<OkObjectResult>();
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

        _aiServiceClient.Setup(a => a.GetAnomalyByIdAsync(anomaly.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Anomaly>.Success(anomaly));

        var result = await _controller.GetById(anomaly.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<AnomalyInfo>().Subject;
        response.Id.Should().Be(anomaly.Id);
    }

    [Fact]
    public async Task GetById_NotFound_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        _aiServiceClient.Setup(a => a.GetAnomalyByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Anomaly>.Failure(Error.NotFound("NOT_FOUND", "Anomaly not found")));

        var result = await _controller.GetById(id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    #endregion
}
