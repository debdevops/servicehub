using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using FluentAssertions;
using Moq;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Providers.Aws;
using ServiceHub.Providers.Aws.Components;

namespace ServiceHub.UnitTests.Components.Sources;

/// <summary>The Step Functions source against a fake cloud: the conformance kit plus what is particular to this service.</summary>
public sealed class StepFunctionsSourceTests : ComponentSourceConformance
{
    private const string Machine = "arn:aws:states:us-east-1:123456789012:stateMachine:orders";
    private const string Execution = "arn:aws:states:us-east-1:123456789012:execution:orders:run-1";

    private readonly Mock<IAmazonStepFunctions> _cloud = new();
    private readonly Mock<IAwsClientFactory> _factory = new();
    private readonly StepFunctionsSource _source;
    private readonly Namespace _ns = Namespace.Create(
        "sqs.us-east-1.amazonaws.com", "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
        provider: CloudProviderType.Aws, awsRegion: "us-east-1").Value;

    public StepFunctionsSourceTests()
    {
        _factory.Setup(f => f.GetStepFunctionsClient(It.IsAny<Namespace>())).Returns(_cloud.Object);
        _source = new StepFunctionsSource(_factory.Object);
        _cloud.Setup(c => c.ListStateMachinesAsync(It.IsAny<ListStateMachinesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListStateMachinesResponse { StateMachines = [new StateMachineListItem { Name = "orders", StateMachineArn = Machine }] });
    }

    protected override IComponentSource Source => _source;

    protected override Namespace Connection => _ns;

    protected override string PlaceId => Machine;

    protected override string ItemId => Execution;

    protected override IReadOnlyList<string> CloudCalls =>
        [.. _cloud.Invocations.Where(i => !i.Method.IsSpecialName && i.Method.Name != "Dispose").Select(i => i.Method.Name.Replace("Async", string.Empty, StringComparison.Ordinal))];

    protected override void ArrangeFailures(int count)
    {
        var baseTime = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var all = Enumerable.Range(0, count)
            .Select(i => new ExecutionListItem { ExecutionArn = $"{Execution}{i}", Name = $"run-{i}", StopDate = baseTime.AddMinutes(i), Status = ExecutionStatus.FAILED })
            .OrderByDescending(e => e.StopDate)
            .ToList();
        _cloud.Setup(c => c.ListExecutionsAsync(It.IsAny<ListExecutionsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ListExecutionsRequest r, CancellationToken _) => r.StatusFilter == "FAILED"
                ? new ListExecutionsResponse { Executions = [.. all.Take(r.MaxResults)], NextToken = all.Count > r.MaxResults ? "more" : null }
                : new ListExecutionsResponse { Executions = [] });
        SetDetail(error: "States.TaskFailed", cause: "the task failed");
    }

    protected override void ArrangeAccessDenied()
    {
        var denied = new AmazonStepFunctionsException("not authorised") { ErrorCode = "AccessDeniedException" };
        _cloud.Setup(c => c.ListStateMachinesAsync(It.IsAny<ListStateMachinesRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(denied);
    }

    protected override void ArrangeCloudFailure(string message)
    {
        var failure = new AmazonStepFunctionsException(message) { ErrorCode = "ServiceUnavailable" };
        _cloud.Setup(c => c.ListStateMachinesAsync(It.IsAny<ListStateMachinesRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        _cloud.Setup(c => c.ListExecutionsAsync(It.IsAny<ListExecutionsRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        _cloud.Setup(c => c.DescribeExecutionAsync(It.IsAny<DescribeExecutionRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);
    }

    protected override void ArrangeSecretInFailureText(string secret)
    {
        _cloud.Setup(c => c.ListExecutionsAsync(It.IsAny<ListExecutionsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListExecutionsResponse
            {
                Executions = [new ExecutionListItem { ExecutionArn = Execution, Name = $"run {secret}", StopDate = DateTime.UtcNow, Status = ExecutionStatus.FAILED }],
            });
        SetDetail(error: $"States.TaskFailed {secret}", cause: $"connection string {secret}");
    }

    protected override void ArrangeMissingItem() =>
        _cloud.Setup(c => c.DescribeExecutionAsync(It.IsAny<DescribeExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExecutionDoesNotExistException("gone"));

    protected override void ArrangeCancellationAware()
    {
        _cloud.Setup(c => c.ListStateMachinesAsync(It.IsAny<ListStateMachinesRequest>(), It.IsAny<CancellationToken>()))
            .Returns((ListStateMachinesRequest _, CancellationToken t) =>
            {
                t.ThrowIfCancellationRequested();
                return Task.FromResult(new ListStateMachinesResponse());
            });
        _cloud.Setup(c => c.ListExecutionsAsync(It.IsAny<ListExecutionsRequest>(), It.IsAny<CancellationToken>()))
            .Returns((ListExecutionsRequest _, CancellationToken t) =>
            {
                t.ThrowIfCancellationRequested();
                return Task.FromResult(new ListExecutionsResponse());
            });
    }

    private void SetDetail(string error, string cause) =>
        _cloud.Setup(c => c.DescribeExecutionAsync(It.IsAny<DescribeExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DescribeExecutionResponse
            {
                ExecutionArn = Execution, StateMachineArn = Machine, Name = "run-1", Status = ExecutionStatus.FAILED,
                StartDate = DateTime.UtcNow.AddMinutes(-2), StopDate = DateTime.UtcNow.AddMinutes(-1), Error = error, Cause = cause,
            });

    [Fact]
    public async Task It_never_reads_an_executions_input_or_output()
    {
        ArrangeFailures(1);

        var detail = (await _source.GetFailedAsync(_ns, Execution, default)).Value!;

        detail.Item.Copy.Should().Be(CopyKind.None);
        _cloud.Invocations.Select(i => i.Method.Name).Should().NotContain(n => n.Contains("GetExecutionHistory", StringComparison.Ordinal));
        detail.Reason.Should().Be("States.TaskFailed");
    }

    [Fact]
    public async Task Without_a_place_it_reads_every_state_machine_and_every_failed_status()
    {
        ArrangeFailures(2);

        await _source.ListFailedAsync(_ns, null, default);

        _cloud.Invocations.Count(i => i.Method.Name == "ListExecutionsAsync").Should().Be(3, "FAILED, TIMED_OUT and ABORTED for the one machine");
    }

    [Fact]
    public async Task One_failed_read_leaves_the_page_incomplete_and_keeps_the_others()
    {
        ArrangeFailures(2);
        _cloud.Setup(c => c.ListExecutionsAsync(It.Is<ListExecutionsRequest>(r => r.StatusFilter == "ABORTED"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonStepFunctionsException("throttled") { ErrorCode = "ThrottlingException" });

        var page = (await _source.ListFailedAsync(_ns, null, default)).Value;

        page.Items.Should().HaveCount(2);
        page.Incomplete.Should().BeTrue("an aborted-runs read failed, so the page may be missing items that exist");
    }

    [Fact]
    public async Task An_arn_from_another_region_is_refused()
    {
        var elsewhere = "arn:aws:states:eu-west-1:123456789012:stateMachine:orders";

        var place = await _source.ListFailedAsync(_ns, elsewhere, default);

        place.IsFailure.Should().BeTrue();
        _cloud.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("us-east-1", "us-east-1.console.aws.amazon.com")]
    [InlineData("cn-north-1", "cn-north-1.console.amazonaws.cn")]
    [InlineData("us-gov-west-1", "us-gov-west-1.console.amazonaws-us-gov.com")]
    public async Task The_console_link_uses_the_partitions_own_host(string region, string host)
    {
        var ns = Namespace.Create("sqs.x.amazonaws.com", "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", provider: CloudProviderType.Aws, awsRegion: region).Value;
        var machine = $"arn:aws:states:{region}:123456789012:stateMachine:orders";
        _cloud.Setup(c => c.ListStateMachinesAsync(It.IsAny<ListStateMachinesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListStateMachinesResponse { StateMachines = [new StateMachineListItem { Name = "orders", StateMachineArn = machine }] });

        var place = (await _source.ListPlacesAsync(ns, default)).Value.Items.Single();

        place.ConsoleUrl.Should().StartWith($"https://{host}/");
    }

    [Fact]
    public async Task An_express_state_machine_is_not_a_place_and_is_never_read()
    {
        const string express = "arn:aws:states:us-east-1:123456789012:stateMachine:express";
        _cloud.Setup(c => c.ListStateMachinesAsync(It.IsAny<ListStateMachinesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListStateMachinesResponse
            {
                StateMachines =
                [
                    new StateMachineListItem { Name = "orders", StateMachineArn = Machine, Type = StateMachineType.STANDARD },
                    new StateMachineListItem { Name = "express", StateMachineArn = express, Type = StateMachineType.EXPRESS },
                ],
            });
        ArrangeFailures(1);

        var places = (await _source.ListPlacesAsync(_ns, default)).Value.Items;
        var page = (await _source.ListFailedAsync(_ns, null, default)).Value;

        places.Select(p => p.Id).Should().Equal(Machine);
        page.Incomplete.Should().BeFalse();
        _cloud.Verify(c => c.ListExecutionsAsync(It.Is<ListExecutionsRequest>(r => r.StateMachineArn == express), It.IsAny<CancellationToken>()), Times.Never);
    }
}
