using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Recovery;

namespace ServiceHub.Infrastructure.Agents;

/// <summary>
/// Watches ServiceHub itself and says so when something is wrong with it (the Health Keeper). Rule-based and read-only: it looks at the
/// agents, the recovery ledger and the connections, and announces each problem once per episode through the same escalation path the other
/// agents use, so every notification channel, the live stream and the "Open Agents" link carry it with no change.
/// </summary>
/// <remarks>
/// It never changes a cloud, a message, a setting or another agent, and it has no model behind it: the same state always gives the same
/// findings (<see cref="HealthChecks"/>). A stopped or failing Health Keeper is reported by <see cref="AgentWatchdog"/> like any agent. The
/// ledger's hash chain is verified at most once an hour per owner, because a full verification reads every event.
/// </remarks>
public sealed class HealthAgent : IAgent
{
    internal static readonly TimeSpan ChainEvery = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<HealthAgent> _logger;
    private readonly TimeProvider _time;
    private readonly HashSet<string> _raised = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _chainCheckedAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _chainValid = new(StringComparer.Ordinal);

    /// <summary>Creates the agent.</summary>
    public HealthAgent(IServiceScopeFactory scopes, ILogger<HealthAgent> logger, TimeProvider? time = null)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
        Descriptor = new AgentDescriptor(
            Id: HealthChecks.OwnAgentId,
            Name: "Health Keeper",
            Purpose: "Watches ServiceHub itself and tells you when something is wrong with it: a paused agent, a stuck recovery attempt, a failing connection, a ledger that no longer verifies.",
            Kind: AgentKind.Watch,
            Authority: AgentAuthority.Observes,
            Cadence: TimeSpan.FromMinutes(5),
            Notes: "Each problem is announced once, when it appears, to your notification channels. It clears itself when the problem goes away. It uses fixed rules, not a model.",
            May: ["Read the agents, the recovery ledger and the connection test results", "Send a notification to your channels"],
            MayNot: ["Change a cloud, a message, a setting or another agent", "Replay, purge or redrive anything", "Resume a paused agent or lift emergency stop"]);
    }

    /// <inheritdoc />
    public AgentDescriptor Descriptor { get; }

    /// <inheritdoc />
    public async Task<AgentCycleResult> ExecuteCycleAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        using var scope = _scopes.CreateScope();
        var sp = scope.ServiceProvider;
        // Resolved here, not in the constructor: the registry is built from every agent, this one included.
        var registry = sp.GetRequiredService<IAgentRegistry>();
        var ledger = sp.GetRequiredService<IRecoveryLedger>();
        var recorder = sp.GetRequiredService<EscalationRecorder>();
        var namespaces = await sp.GetRequiredService<INamespaceRepository>().GetActiveAsync(ct).ConfigureAwait(false);
        if (namespaces.IsFailure || namespaces.Value.Count == 0)
        {
            return AgentCycleResult.Idle("no connections to watch");
        }

        var agents = registry.All();
        var current = new HashSet<string>(StringComparer.Ordinal);
        var raisedNow = 0;
        var all = new List<string>();

        foreach (var group in namespaces.Value.GroupBy(n => n.OwnerId, StringComparer.Ordinal))
        {
            var owner = group.Key;
            var stop = await ledger.GetEmergencyStopStateAsync(owner, ct).ConfigureAwait(false);
            var open = await ledger.GetAgeingAsync(owner, 500, ct).ConfigureAwait(false);
            var (checkedChain, valid) = await ChainAsync(ledger, owner, now, ct).ConfigureAwait(false);
            var findings = HealthChecks.Evaluate(new HealthSnapshot(stop, agents, open, checkedChain, valid, [.. group]), now);

            foreach (var f in findings)
            {
                var key = $"{owner}|{f.Key}";
                current.Add(key);
                all.Add(f.Code);
                if (!_raised.Add(key))
                {
                    continue; // one announcement per episode, not one per cycle
                }

                raisedNow++;
                _logger.LogWarning("Health finding {Code} for an owner: {Reason}", f.Code, f.Reason);
                await recorder.RaiseHealthAsync(f.Code, f.Reason, owner, f.NamespaceId, f.NamespaceName, f.Severity, ct).ConfigureAwait(false);
            }
        }

        _raised.IntersectWith(current); // a problem that went away is a new episode if it comes back
        var summary = all.Count == 0 ? "all clear" : $"{all.Count} problem{(all.Count == 1 ? "" : "s")}: {string.Join(", ", all.Distinct(StringComparer.Ordinal))}";
        return new AgentCycleResult(namespaces.Value.Count, raisedNow, summary);
    }

    // The chain is verified at most once an hour per owner. Between checks the last answer stands, so a broken chain stays a finding.
    private async Task<(bool Checked, bool Valid)> ChainAsync(IRecoveryLedger ledger, string owner, DateTimeOffset now, CancellationToken ct)
    {
        if (_chainCheckedAt.TryGetValue(owner, out var at) && now - at < ChainEvery)
        {
            return (true, _chainValid[owner]);
        }

        try
        {
            var result = await ledger.VerifyChainAsync(owner, ct).ConfigureAwait(false);
            _chainCheckedAt[owner] = now;
            _chainValid[owner] = result.IsValid;
            return (true, result.IsValid);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // An unreadable ledger is not proof of a broken one; say nothing and try again next cycle.
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The ledger chain could not be verified this cycle");
            return (false, true);
        }
#pragma warning restore CA1031
    }
}
