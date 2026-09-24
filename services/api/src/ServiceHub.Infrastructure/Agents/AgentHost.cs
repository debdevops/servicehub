using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

    /// <summary>Creates the host over every agent registered in the container.</summary>
    public AgentHost(
        IEnumerable<IAgent> agents,
        AgentRegistry registry,
        ILogger<AgentHost> logger,
        TimeProvider? timeProvider = null)
    {
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

        await Task.WhenAll(_agents.Select(agent => RunLoopAsync(agent, stoppingToken))).ConfigureAwait(false);
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
