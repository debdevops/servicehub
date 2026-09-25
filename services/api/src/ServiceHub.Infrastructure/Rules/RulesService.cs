using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.BulkOperations;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Rules;

/// <summary>Auto Replay rules: the data, the matching, the numbers under each rule, the 7-day test, and the circuit breaker.</summary>
/// <remarks>
/// <b>No rule DSL</b> (rejected in writing): a rule is a reason, a queue and a pace. Nothing here acts — the
/// <see cref="AutoReplayAgent"/> does, and only through the eligibility gate.
/// </remarks>
public sealed class RulesService : IRulesService
{
    /// <summary>Default: score the last 20 verified outcomes.</summary>
    public const int DefaultSampleSize = 20;

    /// <summary>Default: stop below 50% stayed-fixed.</summary>
    public const double DefaultFloor = 0.50;

    private readonly ServiceHubDbContext _db;
    private readonly INamespaceRepository _namespaces;
    private readonly IDlqReplayService _replay;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;

    /// <summary>Creates the service.</summary>
    public RulesService(ServiceHubDbContext db, INamespaceRepository namespaces, IDlqReplayService replay, IConfiguration configuration, TimeProvider? time = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _namespaces = namespaces ?? throw new ArgumentNullException(nameof(namespaces));
        _replay = replay ?? throw new ArgumentNullException(nameof(replay));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The breaker's sample size (config <c>RecoveryEvidence:CircuitBreakerSampleSize</c>, as in 4.0.0).</summary>
    public int SampleSize => Math.Clamp(_configuration.GetValue("RecoveryEvidence:CircuitBreakerSampleSize", DefaultSampleSize), 1, 10000);

    /// <summary>The breaker's floor (config <c>RecoveryEvidence:CircuitBreakerSuccessRateFloor</c>).</summary>
    public double Floor => Math.Clamp(_configuration.GetValue("RecoveryEvidence:CircuitBreakerSuccessRateFloor", DefaultFloor), 0.05, 1.0);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RuleView>> ListAsync(string ownerId, CloudProviderType provider, CancellationToken ct)
    {
        var rules = await _db.AutoReplayRules.AsNoTracking().Where(r => r.OwnerId == ownerId && r.Provider == provider)
            .OrderByDescending(r => r.Enabled).ThenBy(r => r.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        var views = new List<RuleView>();
        foreach (var rule in rules)
        {
            views.Add(await ViewAsync(rule, ct).ConfigureAwait(false));
        }

        return views;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RuleSource>> SourcesAsync(string ownerId, CloudProviderType provider, CancellationToken ct)
    {
        var rows = await _db.NamespaceSignatures.AsNoTracking()
            .Join(_db.Namespaces.Where(n => n.OwnerId == ownerId && n.Provider == provider), s => s.NamespaceId, n => n.Id, (s, _) => s)
            .OrderByDescending(s => s.LastSeenAt).Take(50).ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.GroupBy(s => s.SignatureHash).Select(g => g.First() is var f
            ? new RuleSource(g.Key, f.DominantDeadletterReason, f.EntityName, g.Sum(x => x.OccurrenceCount), f.ExampleError) : null!)];
    }

    /// <inheritdoc />
    public async Task<Result<RuleView>> CreateAsync(
        string ownerId, CloudProviderType provider, string name, string? reason, string? entity, string? signatureHash,
        int maxPerHour, int waitSeconds, bool backOff, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
        {
            return Result<RuleView>.Failure(Error.Validation(ErrorCodes.ValidationFailed, "Give the rule a name of up to 120 characters."));
        }

        if (string.IsNullOrWhiteSpace(reason) && string.IsNullOrWhiteSpace(entity) && string.IsNullOrWhiteSpace(signatureHash))
        {
            return Result<RuleView>.Failure(Error.Validation(ErrorCodes.ValidationFailed, "A rule needs at least one condition. A rule that matches every dead letter is not allowed."));
        }

        if (maxPerHour is < 1 or > 1000 || waitSeconds is < 0 or > 86400)
        {
            return Result<RuleView>.Failure(Error.Validation(ErrorCodes.ValidationFailed, "Replays per hour must be 1–1000 and the wait 0–86400 seconds."));
        }

        var rule = new AutoReplayRule
        {
            OwnerId = ownerId, Name = name.Trim(), Provider = provider, Reason = Clean(reason), EntityName = Clean(entity), SignatureHash = Clean(signatureHash),
            MaxPerHour = maxPerHour, WaitSeconds = waitSeconds, BackOff = backOff, CreatedAt = _time.GetUtcNow(),
        };
        _db.AutoReplayRules.Add(rule);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<RuleView>.Success(await ViewAsync(rule, ct).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<Result<RuleView>> SetEnabledAsync(string ownerId, long id, bool enabled, CancellationToken ct)
    {
        var rule = await _db.AutoReplayRules.FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == ownerId, ct).ConfigureAwait(false);
        if (rule is null)
        {
            return Result<RuleView>.Failure(Error.NotFound("RULE_NOT_FOUND", $"Rule '{id}' was not found."));
        }

        rule.Enabled = enabled;
        rule.DisabledReason = enabled ? null : "Person";
        rule.DisabledDetail = enabled ? null : "Turned off by a person.";
        rule.UpdatedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<RuleView>.Success(await ViewAsync(rule, ct).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<RuleTest> TestAsync(
        string ownerId, IReadOnlySet<Guid>? allowed, CloudProviderType provider, string? reason, string? entity, string? signatureHash, int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, 30);
        var since = _time.GetUtcNow().AddDays(-days);
        var probe = new AutoReplayRule { OwnerId = ownerId, Name = "test", Provider = provider, Reason = Clean(reason), EntityName = Clean(entity), SignatureHash = Clean(signatureHash), CreatedAt = since };
        var matched = (await Matching(probe).Where(m => m.DetectedAtUtc >= since).ToListAsync(ct).ConfigureAwait(false));

        var actor = new RecoveryActor("System:rule-test", RecoveryActorKind.Automation);
        var holds = new Dictionary<string, int>();
        var wouldRun = 0;
        var waiting = 0;
        var cache = new Dictionary<Guid, Namespace?>();
        foreach (var m in matched)
        {
            if (!cache.TryGetValue(m.NamespaceId, out var ns))
            {
                var found = await _namespaces.GetByIdAsync(m.NamespaceId, ct).ConfigureAwait(false);
                ns = found.IsSuccess && found.Value.OwnerId == ownerId && (allowed is null || allowed.Contains(found.Value.Id)) ? found.Value : null;
                cache[m.NamespaceId] = ns;
            }

            if (m.Status != DlqMessageStatus.Active)
            {
                continue; // already gone from the queue: matched, but there is nothing left to decide
            }

            waiting++;
            var decision = ns is null ? null : await _replay.CheckEligibilityAsync(m.Id, ns, actor, RecoveryOperationKind.Replay, ct).ConfigureAwait(false);
            if (decision is { Verdict: EligibilityVerdict.Allow })
            {
                wouldRun++;
            }
            else
            {
                var code = decision?.ReasonCode ?? "NOT_FOUND";
                holds[code] = holds.GetValueOrDefault(code) + 1;
            }
        }

        return new RuleTest(days, matched.Count, waiting, wouldRun, waiting - wouldRun,
            [.. holds.OrderByDescending(h => h.Value).Select(h => new RuleTestHold(h.Key, BulkOperationService.Remedy(h.Key), h.Value))]);
    }

    /// <summary>The dead letters a rule picks: active, in its cloud, and matching every condition it has.</summary>
    public IQueryable<DlqMessage> Matching(AutoReplayRule rule)
    {
        var q = _db.DlqMessages.AsNoTracking().Where(m => m.OwnerId == rule.OwnerId && m.CloudProvider == rule.Provider);
        if (rule.Reason is { } reason) q = q.Where(m => m.DeadLetterReason == reason);
        if (rule.EntityName is { } entity) q = q.Where(m => m.EntityName == entity);
        if (rule.SignatureHash is { } sig) q = q.Where(m => m.SignatureHash == sig);
        return q;
    }

    /// <summary>
    /// The circuit breaker: if the rule's last <see cref="SampleSize"/> verified outcomes stayed fixed less often than
    /// <see cref="Floor"/>, it turns the rule off, says why, and never turns it back on. Unverified outcomes are not counted —
    /// a fix that could not be checked is neither a success nor a failure. Returns true when it tripped.
    /// </summary>
    public async Task<bool> SweepBreakerAsync(AutoReplayRule rule, CancellationToken ct)
    {
        if (!rule.Enabled)
        {
            return false;
        }

        var outcomes = await VerifiedOutcomesAsync(rule.Id, SampleSize, ct).ConfigureAwait(false);
        if (outcomes.Count < SampleSize)
        {
            return false;
        }

        var fixedCount = outcomes.Count(o => o == RecoveryEntryState.Recovered);
        var rate = (double)fixedCount / outcomes.Count;
        if (rate >= Floor)
        {
            return false;
        }

        rule.Enabled = false;
        rule.DisabledReason = "CircuitBreaker";
        rule.DisabledDetail = $"Only {fixedCount} of its last {outcomes.Count} replays stayed fixed ({rate:P0}) — below the {Floor:P0} floor. Nothing it replayed is lost.";
        rule.UpdatedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private async Task<List<RecoveryEntryState>> VerifiedOutcomesAsync(long ruleId, int take, CancellationToken ct) =>
        await _db.ReplayHistories.AsNoTracking().Where(h => h.RuleId == ruleId && h.RecoveryEntryId != null)
            .Join(_db.RecoveryLedgerEntries.Where(e => e.State == RecoveryEntryState.Recovered || e.State == RecoveryEntryState.Returned),
                h => h.RecoveryEntryId, e => e.Id, (h, e) => new { e.State, h.ReplayedAt })
            .OrderByDescending(x => x.ReplayedAt).Take(take).Select(x => x.State).ToListAsync(ct).ConfigureAwait(false);

    private async Task<RuleView> ViewAsync(AutoReplayRule r, CancellationToken ct)
    {
        var history = _db.ReplayHistories.AsNoTracking().Where(h => h.RuleId == r.Id);
        var replayed = await history.CountAsync(ct).ConfigureAwait(false);
        var last = replayed == 0 ? (DateTimeOffset?)null : (await history.OrderByDescending(h => h.ReplayedAt).Select(h => h.ReplayedAt).FirstAsync(ct).ConfigureAwait(false));
        var outcomes = await VerifiedOutcomesAsync(r.Id, SampleSize, ct).ConfigureAwait(false);
        return new RuleView(
            r.Id, r.Name, r.Provider, r.Reason, r.EntityName, r.SignatureHash, r.MaxPerHour, r.WaitSeconds, r.BackOff, r.Enabled,
            r.DisabledReason, r.DisabledDetail, r.CreatedAt, r.UpdatedAt, r.LastAskedAt, r.LastAskedReason, r.AskedCount,
            replayed, last, outcomes.Count, outcomes.Count(o => o == RecoveryEntryState.Recovered), SampleSize, Floor);
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
