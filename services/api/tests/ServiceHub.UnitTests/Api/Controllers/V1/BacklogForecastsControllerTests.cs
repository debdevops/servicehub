using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public class BacklogForecastsControllerTests
{
    private readonly Mock<IBacklogForecastService> _forecastService;
    private readonly Mock<IBacklogForecastResultCache> _resultCache;
    private readonly Mock<INamespaceRepository> _namespaceRepository;
    private readonly Mock<ILogger<BacklogForecastsController>> _logger;
    private readonly BacklogForecastsController _controller;

    public BacklogForecastsControllerTests()
    {
        _forecastService = new Mock<IBacklogForecastService>();
        _resultCache = new Mock<IBacklogForecastResultCache>();
        _namespaceRepository = new Mock<INamespaceRepository>();
        _logger = new Mock<ILogger<BacklogForecastsController>>();

        _controller = new BacklogForecastsController(
            _forecastService.Object,
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

    private static BacklogForecast CreateTestForecast(Guid namespaceId) =>
        BacklogForecast.Create(
            namespaceId,
            "test-queue",
            80,
            10,
            150,
            7,
            75,
            "projected backlog breach",
            new Dictionary<string, double> { ["currentBacklogCount"] = 80 },
            new List<string> { "Schedule a bulk replay" });

    #region Constructor Tests

    [Fact]
    public void Constructor_NullForecastService_ShouldThrow()
    {
        var act = () => new BacklogForecastsController(null!, _resultCache.Object, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullResultCache_ShouldThrow()
    {
        var act = () => new BacklogForecastsController(_forecastService.Object, null!, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ShouldThrow()
    {
        var act = () => new BacklogForecastsController(_forecastService.Object, _resultCache.Object, _namespaceRepository.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Forecast Tests

    [Fact]
    public async Task Forecast_Success_ShouldReturnOk()
    {
        var ns = CreateTestNamespace();
        var forecast = CreateTestForecast(ns.Id);

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _forecastService.Setup(a => a.ForecastAsync(
            ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<BacklogForecast>>.Success(new List<BacklogForecast> { forecast }));

        var result = await _controller.Forecast(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<BacklogForecastResponse>().Subject;
        response.Forecasts.Should().HaveCount(1);
        response.NamespaceId.Should().Be(ns.Id);
        _resultCache.Verify(c => c.Store(It.Is<IEnumerable<BacklogForecast>>(f => f.Contains(forecast))), Times.Once);
    }

    [Fact]
    public async Task Forecast_NamespaceNotFound_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        _namespaceRepository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("NOT_FOUND", "Not found")));

        var result = await _controller.Forecast(id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Forecast_WithTimeWindowAndThreshold_ShouldPassParameters()
    {
        var ns = CreateTestNamespace();
        var start = DateTimeOffset.UtcNow.AddHours(-2);
        var end = DateTimeOffset.UtcNow;

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _forecastService.Setup(a => a.ForecastAsync(
            ns.Id, start, end, 250, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<BacklogForecast>>.Success(new List<BacklogForecast>()));

        var result = await _controller.Forecast(ns.Id, start, end, 250);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<BacklogForecastResponse>().Subject;
        response.StartTime.Should().Be(start);
        response.EndTime.Should().Be(end);
    }

    [Fact]
    public async Task Forecast_ServiceFails_ShouldReturnError()
    {
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _forecastService.Setup(a => a.ForecastAsync(
            ns.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<BacklogForecast>>.Failure(Error.Validation("BAD_REQUEST", "Forecast failed")));

        var result = await _controller.Forecast(ns.Id);

        result.Result.Should().NotBeOfType<OkObjectResult>();
        _resultCache.Verify(c => c.Store(It.IsAny<IEnumerable<BacklogForecast>>()), Times.Never);
    }

    #endregion

    #region GetById Tests

    [Fact]
    public async Task GetById_Success_ShouldReturnOk()
    {
        var forecast = CreateTestForecast(Guid.NewGuid());

        _resultCache.Setup(c => c.TryGet(forecast.Id)).Returns(forecast);
        _namespaceRepository.Setup(r => r.GetByIdAsync(forecast.NamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateTestNamespace()));

        var result = await _controller.GetById(forecast.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<BacklogForecastInfo>().Subject;
        response.Id.Should().Be(forecast.Id);
    }

    [Fact]
    public async Task GetById_NamespaceOwnedByAnotherTenant_ShouldReturnNotFound()
    {
        var forecast = CreateTestForecast(Guid.NewGuid());

        var foreignNamespace = Namespace.Create(
            "other-namespace",
            "Endpoint=sb://other.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            ownerId: "key_othertenant").Value;

        _resultCache.Setup(c => c.TryGet(forecast.Id)).Returns(forecast);
        _namespaceRepository.Setup(r => r.GetByIdAsync(forecast.NamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(foreignNamespace));

        var result = await _controller.GetById(forecast.Id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetById_NamespaceNotFound_ShouldReturnNotFound()
    {
        var forecast = CreateTestForecast(Guid.NewGuid());

        _resultCache.Setup(c => c.TryGet(forecast.Id)).Returns(forecast);
        _namespaceRepository.Setup(r => r.GetByIdAsync(forecast.NamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("NOT_FOUND", "Namespace not found")));

        var result = await _controller.GetById(forecast.Id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetById_NamespaceLookupFails_ShouldPropagateError()
    {
        var forecast = CreateTestForecast(Guid.NewGuid());

        _resultCache.Setup(c => c.TryGet(forecast.Id)).Returns(forecast);
        _namespaceRepository.Setup(r => r.GetByIdAsync(forecast.NamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.Internal("DB_ERR", "Database unavailable")));

        var result = await _controller.GetById(forecast.Id);

        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task GetById_NotFound_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        _resultCache.Setup(c => c.TryGet(id)).Returns((BacklogForecast?)null);

        var result = await _controller.GetById(id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    #endregion
}
