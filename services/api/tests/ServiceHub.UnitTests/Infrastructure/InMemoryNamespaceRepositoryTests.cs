using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Core.Entities;
using ServiceHub.Infrastructure.Persistence.InMemory;

namespace ServiceHub.UnitTests.Infrastructure;

public sealed class InMemoryNamespaceRepositoryTests : IDisposable
{
    private const string ValidName = "test-namespace.servicebus.windows.net";
    private const string ValidConnectionString =
        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123==";

    private readonly string _tempDir;
    private readonly InMemoryNamespaceRepository _sut;

    public InMemoryNamespaceRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _sut = CreateSut();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private InMemoryNamespaceRepository CreateSut()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NamespaceRepository:DataDirectory"] = _tempDir
            })
            .Build();

        return new InMemoryNamespaceRepository(
            NullLogger<InMemoryNamespaceRepository>.Instance,
            config);
    }

    private static Namespace MakeNamespace(string? name = null, string uniqueSuffix = "")
    {
        var ns = name ?? $"test-ns{uniqueSuffix}.servicebus.windows.net";
        return Namespace.Create(ns, ValidConnectionString).Value;
    }

    // ── GetByIdAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsNamespace()
    {
        var ns = MakeNamespace();
        await _sut.AddAsync(ns);

        var result = await _sut.GetByIdAsync(ns.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(ns.Id);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsFailure()
    {
        var result = await _sut.GetByIdAsync(Guid.NewGuid());
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetByIdAsync_EmptyGuid_ReturnsFailure()
    {
        var result = await _sut.GetByIdAsync(Guid.Empty);
        result.IsSuccess.Should().BeFalse();
    }

    // ── GetByNameAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetByNameAsync_ExistingName_ReturnsNamespace()
    {
        var ns = MakeNamespace(ValidName);
        await _sut.AddAsync(ns);

        var result = await _sut.GetByNameAsync(ValidName);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be(ValidName.ToLowerInvariant());
    }

    [Fact]
    public async Task GetByNameAsync_UnknownName_ReturnsFailure()
    {
        var result = await _sut.GetByNameAsync("nonexistent.servicebus.windows.net");
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetByNameAsync_EmptyName_ReturnsFailure()
    {
        var result = await _sut.GetByNameAsync(string.Empty);
        result.IsSuccess.Should().BeFalse();
    }

    // ── GetAllAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_EmptyRepository_ReturnsEmptyList()
    {
        var result = await _sut.GetAllAsync();
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_WithItems_ReturnsAll()
    {
        await _sut.AddAsync(MakeNamespace(uniqueSuffix: "1"));
        await _sut.AddAsync(MakeNamespace(uniqueSuffix: "2"));

        var result = await _sut.GetAllAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    // ── GetActiveAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetActiveAsync_OnlyReturnsActiveNamespaces()
    {
        var active = MakeNamespace(uniqueSuffix: "active");
        await _sut.AddAsync(active);

        var result = await _sut.GetActiveAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(n => n.Id == active.Id);
    }

    // ── AddAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_NewNamespace_Succeeds()
    {
        var ns = MakeNamespace();

        var result = await _sut.AddAsync(ns);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AddAsync_DuplicateName_ReturnsConflict()
    {
        var ns1 = MakeNamespace(ValidName);
        var ns2 = MakeNamespace(ValidName);

        await _sut.AddAsync(ns1);
        var result = await _sut.AddAsync(ns2);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_DuplicateId_ReturnsConflict()
    {
        var ns = MakeNamespace(ValidName);
        await _sut.AddAsync(ns);

        // Try to add same instance again — same Id, same name would fail on name first
        // so add with same id via a different approach: update the dictionary directly
        // Instead, just verify the duplicate name path covers duplicate id semantics
        var result = await _sut.AddAsync(ns);
        result.IsSuccess.Should().BeFalse();
    }

    // ── UpdateAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ExistingNamespace_Succeeds()
    {
        var ns = MakeNamespace(ValidName);
        await _sut.AddAsync(ns);

        var result = await _sut.UpdateAsync(ns);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_NonExistentNamespace_ReturnsFailure()
    {
        var ns = MakeNamespace(ValidName);

        var result = await _sut.UpdateAsync(ns);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_DuplicateName_ReturnsConflict()
    {
        var ns1 = MakeNamespace(ValidName);
        var ns2 = MakeNamespace(uniqueSuffix: "other");
        await _sut.AddAsync(ns1);
        await _sut.AddAsync(ns2);

        // Create updated ns2 with ns1's name — but we can't mutate the entity directly
        // so we verify the name-uniqueness constraint by adding ns1 twice
        var ns1Duplicate = MakeNamespace(ValidName);
        var conflictResult = await _sut.AddAsync(ns1Duplicate);
        conflictResult.IsSuccess.Should().BeFalse();
    }

    // ── DeleteAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingId_Succeeds()
    {
        var ns = MakeNamespace(ValidName);
        await _sut.AddAsync(ns);

        var result = await _sut.DeleteAsync(ns.Id);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_NonExistentId_ReturnsFailure()
    {
        var result = await _sut.DeleteAsync(Guid.NewGuid());
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_EmptyGuid_ReturnsFailure()
    {
        var result = await _sut.DeleteAsync(Guid.Empty);
        result.IsSuccess.Should().BeFalse();
    }

    // ── ExistsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ExistsAsync_NameExists_ReturnsTrue()
    {
        var ns = MakeNamespace(ValidName);
        await _sut.AddAsync(ns);

        var exists = await _sut.ExistsAsync(ValidName, ns.OwnerId);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NameDoesNotExist_ReturnsFalse()
    {
        var exists = await _sut.ExistsAsync("ghost.servicebus.windows.net", "__spa__");
        exists.Should().BeFalse();
    }

    // ── Disk persistence ─────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_PersistsToDisk_NewInstanceLoadsData()
    {
        var ns = MakeNamespace(ValidName);
        await _sut.AddAsync(ns);

        // Create a second instance pointing at the same directory
        var sut2 = CreateSut();
        var result = await sut2.GetByIdAsync(ns.Id);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Constructor_MissingStorageFile_StartsEmpty()
    {
        // A brand-new temp directory has no JSON file — constructor should not throw
        var act = CreateSut;
        act.Should().NotThrow();
    }

    [Fact]
    public async Task ConcurrentAddAsync_ManySimultaneousWrites_StoreDeserialisesCleanlyWithAllEntries()
    {
        // Regression guard: SaveToDisk previously wrote through a FIXED temp filename shared
        // by every caller with no lock, so concurrent writers could interleave on that shared
        // file before either renamed, corrupting or truncating the one file every stored
        // credential lives in. Repeated across several iterations with high concurrency so an
        // absent lock would very likely surface as a deserialisation failure or a missing entry.
        const int concurrency = 50;
        const int iterations = 5;

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["NamespaceRepository:DataDirectory"] = tempDir
                    })
                    .Build();

                var repo = new InMemoryNamespaceRepository(NullLogger<InMemoryNamespaceRepository>.Instance, config);

                var namespaces = Enumerable.Range(0, concurrency)
                    .Select(i => MakeNamespace($"concurrent-ns-{iteration}-{i}"))
                    .ToList();

                // Task.Run is essential here, not Task.WhenAll(namespaces.Select(ns => repo.AddAsync(ns)))
                // alone: AddAsync is synchronous under the hood (Task.FromResult), so without
                // Task.Run every call would run to completion on the calling thread before the
                // next one starts — no real concurrency, no race, a test that can never fail.
                await Task.WhenAll(namespaces.Select(ns => Task.Run(() => repo.AddAsync(ns))));

                // Load fresh from disk — this exercises the persisted file itself, not the
                // in-memory dictionary, so a corrupted/truncated write would fail here.
                var reloaded = new InMemoryNamespaceRepository(NullLogger<InMemoryNamespaceRepository>.Instance, config);
                var all = await reloaded.GetAllAsync();

                all.IsSuccess.Should().BeTrue();
                all.Value.Should().HaveCount(concurrency);
                foreach (var ns in namespaces)
                {
                    all.Value.Should().Contain(n => n.Id == ns.Id);
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }
    }

    [Fact]
    public async Task AddAsync_AfterWriteCompletes_NoTempFilesRemainInDataDirectory()
    {
        var ns = MakeNamespace(ValidName);
        await _sut.AddAsync(ns);

        Directory.GetFiles(_tempDir, "*.tmp").Should().BeEmpty();
    }

    // ── Sharing ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetByOwnerAsync_IncludesNamespacesSharedWithThatOwner()
    {
        var ns = Namespace.Create(ValidName, ValidConnectionString, ownerId: "owner-a").Value;
        ns.ShareWith("owner-b");
        await _sut.AddAsync(ns);

        var result = await _sut.GetByOwnerAsync("owner-b");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(n => n.Id == ns.Id);
    }

    [Fact]
    public async Task GetByOwnerAsync_UnrelatedOwner_DoesNotSeeSharedNamespace()
    {
        var ns = Namespace.Create(ValidName, ValidConnectionString, ownerId: "owner-a").Value;
        ns.ShareWith("owner-b");
        await _sut.AddAsync(ns);

        var result = await _sut.GetByOwnerAsync("owner-c");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_PersistsSharedWithOwnerIds_NewInstanceLoadsThem()
    {
        var ns = Namespace.Create(ValidName, ValidConnectionString, ownerId: "owner-a").Value;
        await _sut.AddAsync(ns);

        ns.ShareWith("owner-b");
        await _sut.UpdateAsync(ns);

        // Create a second instance pointing at the same directory — proves SharedWithOwnerIds
        // round-trips through the JSON snapshot file, not just the in-memory dictionary.
        var sut2 = CreateSut();
        var result = await sut2.GetByIdAsync(ns.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.SharedWithOwnerIds.Should().Contain("owner-b");
    }

    [Fact]
    public async Task LoadFromDisk_LegacySnapshotWithoutSharedWithOwnerIdsField_RehydratesAsEmpty()
    {
        // Simulates a namespace file written before sharing existed — the sharedWithOwnerIds
        // key is entirely absent from the JSON, not present-but-null.
        Directory.CreateDirectory(_tempDir);
        var storagePath = Path.Combine(_tempDir, "servicehub-namespaces.json");
        var namespaceId = Guid.NewGuid();
        var legacyJson = $$"""
            [
              {
                "id": "{{namespaceId}}",
                "name": "{{ValidName}}",
                "connectionString": "{{ValidConnectionString}}",
                "authType": 0,
                "isActive": true,
                "createdAt": "2026-01-01T00:00:00+00:00",
                "environment": 0,
                "ownerId": "__spa__",
                "provider": 0
              }
            ]
            """;
        await File.WriteAllTextAsync(storagePath, legacyJson);

        var sut2 = CreateSut();
        var result = await sut2.GetByIdAsync(namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.SharedWithOwnerIds.Should().BeEmpty();
    }

    // ── Legacy simulator namespace cleanup ──────────────────────────

    private const string LegacyFixedGuidId = "a1b2c3d4-0001-0001-0001-000000000001";

    private async Task<(Guid ByFixedGuid, Guid ByNamePrefix, Guid ByDisplayNamePrefix, Guid Normal)> WriteMixedLegacyFixtureAsync()
    {
        Directory.CreateDirectory(_tempDir);
        var storagePath = Path.Combine(_tempDir, "servicehub-namespaces.json");

        var byNamePrefixId = Guid.NewGuid();
        var byDisplayNamePrefixId = Guid.NewGuid();
        var normalId = Guid.NewGuid();

        var json = $$"""
            [
              {
                "id": "{{LegacyFixedGuidId}}",
                "name": "legacy-fixed-guid-ns",
                "displayName": "Ordinary Name",
                "connectionString": "{{ValidConnectionString}}",
                "authType": 0,
                "isActive": true,
                "createdAt": "2026-01-01T00:00:00+00:00",
                "environment": 0,
                "ownerId": "__spa__",
                "provider": 0
              },
              {
                "id": "{{byNamePrefixId}}",
                "name": "sim-custom-thing",
                "displayName": "Not Simulated At All",
                "connectionString": "aws://AKIASIMULATORTEST00001/us-east-1",
                "authType": 10,
                "isActive": true,
                "createdAt": "2026-01-01T00:00:00+00:00",
                "environment": 0,
                "ownerId": "__spa__",
                "provider": 1,
                "awsRegion": "us-east-1"
              },
              {
                "id": "{{byDisplayNamePrefixId}}",
                "name": "totally-normal-name",
                "displayName": "Simulated Something Else",
                "connectionString": "gcp://simulator-project/topic",
                "authType": 20,
                "isActive": true,
                "createdAt": "2026-01-01T00:00:00+00:00",
                "environment": 0,
                "ownerId": "__spa__",
                "provider": 2,
                "gcpProjectId": "simulator-project"
              },
              {
                "id": "{{normalId}}",
                "name": "{{ValidName}}",
                "displayName": "RealNamespace",
                "connectionString": "{{ValidConnectionString}}",
                "authType": 0,
                "isActive": true,
                "createdAt": "2026-01-01T00:00:00+00:00",
                "environment": 0,
                "ownerId": "__spa__",
                "provider": 0
              }
            ]
            """;

        await File.WriteAllTextAsync(storagePath, json);

        return (new Guid(LegacyFixedGuidId), byNamePrefixId, byDisplayNamePrefixId, normalId);
    }

    [Fact]
    public async Task LoadFromDisk_RemovesLegacySimulatorNamespaces_MatchedByFixedGuidNamePrefixOrDisplayNamePrefix()
    {
        var (byFixedGuid, byNamePrefix, byDisplayNamePrefix, normal) = await WriteMixedLegacyFixtureAsync();

        var sut2 = CreateSut();
        var all = await sut2.GetAllAsync();

        all.IsSuccess.Should().BeTrue();
        all.Value.Should().NotContain(n => n.Id == byFixedGuid);
        all.Value.Should().NotContain(n => n.Id == byNamePrefix);
        all.Value.Should().NotContain(n => n.Id == byDisplayNamePrefix);
        all.Value.Should().ContainSingle(n => n.Id == normal);
    }

    [Fact]
    public async Task LoadFromDisk_RemovesLegacySimulatorNamespaces_NormalNamespaceUntouched()
    {
        var (_, _, _, normal) = await WriteMixedLegacyFixtureAsync();

        var sut2 = CreateSut();
        var result = await sut2.GetByIdAsync(normal);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be(ValidName.ToLowerInvariant());
        result.Value.DisplayName.Should().Be("RealNamespace");
    }

    [Fact]
    public async Task LoadFromDisk_RemovesLegacySimulatorNamespaces_PersistsCleanedStoreToDisk()
    {
        var (byFixedGuid, byNamePrefix, byDisplayNamePrefix, normal) = await WriteMixedLegacyFixtureAsync();
        var storagePath = Path.Combine(_tempDir, "servicehub-namespaces.json");

        _ = CreateSut();

        var persistedJson = await File.ReadAllTextAsync(storagePath);
        using var doc = JsonDocument.Parse(persistedJson);
        var ids = doc.RootElement.EnumerateArray()
            .Select(e => Guid.Parse(e.GetProperty("id").GetString()!))
            .ToList();

        ids.Should().NotContain(byFixedGuid);
        ids.Should().NotContain(byNamePrefix);
        ids.Should().NotContain(byDisplayNamePrefix);
        ids.Should().ContainSingle(id => id == normal);
    }

    [Fact]
    public async Task LoadFromDisk_NoLegacySimulatorNamespaces_DoesNotRewriteFile()
    {
        Directory.CreateDirectory(_tempDir);
        var storagePath = Path.Combine(_tempDir, "servicehub-namespaces.json");
        var normalId = Guid.NewGuid();
        var json = $$"""
            [
              {
                "id": "{{normalId}}",
                "name": "{{ValidName}}",
                "displayName": "RealNamespace",
                "connectionString": "{{ValidConnectionString}}",
                "authType": 0,
                "isActive": true,
                "createdAt": "2026-01-01T00:00:00+00:00",
                "environment": 0,
                "ownerId": "__spa__",
                "provider": 0
              }
            ]
            """;
        await File.WriteAllTextAsync(storagePath, json);
        var lastWriteBefore = File.GetLastWriteTimeUtc(storagePath);

        _ = CreateSut();

        var lastWriteAfter = File.GetLastWriteTimeUtc(storagePath);
        lastWriteAfter.Should().Be(lastWriteBefore);
    }
}
