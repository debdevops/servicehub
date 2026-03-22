using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.AI;

namespace ServiceHub.UnitTests.Infrastructure.AI;

public sealed class AIServiceClientTests
{
    private readonly AIServiceClient _sut = new(NullLogger<AIServiceClient>.Instance);

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new AIServiceClient(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task AnalyzeMessagesAsync_ReturnsFailure()
    {
        var result = await _sut.AnalyzeMessagesAsync(Array.Empty<Message>());

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("not yet implemented");
    }

    [Fact]
    public async Task AnalyzeMessagesAsync_NullMessages_ReturnsFailure()
    {
        var result = await _sut.AnalyzeMessagesAsync(null!);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task GetMessageInsightsAsync_ReturnsFailure()
    {
        var message = new Message
        {
            MessageId = "test",
            SequenceNumber = 1,
            Body = "test body",
            EnqueuedTime = DateTimeOffset.UtcNow,
        };

        var result = await _sut.GetMessageInsightsAsync(message);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("not yet implemented");
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsFalse()
    {
        var result = await _sut.IsAvailableAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task DetectAnomaliesAsync_ReturnsFailure()
    {
        var result = await _sut.DetectAnomaliesAsync(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("not yet implemented");
    }

    [Fact]
    public async Task GetAnomalyByIdAsync_ReturnsFailure()
    {
        var result = await _sut.GetAnomalyByIdAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("not yet implemented");
    }
}
