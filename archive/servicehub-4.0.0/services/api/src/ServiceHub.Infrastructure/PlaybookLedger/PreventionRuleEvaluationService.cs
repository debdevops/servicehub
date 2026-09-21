using System.Text.Json;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.PlaybookLedger;

/// <summary>
/// <inheritdoc cref="IPreventionRuleEvaluationService"/>
/// </summary>
/// <remarks>
/// P5, staged Option B (<c>PREVENTION-RULE-DESIGN-2026-08-29.md</c>). Every write goes through
/// <see cref="IPlaybookLedger"/> only — there is no reference anywhere in this type to a provider
/// SDK, <c>IRecoveryLedger</c>, or <c>AutoReplayRule</c> (§12). "The currently active rule" is
/// never stored state; it is derived, on every call, by grouping whichever
/// <c>PreventionRuleProposal</c> entries are currently <see cref="PlaybookEntryState.Approved"/>
/// by lineage (§8) — this mirrors <c>BacktestService</c>'s own "query, filter, or grouping over
/// rows already written" discipline rather than introducing a new persisted projection.
/// Reconciling a stale duplicate-lineage version (revoking it) is a write, kept deliberately
/// separate from that read: it only ever happens from the system-authored
/// <see cref="EvaluateAsync"/> path, never as a side effect of the public, read-only
/// <see cref="GetActiveRulesAsync"/> — see that method's remarks.
/// </remarks>
public sealed class PreventionRuleEvaluationService : IPreventionRuleEvaluationService
{
    internal const string ProposalKindRule = "PreventionRuleProposal";
    internal const string ProposalKindTrigger = "PreventionTrigger";

    private static readonly TimeSpan TriggerProposalExpiry = TimeSpan.FromDays(7);
    private static readonly PlaybookActor SystemActor = new("System:PreventionRuleEvaluationService", PlaybookActorKind.System);

    private readonly IPlaybookLedger _playbookLedger;
    private readonly ILogger<PreventionRuleEvaluationService> _logger;

    /// <summary>Initializes a new instance of <see cref="PreventionRuleEvaluationService"/>.</summary>
    public PreventionRuleEvaluationService(IPlaybookLedger playbookLedger, ILogger<PreventionRuleEvaluationService> logger)
    {
        _playbookLedger = playbookLedger ?? throw new ArgumentNullException(nameof(playbookLedger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Deliberately a pure read — no reconciliation happens here (contrast
    /// <see cref="GetActiveRulesAndReconcileAsync"/>). This method is reachable from
    /// <c>GET /prevention-rules/active</c>, gated only by <c>PlaybookRead</c>; a stale duplicate-
    /// lineage <c>Approved</c> entry silently revoking itself as a side effect of a GET request
    /// would let a read-scoped caller trigger a governed ledger mutation nothing else in this
    /// controller lets a read-scoped caller cause. The newest version still wins in the returned
    /// list either way — only the act of closing out the stale version is deferred to the
    /// system-authored evaluation path.
    /// </remarks>
    public async Task<IReadOnlyList<PlaybookEntry>> GetActiveRulesAsync(
        string ownerId, Guid? namespaceId, CancellationToken cancellationToken = default)
    {
        var (active, _) = await LoadAndGroupRulesAsync(ownerId, namespaceId, cancellationToken).ConfigureAwait(false);
        return active;
    }

    /// <summary>
    /// Same result as <see cref="GetActiveRulesAsync"/>, but additionally revokes every stale
    /// duplicate-lineage <c>Approved</c> entry it finds along the way (§8: "there is never a
    /// window… because the Supersede call is made atomically" — this is that reconciliation, run
    /// lazily instead of at approval time, exactly as the design specifies: "the evaluation worker
    /// calls…"). Called only from <see cref="EvaluateAsync"/> — a system-authored, worker-driven
    /// write path — never from a bare read endpoint, so this mutation is never reachable by a
    /// <c>PlaybookRead</c>-scoped caller alone.
    /// </summary>
    private async Task<IReadOnlyList<PlaybookEntry>> GetActiveRulesAndReconcileAsync(
        string ownerId, Guid? namespaceId, CancellationToken cancellationToken)
    {
        var (active, stale) = await LoadAndGroupRulesAsync(ownerId, namespaceId, cancellationToken).ConfigureAwait(false);

        foreach (var (staleEntry, activeVersion, activeEntryId) in stale)
        {
            var revokeResult = await _playbookLedger.RevokeAsync(
                staleEntry.Id,
                ownerId,
                SystemActor,
                $"Superseded by rule version {activeVersion} (entry {activeEntryId}).",
                cancellationToken).ConfigureAwait(false);

            if (revokeResult.IsFailure)
            {
                // Most commonly a lost race against a concurrent reconciliation pass or an
                // operator revoking it directly — harmless, not an error.
                _logger.LogDebug(
                    "Could not reconcile stale PreventionRule version {EntryId}: {Error}",
                    staleEntry.Id, revokeResult.Error.Message);
            }
        }

        return active;
    }

    /// <summary>
    /// Queries every <c>Approved</c> <c>PreventionRuleProposal</c> in scope and groups by
    /// <c>RuleLineageId</c> — newest version (§8) becomes the lineage's active entry; every other
    /// version in a lineage with more than one <c>Approved</c> entry is reported as stale. Pure
    /// read, no ledger writes — <see cref="GetActiveRulesAsync"/> and
    /// <see cref="GetActiveRulesAndReconcileAsync"/> both build on this and differ only in whether
    /// they act on the stale list.
    /// </summary>
    private async Task<(List<PlaybookEntry> Active, List<(PlaybookEntry Entry, int ActiveVersion, Guid ActiveEntryId)> Stale)>
        LoadAndGroupRulesAsync(string ownerId, Guid? namespaceId, CancellationToken cancellationToken)
    {
        var result = await _playbookLedger.QueryEntriesAsync(
            ownerId, PillarKind.Prevent, namespaceId, PlaybookEntryState.Approved, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Could not query active PreventionRule proposals for owner {OwnerId}: {Error}",
                ownerId, result.Error.Message);
            return (new List<PlaybookEntry>(), new List<(PlaybookEntry, int, Guid)>());
        }

        var parsed = result.Value
            .Where(e => e.ProposalKind == ProposalKindRule)
            .Select(e => (Entry: e, Rule: TryParseRule(e.ProposalJson)))
            .Where(x => x.Rule is not null)
            .ToList();

        var active = new List<PlaybookEntry>();
        var stale = new List<(PlaybookEntry, int, Guid)>();

        foreach (var group in parsed.GroupBy(x => x.Rule!.RuleLineageId))
        {
            // Newest version wins. More than one Approved entry for the same lineage only happens
            // when an edit's new version was approved without the prior version ever being
            // explicitly closed out.
            var ordered = group
                .OrderByDescending(x => x.Rule!.RuleVersion)
                .ThenByDescending(x => x.Entry.ProposedAt)
                .ToList();

            active.Add(ordered[0].Entry);

            foreach (var staleEntry in ordered.Skip(1))
            {
                stale.Add((staleEntry.Entry, ordered[0].Rule!.RuleVersion, ordered[0].Entry.Id));
            }
        }

        return (active, stale);
    }

    /// <inheritdoc/>
    public async Task EvaluateAsync(Namespace ns, IReadOnlyList<DriftFinding> findings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentNullException.ThrowIfNull(findings);

        if (findings.Count == 0)
        {
            return;
        }

        var activeRules = await GetActiveRulesAndReconcileAsync(ns.OwnerId, ns.Id, cancellationToken).ConfigureAwait(false);
        if (activeRules.Count == 0)
        {
            return;
        }

        // Loaded once per cycle, reused for every rule×finding match below — CountPriorTriggers
        // used to re-query this namespace's full Prevent-pillar entry set once per match, which
        // grows on every proposed trigger, making a cycle with several matches progressively more
        // expensive. One snapshot per cycle keeps the query count bounded regardless of how many
        // rules/findings this cycle produces. Safe to compute once: DriftDetectionWorker's cycle
        // is strictly sequential (never runs concurrently with itself), and a rule matches at most
        // one finding per cycle in practice (one finding per entity per detection pass, and a
        // rule's EntityName is fixed), so no trigger proposed earlier in this same loop is ever a
        // prior occurrence this snapshot needed to already contain.
        var existingTriggers = await LoadExistingTriggersAsync(ns, cancellationToken).ConfigureAwait(false);

        foreach (var ruleEntry in activeRules)
        {
            var rule = TryParseRule(ruleEntry.ProposalJson);
            if (rule is null)
            {
                continue;
            }

            foreach (var finding in findings)
            {
                if (!Matches(rule, finding))
                {
                    continue;
                }

                await ProposeTriggerAsync(ruleEntry, rule, finding, ns, existingTriggers, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<int> SweepExpiredRulesAsync(string ownerId, DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        var result = await _playbookLedger.QueryEntriesAsync(
            ownerId, PillarKind.Prevent, namespaceId: null, PlaybookEntryState.Approved, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "PreventionRule expiry sweep could not query owner {OwnerId}: {Error}", ownerId, result.Error.Message);
            return 0;
        }

        var revokedCount = 0;

        foreach (var entry in result.Value.Where(e => e.ProposalKind == ProposalKindRule))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rule = TryParseRule(entry.ProposalJson);
            if (rule is null || rule.RuleExpiresAt > asOf)
            {
                continue;
            }

            var revokeResult = await _playbookLedger.RevokeAsync(
                entry.Id, ownerId, SystemActor, "Rule expired, not reconfirmed.", cancellationToken).ConfigureAwait(false);

            if (revokeResult.IsSuccess)
            {
                revokedCount++;
                _logger.LogInformation(
                    "PreventionRule {EntryId} ({RuleName}) revoked: expired without reconfirmation", entry.Id, rule.Name);
            }
            else
            {
                // Most commonly a lost race against a human revoking/editing it between the query
                // above and this call — harmless, not an error.
                _logger.LogDebug(
                    "Skipped expiring PreventionRule {EntryId}: {Error}", entry.Id, revokeResult.Error.Message);
            }
        }

        return revokedCount;
    }

    private static bool Matches(PreventionRuleProposal rule, DriftFinding finding)
    {
        if (!string.Equals(finding.EntityName, rule.EntityName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(rule.Condition.DriftFindingType, "Any", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(rule.Condition.DriftFindingType, finding.Type.ToString(), StringComparison.Ordinal))
        {
            return false;
        }

        return finding.Severity >= rule.Condition.MinSeverity;
    }

    // Every cycle-match is written as evidence — MinOccurrences/WindowHours is never a
    // write-suppression gate (see PreventionRuleCondition.MinOccurrences). Counting prior
    // PreventionTrigger entries for the same lineage, rather than prior DriftFinding entries,
    // avoids undercounting: P2 only ever writes a durable DriftFinding-kind entry when severity
    // crosses the fleet-wide push threshold, which a rule's own MinSeverity may sit below.
    private async Task ProposeTriggerAsync(
        PlaybookEntry ruleEntry, PreventionRuleProposal rule, DriftFinding finding, Namespace ns,
        IReadOnlyList<PlaybookEntry> existingTriggers, CancellationToken cancellationToken)
    {
        var occurrencesInWindow = 1 + CountPriorTriggers(rule, existingTriggers);

        var trigger = new PreventionTriggerProposal(
            RuleLineageId: rule.RuleLineageId,
            RuleEntryId: ruleEntry.Id,
            RuleVersion: rule.RuleVersion,
            Name: rule.Name,
            EntityName: finding.EntityName,
            DriftFindingId: finding.Id,
            FindingType: finding.Type.ToString(),
            FindingSeverity: finding.Severity,
            OccurrencesInWindow: occurrencesInWindow,
            MinOccurrences: rule.Condition.MinOccurrences,
            WindowHours: rule.Condition.WindowHours,
            MetOccurrenceThreshold: occurrencesInWindow >= rule.Condition.MinOccurrences);

        var proposalJson = JsonSerializer.Serialize(trigger);
        var evidenceRefJson = JsonSerializer.Serialize(new
        {
            DriftFindingId = finding.Id,
            finding.DetectedAt,
            RuleEntryId = ruleEntry.Id,
        });

        var result = await _playbookLedger.ProposeAsync(new ProposePlaybookEntryRequest
        {
            OwnerId = ns.OwnerId,
            PillarKind = PillarKind.Prevent,
            ProposalKind = ProposalKindTrigger,
            EvidenceRefJson = evidenceRefJson,
            ProposalJson = proposalJson,
            Proposer = SystemActor,
            NamespaceId = ns.Id,
            NamespaceNameSnapshot = ns.Name,
            ProviderSnapshot = ns.Provider,
            EnvironmentSnapshot = ns.Environment,
            ExpiresAfter = TriggerProposalExpiry,
        }, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Failed to propose PreventionTrigger for rule {RuleEntryId} / drift finding {DriftFindingId}: {Error}",
                ruleEntry.Id, finding.Id, result.Error.Message);
        }
    }

    private async Task<IReadOnlyList<PlaybookEntry>> LoadExistingTriggersAsync(Namespace ns, CancellationToken cancellationToken)
    {
        var result = await _playbookLedger.QueryEntriesAsync(
            ns.OwnerId, PillarKind.Prevent, ns.Id, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Could not query existing PreventionTrigger entries for namespace {NamespaceId}: {Error}",
                ns.Id, result.Error.Message);
            return Array.Empty<PlaybookEntry>();
        }

        return result.Value.Where(e => e.ProposalKind == ProposalKindTrigger).ToList();
    }

    private static int CountPriorTriggers(PreventionRuleProposal rule, IReadOnlyList<PlaybookEntry> existingTriggers)
    {
        var windowStart = DateTimeOffset.UtcNow - TimeSpan.FromHours(rule.Condition.WindowHours);

        return existingTriggers.Count(e =>
            e.ProposedAt >= windowStart
            && TryParseTrigger(e.ProposalJson)?.RuleLineageId == rule.RuleLineageId);
    }

    private static PreventionRuleProposal? TryParseRule(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PreventionRuleProposal>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PreventionTriggerProposal? TryParseTrigger(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PreventionTriggerProposal>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
