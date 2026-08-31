using FluentAssertions;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.SignatureReplay;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.SignatureReplay;

public sealed class ReplayFailureClassifierTests
{
    [Theory]
    [InlineData(ErrorType.NotFound, ReplayFailureReason.NotFound)]
    [InlineData(ErrorType.Conflict, ReplayFailureReason.AmbiguousOutcome)]
    [InlineData(ErrorType.Timeout, ReplayFailureReason.Retryable)]
    [InlineData(ErrorType.RateLimited, ReplayFailureReason.Retryable)]
    [InlineData(ErrorType.ExternalService, ReplayFailureReason.ProviderError)]
    [InlineData(ErrorType.Internal, ReplayFailureReason.ProviderError)]
    [InlineData(ErrorType.Validation, ReplayFailureReason.Other)]
    [InlineData(ErrorType.BusinessRule, ReplayFailureReason.Other)]
    [InlineData(ErrorType.Unauthorized, ReplayFailureReason.Other)]
    [InlineData(ErrorType.Forbidden, ReplayFailureReason.Other)]
    public void Classify_MapsEveryErrorType(ErrorType errorType, ReplayFailureReason expected)
    {
        var error = new Error("Test.Code", "test message", errorType);

        ReplayFailureClassifier.Classify(error).Should().Be(expected);
    }
}
