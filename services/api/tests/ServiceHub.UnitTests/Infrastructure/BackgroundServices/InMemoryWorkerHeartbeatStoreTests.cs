using FluentAssertions;
using ServiceHub.Infrastructure.BackgroundServices;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

public sealed class InMemoryWorkerHeartbeatStoreTests
{
    private readonly InMemoryWorkerHeartbeatStore _sut = new();

    [Fact]
    public void GetAll_NoHeartbeatsRecorded_ReturnsEmpty()
    {
        _sut.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void RecordHeartbeat_ThenGetAll_ContainsTheWorkerWithARecentTimestamp()
    {
        var before = DateTimeOffset.UtcNow;

        _sut.RecordHeartbeat("SomeWorker", TimeSpan.FromMinutes(5));

        var after = DateTimeOffset.UtcNow;
        var all = _sut.GetAll();

        all.Should().ContainKey("SomeWorker");
        var heartbeat = all["SomeWorker"];
        heartbeat.WorkerName.Should().Be("SomeWorker");
        heartbeat.ExpectedInterval.Should().Be(TimeSpan.FromMinutes(5));
        heartbeat.LastHeartbeatAtUtc.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void RecordHeartbeat_NullExpectedInterval_IsStoredAsNull()
    {
        _sut.RecordHeartbeat("QueueWorker", expectedInterval: null);

        _sut.GetAll()["QueueWorker"].ExpectedInterval.Should().BeNull();
    }

    [Fact]
    public void RecordHeartbeat_CalledTwiceForSameWorker_OverwritesWithLatestTimestamp()
    {
        _sut.RecordHeartbeat("Worker", TimeSpan.FromMinutes(1));
        var firstTimestamp = _sut.GetAll()["Worker"].LastHeartbeatAtUtc;

        Thread.Sleep(5);
        _sut.RecordHeartbeat("Worker", TimeSpan.FromMinutes(2));

        var all = _sut.GetAll();
        all.Should().HaveCount(1);
        all["Worker"].LastHeartbeatAtUtc.Should().BeAfter(firstTimestamp);
        all["Worker"].ExpectedInterval.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void RecordHeartbeat_MultipleWorkers_TracksEachIndependently()
    {
        _sut.RecordHeartbeat("WorkerA", TimeSpan.FromSeconds(10));
        _sut.RecordHeartbeat("WorkerB", TimeSpan.FromSeconds(20));

        _sut.GetAll().Should().HaveCount(2);
        _sut.GetAll()["WorkerA"].ExpectedInterval.Should().Be(TimeSpan.FromSeconds(10));
        _sut.GetAll()["WorkerB"].ExpectedInterval.Should().Be(TimeSpan.FromSeconds(20));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordHeartbeat_InvalidWorkerName_Throws(string? workerName)
    {
        var act = () => _sut.RecordHeartbeat(workerName!, TimeSpan.FromMinutes(1));
        act.Should().Throw<ArgumentException>();
    }
}
