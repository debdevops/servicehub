using FluentAssertions;
using ServiceHub.Infrastructure.BulkOperations;

namespace ServiceHub.UnitTests.Infrastructure.BulkOperations;

public sealed class BulkOperationQueueTests
{
    [Fact]
    public async Task Enqueue_ThenDequeueAllAsync_YieldsTheJobId()
    {
        var queue = new BulkOperationQueue();
        var jobId = Guid.NewGuid();
        queue.Enqueue(jobId);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var enumerator = queue.DequeueAllAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        (await enumerator.MoveNextAsync()).Should().BeTrue();
        enumerator.Current.Should().Be(jobId);
    }

    [Fact]
    public async Task Enqueue_MultipleJobs_YieldsInOrder()
    {
        var queue = new BulkOperationQueue();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        queue.Enqueue(first);
        queue.Enqueue(second);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var enumerator = queue.DequeueAllAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        await enumerator.MoveNextAsync();
        enumerator.Current.Should().Be(first);
        await enumerator.MoveNextAsync();
        enumerator.Current.Should().Be(second);
    }

    [Fact]
    public void RegisterRunning_ReturnsATokenThatIsNotAlreadyCancelled()
    {
        var queue = new BulkOperationQueue();
        var jobId = Guid.NewGuid();

        var token = queue.RegisterRunning(jobId);

        token.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void RequestCancellation_ForARegisteredJob_CancelsItsToken()
    {
        var queue = new BulkOperationQueue();
        var jobId = Guid.NewGuid();
        var token = queue.RegisterRunning(jobId);

        queue.RequestCancellation(jobId);

        token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void RequestCancellation_ForAnUnregisteredJob_IsANoOp()
    {
        var queue = new BulkOperationQueue();

        var act = () => queue.RequestCancellation(Guid.NewGuid());

        act.Should().NotThrow();
    }

    [Fact]
    public void Complete_UnregistersTheJob_SoLaterCancellationIsANoOp()
    {
        var queue = new BulkOperationQueue();
        var jobId = Guid.NewGuid();
        var token = queue.RegisterRunning(jobId);

        queue.Complete(jobId);
        queue.RequestCancellation(jobId);

        // The token captured before Complete() is untouched — Complete() disposes the CTS
        // and removes it from the registry, but doesn't retroactively cancel it.
        token.IsCancellationRequested.Should().BeFalse();
    }
}
