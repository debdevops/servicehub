using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ServiceHub.Simulator;

/// <summary>
/// Hosted service that seeds the <see cref="SimulatorDataSeeder"/> once at application startup.
/// </summary>
internal sealed class SimulatorSeedHostedService : IHostedService
{
    private readonly SimulatorDataSeeder _seeder;
    private readonly ILogger<SimulatorSeedHostedService> _logger;

    /// <summary>Initializes a new instance of <see cref="SimulatorSeedHostedService"/>.</summary>
    public SimulatorSeedHostedService(SimulatorDataSeeder seeder, ILogger<SimulatorSeedHostedService> logger)
    {
        _seeder = seeder ?? throw new ArgumentNullException(nameof(seeder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Seeding simulator data store…");
        _seeder.Seed();
        _logger.LogInformation("Simulator data store seeded successfully.");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
