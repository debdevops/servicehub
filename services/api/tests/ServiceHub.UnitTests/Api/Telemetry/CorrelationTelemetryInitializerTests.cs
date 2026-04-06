using FluentAssertions;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Http;
using ServiceHub.Api.Telemetry;

namespace ServiceHub.UnitTests.Api.Telemetry;

public sealed class CorrelationTelemetryInitializerTests
{
    private static CorrelationTelemetryInitializer Create(HttpContext? context = null)
    {
        var accessor = new HttpContextAccessor { HttpContext = context };
        return new CorrelationTelemetryInitializer(accessor);
    }

    [Fact]
    public void Initialize_NoHttpContext_DoesNotThrow()
    {
        var sut = Create(null);
        var telemetry = new RequestTelemetry();

        var act = () => sut.Initialize(telemetry);

        act.Should().NotThrow();
    }

    [Fact]
    public void Initialize_WithCorrelationId_SetsCustomProperty()
    {
        var context = new DefaultHttpContext();
        context.Items["CorrelationId"] = "test-correlation-123";
        var sut = Create(context);
        var telemetry = new RequestTelemetry();

        sut.Initialize(telemetry);

        telemetry.Properties["CorrelationId"].Should().Be("test-correlation-123");
    }

    [Fact]
    public void Initialize_NullCorrelationId_DoesNotSetProperty()
    {
        var context = new DefaultHttpContext();
        context.Items["CorrelationId"] = null;
        var sut = Create(context);
        var telemetry = new RequestTelemetry();

        sut.Initialize(telemetry);

        telemetry.Properties.Should().NotContainKey("CorrelationId");
    }

    [Fact]
    public void Initialize_EmptyCorrelationId_DoesNotSetProperty()
    {
        var context = new DefaultHttpContext();
        context.Items["CorrelationId"] = string.Empty;
        var sut = Create(context);
        var telemetry = new RequestTelemetry();

        sut.Initialize(telemetry);

        telemetry.Properties.Should().NotContainKey("CorrelationId");
    }

    [Fact]
    public void Initialize_SetsComponentVersion()
    {
        var context = new DefaultHttpContext();
        var sut = Create(context);
        var telemetry = new RequestTelemetry();

        sut.Initialize(telemetry);

        telemetry.Context.Component.Version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Initialize_TelemetryNotISupportProperties_DoesNotThrow()
    {
        var context = new DefaultHttpContext();
        context.Items["CorrelationId"] = "id-123";
        var sut = Create(context);
        // AvailabilityTelemetry implements ISupportProperties — use SessionTelemetry which does not
        var telemetry = new MetricTelemetry("my-metric", 1.0);

        var act = () => sut.Initialize(telemetry);

        act.Should().NotThrow();
    }
}
