using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public sealed class NarrationsControllerTests
{
    private readonly Mock<IAnomalyDetectionService> _anomalyDetectionService = new();
    private readonly Mock<IDriftDetectionService> _driftDetectionService = new();
    private readonly Mock<ICorrelationDetectionService> _correlationDetectionService = new();
    private readonly Mock<INarrationService> _narrationService = new();
    private readonly Mock<INarrationResultCache> _resultCache = new();
    private readonly Mock<INamespaceRepository> _namespaceRepository = new();
    private readonly Mock<ILogger<NarrationsController>> _logger = new();
    private readonly NarrationsController _controller;

    public NarrationsControllerTests()
    {
        _controller = new NarrationsController(
            _anomalyDetectionService.Object,
            _driftDetectionService.Object,
            _correlationDetectionService.Object,
            _narrationService.Object,
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

    private static Namespace CreateTestNamespace(string name = "test-namespace", string ownerId = Namespace.SpaOwnerId) =>
        Namespace.Create(
            name,
            $"Endpoint=sb://{name}.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS",
            ownerId: ownerId).Value;

    #region Constructor Tests

    [Fact]
    public void Constructor_NullAnomalyDetectionService_Throws()
    {
        var act = () => new NarrationsController(
            null!, _driftDetectionService.Object, _correlationDetectionService.Object,
            _narrationService.Object, _resultCache.Object, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullNarrationService_Throws()
    {
        var act = () => new NarrationsController(
            _anomalyDetectionService.Object, _driftDetectionService.Object, _correlationDetectionService.Object,
            null!, _resultCache.Object, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullResultCache_Throws()
    {
        var act = () => new NarrationsController(
            _anomalyDetectionService.Object, _driftDetectionService.Object, _correlationDetectionService.Object,
            _narrationService.Object, null!, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Generate Tests

    [Fact]
    public async Task Generate_EndBeforeStart_ReturnsBadRequest()
    {
        var start = DateTimeOffset.UtcNow;
        var end = start.AddHours(-1);

        var result = await _controller.Generate(start, end);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Generate_Success_CachesAndReturnsNarrations()
    {
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        _anomalyDetectionService.Setup(a => a.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(Array.Empty<Anomaly>()));
        _driftDetectionService.Setup(d => d.DetectDriftAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Success(Array.Empty<DriftFinding>()));
        _correlationDetectionService.Setup(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>()))
            .Returns(Array.Empty<CorrelationFinding>());

        var narration = Narration.Create(NarrationKind.NamespaceActivity, ns.Id, [ns.Id], "headline", "summary", 70);
        _narrationService.Setup(n => n.GenerateNarrations(
                It.IsAny<IReadOnlyDictionary<Guid, Namespace>>(),
                It.IsAny<IReadOnlyList<Anomaly>>(),
                It.IsAny<IReadOnlyList<DriftFinding>>(),
                It.IsAny<IReadOnlyList<CorrelationFinding>>()))
            .Returns(new[] { narration });

        var result = await _controller.Generate();

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<NarrationGenerationResponse>().Subject;
        response.Narrations.Should().ContainSingle().Which.Id.Should().Be(narration.Id);
        _resultCache.Verify(c => c.Store(It.Is<IEnumerable<Narration>>(n => n.Contains(narration))), Times.Once);
    }

    [Fact]
    public async Task Generate_NamespaceLookupFails_PropagatesError()
    {
        _namespaceRepository.Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Failure(Error.Internal("DB_ERR", "unavailable")));

        var result = await _controller.Generate();

        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    #endregion

    #region GetById Tests

    [Fact]
    public async Task GetById_NotFound_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _resultCache.Setup(c => c.TryGet(id)).Returns((Narration?)null);

        var result = await _controller.GetById(id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetById_AccessibleNamespace_ReturnsOk()
    {
        var ns = CreateTestNamespace();
        var narration = Narration.Create(NarrationKind.NamespaceActivity, ns.Id, [ns.Id], "headline", "summary", 70);

        _resultCache.Setup(c => c.TryGet(narration.Id)).Returns(narration);
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var result = await _controller.GetById(narration.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<NarrationInfo>().Subject;
        response.Id.Should().Be(narration.Id);
    }

    [Fact]
    public async Task GetById_OneContributingNamespaceInaccessible_ReturnsNotFound()
    {
        var accessibleNs = CreateTestNamespace("ns-a");
        var foreignNs = Namespace.Create(
            "ns-b",
            "Endpoint=sb://ns-b.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            ownerId: "key_othertenant").Value;

        var narration = Narration.Create(
            NarrationKind.CrossNamespaceCorrelation,
            namespaceId: null,
            accessNamespaceIds: [accessibleNs.Id, foreignNs.Id],
            headline: "headline",
            summary: "summary",
            severity: 80);

        _resultCache.Setup(c => c.TryGet(narration.Id)).Returns(narration);
        _namespaceRepository.Setup(r => r.GetByIdAsync(accessibleNs.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(accessibleNs));
        _namespaceRepository.Setup(r => r.GetByIdAsync(foreignNs.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(foreignNs));

        var result = await _controller.GetById(narration.Id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    #endregion
}
