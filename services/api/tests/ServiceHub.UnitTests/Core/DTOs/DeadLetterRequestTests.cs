using FluentAssertions;
using ServiceHub.Core.DTOs.Requests;

namespace ServiceHub.UnitTests.Core.DTOs;

public sealed class DeadLetterRequestTests
{
    [Fact]
    public void MaxMessageCount_Is10()
    {
        DeadLetterRequest.MaxMessageCount.Should().Be(10);
    }

    [Fact]
    public void ValidatedMessageCount_ClampsToMin1()
    {
        var req = new DeadLetterRequest(Guid.NewGuid(), "queue", null, MessageCount: 0);
        req.ValidatedMessageCount.Should().Be(1);
    }

    [Fact]
    public void ValidatedMessageCount_ClampsToMax10()
    {
        var req = new DeadLetterRequest(Guid.NewGuid(), "queue", null, MessageCount: 50);
        req.ValidatedMessageCount.Should().Be(10);
    }

    [Fact]
    public void ValidatedMessageCount_ReturnsActualValue_WhenWithinBounds()
    {
        var req = new DeadLetterRequest(Guid.NewGuid(), "queue", null, MessageCount: 5);
        req.ValidatedMessageCount.Should().Be(5);
    }

    [Fact]
    public void Defaults_AreCorrect()
    {
        var req = new DeadLetterRequest(Guid.NewGuid(), "queue", null);
        req.MessageCount.Should().Be(1);
        req.Reason.Should().Be("ManualDeadLetter");
        req.ErrorDescription.Should().BeNull();
    }
}
