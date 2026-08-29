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

public class CorrelationFindingsControllerTests
{
    private readonly Mock<IAnomalyDetectionService> _anomalyDetectionService;
    private readonly Mock<ICorrelationDetectionService> _correlationDetectionService;
    private readonly Mock<ICorrelationResultCache> _resultCache;
    private readonly Mock<INamespaceRepository> _namespaceRepository;
    private readonly Mock<ILogger<CorrelationFindingsController>> _logger;
    private readonly CorrelationFindingsController _controller;

    public CorrelationFindingsControllerTests()
    {
        _anomalyDetectionService = new Mock<IAnomalyDetectionService>();
        _correlationDetectionService = new Mock<ICorrelationDetectionService>();
        _resultCache = new Mock<ICorrelationResultCache>();
        _namespaceRepository = new Mock<INamespaceRepository>();
        _logger = new Mock<ILogger<CorrelationFindingsController>>();

        _controller = new CorrelationFindingsController(
            _anomalyDetectionService.Object,
            _correlationDetectionService.Object,
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

    private static Namespace CreateTestNamespace(string name = "test-namespace", string? ownerId = null) =>
        Namespace.Create(
            name,
            $"Endpoint=sb://{name}.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS",
            ownerId: ownerId).Value;

    private static CorrelationFinding CreateTestFinding(string ownerId, Guid namespaceId) =>
        CorrelationFinding.Create(
            ownerId,
            new[]
            {
                new CorrelationMember(namespaceId, "queue-1", AnomalyType.HighMessageVolume, 80, CloudProviderType.Azure),
                new CorrelationMember(Guid.NewGuid(), "queue-2", AnomalyType.HighMessageVolume, 60, CloudProviderType.Azure),
            },
            80,
            "correlated spike");

    #region Constructor Tests

    [Fact]
    public void Constructor_NullAnomalyDetectionService_ShouldThrow()
    {
        var act = () => new CorrelationFindingsController(null!, _correlationDetectionService.Object, _resultCache.Object, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullCorrelationDetectionService_ShouldThrow()
    {
        var act = () => new CorrelationFindingsController(_anomalyDetectionService.Object, null!, _resultCache.Object, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullResultCache_ShouldThrow()
    {
        var act = () => new CorrelationFindingsController(_anomalyDetectionService.Object, _correlationDetectionService.Object, null!, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ShouldThrow()
    {
        var act = () => new CorrelationFindingsController(_anomalyDetectionService.Object, _correlationDetectionService.Object, _resultCache.Object, _namespaceRepository.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region DetectCorrelations Tests

    [Fact]
    public async Task DetectCorrelations_Success_ShouldReturnOk()
    {
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByOwnerAsync(Namespace.SpaOwnerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        _anomalyDetectionService.Setup(a => a.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(Array.Empty<Anomaly>()));

        var finding = CreateTestFinding(Namespace.SpaOwnerId, ns.Id);
        _correlationDetectionService.Setup(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>()))
            .Returns(new[] { finding });

        var result = await _controller.DetectCorrelations();

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<CorrelationDetectionResponse>().Subject;
        response.Findings.Should().HaveCount(1);
        _resultCache.Verify(c => c.Store(It.Is<IEnumerable<CorrelationFinding>>(f => f.Contains(finding))), Times.Once);
    }

    [Fact]
    public async Task DetectCorrelations_CrossProviderFinding_ShouldReportAllProvidersOnResponse()
    {
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByOwnerAsync(Namespace.SpaOwnerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        _anomalyDetectionService.Setup(a => a.DetectAnomaliesAsync(ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(Array.Empty<Anomaly>()));

        var crossProviderFinding = CorrelationFinding.Create(
            Namespace.SpaOwnerId,
            new[]
            {
                new CorrelationMember(ns.Id, "queue-1", AnomalyType.HighMessageVolume, 80, CloudProviderType.Azure),
                new CorrelationMember(Guid.NewGuid(), "queue-2", AnomalyType.HighMessageVolume, 60, CloudProviderType.Aws),
            },
            80,
            "cross-cloud correlated spike");
        _correlationDetectionService.Setup(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>()))
            .Returns(new[] { crossProviderFinding });

        var result = await _controller.DetectCorrelations();

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<CorrelationDetectionResponse>().Subject;
        var findingInfo = response.Findings.Should().ContainSingle().Subject;
        findingInfo.Providers.Should().BeEquivalentTo("Azure", "Aws");
        findingInfo.Members.Should().Contain(m => m.EntityName == "queue-1" && m.Provider == "Azure");
        findingInfo.Members.Should().Contain(m => m.EntityName == "queue-2" && m.Provider == "Aws");
    }

    [Fact]
    public async Task DetectCorrelations_NamespaceLookupFails_ShouldReturnError()
    {
        _namespaceRepository.Setup(r => r.GetByOwnerAsync(Namespace.SpaOwnerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Failure(Error.Internal("DB_ERR", "unavailable")));

        var result = await _controller.DetectCorrelations();

        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task DetectCorrelations_EndTimeNotAfterStartTime_ReturnsBadRequest_WithoutQueryingNamespaces()
    {
        var now = DateTimeOffset.UtcNow;

        var result = await _controller.DetectCorrelations(now, now);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        // Must fail before touching any namespace — a per-namespace DetectAnomaliesAsync
        // validation failure would otherwise be silently swallowed and return 200 with no findings.
        _namespaceRepository.Verify(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DetectCorrelations_InactiveNamespace_IsExcludedFromDetection()
    {
        var activeNs = CreateTestNamespace("active-ns");
        var inactiveNs = CreateTestNamespace("inactive-ns");
        inactiveNs.Deactivate();

        _namespaceRepository.Setup(r => r.GetByOwnerAsync(Namespace.SpaOwnerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { activeNs, inactiveNs }));

        _anomalyDetectionService.Setup(a => a.DetectAnomaliesAsync(activeNs.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Anomaly>>.Success(Array.Empty<Anomaly>()));

        _correlationDetectionService.Setup(c => c.DetectCorrelations(It.IsAny<IReadOnlyList<AnomalyObservation>>()))
            .Returns(Array.Empty<CorrelationFinding>());

        await _controller.DetectCorrelations();

        _anomalyDetectionService.Verify(a => a.DetectAnomaliesAsync(inactiveNs.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region GetById Tests

    [Fact]
    public async Task GetById_Success_ShouldReturnOk()
    {
        var ns = CreateTestNamespace();
        var finding = CreateTestFinding(Namespace.SpaOwnerId, ns.Id);

        _resultCache.Setup(c => c.TryGet(finding.Id)).Returns(finding);
        foreach (var member in finding.Members)
        {
            _namespaceRepository.Setup(r => r.GetByIdAsync(member.NamespaceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Namespace>.Success(CreateTestNamespace($"ns-{member.NamespaceId}")));
        }

        var result = await _controller.GetById(finding.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<CorrelationFindingInfo>().Subject;
        response.Id.Should().Be(finding.Id);
    }

    [Fact]
    public async Task GetById_NotFound_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        _resultCache.Setup(c => c.TryGet(id)).Returns((CorrelationFinding?)null);

        var result = await _controller.GetById(id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetById_OneMemberNamespaceNotAccessible_ShouldReturnNotFound()
    {
        var accessibleNamespaceId = Guid.NewGuid();
        var inaccessibleNamespaceId = Guid.NewGuid();

        var finding = CorrelationFinding.Create(
            "key_trueowner",
            new[]
            {
                new CorrelationMember(accessibleNamespaceId, "queue-1", AnomalyType.HighMessageVolume, 80, CloudProviderType.Azure),
                new CorrelationMember(inaccessibleNamespaceId, "queue-2", AnomalyType.HighMessageVolume, 60, CloudProviderType.Azure),
            },
            80,
            "correlated spike");

        _resultCache.Setup(c => c.TryGet(finding.Id)).Returns(finding);

        // The caller (default SpaOwnerId) can see one member's namespace...
        _namespaceRepository.Setup(r => r.GetByIdAsync(accessibleNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateTestNamespace("ns-a", Namespace.SpaOwnerId)));

        // ...but not the other, which belongs to (and is not shared by) a different owner.
        _namespaceRepository.Setup(r => r.GetByIdAsync(inaccessibleNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateTestNamespace("ns-b", "key_someoneelse")));

        var result = await _controller.GetById(finding.Id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    #endregion
}
