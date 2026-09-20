using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.Persistence;

public sealed class SqliteNamespaceRepositoryTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly SqliteNamespaceRepository _repository;

    public SqliteNamespaceRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _repository = new SqliteNamespaceRepository(_dbContext, NullLogger<SqliteNamespaceRepository>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static Namespace BuildNamespace(
        string name = "test-ns",
        string ownerId = "owner-1",
        CloudProviderType provider = CloudProviderType.Azure,
        EnvironmentType environment = EnvironmentType.Dev) =>
        Namespace.Create(
            name,
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=servicehub;SharedAccessKey=dGVzdGtleQ==",
            environment: environment,
            provider: provider,
            ownerId: ownerId).Value;

    [Fact]
    public async Task AddAsync_ThenGetById_RoundTripsAllFields()
    {
        var ns = BuildNamespace(provider: CloudProviderType.Aws, environment: EnvironmentType.Uat);
        (await _repository.AddAsync(ns)).IsSuccess.Should().BeTrue();

        var result = await _repository.GetByIdAsync(ns.Id);
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(ns.Id);
        result.Value.Name.Should().Be("test-ns");
        result.Value.Provider.Should().Be(CloudProviderType.Aws);
        result.Value.Environment.Should().Be(EnvironmentType.Uat);
        result.Value.OwnerId.Should().Be("owner-1");
        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsFailure()
    {
        var result = await _repository.GetByIdAsync(Guid.NewGuid());
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdAsync_EmptyGuid_ReturnsValidationFailure()
    {
        var result = await _repository.GetByIdAsync(Guid.Empty);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task GetByNameAsync_IsCaseInsensitive()
    {
        var ns = BuildNamespace(name: "MixedCase-NS");
        await _repository.AddAsync(ns);

        var result = await _repository.GetByNameAsync("MIXEDCASE-ns");
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(ns.Id);
    }

    [Fact]
    public async Task AddAsync_DuplicateNameSameOwner_ReturnsConflict()
    {
        var first = BuildNamespace(name: "dup-ns", ownerId: "owner-1");
        await _repository.AddAsync(first);

        var second = BuildNamespace(name: "dup-ns", ownerId: "owner-1");
        var result = await _repository.AddAsync(second);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task AddAsync_SameNameDifferentOwner_Succeeds()
    {
        await _repository.AddAsync(BuildNamespace(name: "shared-name", ownerId: "owner-1"));
        var result = await _repository.AddAsync(BuildNamespace(name: "shared-name", ownerId: "owner-2"));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEveryNamespace()
    {
        await _repository.AddAsync(BuildNamespace(name: "ns-1", ownerId: "owner-1"));
        await _repository.AddAsync(BuildNamespace(name: "ns-2", ownerId: "owner-2"));

        var result = await _repository.GetAllAsync();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetActiveAsync_ExcludesDeactivatedNamespaces()
    {
        var active = BuildNamespace(name: "active-ns");
        await _repository.AddAsync(active);

        var inactive = BuildNamespace(name: "inactive-ns");
        inactive.Deactivate();
        await _repository.AddAsync(inactive);

        var result = await _repository.GetActiveAsync();
        result.Value.Should().ContainSingle(n => n.Id == active.Id);
    }

    [Fact]
    public async Task GetByOwnerAsync_IncludesOwnedAndSharedNamespaces_ExcludesOthers()
    {
        var owned = BuildNamespace(name: "owned-ns", ownerId: "owner-1");
        await _repository.AddAsync(owned);

        var sharedWithMe = BuildNamespace(name: "shared-ns", ownerId: "owner-2");
        sharedWithMe.ShareWith("owner-1");
        await _repository.AddAsync(sharedWithMe);

        var notMine = BuildNamespace(name: "not-mine-ns", ownerId: "owner-3");
        await _repository.AddAsync(notMine);

        var result = await _repository.GetByOwnerAsync("owner-1");
        result.Value.Select(n => n.Id).Should().BeEquivalentTo([owned.Id, sharedWithMe.Id]);
    }

    [Fact]
    public async Task GetByOwnerAsync_AllowedNamespaceIds_NarrowsFurther()
    {
        var ns1 = BuildNamespace(name: "ns-1", ownerId: "owner-1");
        var ns2 = BuildNamespace(name: "ns-2", ownerId: "owner-1");
        await _repository.AddAsync(ns1);
        await _repository.AddAsync(ns2);

        var result = await _repository.GetByOwnerAsync("owner-1", new HashSet<Guid> { ns1.Id });
        result.Value.Should().ContainSingle(n => n.Id == ns1.Id);
    }

    [Fact]
    public async Task ShareWith_PersistsAcrossUpdateAndReload()
    {
        var ns = BuildNamespace(ownerId: "owner-1");
        await _repository.AddAsync(ns);

        ns.ShareWith("owner-2");
        ns.ShareWith("owner-3");
        (await _repository.UpdateAsync(ns)).IsSuccess.Should().BeTrue();

        var reloaded = await _repository.GetByIdAsync(ns.Id);
        reloaded.Value.SharedWithOwnerIds.Should().BeEquivalentTo(["owner-2", "owner-3"]);
    }

    [Fact]
    public async Task RevokeShare_RemovesTheJoinRow_NotJustTheDomainList()
    {
        var ns = BuildNamespace(ownerId: "owner-1");
        ns.ShareWith("owner-2");
        await _repository.AddAsync(ns);

        ns.RevokeShare("owner-2");
        await _repository.UpdateAsync(ns);

        var remainingShares = await _dbContext.NamespaceSharedOwners
            .Where(s => s.NamespaceId == ns.Id)
            .ToListAsync();
        remainingShares.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_ChangingOwnerId_IsRejected()
    {
        var ns = BuildNamespace(ownerId: "owner-1");
        await _repository.AddAsync(ns);

        // Simulate a caller passing back an object with the same Id but a different OwnerId.
        var tampered = BuildNamespace(ownerId: "owner-2");
        typeof(Namespace).GetProperty(nameof(Namespace.Id), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(tampered, ns.Id);

        var result = await _repository.UpdateAsync(tampered);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_NonExistentNamespace_ReturnsNotFound()
    {
        var ns = BuildNamespace();
        var result = await _repository.UpdateAsync(ns);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_CascadesSharedOwnerRows()
    {
        var ns = BuildNamespace(ownerId: "owner-1");
        ns.ShareWith("owner-2");
        await _repository.AddAsync(ns);

        (await _repository.DeleteAsync(ns.Id)).IsSuccess.Should().BeTrue();

        var remainingShares = await _dbContext.NamespaceSharedOwners
            .Where(s => s.NamespaceId == ns.Id)
            .ToListAsync();
        remainingShares.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_ReturnsNotFound()
    {
        var result = await _repository.DeleteAsync(Guid.NewGuid());
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_IsCaseInsensitiveAndOwnerScoped()
    {
        await _repository.AddAsync(BuildNamespace(name: "exists-ns", ownerId: "owner-1"));

        (await _repository.ExistsAsync("EXISTS-ns", "owner-1")).Should().BeTrue();
        (await _repository.ExistsAsync("exists-ns", "owner-2")).Should().BeFalse();
        (await _repository.ExistsAsync("", "owner-1")).Should().BeFalse();
    }
}
