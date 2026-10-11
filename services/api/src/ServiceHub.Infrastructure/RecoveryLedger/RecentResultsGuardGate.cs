using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>
/// A second, stricter look at unattended replays (design 10 §8): even a signature that has earned unattended replay is
/// handed to a person when its <b>most recent</b> verified results are mixed.
/// </summary>
/// <remarks>
/// <para>
/// A signature's all-time success rate hides a change that has just started. A signature with 420 good replays and 2 bad ones
/// reads 99% for a long time, so when a different cause begins to share it (design 10 §1), the scorer keeps trusting it while
/// the bad replays pile up. This guard looks only at the last <see cref="WindowSize"/> verified results and stops automation
/// as soon as <see cref="MaxBadInWindow"/> of them were bad.
/// </para>
/// <para>
/// It wraps <see cref="IRecoveryEligibilityGate"/> and never replaces it: <c>RecoveryEligibilityGate</c> is copied code and is
/// not edited (autonomy spec R11). It can only make a decision stricter — an inner Escalate or Block is returned untouched, and
/// it only ever turns an inner Allow for <b>automation</b> into an Escalate. A person acting is never held by it. Nothing is
/// stored; once newer good results (for example a person approving fixes that hold) push the bad ones out of the window,
/// automation resumes on its own.
/// </para>
/// </remarks>
public sealed class RecentResultsGuardGate : IRecoveryEligibilityGate
{
    /// <summary>How many of the signature's latest verified results are looked at.</summary>
    public const int WindowSize = 10;

    /// <summary>How many bad results in that window hand the signature to a person.</summary>
    public const int MaxBadInWindow = 2;

    /// <summary>Reason code: the latest verified results for this kind of failure are mixed.</summary>
    public const string ReasonRecentResultsMixed = "SIGNATURE_RECENT_RESULTS_MIXED";

    /// <summary>Reason code: the latest results could not be read, so automation stopped rather than guess.</summary>
    public const string ReasonRecentResultsQueryError = "SIGNATURE_RECENT_RESULTS_QUERY_ERROR";

    private readonly IRecoveryEligibilityGate _inner;
    private readonly ServiceHubDbContext _db;
    private readonly ILogger<RecentResultsGuardGate> _logger;

    /// <summary>Wraps <paramref name="inner"/>.</summary>
    public RecentResultsGuardGate(IRecoveryEligibilityGate inner, ServiceHubDbContext db, ILogger<RecentResultsGuardGate> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<EligibilityDecision> EvaluateAsync(RecoveryEligibilityRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var decision = await _inner.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        if (decision.Verdict != EligibilityVerdict.Allow
            || request.ActorKind != RecoveryActorKind.Automation
            || string.IsNullOrEmpty(request.SignatureHash))
        {
            return decision;
        }

        try
        {
            var latest = await _db.RecoveryLedgerEntries.AsNoTracking()
                .ForSignature(_db, request.OwnerId, request.SignatureHash)
                .Where(e => e.ClosedAt != null
                    && (e.Disposition == RecoveryDisposition.Recovered || e.Disposition == RecoveryDisposition.Returned
                        || e.Disposition == RecoveryDisposition.Failed))
                .Join(_db.RecoveryOperations.AsNoTracking().Where(o => o.Kind == request.ActionKind), e => e.OperationId, o => o.Id, (e, _) => e)
                .OrderByDescending(e => e.ClosedAt).ThenByDescending(e => e.Id)
                .Take(WindowSize)
                .Select(e => e.Disposition!.Value)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var bad = latest.Count(d => d != RecoveryDisposition.Recovered);
            if (bad < MaxBadInWindow)
            {
                return decision;
            }

            _logger.LogWarning(
                "Recent-results guard escalating for owner {OwnerId} signature {SignatureHash}: {Bad} of the last {Count} verified results were bad",
                request.OwnerId, request.SignatureHash, bad, latest.Count);
            return new EligibilityDecision(EligibilityVerdict.Escalate, ReasonRecentResultsMixed,
                DetailJson: $"{{\"bad\":{bad},\"window\":{latest.Count}}}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An unrunnable safety check blocks automation; it never lets it through (roadmap §18).
            _logger.LogError(ex, "Recent-results query failed for owner {OwnerId} signature {SignatureHash}; failing closed",
                request.OwnerId, request.SignatureHash);
            return new EligibilityDecision(EligibilityVerdict.Escalate, ReasonRecentResultsQueryError);
        }
    }
}
