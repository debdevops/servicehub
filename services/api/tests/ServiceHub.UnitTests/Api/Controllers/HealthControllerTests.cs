using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Controllers;

namespace ServiceHub.UnitTests.Api.Controllers;

public class HealthControllerTests
{
    private readonly HealthController _sut;

    public HealthControllerTests()
    {
        var logger = new Mock<ILogger<HealthController>>();
        _sut = new HealthController(logger.Object);
    }

    [Fact]
    public void GetVersion_ShouldReturnOk()
    {
        var result = _sut.GetVersion();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }

    [Fact]
    public void GetVersion_ShouldReturnVersionInfo()
    {
        var result = _sut.GetVersion() as OkObjectResult;

        result.Should().NotBeNull();
        var versionInfo = result!.Value as VersionInfo;
        versionInfo.Should().NotBeNull();
        versionInfo!.Version.Should().NotBeNullOrEmpty();
        versionInfo.FrameworkDescription.Should().NotBeNullOrEmpty();
        versionInfo.OsDescription.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetVersion_ShouldIncludeEnvironmentInfo()
    {
        var result = _sut.GetVersion() as OkObjectResult;
        var versionInfo = result!.Value as VersionInfo;

        versionInfo!.MachineName.Should().NotBeNullOrEmpty();
        versionInfo.Environment.Should().NotBeNullOrEmpty();
        versionInfo.StartedAt.Should().BeBefore(DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void GetStatus_ShouldReturnOk()
    {
        var result = _sut.GetStatus();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }

    [Fact]
    public void GetStatus_ShouldReturnStatusInfo()
    {
        var result = _sut.GetStatus() as OkObjectResult;

        result.Should().NotBeNull();
        var statusInfo = result!.Value as StatusInfo;
        statusInfo.Should().NotBeNull();
        statusInfo!.IsHealthy.Should().BeTrue();
        statusInfo.MemoryUsageMb.Should().BeGreaterThan(0);
        statusInfo.ThreadCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetStatus_ShouldIncludeGCInfo()
    {
        var result = _sut.GetStatus() as OkObjectResult;
        var statusInfo = result!.Value as StatusInfo;

        statusInfo!.Gen0Collections.Should().BeGreaterThanOrEqualTo(0);
        statusInfo.Gen1Collections.Should().BeGreaterThanOrEqualTo(0);
        statusInfo.Gen2Collections.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        var act = () => new HealthController(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }
}
