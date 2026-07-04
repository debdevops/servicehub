using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Shared.Results;
using System.Text;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public class AuditControllerTests
{
    private readonly Mock<IAuditService> _auditService;
    private readonly Mock<ILogger<AuditController>> _logger;
    private readonly AuditController _controller;

    public AuditControllerTests()
    {
        _auditService = new Mock<IAuditService>();
        _logger = new Mock<ILogger<AuditController>>();

        _controller = new AuditController(_auditService.Object, _logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Fact]
    public void Constructor_NullService_ShouldThrow()
    {
        var act = () => new AuditController(null!, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ShouldThrow()
    {
        var act = () => new AuditController(_auditService.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task GetLogs_Success_ShouldReturnOk()
    {
        var logs = new List<AuditLog>
        {
            new AuditLog
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                OwnerId = "__spa__",
                UserIdentity = "test@user.com",
                Action = "Messages.Replay",
                Outcome = "Success"
            }
        };

        var pageResult = new AuditPageResult
        {
            Items = logs,
            TotalCount = 1,
            Page = 1,
            PageSize = 50
        };

        _auditService.Setup(s => s.GetLogsAsync(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuditPageResult>.Success(pageResult));

        var result = await _controller.GetLogs();

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<AuditPageResponse>().Subject;
        response.TotalCount.Should().Be(1);
        response.Items.Should().HaveCount(1);
        response.Items[0].Action.Should().Be("Messages.Replay");
    }

    [Fact]
    public async Task GetLogs_Failure_ShouldReturnError()
    {
        var error = Error.NotFound("AUDIT_NOT_FOUND", "No audit logs found");

        _auditService.Setup(s => s.GetLogsAsync(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuditPageResult>.Failure(error));

        var result = await _controller.GetLogs();

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetSummary_Success_ShouldReturnOk()
    {
        var summary = new AuditSummary
        {
            TotalEvents = 10,
            SuccessCount = 8,
            FailureCount = 2,
            PartialCount = 0,
            ActiveUsers = 2
        };

        _auditService.Setup(s => s.GetSummaryAsync(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuditSummary>.Success(summary));

        var result = await _controller.GetSummary();

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<AuditSummaryResponse>().Subject;
        response.TotalEvents.Should().Be(10);
        response.SuccessRate.Should().Be(80.0);
    }

    [Fact]
    public async Task GetSummary_Failure_ShouldReturnError()
    {
        var error = Error.NotFound("SUMMARY_ERROR", "Could not fetch summary");

        _auditService.Setup(s => s.GetSummaryAsync(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuditSummary>.Failure(error));

        var result = await _controller.GetSummary();

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Export_CsvFormat_ShouldReturnFile()
    {
        var logs = new List<AuditLog>
        {
            new AuditLog
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                OwnerId = "__spa__",
                UserIdentity = "test@user.com",
                Action = "Messages.Replay",
                Outcome = "Success"
            }
        };

        _auditService.Setup(s => s.ExportAsync(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<AuditLog>>.Success(logs));

        var result = await _controller.Export(format: "csv");

        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.ContentType.Should().Be("text/csv");
        var csvContent = Encoding.UTF8.GetString(fileResult.FileContents);
        csvContent.Should().Contain("Messages.Replay");
    }

    [Fact]
    public async Task Export_JsonFormat_ShouldReturnFile()
    {
        var logs = new List<AuditLog>
        {
            new AuditLog
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                OwnerId = "__spa__",
                UserIdentity = "test@user.com",
                Action = "Messages.Replay",
                Outcome = "Success"
            }
        };

        _auditService.Setup(s => s.ExportAsync(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<AuditLog>>.Success(logs));

        var result = await _controller.Export(format: "json");

        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.ContentType.Should().Be("application/json");
        var jsonContent = Encoding.UTF8.GetString(fileResult.FileContents);
        jsonContent.Should().Contain("Messages.Replay");
    }
}
