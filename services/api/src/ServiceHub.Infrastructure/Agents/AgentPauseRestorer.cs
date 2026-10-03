using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Agents;

/// <summary>
/// Puts back, before any agent runs a cycle, every pause a person set before the last restart (unit 4.4).
/// </summary>
/// <remarks>
/// Pause lives in memory like the rest of an agent's runtime state, but a pause that silently lifted on restart would
/// let an acting agent act again with nobody having said so. The audit trail already records every pause and resume,
/// so the latest row per agent is the answer — no new table. Registered before the host, so it runs first.
/// </remarks>
public sealed class AgentPauseRestorer : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IAgentRegistry _registry;
    private readonly ILogger<AgentPauseRestorer> _logger;

    /// <summary>Creates it.</summary>
    public AgentPauseRestorer(IServiceScopeFactory scopes, IAgentRegistry registry, ILogger<AgentPauseRestorer> logger)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        if (scope.ServiceProvider.GetService<ServiceHubDbContext>() is not { } db)
        {
            return;
        }

        try
        {
            var rows = await db.AuditLogs.AsNoTracking()
                .Where(a => (a.Action == AuditActions.AgentPause || a.Action == AuditActions.AgentResume)
                            && a.Outcome == AuditActions.Success && a.ResourceName != null)
                .Select(a => new { a.ResourceName, a.Action, a.Timestamp })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var latest in rows.GroupBy(r => r.ResourceName!).Select(g => g.OrderByDescending(r => r.Timestamp).First()))
            {
                if (latest.Action == AuditActions.AgentPause && _registry.SetPaused(latest.ResourceName!, true))
                {
                    _logger.LogWarning("Agent {AgentId} is still paused from before the restart: it will not act until someone resumes it", latest.ResourceName);
                }
            }
        }
#pragma warning disable CA1031 // Say it loudly; the host still starts.
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read which agents were paused before the restart — any earlier pause is NOT in force");
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
