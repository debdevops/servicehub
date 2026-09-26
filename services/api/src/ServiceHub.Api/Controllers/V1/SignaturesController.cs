using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>Failure signatures (unit 3.8): which failures are the same failure. Read-only — a rule is created on Auto Replay, never here.</summary>
[Route("api/v1/signatures")]
public sealed class SignaturesController : ApiControllerBase
{
    private readonly ISignaturesService _signatures;
    private readonly IRecoveryTrustScoringService _trust;
    private readonly IRecoveryLedger _ledger;

    /// <summary>Creates the controller.</summary>
    public SignaturesController(ISignaturesService signatures, IRecoveryTrustScoringService trust, IRecoveryLedger ledger)
    {
        _signatures = signatures ?? throw new ArgumentNullException(nameof(signatures));
        _trust = trust ?? throw new ArgumentNullException(nameof(trust));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }

    /// <summary>
    /// A page of signatures. <paramref name="tab"/> is all (default), growing, helps or doesnt. <paramref name="namespaceId"/> or
    /// <paramref name="environment"/> counts only that namespace, or only the namespaces of that environment.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SignaturePage), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] CloudProviderType? provider, [FromQuery] int? days, [FromQuery] string? tab, [FromQuery] string? sort,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken,
        [FromQuery] Guid? namespaceId = null, [FromQuery] EnvironmentType? environment = null)
    {
        if (tab is not (null or "all" or "growing" or "helps" or "doesnt"))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'tab' must be all, growing, helps or doesnt.");
        }

        if (sort is not (null or "messages" or "recent"))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'sort' must be messages or recent.");
        }

        if (days is < 1 or > 30)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'days' must be between 1 and 30.");
        }

        if (page is < 1 || pageSize is < 1 or > 100)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'page' starts at 1 and 'pageSize' must be between 1 and 100.");
        }

        return Ok(await _signatures.ListAsync(OwnerId, AllowedNamespaceIds, provider, days ?? 7, tab, sort ?? "messages", page ?? 1, pageSize ?? 25, cancellationToken, namespaceId, environment));
    }

    /// <summary>One signature.</summary>
    [HttpGet("{hash}")]
    [ProducesResponseType(typeof(SignatureSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string hash, [FromQuery] CloudProviderType? provider, [FromQuery] int? days, CancellationToken cancellationToken)
    {
        // The same failure can exist in two clouds, so the cloud is part of the question — never defaulted to the first.
        if (provider is not { } cloud)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "Say which cloud: give a 'provider'.");
        }

        if (days is < 1 or > 30)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'days' must be between 1 and 30.");
        }

        var found = await _signatures.GetAsync(OwnerId, AllowedNamespaceIds, hash, cloud, days ?? 7, cancellationToken);
        return found is null
            ? Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, $"No signature '{hash}' was found.")
            : Ok(found);
    }

    /// <summary>
    /// What this signature has earned (unit 4.1): whether ServiceHub may replay it without asking, and what it still needs.
    /// Read-only; the eligibility gate is what reads a grant and decides.
    /// </summary>
    [HttpGet("{hash}/trust")]
    [ProducesResponseType(typeof(SignatureTrust), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Trust(string hash, [FromQuery] CloudProviderType? provider, CancellationToken cancellationToken)
    {
        if (provider is not { } cloud)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "Say which cloud: give a 'provider'.");
        }

        // Only a signature the caller can see: anything else is indistinguishable from one that does not exist.
        if (await _signatures.GetAsync(OwnerId, AllowedNamespaceIds, hash, cloud, 7, cancellationToken) is null)
        {
            return Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, $"No signature '{hash}' was found.");
        }

        var evaluated = await _trust.EvaluateAsync(OwnerId, hash, RecoveryOperationKind.Replay, cancellationToken);
        if (evaluated.IsFailure)
        {
            return Problem(evaluated.Error);
        }

        var e = evaluated.Value;
        var grant = await _ledger.GetAutonomyGrantAsync(OwnerId, hash, RecoveryOperationKind.Replay, cancellationToken);
        var level = grant?.CurrentLevel ?? AutonomyLevel.Approve;
        var prod = await _ledger.GetSignatureEnvironmentAsync(OwnerId, hash, cancellationToken) == EnvironmentType.Prod;
        // A live DLQ observer attestation will also count here once unit 4.2 exists.
        var canConfirm = ProviderCapabilities.For(cloud).CanProveDlqAbsence;

        var (next, sample, rate) = level switch
        {
            AutonomyLevel.Approve => ((AutonomyLevel?)AutonomyLevel.Standing, Infrastructure.RecoveryLedger.RecoveryTrustScoringService.L4MinimumSample, (double?)Infrastructure.RecoveryLedger.RecoveryTrustScoringService.L4MinimumRate),
            AutonomyLevel.Standing => (AutonomyLevel.Unattended, Infrastructure.RecoveryLedger.RecoveryTrustScoringService.L5MinimumSample, Infrastructure.RecoveryLedger.RecoveryTrustScoringService.L5MinimumRate),
            _ => ((AutonomyLevel?)null, 0, (double?)null),
        };
        var climbs = next is not null && canConfirm && !prod;

        return Ok(new SignatureTrust(
            LevelWord(level), e.SampleSize, e.VerifiedSuccessRate, e.RecoveredCount, e.ReturnedCount, e.FailedCount, e.UnverifiedCount,
            climbs ? LevelWord(next!.Value) : null, climbs ? Math.Max(0, sample - e.SampleSize) : null, climbs ? rate : null,
            canConfirm, prod, e.Reasons));
    }

    /// <summary>
    /// Authority — and why (unit 6.8): how many of the signatures seen in the window sit at each level, and for those held at
    /// "a person approves" (the floor, not a waypoint), what holds them there. Counted from grants and recorded outcomes — the same
    /// facts <see cref="Trust"/> reads for one signature. Read-only.
    /// </summary>
    /// <param name="provider">Only this cloud.</param>
    /// <param name="days">The window, 1–30 (default 7).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("authority")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Authority([FromQuery] CloudProviderType? provider, [FromQuery] int? days, CancellationToken cancellationToken)
    {
        if (days is < 1 or > 30)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'days' must be between 1 and 30.");
        }

        const int PageSize = 100, MaxPages = 5;
        var seen = new List<SignatureSummary>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var chunk = await _signatures.ListAsync(OwnerId, AllowedNamespaceIds, provider, days ?? 7, "all", "messages", page, PageSize, cancellationToken);
            seen.AddRange(chunk.Items);
            if (seen.Count >= chunk.Total || chunk.Items.Count == 0) break;
        }

        var grants = (await _ledger.GetAutonomyGrantsAsync(OwnerId, cancellationToken))
            .Where(g => g.ActionKind == RecoveryOperationKind.Replay)
            .ToDictionary(g => g.SignatureHash, g => g.CurrentLevel);

        int unattended = 0, standing = 0;
        var held = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var hash in seen.Select(s => (s.SignatureHash, s.Provider)).Distinct())
        {
            var level = grants.TryGetValue(hash.SignatureHash, out var l) ? l : AutonomyLevel.Approve;
            if (level == AutonomyLevel.Unattended) { unattended++; continue; }
            if (level == AutonomyLevel.Standing) { standing++; continue; }

            string reason;
            if (await _ledger.GetSignatureEnvironmentAsync(OwnerId, hash.SignatureHash, cancellationToken) == EnvironmentType.Prod) reason = "production";
            else if (!ProviderCapabilities.For(hash.Provider).CanProveDlqAbsence) reason = "cannot_verify";
            else
            {
                var e = await _trust.EvaluateAsync(OwnerId, hash.SignatureHash, RecoveryOperationKind.Replay, cancellationToken);
                reason = e.IsSuccess && e.Value.SampleSize < Infrastructure.RecoveryLedger.RecoveryTrustScoringService.L4MinimumSample ? "needs_evidence"
                    : e.IsSuccess && e.Value.VerifiedSuccessRate < Infrastructure.RecoveryLedger.RecoveryTrustScoringService.L4MinimumRate ? "rate_too_low"
                    : "awaiting_evaluation";
            }

            held[reason] = held.GetValueOrDefault(reason) + 1;
        }

        return Ok(new
        {
            total = unattended + standing + held.Values.Sum(),
            capped = seen.Count >= PageSize * MaxPages,
            unattended,
            standing,
            approve = held.Values.Sum(),
            held = held.OrderByDescending(h => h.Value).Select(h => new { reason = h.Key, count = h.Value }),
            needs = new
            {
                sample = Infrastructure.RecoveryLedger.RecoveryTrustScoringService.L4MinimumSample,
                rate = Infrastructure.RecoveryLedger.RecoveryTrustScoringService.L4MinimumRate,
            },
        });
    }

    private static string LevelWord(AutonomyLevel level) => level switch
    {
        AutonomyLevel.Standing => "standing",
        AutonomyLevel.Unattended => "unattended",
        _ => "approve",
    };
}
