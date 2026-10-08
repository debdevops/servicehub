using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Helpers;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Signatures;

/// <summary>What a re-sign did (or, for a dry run, would do) to one namespace.</summary>
/// <param name="Messages">Dead letters looked at.</param>
/// <param name="MessagesChanged">Dead letters whose signature is different under fingerprint v2.</param>
/// <param name="SignaturesBefore">Signature rows the namespace had.</param>
/// <param name="SignaturesAfter">Signature rows it has (or would have) afterwards.</param>
/// <param name="FoldedIntoOther">Dead letters that went to the <c>&lt;other&gt;</c> group because their queue had too many different error shapes.</param>
/// <param name="Saved">False for a dry run.</param>
/// <param name="RulesDisabled">Auto-replay rules whose signature no longer exists anywhere, switched off so a person picks which new group they should cover (design 10 §7).</param>
public sealed record ResignResult(int Messages, int MessagesChanged, int SignaturesBefore, int SignaturesAfter, int FoldedIntoOther, bool Saved, int RulesDisabled = 0);

/// <summary>
/// Gives every dead letter in a namespace its fingerprint-v2 signature and rebuilds the namespace's signature tallies to match
/// (design 10 §5–§7). A one-off data rewrite: no schema change, and <b>not wired to anything</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Do not run this on real data until the owner has signed off the data rewrite</b> (design 10 O1 / ADR-0017) and answered
/// what happens to trust (O2) — every signature's identity changes, so trust, grants and rules keyed by the old hashes stop
/// matching. It moves the hashes and switches off the rules that depended on an old one (never deleted, never re-pointed: which new
/// group a rule should cover is a person's choice). It touches no ledger entry and no grant.
/// </para>
/// <para>
/// Safe to repeat and to interrupt: the result depends only on the recorded dead letters, never on progress, so a second run
/// changes nothing; and the whole namespace is one transaction, so an interrupted run leaves it exactly as it was. Occurrence
/// counts are rebuilt from the dead letters still recorded, so messages purged since they were counted no longer add to them.
/// </para>
/// </remarks>
public static class SignatureResigner
{
    private static readonly FailureFeatureExtractor Extractor = new();
    private static readonly FailureFingerprintBuilder Builder = new(includeErrorTemplate: true);

    /// <summary>
    /// Re-signs one namespace. With <paramref name="dryRun"/> nothing is written and the result says what would change.
    /// <paramref name="onGroupDone"/> is told how many dead letters are done so far (progress; tests also use it to simulate a crash).
    /// </summary>
    public static async Task<ResignResult> ResignNamespaceAsync(
        ServiceHubDbContext db, string ownerId, Guid namespaceId, bool dryRun, int maxTemplatesPerQueue = ErrorTemplateCap.DefaultMax,
        Action<int>? onGroupDone = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var keys = await db.DlqMessages.AsNoTracking()
            .Where(m => m.OwnerId == ownerId && m.NamespaceId == namespaceId)
            .Select(m => new { m.DeadLetterReason, m.EntityName }).Distinct().ToListAsync(ct).ConfigureAwait(false);

        var tallies = new Dictionary<string, NamespaceSignature>(StringComparer.Ordinal);
        int total = 0, changed = 0, folded = 0;
        var oldHashes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();
            var reason = key.DeadLetterReason;
            var entity = key.EntityName;
            var group = await db.DlqMessages
                .Where(m => m.OwnerId == ownerId && m.NamespaceId == namespaceId && m.EntityName == entity && m.DeadLetterReason == reason)
                .OrderBy(m => m.DetectedAtUtc).ThenBy(m => m.Id)
                .ToListAsync(ct).ConfigureAwait(false);

            var shapes = ErrorTemplateCap.AssignAll(
                group.Select(m => ErrorTemplate.Normalize(m.DeadLetterErrorDescription ?? m.BodyPreview)).ToList(), maxTemplatesPerQueue);

            for (var i = 0; i < group.Count; i++)
            {
                var message = group[i];
                var features = (await Extractor.ExtractAsync(message, ct).ConfigureAwait(false)).Value with { ErrorTemplate = shapes[i] };
                var fingerprint = (await Builder.ComputeAsync(features, ct).ConfigureAwait(false)).Value;

                total++;
                if (shapes[i] == ErrorTemplateCap.Other)
                {
                    folded++;
                }

                if (message.SignatureHash != fingerprint.Hash)
                {
                    changed++;
                    if (message.SignatureHash is { } old)
                    {
                        oldHashes.Add(old);
                    }

                    message.SignatureHash = fingerprint.Hash;
                }

                if (tallies.TryGetValue(fingerprint.Hash, out var row))
                {
                    row.OccurrenceCount++;
                    if (message.DetectedAtUtc > row.LastSeenAt)
                    {
                        row.LastSeenAt = message.DetectedAtUtc;
                    }
                }
                else
                {
                    tallies[fingerprint.Hash] = new NamespaceSignature
                    {
                        NamespaceId = namespaceId,
                        OwnerId = ownerId,
                        SignatureHash = fingerprint.Hash,
                        FirstSeenAt = message.DetectedAtUtc,
                        LastSeenAt = message.DetectedAtUtc,
                        OccurrenceCount = 1,
                        DominantDeadletterReason = message.DeadLetterReason ?? "Unknown",
                        EntityName = message.EntityName,
                        ExampleError = message.DeadLetterErrorDescription is { Length: > 1024 } err ? err[..1024] : message.DeadLetterErrorDescription,
                        TopTermsJson = JsonSerializer.Serialize(fingerprint.TopTerms),
                    };
                }
            }

            if (!dryRun)
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            db.ChangeTracker.Clear();
            onGroupDone?.Invoke(total);
        }

        var before = await db.NamespaceSignatures.CountAsync(s => s.OwnerId == ownerId && s.NamespaceId == namespaceId, ct).ConfigureAwait(false);

        var rulesDisabled = await DisableStaleRulesAsync(db, ownerId, namespaceId, oldHashes, tallies.Values, dryRun, ct).ConfigureAwait(false);

        if (!dryRun)
        {
            // Deleted and saved first, so the unique (owner, namespace, hash) index never sees an old and a new row together.
            await db.NamespaceSignatures.Where(s => s.OwnerId == ownerId && s.NamespaceId == namespaceId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            db.NamespaceSignatures.AddRange(tallies.Values);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        else
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
        }

        db.ChangeTracker.Clear();
        return new ResignResult(total, changed, before, tallies.Count, folded, Saved: !dryRun, rulesDisabled);
    }

    /// <summary>
    /// A rule made from an old signature matches nothing once no dead letter anywhere carries that signature. That is safe but
    /// silent, so it is switched off and told why, with the new groups it could cover. Other namespaces still carrying the old
    /// signature keep the rule alive until they are re-signed too.
    /// </summary>
    private static async Task<int> DisableStaleRulesAsync(
        ServiceHubDbContext db, string ownerId, Guid namespaceId, HashSet<string> oldHashes, IEnumerable<NamespaceSignature> groups, bool dryRun, CancellationToken ct)
    {
        if (oldHashes.Count == 0)
        {
            return 0;
        }

        var stillCarried = await db.DlqMessages.AsNoTracking()
            .Where(m => m.OwnerId == ownerId && m.NamespaceId != namespaceId && m.SignatureHash != null && oldHashes.Contains(m.SignatureHash))
            .Select(m => m.SignatureHash!).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var gone = oldHashes.Except(stillCarried).ToList();
        if (gone.Count == 0)
        {
            return 0;
        }

        var rules = await db.AutoReplayRules
            .Where(r => r.OwnerId == ownerId && r.SignatureHash != null && gone.Contains(r.SignatureHash) && r.DisabledReason != StaleRuleReason)
            .ToListAsync(ct).ConfigureAwait(false);
        var candidates = groups.ToList();
        foreach (var rule in rules)
        {
            rule.Enabled = false;
            rule.DisabledReason = StaleRuleReason;
            rule.DisabledDetail = StaleRuleDetail(rule, candidates);
            rule.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (!dryRun && rules.Count > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        db.ChangeTracker.Clear();
        return rules.Count;
    }

    /// <summary><see cref="AutoReplayRule.DisabledReason"/> for a rule whose signature was split.</summary>
    public const string StaleRuleReason = "SignatureSplit";

    private static string StaleRuleDetail(AutoReplayRule rule, List<NamespaceSignature> groups)
    {
        var near = groups
            .Where(g => (rule.EntityName is null || g.EntityName == rule.EntityName)
                && (rule.Reason is null || g.DominantDeadletterReason == rule.Reason))
            .OrderByDescending(g => g.OccurrenceCount).Take(3)
            .Select(g => $"\u201c{Excerpt(g.ExampleError)}\u201d ({g.OccurrenceCount})")
            .ToList();
        var text = "ServiceHub now groups failures by their error message, so the group this rule was made for no longer exists and it was switched off."
            + (near.Count == 0 ? " Make a new rule from a current group."
                : $" Make a new rule from the group it should cover: {string.Join(", ", near)}.");
        return text.Length <= 512 ? text : text[..511] + "\u2026";
    }

    private static string Excerpt(string? error) =>
        string.IsNullOrWhiteSpace(error) ? "no error text" : error.Length <= 60 ? error.Trim() : error[..60].Trim() + "\u2026";
}
