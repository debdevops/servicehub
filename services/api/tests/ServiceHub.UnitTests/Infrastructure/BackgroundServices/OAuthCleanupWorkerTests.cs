using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Infrastructure.OAuth;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

public sealed class OAuthCleanupWorkerTests
{
    private readonly InMemoryOAuthStore _store = new();

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        var act = () => new OAuthCleanupWorker(null!, NullLogger<OAuthCleanupWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("store");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new OAuthCleanupWorker(_store, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_StopsGracefully()
    {
        var worker = new OAuthCleanupWorker(_store, NullLogger<OAuthCleanupWorker>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await worker.StartAsync(cts.Token);

        // Wait for cancellation to propagate
        await Task.Delay(200);

        var stopping = worker.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(stopping, Task.Delay(1000));
        completed.Should().Be(stopping);
    }

    [Fact]
    public async Task StopAsync_CompletesCleanly()
    {
        var worker = new OAuthCleanupWorker(_store, NullLogger<OAuthCleanupWorker>.Instance);
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await cts.CancelAsync();

        var stop = worker.StopAsync(CancellationToken.None);
        var done = await Task.WhenAny(stop, Task.Delay(2000));
        done.Should().Be(stop, "worker should stop within 2 seconds");
    }
}
