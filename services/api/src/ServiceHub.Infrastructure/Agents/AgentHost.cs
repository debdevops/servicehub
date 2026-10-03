using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.Agents;

/// <summary>
/// Runs every registered agent on its own cadence.
/// </summary>
/// <remarks>
/// <para>
/// <b>One host, not twenty loops.</b> A background worker written as its own
/// loop would carry its <c>BackgroundService</c>, its own timing, its own try/catch and its own heartbeat call — so
/// "what happens when an agent throws?" would have one answer per worker. Here it has one, and an agent is a
/// single <see cref="IAgent.ExecuteCycleAsync"/>.
/// </para>
/// <para>
/// Each agent gets an independent loop, so a slow or failing agent delays only itself. A cycle that
/// throws is recorded and the loop continues — an agent that cannot work is a reportable state, not
/// a reason to stop the others.
/// </para>
/// </remarks>
public sealed class AgentHost : BackgroundService
{
    private readonly IReadOnlyList<IAgent> _agents;
    private readonly AgentRegistry _registry;
    private readonly ILogger<AgentHost> _logger;
    private readonly TimeProvider _time;
    private readonly IServiceScopeFactory? _scopes;
    private readonly Dictionary<string, (CancellationTokenSource Cts, Task Loop)> _running = new(StringComparer.Ordinal);
    private readonly HashSet<string> _decidedDormant = new(StringComparer.Ordinal);

    /// <summary>How often the host re-checks which agents the connected clouds still give something to do.</summary>
    internal static readonly TimeSpan ReconcileEvery = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Creates the host over every agent registered in the container. Without <paramref name="scopes"/> there is no way to read
    /// which clouds are connected, so every agent runs.
    /// </summary>
    public AgentHost(
        IEnumerable<IAgent> agents,
        AgentRegistry registry,
        ILogger<AgentHost> logger,
        TimeProvider? timeProvider = null,
        IServiceScopeFactory? scopes = null)
    {
        _scopes = scopes;
        ArgumentNullException.ThrowIfNull(agents);
        _agents = [.. agents];
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_agents.Count == 0)
        {
            // Wave 0 state, and a legitimate one. Said out loud so an empty Agents screen is never
            // mistaken for a broken one.
            _logger.LogInformation("Agent host started with no agents registered.");
            return;
        }

        _logger.LogInformation(
            "Agent host started with {AgentCount} agent(s): {AgentIds}",
            _agents.Count,
            string.Join(", ", _agents.Select(a => a.Descriptor.Id)));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ReconcileAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(ReconcileEvery, _time, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        finally
        {
            foreach (var (cts, _) in _running.Values)
            {
                await cts.CancelAsync().ConfigureAwait(false);
            }

            await Task.WhenAll(_running.Values.Select(r => r.Loop)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs the agents the connected clouds give something to do, and stops (and delists) the rest — so an agent that could only
    /// ever do nothing costs no cycles and raises no false "stalled". Connecting a cloud that needs one starts it again; a pause a
    /// person set is untouched either way. Internal so a test can drive it without waiting.
    /// </summary>
    internal async Task ReconcileAsync(CancellationToken ct)
    {
        IReadOnlyList<CloudProviderType>? connected = null;
        if (_scopes is not null && _agents.Any(a => a.Descriptor.Needs != AgentNeeds.None))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var namespaces = await scope.ServiceProvider.GetRequiredService<INamespaceRepository>().GetActiveAsync(ct).ConfigureAwait(false);
                if (namespaces.IsSuccess)
                {
                    connected = [.. namespaces.Value.Select(n => n.Provider).Distinct()];
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // Not knowing which clouds are connected must leave every agent as it is, never switch one off.
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read the connected clouds; agents stay as they are.");
            }
#pragma warning restore CA1031
        }

        foreach (var agent in _agents)
        {
            var descriptor = agent.Descriptor;
            var id = descriptor.Id;
            var meaningful = connected is null ? !_decidedDormant.Contains(id) : descriptor.IsMeaningfulFor(connected);

            if (meaningful && !_running.ContainsKey(id))
            {
                _decidedDormant.Remove(id);
                _registry.SetDormant(id, false);
                var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _running[id] = (cts, RunLoopAsync(agent, cts.Token));
                _logger.LogInformation("Agent {AgentId} started: a connected cloud gives it something to do.", id);
            }
            else if (!meaningful && _running.Remove(id, out var running))
            {
                await running.Cts.CancelAsync().ConfigureAwait(false);
                await running.Loop.ConfigureAwait(false);
                running.Cts.Dispose();
                _decidedDormant.Add(id);
                _registry.SetDormant(id, true);
                _logger.LogInformation("Agent {AgentId} stopped: no connected cloud gives it anything to do.", id);
            }
            else if (!meaningful && !_running.ContainsKey(id))
            {
                _decidedDormant.Add(id);
                _registry.SetDormant(id, true);
            }
        }
    }

    private async Task RunLoopAsync(IAgent agent, CancellationToken stoppingToken)
    {
        var descriptor = agent.Descriptor;

        // Yield first so one agent's synchronous start-up cannot delay the others.
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOneCycleAsync(agent, descriptor, stoppingToken).ConfigureAwait(false);

            try
            {
                await Task.Delay(descriptor.Cadence, _time, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunOneCycleAsync(IAgent agent, AgentDescriptor descriptor, CancellationToken ct)
    {
        if (_registry.StateOf(descriptor.Id)?.IsPaused == true)
        {
            // The loop keeps running; the agent will not act. That wording is deliberate — saying
            // "stopped" would be untrue, and the difference matters when someone is deciding
            // whether the system is still watching.
            return;
        }

        var startedUtc = _time.GetUtcNow();

        try
        {
            var result = await agent.ExecuteCycleAsync(ct).ConfigureAwait(false);

            if (result.Changed > 0 && !descriptor.CanAct)
            {
                // Rule R8. An agent that declares it only observes, and then reports a mutation,
                // has broken its contract — that is a defect to surface loudly, never a statistic
                // to record quietly.
                var violation =
                    $"Agent '{descriptor.Id}' declares authority {descriptor.Authority} but reported " +
                    $"{result.Changed} change(s). Authority is a contract, not a comment.";
                _logger.LogError("{Violation}", violation);
                _registry.RecordFailure(descriptor.Id, violation, startedUtc);
                return;
            }

            _registry.RecordSuccess(descriptor.Id, result, startedUtc);

            _logger.LogDebug(
                "Agent {AgentId} cycle complete: examined {Examined}, changed {Changed} — {Summary}",
                descriptor.Id, result.Examined, result.Changed, result.Summary);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown, not a failure.
        }
#pragma warning disable CA1031 // One agent's failure must never take down the host or its siblings.
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent {AgentId} cycle failed", descriptor.Id);
            _registry.RecordFailure(descriptor.Id, ex.Message, startedUtc);
        }
#pragma warning restore CA1031
    }
}
