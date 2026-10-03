using FluentAssertions;
using ServiceHub.Core.Models.Backup;
using ServiceHub.Infrastructure.Backup;

namespace ServiceHub.UnitTests.Backup;

/// <summary>A manifest's database file name is untrusted: it must be a real file inside the bundle, never a path or a link out of it.</summary>
public sealed class SnapshotPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"servicehub-snapshotpath-{Guid.NewGuid():N}");

    public SnapshotPathTests() => Directory.CreateDirectory(Path.Combine(_root, "bundle"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static BackupManifest Manifest(string fileName) => new()
    {
        BackupId = "b", CreatedAtUtc = DateTimeOffset.UtcNow, ServiceHubVersion = "4.1.0",
        Sqlite = new BackupFileInfo { FileName = fileName, SizeBytes = 1, Sha256 = "x" },
        IntegrityCheck = "ok", EncryptionKeyFingerprint = "f", ConsistencyNote = "n",
    };

    [Fact]
    public void A_plain_file_in_the_bundle_is_accepted()
    {
        var bundle = Path.Combine(_root, "bundle");
        File.WriteAllText(Path.Combine(bundle, "servicehub.db"), "x");

        BackupRestoreService.SnapshotPath(bundle, Manifest("servicehub.db")).Should().Be(Path.Combine(bundle, "servicehub.db"));
    }

    [Theory]
    [InlineData("../outside.db")]
    [InlineData("..")]
    [InlineData("")]
    public void A_name_that_is_not_a_plain_file_name_is_rejected(string name)
    {
        BackupRestoreService.SnapshotPath(Path.Combine(_root, "bundle"), Manifest(name)).Should().BeNull();
    }

    [Fact]
    public void A_symlink_to_a_database_outside_the_bundle_is_rejected()
    {
        var outside = Path.Combine(_root, "outside.db");
        File.WriteAllText(outside, "x");
        var bundle = Path.Combine(_root, "bundle");
        try
        {
            File.CreateSymbolicLink(Path.Combine(bundle, "servicehub.db"), outside);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return; // this machine may not create symlinks (Windows without developer mode); nothing to prove here
        }

        BackupRestoreService.SnapshotPath(bundle, Manifest("servicehub.db")).Should().BeNull();
    }

    [Fact]
    public void Staging_keeps_a_copy_only_when_its_bytes_are_the_ones_the_manifest_checksummed()
    {
        var source = Path.Combine(_root, "bundle", "servicehub.db");
        File.WriteAllText(source, "snapshot");
        var good = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source)));

        var staged = Path.Combine(_root, "pending.tmp");
        BackupRestoreService.CopyVerified(source, staged, good);
        File.ReadAllText(staged).Should().Be("snapshot");

        var rejected = Path.Combine(_root, "rejected.tmp");
        var act = () => BackupRestoreService.CopyVerified(source, rejected, "0000");
        act.Should().Throw<InvalidOperationException>();
        File.Exists(rejected).Should().BeFalse("a copy that does not match the manifest must not be left behind");
    }
}
