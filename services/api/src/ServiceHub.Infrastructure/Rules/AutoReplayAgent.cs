using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Rules;

/// <summary>
/// Applies Auto Replay rules (unit 3.6): for each rule that is on, replays the dead letters it matches — but only those the
/// eligibility gate lets an <b>automation</b> actor replay. Everything else it holds, counts, and shows on the rule as
/// "waiting for a person".
/// </summary>
/// <remarks>
/// <para><b>It cannot act where trust has not been earned.</b> The gate's autonomy predicate lets automation through only for a
/// failure signature with a Standing or Unattended grant, and grants arrive with unit 4.1. Until then every match is held —
/// the rule's replays simply do not happen, and the rule says so. This agent never bypasses, softens or caches the gate.</para>
/// <para><b>The circuit breaker runs first</b> each cycle, so a rule that has started failing is off before it can send more.
/// A tripped breaker is never reset by this agent.</para>
/// </remarks>
public sealed class AutoReplayAgent : IAgent
{
    private const int MaxPerCycle = 20;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AutoReplayAgent> _logger;

    /// <summary>Creates the agent.</summary>
    public AutoReplayAgent(IServiceScopeFactory scopes, ILogger<AutoReplayAgent> logger)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Descriptor = new AgentDescriptor(
            Id: "auto-replay",
            Name: "Auto Replay",
            Purpose: "Retries the failures your rules name, on its own, only where the safety checks say a fix can be trusted — and stops itself when fewer than half stay fixed.",
            Kind: AgentKind.Act,
            Authority: AgentAuthority.ActsAutonomously,
            Cadence: TimeSpan.FromSeconds(30),
            Notes: "Bounded by the eligibility gate: it never acts on a failure that has not earned unattended replay, and never in a Production namespace.");
    }

    /// <inheritdoc />
    public AgentDescriptor Descriptor { get; }

    /// <inheritdoc />
    public async Task<AgentCycleResult> ExecuteCycleAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        var rules = scope.ServiceProvider.GetRequiredService<IRulesService>() as RulesService
            ?? throw new InvalidOperationException("The rules service is not the one that owns the breaker.");
        var namespaces = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();
        var time = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;

        var enabled = await db.AutoReplayRules.Where(r => r.Enabled).OrderBy(r => r.Id).ToListAsync(ct).ConfigureAwait(false);
        if (enabled.Count == 0)
        {
            return AgentCycleResult.Idle("no rule is on");
        }

        int examined = 0, replayed = 0, held = 0, tripped = 0;
        foreach (var rule in enabled)
        {
            if (await rules.SweepBreakerAsync(rule, ct).ConfigureAwait(false))
            {
                tripped++;
                _logger.LogWarning("Auto Replay rule {RuleId} '{Name}' stopped itself: {Detail}", rule.Id, rule.Name, rule.DisabledDetail);
                continue;
            }

            var now = time.GetUtcNow();
            var candidates = await rules.Matching(rule).Where(m => m.Status == DlqMessageStatus.Active).OrderBy(m => m.DetectedAtUtc).Take(200).ToListAsync(ct).ConfigureAwait(false);
            var attempts = await db.ReplayHistories.AsNoTracking().Where(h => candidates.Select(c => c.Id).Contains(h.DlqMessageId))
                .GroupBy(h => h.DlqMessageId).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct).ConfigureAwait(false);
            var lastHour = await db.ReplayHistories.CountAsync(h => h.RuleId == rule.Id && h.ReplayedAt >= now.AddHours(-1), ct).ConfigureAwait(false);

            var actor = new RecoveryActor($"System:AutoReplay:{rule.Id}", RecoveryActorKind.Automation);
            var holding = 0;
            string? holdReason = null;
            foreach (var m in candidates)
            {
                examined++;
                var prior = attempts.GetValueOrDefault(m.Id);
                var wait = TimeSpan.FromSeconds(rule.WaitSeconds * (rule.BackOff ? Math.Pow(2, Math.Min(prior, 10)) : 1));
                if (m.DetectedAtUtc + wait > now)
                {
                    continue; // not its time yet
                }

                var found = await namespaces.GetByIdAsync(m.NamespaceId, ct).ConfigureAwait(false);
                if (found.IsFailure || found.Value.OwnerId != rule.OwnerId)
                {
                    continue;
                }

                var checker = scope.ServiceProvider.GetRequiredService<IDlqReplayService>();
                var decision = await checker.CheckEligibilityAsync(m.Id, found.Value, actor, RecoveryOperationKind.Replay, ct).ConfigureAwait(false);
                if (decision is null)
                {
                    continue;
                }

                if (decision.Verdict != EligibilityVerdict.Allow)
                {
                    holding++;
                    holdReason ??= decision.ReasonCode ?? decision.Verdict.ToString().ToUpperInvariant();
                    continue;
                }

                if (lastHour >= rule.MaxPerHour || replayed >= MaxPerCycle)
                {
                    continue; // over its pace: waits for the next cycle, never bursts
                }

                // Its own scope, so a concurrency conflict with the DLQ monitor cannot poison this rule's bookkeeping.
                using var itemScope = _scopes.CreateScope();
                var replay = itemScope.ServiceProvider.GetRequiredService<IDlqReplayService>();
                var outcome = await replay.ReplayAsync(m.Id, found.Value, actor, "auto-replay", $"rule-{rule.Id}", CancellationToken.None, rule.Id).ConfigureAwait(false);
                if (outcome.IsSuccess)
                {
                    replayed++;
                    lastHour++;
                }
            }

            held += holding;
            var changed = rule.AskedCount != holding || (holding > 0 && rule.LastAskedReason != holdReason);
            if (changed)
            {
                rule.AskedCount = holding;
                if (holding > 0)
                {
                    rule.LastAskedAt = now;
                    rule.LastAskedReason = holdReason;
                }

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }

        return new AgentCycleResult(examined, replayed, $"{enabled.Count} rule(s) on, {examined} matching message(s) looked at, {replayed} replayed, {held} held for a person, {tripped} rule(s) stopped by the breaker");
    }
}
