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

public class DriftFindingsControllerTests
{
    private readonly Mock<IDriftDetectionService> _detectionService;
    private readonly Mock<IDriftResultCache> _resultCache;
    private readonly Mock<IContractViolationExportService> _exportService;
    private readonly Mock<INamespaceRepository> _namespaceRepository;
    private readonly Mock<ILogger<DriftFindingsController>> _logger;
    private readonly DriftFindingsController _controller;

    public DriftFindingsControllerTests()
    {
        _detectionService = new Mock<IDriftDetectionService>();
        _resultCache = new Mock<IDriftResultCache>();
        _exportService = new Mock<IContractViolationExportService>();
        _namespaceRepository = new Mock<INamespaceRepository>();
        _logger = new Mock<ILogger<DriftFindingsController>>();

        _controller = new DriftFindingsController(
            _detectionService.Object,
            _resultCache.Object,
            _exportService.Object,
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

    private static DriftFinding CreateTestFinding(Guid namespaceId) =>
        DriftFinding.Create(
            namespaceId,
            "test-queue",
            DriftFindingType.SchemaShapeDrift,
            75,
            "Unusual schema drift in dead letter messages",
            new Dictionary<string, double> { ["driftShare"] = 0.6 },
            new List<string> { "Check recent producer deployments" });

    #region Constructor Tests

    [Fact]
    public void Constructor_NullDetectionService_ShouldThrow()
    {
        var act = () => new DriftFindingsController(null!, _resultCache.Object, _exportService.Object, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullResultCache_ShouldThrow()
    {
        var act = () => new DriftFindingsController(_detectionService.Object, null!, _exportService.Object, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullExportService_ShouldThrow()
    {
        var act = () => new DriftFindingsController(_detectionService.Object, _resultCache.Object, null!, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ShouldThrow()
    {
        var act = () => new DriftFindingsController(_detectionService.Object, _resultCache.Object, _exportService.Object, _namespaceRepository.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region DetectDrift Tests

    [Fact]
    public async Task DetectDrift_Success_ShouldReturnOk()
    {
        var ns = CreateTestNamespace();
        var finding = CreateTestFinding(ns.Id);

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _detectionService.Setup(a => a.DetectDriftAsync(
            ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Success(new List<DriftFinding> { finding }));

        var result = await _controller.DetectDrift(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<DriftDetectionResponse>().Subject;
        response.Findings.Should().HaveCount(1);
        response.NamespaceId.Should().Be(ns.Id);
        _resultCache.Verify(c => c.Store(It.Is<IEnumerable<DriftFinding>>(f => f.Contains(finding))), Times.Once);
    }

    [Fact]
    public async Task DetectDrift_NamespaceNotFound_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        _namespaceRepository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("NOT_FOUND", "Not found")));

        var result = await _controller.DetectDrift(id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DetectDrift_WithTimeWindow_ShouldPassParameters()
    {
        var ns = CreateTestNamespace();
        var start = DateTimeOffset.UtcNow.AddHours(-2);
        var end = DateTimeOffset.UtcNow;

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _detectionService.Setup(a => a.DetectDriftAsync(
            ns.Id, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Success(new List<DriftFinding>()));

        var result = await _controller.DetectDrift(ns.Id, start, end);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<DriftDetectionResponse>().Subject;
        response.StartTime.Should().Be(start);
        response.EndTime.Should().Be(end);
    }

    [Fact]
    public async Task DetectDrift_DetectionFails_ShouldReturnError()
    {
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _detectionService.Setup(a => a.DetectDriftAsync(
            ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Failure(Error.Validation("BAD_REQUEST", "Detection failed")));

        var result = await _controller.DetectDrift(ns.Id);

        result.Result.Should().NotBeOfType<OkObjectResult>();
        _resultCache.Verify(c => c.Store(It.IsAny<IEnumerable<DriftFinding>>()), Times.Never);
    }

    #endregion

    #region Export Tests

    [Fact]
    public async Task Export_Success_ShouldReturnOk()
    {
        var ns = CreateTestNamespace();
        var finding = CreateTestFinding(ns.Id);
        var report = new ContractViolationReport(
            ns.Id,
            ns.Name,
            DateTimeOffset.UtcNow.AddHours(-24),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            new List<ContractViolationEntry>
            {
                new("test-queue", "Message field shape changed", "High", "Unusual schema drift", new List<string> { "Check recent producer deployments" }),
            },
            "# Contract Violation Report — Test NS");

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _detectionService.Setup(a => a.DetectDriftAsync(
            ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Success(new List<DriftFinding> { finding }));

        _exportService.Setup(e => e.BuildReport(ns, It.Is<IReadOnlyList<DriftFinding>>(f => f.Contains(finding)), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>()))
            .Returns(report);

        var result = await _controller.Export(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ContractViolationExportResponse>().Subject;
        response.NamespaceId.Should().Be(ns.Id);
        response.Violations.Should().HaveCount(1);
        response.Violations[0].Priority.Should().Be("High");
        response.MarkdownReport.Should().Be(report.MarkdownReport);
    }

    [Fact]
    public async Task Export_NamespaceNotFound_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        _namespaceRepository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("NOT_FOUND", "Not found")));

        var result = await _controller.Export(id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
        _exportService.Verify(e => e.BuildReport(It.IsAny<Namespace>(), It.IsAny<IReadOnlyList<DriftFinding>>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>()), Times.Never);
    }

    [Fact]
    public async Task Export_DetectionFails_ShouldReturnError()
    {
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _detectionService.Setup(a => a.DetectDriftAsync(
            ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Failure(Error.Validation("BAD_REQUEST", "Detection failed")));

        var result = await _controller.Export(ns.Id);

        result.Result.Should().NotBeOfType<OkObjectResult>();
        _exportService.Verify(e => e.BuildReport(It.IsAny<Namespace>(), It.IsAny<IReadOnlyList<DriftFinding>>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>()), Times.Never);
    }

    [Fact]
    public async Task Export_WithTimeWindow_ShouldPassParameters()
    {
        var ns = CreateTestNamespace();
        var start = DateTimeOffset.UtcNow.AddHours(-2);
        var end = DateTimeOffset.UtcNow;
        var report = new ContractViolationReport(ns.Id, ns.Name, start, end, DateTimeOffset.UtcNow, new List<ContractViolationEntry>(), "no violations");

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _detectionService.Setup(a => a.DetectDriftAsync(
            ns.Id, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DriftFinding>>.Success(new List<DriftFinding>()));

        _exportService.Setup(e => e.BuildReport(ns, It.IsAny<IReadOnlyList<DriftFinding>>(), start, end))
            .Returns(report);

        var result = await _controller.Export(ns.Id, start, end);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ContractViolationExportResponse>().Subject;
        response.StartTime.Should().Be(start);
        response.EndTime.Should().Be(end);
        response.Violations.Should().BeEmpty();
    }

    #endregion

    #region GetById Tests

    [Fact]
    public async Task GetById_Success_ShouldReturnOk()
    {
        var finding = DriftFinding.Create(
            Guid.NewGuid(),
            "test-queue",
            DriftFindingType.PayloadFormatDrift,
            50,
            "Payload format drift");

        _resultCache.Setup(c => c.TryGet(finding.Id)).Returns(finding);
        _namespaceRepository.Setup(r => r.GetByIdAsync(finding.NamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateTestNamespace()));

        var result = await _controller.GetById(finding.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<DriftFindingInfo>().Subject;
        response.Id.Should().Be(finding.Id);
    }

    [Fact]
    public async Task GetById_NamespaceSharedWithCaller_ShouldReturnOk()
    {
        // A collaborator who can trigger DetectDrift via GetOwnedNamespaceAsync's shared-access
        // check must also be able to retrieve the finding it produced — not just the true owner.
        var finding = DriftFinding.Create(
            Guid.NewGuid(),
            "test-queue",
            DriftFindingType.PayloadFormatDrift,
            50,
            "Payload format drift");

        var sharedNamespace = Namespace.Create(
            "shared-namespace",
            "Endpoint=sb://shared.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            ownerId: "key_trueowner").Value;
        sharedNamespace.ShareWith(ServiceHub.Core.Entities.Namespace.SpaOwnerId);

        _resultCache.Setup(c => c.TryGet(finding.Id)).Returns(finding);
        _namespaceRepository.Setup(r => r.GetByIdAsync(finding.NamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(sharedNamespace));

        var result = await _controller.GetById(finding.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<DriftFindingInfo>().Subject;
        response.Id.Should().Be(finding.Id);
    }

    [Fact]
    public async Task GetById_NamespaceOwnedByAnotherTenant_ShouldReturnNotFound()
    {
        var finding = DriftFinding.Create(
            Guid.NewGuid(),
            "test-queue",
            DriftFindingType.PayloadFormatDrift,
            50,
            "Payload format drift");

        var foreignNamespace = Namespace.Create(
            "other-namespace",
            "Endpoint=sb://other.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            ownerId: "key_othertenant").Value;

        _resultCache.Setup(c => c.TryGet(finding.Id)).Returns(finding);
        _namespaceRepository.Setup(r => r.GetByIdAsync(finding.NamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(foreignNamespace));

        var result = await _controller.GetById(finding.Id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetById_NamespaceNotFound_ShouldReturnNotFound()
    {
        var finding = DriftFinding.Create(
            Guid.NewGuid(),
            "test-queue",
            DriftFindingType.PayloadFormatDrift,
            50,
            "Payload format drift");

        _resultCache.Setup(c => c.TryGet(finding.Id)).Returns(finding);
        _namespaceRepository.Setup(r => r.GetByIdAsync(finding.NamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("NOT_FOUND", "Namespace not found")));

        var result = await _controller.GetById(finding.Id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetById_NamespaceLookupFails_ShouldPropagateError()
    {
        var finding = DriftFinding.Create(
            Guid.NewGuid(),
            "test-queue",
            DriftFindingType.PayloadFormatDrift,
            50,
            "Payload format drift");

        _resultCache.Setup(c => c.TryGet(finding.Id)).Returns(finding);
        _namespaceRepository.Setup(r => r.GetByIdAsync(finding.NamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.Internal("DB_ERR", "Database unavailable")));

        var result = await _controller.GetById(finding.Id);

        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task GetById_NotFound_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        _resultCache.Setup(c => c.TryGet(id)).Returns((DriftFinding?)null);

        var result = await _controller.GetById(id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    #endregion
}
