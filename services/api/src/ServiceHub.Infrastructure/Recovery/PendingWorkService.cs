using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Recovery;

/// <inheritdoc cref="IPendingWorkService"/>
/// <remarks>
/// The approval half keeps 4.0.0's <c>ApprovalQueueService</c> query shape exactly — Declined entries of rule-triggered
/// operations whose dead letter is still active, joined to their one <c>EligibilityDeclined</c> event for the reason
/// code, <b>narrowed by the namespace allow-list even when no namespace is asked for</b> — plus one 4.1.0 rule: an entry a
/// person already declined (an <c>OperatorNote</c> decision) is answered and no longer waits.
/// </remarks>
public sealed class PendingWorkService : IPendingWorkService
{
    // The gate's two Deny codes: not approvable, so never pending work, even if one ever reached a Declined entry.
    private static readonly HashSet<string> NotApprovable = ["PURGE_AUTOMATION_PROHIBITED", "PRODUCTION_ELEVATION_REQUIRED"];

    private readonly ServiceHubDbContext _db;
    private readonly IAgentRegistry _agents;
    private readonly TimeProvider _time;

    /// <summary>Creates it.</summary>
    public PendingWorkService(ServiceHubDbContext db, IAgentRegistry agents, TimeProvider? time = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<PendingWorkPage> ListAsync(PendingWorkScope scope, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var query =
            from entry in _db.RecoveryLedgerEntries.AsNoTracking()
            join op in _db.RecoveryOperations.AsNoTracking() on entry.OperationId equals op.Id
            join message in _db.DlqMessages.AsNoTracking() on entry.DlqMessageId equals message.Id
            where entry.OwnerId == scope.OwnerId
                && entry.State == RecoveryEntryState.Declined
                && entry.NamespaceId != null
                && entry.DlqMessageId != null
                && op.Trigger == RecoveryTrigger.AutoRule
                && op.SourceRuleId != null
                && message.Status == DlqMessageStatus.Active
                && !_db.RecoveryEvents.Any(e => e.EntryId == entry.Id && e.EventType == RecoveryEventType.OperatorNote)
            select new { entry, op, message.DeadLetterReason };

        if (scope.NamespaceId is { } ns) query = query.Where(x => x.entry.NamespaceId == ns);
        if (scope.Provider is { } provider) query = query.Where(x => x.entry.ProviderSnapshot == provider);
        if (scope.Environment is { } env) query = query.Where(x => x.entry.EnvironmentSnapshot == env);
        if (scope.AllowedNamespaceIds is { } allowed)
        {
            var ids = allowed.ToList();
            query = query.Where(x => ids.Contains(x.entry.NamespaceId!.Value));
        }

        var rows = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        var entryIds = rows.Select(r => r.entry.Id).ToList();
        var reasons = (await _db.RecoveryEvents.AsNoTracking()
                .Where(e => e.EntryId != null && entryIds.Contains(e.EntryId.Value) && e.EventType == RecoveryEventType.EligibilityDeclined)
                .Select(e => new { EntryId = e.EntryId!.Value, e.DetailJson, e.Seq })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(e => e.EntryId).ToDictionary(g => g.Key, g => ReasonCodeOf(g.OrderByDescending(e => e.Seq).First().DetailJson));
        var ruleIds = rows.Select(r => r.op.SourceRuleId!.Value).Distinct().ToList();
        var ruleNames = await _db.AutoReplayRules.AsNoTracking().Where(r => ruleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken).ConfigureAwait(false);

        var items = new List<PendingWorkItem>();
        foreach (var r in rows)
        {
            var code = reasons.GetValueOrDefault(r.entry.Id) ?? "UNKNOWN";
            if (NotApprovable.Contains(code) || (scope.ReasonCode is { } wanted && wanted != code))
            {
                continue;
            }

            var ruleId = r.op.SourceRuleId!.Value;
            items.Add(new PendingWorkItem(
                "approval", r.entry.Id.ToString(), r.entry.Id, null, r.entry.DlqMessageId, r.entry.NamespaceId, r.entry.NamespaceNameSnapshot,
                r.entry.ProviderSnapshot?.ToString().ToLowerInvariant(), r.entry.EnvironmentSnapshot?.ToString().ToLowerInvariant(),
                r.entry.EntityNameSnapshot ?? r.entry.TargetEntity, r.DeadLetterReason, ruleId, ruleNames.GetValueOrDefault(ruleId) ?? $"Rule {ruleId}",
                code, EscalationReasons.Describe(code), r.entry.BegunAt));
        }

        // A rule the breaker switched off waits until a person turns it back on, changes it or deletes it (3.6). Rules are
        // per cloud, not per namespace, so a caller narrowed to one namespace or a restricted key still sees its cloud's rules.
        var stopped = await _db.AutoReplayRules.AsNoTracking()
            .Where(r => r.OwnerId == scope.OwnerId && !r.Enabled && r.DisabledReason == "CircuitBreaker" && (scope.Provider == null || r.Provider == scope.Provider))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var ruleItems = stopped
            .Where(_ => scope.ReasonCode is null || scope.ReasonCode == EscalationReasons.RuleStoppedItself)
            .Select(r => new PendingWorkItem("rule", $"rule:{r.Id}", null, null, null, null, null, r.Provider.ToString().ToLowerInvariant(), null,
                r.EntityName, r.Reason, r.Id, r.Name, EscalationReasons.RuleStoppedItself,
                r.DisabledDetail is { Length: > 0 } d ? $"{EscalationReasons.Describe(EscalationReasons.RuleStoppedItself)} ({d})" : EscalationReasons.Describe(EscalationReasons.RuleStoppedItself),
                r.UpdatedAt ?? r.CreatedAt))
            .ToList();
        items.AddRange(ruleItems);

        // Agents serve every cloud, so a stopped agent is pending work on every scope — but only for a caller who is
        // not narrowed to a reason code that is not an agent's.
        var now = _time.GetUtcNow();
        var agentItems = _agents.All().Where(a => a.NeedsAPerson(now)).Select(a =>
        {
            var code = a.IsLate(now) ? EscalationReasons.AgentStale : EscalationReasons.AgentFailing;
            return new PendingWorkItem("agent", $"agent:{a.Descriptor.Id}", null, a.Descriptor.Id, null, null, null, null, null, null, null, null, null,
                code, $"{a.Descriptor.Name}: {EscalationReasons.Describe(code)}", a.LastRunUtc ?? now);
        }).Where(i => scope.ReasonCode is null || scope.ReasonCode == i.ReasonCode).ToList();

        // Most urgent first: a stopped agent (it silently stops everything it does), then a stopped rule, then the oldest question.
        var all = agentItems.OrderBy(i => i.Since)
            .Concat(items.Where(i => i.Kind == "rule").OrderBy(i => i.Since))
            .Concat(items.Where(i => i.Kind == "approval").OrderBy(i => i.Since)).ToList();
        var byProvider = items.Where(i => i.Provider is not null).GroupBy(i => i.Provider!)
            .Select(g => new PendingWorkProviderCount(g.Key, g.Count())).OrderByDescending(p => p.Count).ToList();

        return new PendingWorkPage([.. all.Take(Math.Clamp(limit, 1, 500))], all.Count, byProvider, agentItems.Count);
    }

    private static string? ReasonCodeOf(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("reasonCode", out var code) ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
