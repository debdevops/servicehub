using System.Collections.Concurrent;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.Agents;

/// <summary>
/// Holds the live state of every registered agent, in memory.
/// </summary>
/// <remarks>
/// <para>
/// In memory on purpose: worker heartbeats belong in memory. State that is
/// rebuilt from the running process cannot drift from it, needs no table, and cannot survive a
/// restart as a stale lie. A restart means "unknown until the first cycle", which is the truth.
/// </para>
/// <para>
/// Populated from DI: every <see cref="IAgent"/> registered with <c>AddAgent&lt;T&gt;()</c> appears
/// here, and the Agents screen renders this and nothing else.
/// </para>
/// </remarks>
public sealed class AgentRegistry : IAgentRegistry
{
    /// <summary>How many recent cycles each agent keeps — enough for a timeline and a health judgement, not a history.</summary>
    public const int RecentCycleLimit = 30;

    private readonly ConcurrentDictionary<string, AgentRuntimeState> _states = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LinkedList<AgentCycleRecord>> _cycles = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<string> _order;

    /// <summary>Creates the registry from every agent registered in the container.</summary>
    /// <exception cref="InvalidOperationException">Two agents declare the same id.</exception>
    public AgentRegistry(IEnumerable<IAgent> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        var order = new List<string>();
        foreach (var agent in agents)
        {
            var descriptor = agent.Descriptor;
            if (!_states.TryAdd(descriptor.Id, Initial(descriptor)))
            {
                throw new InvalidOperationException(
                    $"Two agents are registered with the id '{descriptor.Id}'. Agent ids are stable " +
                    "identities — pause state and history are keyed on them — so they must be unique.");
            }

            order.Add(descriptor.Id);
        }

        _order = order;
    }

    private static AgentRuntimeState Initial(AgentDescriptor descriptor) =>
        new(descriptor, AgentHealth.Unknown, null, null, null, 0, false);

    /// <inheritdoc />
    public IReadOnlyList<AgentCycleRecord> RecentCycles(string agentId)
    {
        if (!_cycles.TryGetValue(agentId, out var list))
        {
            return [];
        }

        lock (list)
        {
            return [.. list];
        }
    }

    private void Remember(string agentId, AgentCycleRecord record)
    {
        var list = _cycles.GetOrAdd(agentId, static _ => new LinkedList<AgentCycleRecord>());
        lock (list)
        {
            list.AddFirst(record);
            while (list.Count > RecentCycleLimit)
            {
                list.RemoveLast();
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<AgentRuntimeState> All() =>
        [.. _order.Select(id => _states[id])];

    /// <inheritdoc />
    public AgentRuntimeState? StateOf(string agentId) =>
        _states.TryGetValue(agentId, out var state) ? state : null;

    /// <inheritdoc />
    public bool SetPaused(string agentId, bool paused)
    {
        if (!_states.TryGetValue(agentId, out var current))
        {
            return false;
        }

        // A paused agent is Paused; an unpaused one goes back to Unknown until its next cycle
        // reports something real. Restoring a remembered "Healthy" would be claiming knowledge
        // about a cycle that has not run.
        var health = paused
            ? AgentHealth.Paused
            : current.LastResult is null ? AgentHealth.Unknown : HealthFrom(current.LastResult, current.ConsecutiveFailures);

        _states[agentId] = current with { IsPaused = paused, Health = health };
        return true;
    }

    internal void RecordSuccess(string agentId, AgentCycleResult result, DateTimeOffset runUtc)
    {
        if (!_states.TryGetValue(agentId, out var current))
        {
            return;
        }

        Remember(agentId, new AgentCycleRecord(runUtc, result, null));
        _states[agentId] = current with
        {
            Health = current.IsPaused ? AgentHealth.Paused : HealthFrom(result, 0),
            LastRunUtc = runUtc,
            LastResult = result,
            LastFailure = null,
            ConsecutiveFailures = 0
        };
    }

    internal void RecordFailure(string agentId, string reason, DateTimeOffset runUtc)
    {
        if (!_states.TryGetValue(agentId, out var current))
        {
            return;
        }

        Remember(agentId, new AgentCycleRecord(runUtc, null, reason));
        _states[agentId] = current with
        {
            Health = current.IsPaused ? AgentHealth.Paused : AgentHealth.Failing,
            LastRunUtc = runUtc,
            LastFailure = reason,
            ConsecutiveFailures = current.ConsecutiveFailures + 1
        };
    }

    private static AgentHealth HealthFrom(AgentCycleResult result, int consecutiveFailures) =>
        consecutiveFailures > 0 ? AgentHealth.Failing
        : result.Degraded ? AgentHealth.Degraded
        : AgentHealth.Healthy;
}
