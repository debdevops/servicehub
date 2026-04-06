using FluentAssertions;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Moq;
using ServiceHub.Api.Telemetry;

namespace ServiceHub.UnitTests.Api.Telemetry;

public sealed class HealthCheckTelemetryFilterTests
{
    private readonly Mock<ITelemetryProcessor> _nextMock = new();
    private readonly HealthCheckTelemetryFilter _sut;

    public HealthCheckTelemetryFilterTests()
    {
        _sut = new HealthCheckTelemetryFilter(_nextMock.Object);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/healthz")]
    [InlineData("/ready")]
    [InlineData("/internal/spa-token")]
    [InlineData("/openapi/v1")]
    [InlineData("/scalar/ui")]
    [InlineData("/HEALTH")]          // case-insensitive
    [InlineData("/Healthz")]
    public void Process_ExcludedPath_DropsItem(string path)
    {
        var request = new RequestTelemetry();
        request.Url = new Uri($"https://host{path}");

        _sut.Process(request);

        _nextMock.Verify(n => n.Process(It.IsAny<ITelemetry>()), Times.Never);
    }

    [Theory]
    [InlineData("/api/v1/namespaces")]
    [InlineData("/")]
    [InlineData("/status")]
    public void Process_AllowedPath_ForwardsItem(string path)
    {
        var request = new RequestTelemetry();
        request.Url = new Uri($"https://host{path}");

        _sut.Process(request);

        _nextMock.Verify(n => n.Process(request), Times.Once);
    }

    [Fact]
    public void Process_NonRequestTelemetry_AlwaysForwards()
    {
        var trace = new TraceTelemetry("hello");

        _sut.Process(trace);

        _nextMock.Verify(n => n.Process(trace), Times.Once);
    }

    [Fact]
    public void Process_RequestTelemetry_NullUrl_Forwards()
    {
        var request = new RequestTelemetry();
        request.Url = null;

        _sut.Process(request);

        _nextMock.Verify(n => n.Process(request), Times.Once);
    }
}
