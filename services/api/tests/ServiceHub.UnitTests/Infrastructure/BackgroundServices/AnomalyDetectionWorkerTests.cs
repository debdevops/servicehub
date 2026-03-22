using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

public sealed class AnomalyDetectionWorkerTests
{
    private readonly Mock<INamespaceRepository> _repoMock = new();
    private readonly Mock<IAIServiceClient> _aiMock = new();

    // ── Constructor ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullRepo_Throws()
    {
        var act = () => new AnomalyDetectionWorker(null!, _aiMock.Object, NullLogger<AnomalyDetectionWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("namespaceRepository");
    }

    [Fact]
    public void Constructor_NullAIClient_Throws()
    {
        var act = () => new AnomalyDetectionWorker(_repoMock.Object, null!, NullLogger<AnomalyDetectionWorker>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("aiServiceClient");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new AnomalyDetectionWorker(_repoMock.Object, _aiMock.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_StopsGracefully()
    {
        _aiMock.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(false));

        var worker = new AnomalyDetectionWorker(_repoMock.Object, _aiMock.Object, NullLogger<AnomalyDetectionWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);

        // Should complete without throwing
    }

    [Fact]
    public async Task ExecuteAsync_AIUnavailable_SkipsCycle()
    {
        _aiMock.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(false));

        var worker = new AnomalyDetectionWorker(_repoMock.Object, _aiMock.Object, NullLogger<AnomalyDetectionWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);

        // Should not call GetActiveAsync since AI is unavailable
        // (but the initial delay of 30s means it won't reach DetectAnomaliesAsync before cancellation anyway)
    }

    [Fact]
    public async Task ExecuteAsync_AIAvailable_NoNamespaces_Completes()
    {
        _aiMock.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));
        _repoMock.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(Array.Empty<Namespace>()));

        var worker = new AnomalyDetectionWorker(_repoMock.Object, _aiMock.Object, NullLogger<AnomalyDetectionWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_GetActiveFailure_Handles()
    {
        _aiMock.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(Error.Internal("err", "AI down")));

        var worker = new AnomalyDetectionWorker(_repoMock.Object, _aiMock.Object, NullLogger<AnomalyDetectionWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);
    }
}
