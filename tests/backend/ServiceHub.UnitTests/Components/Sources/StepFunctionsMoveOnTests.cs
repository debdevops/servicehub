using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using FluentAssertions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;
using ServiceHub.Providers.Aws;
using ServiceHub.Providers.Aws.MoveOn;

namespace ServiceHub.UnitTests.Components.Sources;

/// <summary>The Step Functions mover against a fake AWS: it makes exactly one RedriveExecution call, with the idempotency token, and tells "did nothing" from "may have".</summary>
public sealed class StepFunctionsMoveOnTests
{
    private const string Run = "arn:aws:states:us-east-1:123456789012:execution:orders:run-1";

    private readonly Mock<IAmazonStepFunctions> _cloud = new();
    private readonly StepFunctionsMoveOn _mover;
    private readonly Namespace _ns = Namespace.Create(
        "sqs.us-east-1.amazonaws.com", "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
        provider: CloudProviderType.Aws, awsRegion: "us-east-1").Value;

    public StepFunctionsMoveOnTests()
    {
        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetStepFunctionsClient(It.IsAny<Namespace>())).Returns(_cloud.Object);
        _mover = new StepFunctionsMoveOn(factory.Object);
    }

    private void CloudAnswers(Exception? error = null)
    {
        var setup = _cloud.Setup(c => c.RedriveExecutionAsync(It.IsAny<RedriveExecutionRequest>(), It.IsAny<CancellationToken>()));
        if (error is null)
        {
            setup.ReturnsAsync(new RedriveExecutionResponse { RedriveDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) });
        }
        else
        {
            setup.ThrowsAsync(error);
        }
    }

    [Fact]
    public async Task It_asks_aws_once_with_the_token_and_reads_nothing_else()
    {
        CloudAnswers();

        var result = await _mover.RedriveAsync(_ns, Run, "token-1", default);

        result.IsSuccess.Should().BeTrue();
        result.Value.AcceptedAtUtc.Should().Be(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        _cloud.Verify(c => c.RedriveExecutionAsync(It.Is<RedriveExecutionRequest>(r => r.ExecutionArn == Run && r.ClientToken == "token-1"), It.IsAny<CancellationToken>()), Times.Once);
        _cloud.Invocations.Should().ContainSingle("no describe, list or start call is made: the run's input is never read");
    }

    [Theory]
    [InlineData("arn:aws:states:eu-west-1:123456789012:execution:orders:run-1")]
    [InlineData("arn:aws:states:us-east-1:123456789012:stateMachine:orders")]
    [InlineData("not-an-arn")]
    public async Task A_run_outside_this_connections_region_or_that_is_not_an_execution_is_refused_before_any_call(string item)
    {
        _mover.Owns(_ns, item).Should().BeFalse();

        var result = await _mover.RedriveAsync(_ns, item, "t", default);

        result.IsFailure.Should().BeTrue();
        _cloud.Invocations.Should().BeEmpty();
    }

    [Fact]
    public void The_place_of_a_run_is_its_state_machine()
    {
        _mover.PlaceOf(Run).Should().Be("arn:aws:states:us-east-1:123456789012:stateMachine:orders");
    }

    [Theory]
    [InlineData(typeof(ExecutionNotRedrivableException), ErrorType.Conflict)]
    [InlineData(typeof(ExecutionDoesNotExistException), ErrorType.NotFound)]
    [InlineData(typeof(ExecutionLimitExceededException), ErrorType.RateLimited)]
    [InlineData(typeof(InvalidArnException), ErrorType.Validation)]
    public async Task What_aws_refuses_without_acting_is_told_apart_from_what_may_have_happened(Type exception, ErrorType expected)
    {
        CloudAnswers((Exception)Activator.CreateInstance(exception, "nope")!);

        (await _mover.RedriveAsync(_ns, Run, "t", default)).Error.Type.Should().Be(expected);
    }

    [Fact]
    public async Task A_missing_permission_says_what_is_needed_and_anything_else_is_left_unknown()
    {
        CloudAnswers(new AmazonStepFunctionsException("denied") { ErrorCode = "AccessDeniedException" });
        var denied = (await _mover.RedriveAsync(_ns, Run, "t", default)).Error;
        CloudAnswers(new AmazonStepFunctionsException("The call failed (hunter2)"));
        var other = (await _mover.RedriveAsync(_ns, Run, "t", default)).Error;

        denied.Type.Should().Be(ErrorType.Forbidden);
        denied.Message.Should().Contain("states:RedriveExecution");
        other.Type.Should().Be(ErrorType.ExternalService, "an upstream failure may have struck after AWS acted, so the service records it as unknown");
    }
}
