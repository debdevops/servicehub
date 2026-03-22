using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.ServiceBus;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.ServiceBus;

public sealed class ServiceBusHealthCheckTests
{
    private readonly Mock<IServiceBusClientCache> _cacheMock = new();
    private readonly Mock<INamespaceRepository> _repoMock = new();
    private readonly ServiceBusHealthCheck _sut;

    private static readonly string ValidConnString =
        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=TestPolicy;SharedAccessKey=abc123=";

    public ServiceBusHealthCheckTests()
    {
        _sut = new ServiceBusHealthCheck(
            _cacheMock.Object,
            _repoMock.Object,
            NullLogger<ServiceBusHealthCheck>.Instance);
    }

    // ── Constructor ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullCache_Throws()
    {
        var act = () => new ServiceBusHealthCheck(null!, _repoMock.Object, NullLogger<ServiceBusHealthCheck>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("clientCache");
    }

    [Fact]
    public void Constructor_NullRepo_Throws()
    {
        var act = () => new ServiceBusHealthCheck(_cacheMock.Object, null!, NullLogger<ServiceBusHealthCheck>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("namespaceRepository");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new ServiceBusHealthCheck(_cacheMock.Object, _repoMock.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── Helper ──────────────────────────────────────────────────────

    private static Namespace CreateNamespace(string name = "test-ns")
    {
        return Namespace.Create(name, ValidConnString).Value;
    }

    // ═══════════════════════════════════════════════════════════════
    // CheckHealthAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckHealth_GetActiveFails_ReturnsDegraded()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Failure(Error.Internal("err", "fail")));

        var result = await _sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("Failed to retrieve");
    }

    [Fact]
    public async Task CheckHealth_NoActiveNamespaces_ReturnsHealthy()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var result = await _sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("No active");
        result.Data["TotalNamespaces"].Should().Be(0);
    }

    [Fact]
    public async Task CheckHealth_AllHealthy_ReturnsHealthy()
    {
        var ns = CreateNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var wrapperMock = new Mock<IServiceBusClientWrapper>();
        wrapperMock.Setup(w => w.TestConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));
        _cacheMock.Setup(c => c.GetOrCreate(ns.Id, ns.ConnectionString!))
            .Returns(wrapperMock.Object);

        var result = await _sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["HealthyNamespaces"].Should().Be(1);
        result.Data["UnhealthyNamespaces"].Should().Be(0);
    }

    [Fact]
    public async Task CheckHealth_AllUnhealthy_ReturnsUnhealthy()
    {
        var ns = CreateNamespace();
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns }));

        var wrapperMock = new Mock<IServiceBusClientWrapper>();
        wrapperMock.Setup(w => w.TestConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(Error.ExternalService("err", "connection failed")));
        _cacheMock.Setup(c => c.GetOrCreate(ns.Id, ns.ConnectionString!))
            .Returns(wrapperMock.Object);

        var result = await _sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Data["UnhealthyNamespaces"].Should().Be(1);
    }

    [Fact]
    public async Task CheckHealth_MixedHealth_ReturnsDegraded()
    {
        var ns1 = CreateNamespace("healthy-ns");
        var ns2 = CreateNamespace("unhealthy-ns");

        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new[] { ns1, ns2 }));

        var healthyWrapper = new Mock<IServiceBusClientWrapper>();
        healthyWrapper.Setup(w => w.TestConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));
        _cacheMock.Setup(c => c.GetOrCreate(ns1.Id, ns1.ConnectionString!))
            .Returns(healthyWrapper.Object);

        var unhealthyWrapper = new Mock<IServiceBusClientWrapper>();
        unhealthyWrapper.Setup(w => w.TestConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(Error.ExternalService("err", "fail")));
        _cacheMock.Setup(c => c.GetOrCreate(ns2.Id, ns2.ConnectionString!))
            .Returns(unhealthyWrapper.Object);

        var result = await _sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data["HealthyNamespaces"].Should().Be(1);
        result.Data["UnhealthyNamespaces"].Should().Be(1);
    }

    [Fact]
    public async Task CheckHealth_ExceptionThrown_ReturnsUnhealthy()
    {
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Boom"));

        var result = await _sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("exception");
    }

    // ═══════════════════════════════════════════════════════════════
    // CheckNamespaceHealthAsync (public overload)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckNamespaceHealth_NotFound_ReturnsFailure()
    {
        var id = Guid.NewGuid();
        _repoMock.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("ns", "not found")));

        var result = await _sut.CheckNamespaceHealthAsync(id);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task CheckNamespaceHealth_HealthyNamespace_ReturnsSuccess()
    {
        var ns = CreateNamespace();
        _repoMock.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var wrapperMock = new Mock<IServiceBusClientWrapper>();
        wrapperMock.Setup(w => w.TestConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));
        _cacheMock.Setup(c => c.GetOrCreate(ns.Id, ns.ConnectionString!))
            .Returns(wrapperMock.Object);

        var result = await _sut.CheckNamespaceHealthAsync(ns.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task CheckNamespaceHealth_CacheThrows_ReturnsFailure()
    {
        var ns = CreateNamespace();
        _repoMock.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _cacheMock.Setup(c => c.GetOrCreate(ns.Id, ns.ConnectionString!))
            .Throws(new InvalidOperationException("cache error"));

        var result = await _sut.CheckNamespaceHealthAsync(ns.Id);

        result.IsFailure.Should().BeTrue();
    }
}
