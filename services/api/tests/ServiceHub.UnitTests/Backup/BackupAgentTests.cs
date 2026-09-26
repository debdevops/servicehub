using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Models.Backup;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Agents;

namespace ServiceHub.UnitTests.Backup;

public sealed class BackupAgentTests
{
    private static (BackupAgent Agent, Mock<IBackupService> Service) Create(int hours, Result<BackupManifest> outcome)
    {
        var service = new Mock<IBackupService>();
        service.Setup(s => s.CreateBackupAsync(It.IsAny<CancellationToken>())).ReturnsAsync(outcome);
        var provider = new ServiceCollection().AddSingleton(service.Object).BuildServiceProvider();
        var agent = new BackupAgent(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new BackupOptions { ScheduledBackupIntervalHours = hours, RetentionCount = 7 }));
        return (agent, service);
    }

    private static BackupManifest Manifest() => new()
    {
        BackupId = "20260926-120000Z", CreatedAtUtc = DateTimeOffset.UtcNow, ServiceHubVersion = "4.1.0",
        Sqlite = new BackupFileInfo { FileName = "servicehub-dlq.db", SizeBytes = 1, Sha256 = "x" },
        IntegrityCheck = "ok", EncryptionKeyFingerprint = "f", ConsistencyNote = "n",
    };

    [Fact]
    public void It_only_observes_and_runs_on_the_configured_interval()
    {
        var (agent, _) = Create(6, Result.Success(Manifest()));
        agent.Descriptor.Authority.Should().Be(AgentAuthority.Observes);
        agent.Descriptor.Kind.Should().Be(AgentKind.Maintain);
        agent.Descriptor.Cadence.Should().Be(TimeSpan.FromHours(6));
        agent.Descriptor.Notes.Should().Contain("never in a backup");
    }

    [Fact]
    public async Task A_cycle_takes_one_backup_and_a_failed_one_is_a_failed_cycle_not_a_quiet_one()
    {
        var (agent, service) = Create(1, Result.Success(Manifest()));
        (await agent.ExecuteCycleAsync(default)).Summary.Should().Contain("20260926-120000Z");
        service.Verify(s => s.CreateBackupAsync(It.IsAny<CancellationToken>()), Times.Once);

        var (failing, _) = Create(1, Result.Failure<BackupManifest>(Error.Internal("Backup.CreateFailed", "disk full")));
        await failing.Invoking(a => a.ExecuteCycleAsync(default)).Should().ThrowAsync<InvalidOperationException>();
    }
}
