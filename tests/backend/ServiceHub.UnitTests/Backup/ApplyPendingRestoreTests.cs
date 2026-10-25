using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Infrastructure.Backup;

namespace ServiceHub.UnitTests.Backup;

public sealed class ApplyPendingRestoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sh-apply-restore-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void It_keeps_the_replaced_database_and_its_write_ahead_log_beside_the_restored_one()
    {
        File.WriteAllText(Path.Combine(_dir, "servicehub.db"), "old");
        File.WriteAllText(Path.Combine(_dir, "servicehub.db-wal"), "old-wal");
        File.WriteAllText(Path.Combine(_dir, BackupRestoreService.PendingFileName), "new");

        var kept = BackupRestoreService.ApplyPendingRestore(_dir, new DateTimeOffset(2026, 9, 27, 5, 0, 0, TimeSpan.Zero), NullLogger.Instance);

        kept.Should().EndWith("servicehub.db.before-restore-20260927-050000");
        File.ReadAllText(kept!).Should().Be("old");
        File.ReadAllText(kept + "-wal").Should().Be("old-wal", "the old log must never be replayed into the restored file");
        File.ReadAllText(Path.Combine(_dir, "servicehub.db")).Should().Be("new");
        File.Exists(Path.Combine(_dir, "servicehub.db-wal")).Should().BeFalse();
    }

    [Fact]
    public void Into_an_empty_data_directory_it_restores_and_claims_to_have_kept_nothing()
    {
        File.WriteAllText(Path.Combine(_dir, BackupRestoreService.PendingFileName), "new");

        BackupRestoreService.ApplyPendingRestore(_dir, DateTimeOffset.UtcNow, NullLogger.Instance).Should().BeNull();
        File.ReadAllText(Path.Combine(_dir, "servicehub.db")).Should().Be("new");
        Directory.EnumerateFiles(_dir, "*before-restore*").Should().BeEmpty();
    }

    [Fact]
    public void With_nothing_staged_it_touches_nothing()
    {
        File.WriteAllText(Path.Combine(_dir, "servicehub.db"), "live");
        BackupRestoreService.ApplyPendingRestore(_dir, DateTimeOffset.UtcNow, NullLogger.Instance).Should().BeNull();
        File.ReadAllText(Path.Combine(_dir, "servicehub.db")).Should().Be("live");
    }

    [Fact]
    public void A_fresh_start_sets_the_database_and_its_log_aside_and_leaves_backups_alone()
    {
        File.WriteAllText(Path.Combine(_dir, "servicehub.db"), "old");
        File.WriteAllText(Path.Combine(_dir, "servicehub.db-wal"), "old-wal");
        Directory.CreateDirectory(Path.Combine(_dir, "backups", "b1"));
        File.WriteAllText(Path.Combine(_dir, BackupRestoreService.FreshMarkerName), "{}");

        var kept = BackupRestoreService.ApplyPendingRestore(_dir, new DateTimeOffset(2026, 10, 25, 5, 0, 0, TimeSpan.Zero), NullLogger.Instance);

        kept.Should().EndWith("servicehub.db.before-fresh-20261025-050000");
        File.ReadAllText(kept!).Should().Be("old");
        File.ReadAllText(kept + "-wal").Should().Be("old-wal");
        File.Exists(Path.Combine(_dir, "servicehub.db")).Should().BeFalse("the migrations make the new, empty one");
        File.Exists(Path.Combine(_dir, BackupRestoreService.FreshMarkerName)).Should().BeFalse("it happens once");
        Directory.Exists(Path.Combine(_dir, "backups", "b1")).Should().BeTrue();
    }

    [Fact]
    public void A_staged_restore_wins_over_a_fresh_marker_and_clears_it()
    {
        File.WriteAllText(Path.Combine(_dir, "servicehub.db"), "old");
        File.WriteAllText(Path.Combine(_dir, BackupRestoreService.PendingFileName), "new");
        File.WriteAllText(Path.Combine(_dir, BackupRestoreService.FreshMarkerName), "{}");

        BackupRestoreService.ApplyPendingRestore(_dir, DateTimeOffset.UtcNow, NullLogger.Instance);

        File.ReadAllText(Path.Combine(_dir, "servicehub.db")).Should().Be("new");
        File.Exists(Path.Combine(_dir, BackupRestoreService.FreshMarkerName)).Should().BeFalse();
    }
}
