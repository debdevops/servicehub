using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Agent;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.Agent;

public sealed class ReasoningAgentHealthCheckTests
{
    [Fact]
    public void Constructor_NullClient_Throws()
    {
        var act = () => new ReasoningAgentHealthCheck(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task CheckHealthAsync_Available_ReturnsHealthy()
    {
        var client = new Mock<IReasoningAgentClient>();
        client.Setup(c => c.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));
        var sut = new ReasoningAgentHealthCheck(client.Object);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_Unavailable_ReturnsDegradedNotUnhealthy()
    {
        var client = new Mock<IReasoningAgentClient>();
        client.Setup(c => c.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));
        var sut = new ReasoningAgentHealthCheck(client.Object);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_ClientReturnsFailure_ReturnsDegradedNotUnhealthy()
    {
        var client = new Mock<IReasoningAgentClient>();
        client.Setup(c => c.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<bool>(Error.Internal("test.error", "boom")));
        var sut = new ReasoningAgentHealthCheck(client.Object);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
    }
}
