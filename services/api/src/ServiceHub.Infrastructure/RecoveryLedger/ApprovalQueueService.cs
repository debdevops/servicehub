using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>
/// <inheritdoc cref="IApprovalQueueService"/>
/// </summary>
/// <remarks>
/// Reads three existing tables (no schema change, honours the RC1 migration freeze) —
/// <c>RecoveryLedgerEntries</c>, <c>RecoveryOperations</c>, and <c>DlqMessages</c> — the same
/// tables <see cref="RecoveryLedgerService"/> and <see cref="AutoReplayExecutor"/> already write.
/// Approval is deliberately not a method here: an operator "approves" a queued item by calling the
/// existing, already-gated <c>POST /api/v1/messages/replay</c> endpoint with the fields this
/// service returns — reusing that path exactly, rather than adding a second execution route that
/// would have to re-derive the same safety checks (roadmap principle: "no gate change").
/// </remarks>
public sealed class ApprovalQueueService : IApprovalQueueService
{
    // Mirrors RecoveryEligibilityGate's private Deny-verdict reason codes. Defensive only: every
    // current AutoReplayRule call site is replay-only against a non-Prod namespace (DlqMonitorWorker
    // never evaluates rules against Prod), so predicates 1 (purge origin) and 2 (production
    // elevation) — the gate's only two Deny outcomes — can never actually produce a rule-triggered
    // Declined entry today. Excluded by name anyway so a future change to that calling context can
    // never surface a non-overridable Deny as if it were an approvable Escalate.
    private const string ReasonPurgeAutomationProhibited = "PURGE_AUTOMATION_PROHIBITED";
    private const string ReasonProductionElevationRequired = "PRODUCTION_ELEVATION_REQUIRED";

    private static readonly string SubscriptionEntityType = ServiceBusEntityType.Subscription.ToString();

    private readonly DlqDbContext _dbContext;

    /// <summary>Initialises a new instance of <see cref="ApprovalQueueService"/>.</summary>
    public ApprovalQueueService(DlqDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalQueueEntryResponse>> GetPendingApprovalsAsync(
        string ownerId, Guid? namespaceId, int limit, CancellationToken cancellationToken = default)
    {
        var declinedQuery =
            from entry in _dbContext.RecoveryLedgerEntries.AsNoTracking()
            join op in _dbContext.RecoveryOperations.AsNoTracking() on entry.OperationId equals op.Id
            join message in _dbContext.DlqMessages.AsNoTracking() on entry.DlqMessageId equals message.Id
            where entry.OwnerId == ownerId
                && entry.State == RecoveryEntryState.Declined
                && entry.NamespaceId != null
                && entry.DlqMessageId != null
                && op.Trigger == RecoveryTrigger.AutoRule
                && op.SourceRuleId != null
                && message.Status == DlqMessageStatus.Active
            select new { entry, op };

        if (namespaceId is { } ns)
        {
            declinedQuery = declinedQuery.Where(x => x.entry.NamespaceId == ns);
        }

        var declined = await declinedQuery
            .OrderByDescending(x => x.entry.BegunAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken);

        if (declined.Count == 0)
        {
            return Array.Empty<ApprovalQueueEntryResponse>();
        }

        var entryIds = declined.Select(x => x.entry.Id).ToList();
        var ruleIds = declined.Select(x => x.op.SourceRuleId!.Value).Distinct().ToList();

        // One RecoveryEvent per Declined entry (RecoveryLedgerService.RecordDeclinedAsync appends
        // exactly one EligibilityDeclined event when it creates the entry) — batched here rather
        // than queried per-row to avoid an N+1 against the ledger's event table.
        var declineEvents = await _dbContext.RecoveryEvents
            .AsNoTracking()
            .Where(e => e.EntryId != null && entryIds.Contains(e.EntryId.Value)
                && e.EventType == RecoveryEventType.EligibilityDeclined)
            .ToListAsync(cancellationToken);
        var declineEventsByEntry = declineEvents
            .GroupBy(e => e.EntryId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Seq).First());

        var rules = await _dbContext.AutoReplayRules
            .AsNoTracking()
            .Where(r => ruleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        var results = new List<ApprovalQueueEntryResponse>(declined.Count);
        foreach (var (entry, op) in declined.Select(x => (x.entry, x.op)))
        {
            var (reasonCode, matchedCount) = ExtractDeclineDetail(declineEventsByEntry.GetValueOrDefault(entry.Id));
            if (reasonCode is ReasonPurgeAutomationProhibited or ReasonProductionElevationRequired)
            {
                continue;
            }

            var ruleId = op.SourceRuleId!.Value;
            var (entityName, subscriptionName) = ResolveEntityAndSubscription(entry);

            if (entry.SourceSequenceNumberSnapshot is not { } sequenceNumber)
            {
                // No sequence number captured for this entry — cannot drive the replay endpoint,
                // which identifies the message by (namespace, entity, sequenceNumber). Should not
                // occur for an AutoRule-triggered decline (the rule engine always evaluates a real,
                // already-loaded DlqMessage), but skip rather than surface an unactionable row.
                continue;
            }

            results.Add(new ApprovalQueueEntryResponse(
                EntryId: entry.Id,
                NamespaceId: entry.NamespaceId!.Value,
                NamespaceName: entry.NamespaceNameSnapshot,
                Provider: entry.ProviderSnapshot?.ToString(),
                Environment: entry.EnvironmentSnapshot?.ToString(),
                EntityName: entityName,
                SubscriptionName: subscriptionName,
                SequenceNumber: sequenceNumber,
                FailureCategory: entry.FailureCategorySnapshot?.ToString(),
                RuleId: ruleId,
                RuleName: rules.GetValueOrDefault(ruleId) ?? $"Rule {ruleId}",
                ReasonCode: reasonCode,
                MatchedCount: matchedCount,
                DeclinedAt: entry.BegunAt));
        }

        return results;
    }

    private static (string entityName, string? subscriptionName) ResolveEntityAndSubscription(
        RecoveryLedgerEntry entry)
    {
        if (entry.EntityTypeSnapshot == SubscriptionEntityType
            && !string.IsNullOrEmpty(entry.TopicNameSnapshot)
            && !string.IsNullOrEmpty(entry.EntityNameSnapshot))
        {
            var prefix = $"{entry.TopicNameSnapshot}/subscriptions/";
            var subscriptionName = entry.EntityNameSnapshot.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? entry.EntityNameSnapshot[prefix.Length..]
                : entry.EntityNameSnapshot;
            return (entry.TopicNameSnapshot, subscriptionName);
        }

        return (entry.EntityNameSnapshot ?? entry.TargetEntity, null);
    }

    private static (string? reasonCode, int? matchedCount) ExtractDeclineDetail(RecoveryEvent? declineEvent)
    {
        if (declineEvent?.DetailJson is not { Length: > 0 } json)
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var reasonCode = doc.RootElement.TryGetProperty("reasonCode", out var reasonProp)
                ? reasonProp.GetString()
                : null;
            var matchedCount = doc.RootElement.TryGetProperty("matchedCount", out var matchedProp)
                && matchedProp.ValueKind == JsonValueKind.Number
                ? matchedProp.GetInt32()
                : (int?)null;
            return (reasonCode, matchedCount);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
