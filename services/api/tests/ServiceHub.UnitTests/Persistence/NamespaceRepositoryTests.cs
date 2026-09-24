using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Persistence;

/// <summary>
/// The namespace store: one owner per namespace, isolation between owners, and a connection string
/// that is only ever an envelope once it leaves the repository.
/// </summary>
public sealed class NamespaceRepositoryTests : IAsyncLifetime
{
    private const string Cs =
        "Endpoint=sb://{0}.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=secret-value==";

    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), $"servicehub-ns-tests-{Guid.NewGuid():N}");
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ServiceHubDataDirectory.ConfigurationKey] = _dataDir,
                ["Security:EncryptionKey"] = "DEV_KEY_NOT_FOR_PRODUCTION_12345678901234567890",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(Mock.Of<IHostEnvironment>(e => e.EnvironmentName == Environments.Development));
        services.AddLogging();
        services.AddServiceHubPersistence();
        _provider = services.BuildServiceProvider();
        await _provider.InitializeServiceHubDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    private INamespaceRepository NewRepository() =>
        _provider.CreateScope().ServiceProvider.GetRequiredService<INamespaceRepository>();

    private static Namespace Make(string name, string owner) =>
        Namespace.Create($"{name}.servicebus.windows.net", string.Format(Cs, name), ownerId: owner).Value;

    [Fact]
    public async Task GetByOwner_ReturnsOnlyThatOwnersNamespaces()
    {
        var repo = NewRepository();
        await repo.AddAsync(Make("owner1-ns", "owner1"));
        await repo.AddAsync(Make("owner2-ns", "owner2"));

        var owner1 = await repo.GetByOwnerAsync("owner1");
        var owner2 = await repo.GetByOwnerAsync("owner2");

        owner1.Value.Should().ContainSingle().Which.OwnerId.Should().Be("owner1");
        owner2.Value.Should().ContainSingle().Which.OwnerId.Should().Be("owner2");
    }

    [Fact]
    public async Task GetByOwner_WithAnAllowList_HidesEverythingOutsideIt()
    {
        var repo = NewRepository();
        var a = Make("allowed", "owner1");
        var b = Make("hidden", "owner1");
        await repo.AddAsync(a);
        await repo.AddAsync(b);

        var result = await repo.GetByOwnerAsync("owner1", new HashSet<Guid> { a.Id });

        result.Value.Should().ContainSingle().Which.Id.Should().Be(a.Id);
    }

    [Fact]
    public async Task Exists_IsScopedToTheOwner_AndIgnoresBlankNames()
    {
        var repo = NewRepository();
        await repo.AddAsync(Make("shared-name", "owner1"));

        (await repo.ExistsAsync("shared-name.servicebus.windows.net", "owner1")).Should().BeTrue();
        (await repo.ExistsAsync("shared-name.servicebus.windows.net", "owner2")).Should().BeFalse();
        (await repo.ExistsAsync("", "owner1")).Should().BeFalse();
    }

    [Fact]
    public async Task Update_ThatChangesTheOwner_IsRefused()
    {
        var repo = NewRepository();
        var original = Make("change-owner", "owner1");
        await repo.AddAsync(original);

        var tampered = Make("change-owner", "owner2");
        typeof(Namespace).GetProperty(nameof(Namespace.Id))!.SetValue(tampered, original.Id);

        (await repo.UpdateAsync(tampered)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ADuplicateNameForTheSameOwner_IsAConflict()
    {
        var repo = NewRepository();
        (await repo.AddAsync(Make("dup", "owner1"))).IsSuccess.Should().BeTrue();

        var second = await repo.AddAsync(Make("dup", "owner1"));

        second.IsFailure.Should().BeTrue();
        second.Error.Type.Should().Be(ServiceHub.Core.Results.ErrorType.Conflict);
    }

    [Fact]
    public async Task ALoadedNamespace_NeverCarriesThePlaintextConnectionString()
    {
        var repo = NewRepository();
        var ns = Make("secret", "owner1");
        await repo.AddAsync(ns);

        var loaded = (await repo.GetByIdAsync(ns.Id)).Value;

        loaded.ConnectionString.Should().StartWith("ENC[").And.NotContain("secret-value");
    }

    [Fact]
    public async Task GetById_ForAnUnknownId_IsNotFound()
    {
        var result = await NewRepository().GetByIdAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ServiceHub.Core.Results.ErrorType.NotFound);
    }
}
