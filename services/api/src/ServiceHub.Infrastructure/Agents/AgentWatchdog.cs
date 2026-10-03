using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Recovery;

namespace ServiceHub.Infrastructure.Agents;

/// <summary>
/// Makes an agent that stopped working reach the bell, not only <c>/health</c> (unit 5.6). It surfaces what is already
/// measured — late cycles and repeated failures, the same rule the Agents page uses — and never changes a cadence.
/// </summary>
/// <remarks>
/// It runs on its own loop, apart from the agents' loops, so a stuck agent cannot stop it from being noticed. The pending
/// item is live registry state (it clears the moment the agent recovers); this only raises the one event per episode that
/// the toast and the webhooks carry.
/// </remarks>
public sealed class AgentWatchdog : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IAgentRegistry _registry;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AgentWatchdog> _logger;
    private readonly TimeProvider _time;
    private readonly HashSet<string> _raised = new(StringComparer.Ordinal);

    /// <summary>Creates it.</summary>
    public AgentWatchdog(IAgentRegistry registry, IServiceScopeFactory scopes, ILogger<AgentWatchdog> logger, TimeProvider? time = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // The watchdog must outlive whatever it is watching.
            catch (Exception ex)
            {
                _logger.LogError(ex, "The agent watchdog's check failed");
            }
#pragma warning restore CA1031

            try
            {
                await Task.Delay(Interval, _time, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One check. Internal so a test can drive it without waiting.</summary>
    internal async Task CheckAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        foreach (var agent in _registry.All())
        {
            var id = agent.Descriptor.Id;
            if (!agent.NeedsAPerson(now))
            {
                _raised.Remove(id); // recovered: a later stop is a new episode
                continue;
            }

            if (!_raised.Add(id))
            {
                continue; // one event per episode, not one per check
            }

            var code = agent.IsLate(now) ? EscalationReasons.AgentStale : EscalationReasons.AgentFailing;
            _logger.LogWarning("Agent {AgentId} needs a person: {Code}", id, code);

            using var scope = _scopes.CreateScope();
            var recorder = scope.ServiceProvider.GetRequiredService<EscalationRecorder>();
            var owners = await scope.ServiceProvider.GetRequiredService<INamespaceRepository>().GetActiveAsync(ct).ConfigureAwait(false);
            // Agents serve everyone; tell every owner whose clouds they watch (the stream shows owner-scoped events only).
            foreach (var owner in owners.IsSuccess ? owners.Value.Select(n => n.OwnerId).Distinct(StringComparer.Ordinal) : [])
            {
                await recorder.RaiseAgentAsync(agent, code, owner, ct).ConfigureAwait(false);
            }
        }
    }
}
