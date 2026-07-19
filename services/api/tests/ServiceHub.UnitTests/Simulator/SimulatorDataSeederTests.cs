using FluentAssertions;
using Moq;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;
using ServiceHub.Simulator;
using ServiceHub.Simulator.Store;
using SHNamespace = ServiceHub.Core.Entities.Namespace;

namespace ServiceHub.UnitTests.Simulator;

/// <summary>
/// Regression pack for the simulator seeding path — the `--simulator` mode is
/// the credential-free demo of the whole product, so a working seed of all
/// three clouds (namespaces registered, entities present, messages and DLQs
/// populated) is a core capability, not test fixture noise.
/// </summary>
public sealed class SimulatorDataSeederTests
{
    private static (SimulatorDataSeeder Seeder, InMemorySimulatorStore Store, List<SHNamespace> Registered) BuildSut()
    {
        var store = new InMemorySimulatorStore();
        var registered = new List<SHNamespace>();

        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SHNamespace>.Failure(Error.NotFound("Namespace.NotFound", "not seeded yet")));
        repo.Setup(r => r.AddAsync(It.IsAny<SHNamespace>(), It.IsAny<CancellationToken>()))
            .Callback<SHNamespace, CancellationToken>((ns, _) => registered.Add(ns))
            .ReturnsAsync((SHNamespace ns, CancellationToken _) => Result<SHNamespace>.Success(ns));

        return (new SimulatorDataSeeder(store, repo.Object), store, registered);
    }

    [Fact]
    public void Seed_RegistersAllThreeCloudNamespaces()
    {
        var (seeder, store, registered) = BuildSut();

        seeder.Seed();

        var namespaces = store.GetAllNamespaces();
        namespaces.Select(n => n.Provider).Should().Contain(
        [
            CloudProviderType.Azure,
            CloudProviderType.Aws,
            CloudProviderType.Gcp,
        ]);
        // The namespace repository must know them too, or the API returns 404s.
        registered.Should().HaveCount(3);
        registered.Select(n => n.Provider).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Seed_PopulatesEntitiesForEveryCloud()
    {
        var (seeder, store, _) = BuildSut();

        seeder.Seed();

        store.GetEntities(SimulatorDataSeeder.AzureNamespaceId).Should().NotBeEmpty();
        store.GetEntities(SimulatorDataSeeder.AwsNamespaceId).Should().NotBeEmpty();
        store.GetEntities(SimulatorDataSeeder.GcpNamespaceId).Should().NotBeEmpty();
    }

    [Fact]
    public void Seed_ProvidesActiveAndDeadLetterMessagesToDemo()
    {
        var (seeder, store, _) = BuildSut();

        seeder.Seed();

        // The demo's whole point: something to browse and a DLQ story to tell.
        var allEntities = new[]
            {
                SimulatorDataSeeder.AzureNamespaceId,
                SimulatorDataSeeder.AwsNamespaceId,
                SimulatorDataSeeder.GcpNamespaceId,
            }
            .SelectMany(store.GetEntities)
            .ToList();

        allEntities.Sum(e => e.GetMessageCount()).Should().BeGreaterThan(0);
        allEntities.Sum(e => e.GetDlqCount()).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Seed_IsIdempotentForRepositoryRegistrations()
    {
        var store = new InMemorySimulatorStore();
        var addCalls = 0;
        var repo = new Mock<INamespaceRepository>();
        // Second run: namespaces already exist.
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => Result<SHNamespace>.Success(
                SHNamespace.Create("existing",
                    "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=P;SharedAccessKey=abc=").Value));
        repo.Setup(r => r.AddAsync(It.IsAny<SHNamespace>(), It.IsAny<CancellationToken>()))
            .Callback(() => addCalls++)
            .ReturnsAsync((SHNamespace ns, CancellationToken _) => Result<SHNamespace>.Success(ns));

        new SimulatorDataSeeder(store, repo.Object).Seed();

        // Existing registrations are not duplicated on restart.
        addCalls.Should().Be(0);
    }
}
