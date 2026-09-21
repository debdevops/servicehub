using FluentAssertions;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Moq;
using ServiceHub.Api.Telemetry;

namespace ServiceHub.UnitTests.Api.Telemetry;

public sealed class SensitiveDataTelemetryProcessorTests
{
    private readonly Mock<ITelemetryProcessor> _nextMock = new();
    private readonly SensitiveDataTelemetryProcessor _sut;

    public SensitiveDataTelemetryProcessorTests()
    {
        _sut = new SensitiveDataTelemetryProcessor(_nextMock.Object);
    }

    // ── Always forwards ──────────────────────────────────────────────────────

    [Fact]
    public void Process_AlwaysCallsNext()
    {
        var trace = new TraceTelemetry("hello world");
        _sut.Process(trace);
        _nextMock.Verify(n => n.Process(trace), Times.Once);
    }

    // ── RequestTelemetry — URL redaction ─────────────────────────────────────

    [Theory]
    [InlineData("key")]
    [InlineData("token")]
    [InlineData("secret")]
    [InlineData("connectionstring")]
    [InlineData("password")]
    [InlineData("apikey")]
    [InlineData("api_key")]
    [InlineData("KEY")]         // case-insensitive
    [InlineData("Token")]
    public void Process_RequestWithSensitiveQueryParam_RedactsValue(string paramName)
    {
        var request = new RequestTelemetry();
        request.Url = new Uri($"https://host/api/v1/resource?{paramName}=super-secret");

        _sut.Process(request);

        // URL-encode of "[REDACTED]" in a query string is "%5BREDACTED%5D"
        var rawQuery = request.Url!.Query;
        rawQuery.Should().ContainAny("[REDACTED]", "%5BREDACTED%5D", "REDACTED");
        rawQuery.Should().NotContain("super-secret");
    }

    [Fact]
    public void Process_RequestWithSafeQueryParam_DoesNotRedact()
    {
        var request = new RequestTelemetry();
        request.Url = new Uri("https://host/api/v1/resource?page=1&size=10");

        _sut.Process(request);

        request.Url.Query.Should().Contain("page=1");
        request.Url.Query.Should().Contain("size=10");
    }

    [Fact]
    public void Process_RequestWithNoQuery_DoesNotModifyUrl()
    {
        var originalUri = new Uri("https://host/api/v1/namespaces");
        var request = new RequestTelemetry();
        request.Url = originalUri;

        _sut.Process(request);

        request.Url.Should().Be(originalUri);
    }

    [Fact]
    public void Process_RequestWithNullUrl_DoesNotThrow()
    {
        var request = new RequestTelemetry();
        request.Url = null;

        var act = () => _sut.Process(request);

        act.Should().NotThrow();
    }

    // ── TraceTelemetry — message redaction ───────────────────────────────────

    [Fact]
    public void Process_TraceTelemetry_RedactsConnectionStringInMessage()
    {
        var trace = new TraceTelemetry("Connection: Endpoint=sb://mybus.servicebus.windows.net/;SharedAccessKey=abc123");

        _sut.Process(trace);

        trace.Message.Should().NotContain("abc123");
    }

    [Fact]
    public void Process_TraceTelemetry_SafeMessage_Unchanged()
    {
        var trace = new TraceTelemetry("Processing message id=42");

        _sut.Process(trace);

        trace.Message.Should().Be("Processing message id=42");
    }

    // ── ExceptionTelemetry — redaction ───────────────────────────────────────

    [Fact]
    public void Process_ExceptionTelemetry_NullException_DoesNotThrow()
    {
        var exception = new ExceptionTelemetry();

        var act = () => _sut.Process(exception);

        act.Should().NotThrow();
    }

    [Fact]
    public void Process_ExceptionTelemetry_RedactsMessageAndProperties()
    {
        var exception = new ExceptionTelemetry(new InvalidOperationException("boom"));
        exception.Message = "SharedAccessKey=secret123";
        exception.Properties["detail"] = "Endpoint=sb://x.servicebus.windows.net/;SharedAccessKey=secret";

        _sut.Process(exception);

        exception.Message.Should().NotContain("secret123");
        exception.Properties["detail"].Should().NotContain("secret");
    }

    // ── DependencyTelemetry — data redaction ─────────────────────────────────

    [Theory]
    [InlineData("SharedAccessKey=abc123")]
    [InlineData("AccountKey=xyz")]
    [InlineData("SharedAccessSignature=sig")]
    [InlineData("sharedaccesskey=lower")]        // case-insensitive
    public void Process_DependencyWithSensitiveData_RedactsData(string data)
    {
        var dependency = new DependencyTelemetry { Data = data };

        _sut.Process(dependency);

        dependency.Data.Should().Contain("[REDACTED");
        dependency.Data.Should().NotBe(data);
    }

    [Fact]
    public void Process_DependencyWithSafeData_DoesNotRedact()
    {
        var dependency = new DependencyTelemetry { Data = "SELECT 1" };

        _sut.Process(dependency);

        dependency.Data.Should().Be("SELECT 1");
    }

    [Fact]
    public void Process_DependencyWithNullData_DoesNotThrow()
    {
        var dependency = new DependencyTelemetry { Data = null };

        var act = () => _sut.Process(dependency);

        act.Should().NotThrow();
    }

    [Fact]
    public void Process_DependencyWithEmptyData_DoesNotRedact()
    {
        var dependency = new DependencyTelemetry { Data = string.Empty };

        _sut.Process(dependency);

        dependency.Data.Should().BeEmpty();
    }
}
