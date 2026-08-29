using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Filters;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Surface over the Playbook Ledger (M4 of the persistence wave): read queries, chain
/// verification, and the <c>playbook:write</c>-gated human-disposition actions — mark under
/// review and approve/reject. Every write remains inside <see cref="IPlaybookLedger"/>; this
/// controller only translates HTTP concerns (scopes, actor resolution) and never mutates ledger
/// state itself. Nothing here ever authorizes a replay or purge — see
/// <see cref="PlaybookEntry"/> for the full design rationale.
/// </summary>
[Route(ApiRoutes.Playbook.Base)]
[Tags("Playbook")]
[RequireNamespaceOwnership]
public sealed class PlaybookController : ApiControllerBase
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    private readonly IPlaybookLedger _playbookLedger;
    private readonly ICorrelationAccountabilityService _correlationAccountability;
    private readonly IBacktestService _backtestService;

    /// <summary>Initializes a new instance of the <see cref="PlaybookController"/> class.</summary>
    public PlaybookController(
        IPlaybookLedger playbookLedger,
        ICorrelationAccountabilityService correlationAccountability,
        IBacktestService backtestService)
    {
        _playbookLedger = playbookLedger ?? throw new ArgumentNullException(nameof(playbookLedger));
        _correlationAccountability = correlationAccountability ?? throw new ArgumentNullException(nameof(correlationAccountability));
        _backtestService = backtestService ?? throw new ArgumentNullException(nameof(backtestService));
    }

    /// <summary>
    /// Lists Playbook Ledger entries for the caller, most recently proposed first, optionally
    /// narrowed by pillar, namespace, or lifecycle state.
    /// </summary>
    /// <param name="pillarKind">Optional pillar filter.</param>
    /// <param name="namespaceId">Optional namespace filter. Correlation hypotheses and other
    /// fleet-wide proposals carry no namespace and are excluded when this is set.</param>
    /// <param name="state">Optional lifecycle-state filter.</param>
    /// <param name="limit">Maximum number of entries to return (1-500, default 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.PlaybookRead)]
    [HttpGet("entries")]
    [ProducesResponseType(typeof(IReadOnlyList<PlaybookEntryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlaybookEntryResponse>>> GetEntries(
        [FromQuery] PillarKind? pillarKind = null,
        [FromQuery] Guid? namespaceId = null,
        [FromQuery] PlaybookEntryState? state = null,
        [FromQuery] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var result = await _playbookLedger.QueryEntriesAsync(
            OwnerId, pillarKind, namespaceId, state, cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult<IReadOnlyList<PlaybookEntryResponse>>(result.Error);
        }

        return Ok(result.Value.Take(ClampLimit(limit)).Select(MapToResponse).ToList());
    }

    /// <summary>
    /// Gets one Playbook Ledger entry by ID, scoped to the caller — the current projection plus
    /// its full event chain, for the entry detail view.
    /// </summary>
    /// <param name="id">The entry ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.PlaybookRead)]
    [HttpGet("entries/{id:guid}")]
    [ProducesResponseType(typeof(PlaybookEntryDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlaybookEntryDetailResponse>> GetEntryById(
        Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await _playbookLedger.GetEntryAsync(id, OwnerId, cancellationToken);
        if (entry is null)
        {
            return NotFound();
        }

        var eventsResult = await _playbookLedger.GetEventsForEntryAsync(id, OwnerId, cancellationToken);
        if (eventsResult.IsFailure)
        {
            return ToActionResult<PlaybookEntryDetailResponse>(eventsResult.Error);
        }

        return Ok(new PlaybookEntryDetailResponse(
            MapToResponse(entry),
            eventsResult.Value.Select(MapToResponse).ToList()));
    }

    /// <summary>
    /// Marks an entry <see cref="PlaybookEntryState.UnderReview"/> — a UX nicety an operator can
    /// use to signal "I'm looking at this" before deciding, valid only from
    /// <see cref="PlaybookEntryState.Proposed"/>.
    /// </summary>
    /// <param name="id">The entry to mark under review.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.PlaybookWrite)]
    [HttpPost("entries/{id:guid}/review")]
    [ProducesResponseType(typeof(PlaybookEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PlaybookEntryResponse>> MarkUnderReview(
        Guid id, CancellationToken cancellationToken = default)
    {
        var actor = ResolvePlaybookActor();
        var result = await _playbookLedger.MarkUnderReviewAsync(id, OwnerId, actor, cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult<PlaybookEntryResponse>(result.Error);
        }

        return Ok(MapToResponse(result.Value));
    }

    /// <summary>
    /// Records a human's terminal decision — approve or reject — on a proposal, valid only from
    /// <see cref="PlaybookEntryState.Proposed"/>/<see cref="PlaybookEntryState.UnderReview"/>/
    /// <see cref="PlaybookEntryState.Edited"/>. A reason is required to reject. This is the only
    /// human-in-the-loop gate the Playbook Ledger has — approving a proposal here means "a human
    /// agrees this is sound," never itself calling <see cref="IRecoveryLedger"/>.
    /// </summary>
    /// <param name="id">The entry to disposition.</param>
    /// <param name="request">The disposition and, when rejecting, the mandatory reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.PlaybookWrite)]
    [HttpPost("entries/{id:guid}/disposition")]
    [ProducesResponseType(typeof(PlaybookEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlaybookEntryResponse>> Disposition(
        Guid id,
        [FromBody] DispositionPlaybookEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = ResolvePlaybookActor();
        var result = await _playbookLedger.DispositionAsync(
            id, OwnerId, actor, request.Disposition, request.Reason, cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult<PlaybookEntryResponse>(result.Error);
        }

        return Ok(MapToResponse(result.Value));
    }

    /// <summary>
    /// Recomputes and compares the caller's Playbook Ledger hash chain — a fully independent
    /// chain from the Recovery Evidence Ledger's (see <see cref="PlaybookEvent"/>).
    /// Tamper-EVIDENT, not tamper-PROOF: it detects casual or partial alteration of the
    /// underlying SQLite file, nothing more.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.PlaybookRead)]
    [HttpGet("verify")]
    [ProducesResponseType(typeof(ChainVerificationResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChainVerificationResult>> VerifyChain(
        CancellationToken cancellationToken = default)
    {
        var result = await _playbookLedger.VerifyChainAsync(OwnerId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Correlation accountability (roadmap §5.D C4, §11 item 17): how many correlation hypotheses
    /// (C1 same-provider, C2 cross-cloud) ServiceHub has proposed and what humans decided about
    /// them — making correlation quality measurable instead of a black box. Pure read-side
    /// aggregation over the caller's Correlate-pillar Playbook entries.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.PlaybookRead)]
    [HttpGet("correlation-accountability")]
    [ProducesResponseType(typeof(CorrelationAccountabilityReport), StatusCodes.Status200OK)]
    public async Task<ActionResult<CorrelationAccountabilityReport>> GetCorrelationAccountability(
        CancellationToken cancellationToken = default)
    {
        var report = await _correlationAccountability.GetReportAsync(OwnerId, cancellationToken);
        return Ok(report);
    }

    /// <summary>
    /// Counterfactual backtesting (roadmap §11 item 14): whether the caller's dispositioned
    /// anomaly-flag (I3) and drift-finding (P2) proposals were followed by real recovery activity
    /// for the same entity — making proactive-finding quality measurable against what actually
    /// happened, not just judged "looks reasonable." Pure read-side join over the caller's
    /// Playbook entries and Recovery Evidence Ledger history.
    /// </summary>
    /// <param name="pillarKind">Optional pillar filter (only <c>Investigate</c>/<c>Prevent</c>
    /// proposals are ever backtestable today).</param>
    /// <param name="limit">Maximum number of dispositioned proposals to backtest, most recently
    /// proposed first (1-200, default 50).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.PlaybookRead)]
    [HttpGet("backtest")]
    [ProducesResponseType(typeof(BacktestReport), StatusCodes.Status200OK)]
    public async Task<ActionResult<BacktestReport>> GetBacktestReport(
        [FromQuery] PillarKind? pillarKind = null,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var report = await _backtestService.GetReportAsync(OwnerId, pillarKind, limit, cancellationToken);
        return Ok(report);
    }

    private static int ClampLimit(int limit) => Math.Clamp(limit, 1, MaxLimit);

    private static PlaybookEntryResponse MapToResponse(PlaybookEntry entry) => new(
        Id: entry.Id,
        PillarKind: entry.PillarKind.ToString(),
        ProposalKind: entry.ProposalKind,
        EvidenceRefJson: entry.EvidenceRefJson,
        ProposalJson: entry.ProposalJson,
        ProposedAt: entry.ProposedAt,
        ProposerIdentity: entry.ProposerIdentity,
        ProposerKind: entry.ProposerKind.ToString(),
        SignatureHashSnapshot: entry.SignatureHashSnapshot,
        NamespaceId: entry.NamespaceId,
        NamespaceNameSnapshot: entry.NamespaceNameSnapshot,
        ProviderSnapshot: entry.ProviderSnapshot?.ToString(),
        EnvironmentSnapshot: entry.EnvironmentSnapshot?.ToString(),
        RelatedRecoveryOperationId: entry.RelatedRecoveryOperationId,
        ExpiresAt: entry.ExpiresAt,
        State: entry.State.ToString(),
        Disposition: entry.Disposition?.ToString(),
        ClosedAt: entry.ClosedAt);

    private static PlaybookEventResponse MapToResponse(PlaybookEvent evt) => new(
        Id: evt.Id,
        Seq: evt.Seq,
        EntryId: evt.EntryId,
        EventType: evt.EventType.ToString(),
        OccurredAt: evt.OccurredAt,
        ActorIdentity: evt.ActorIdentity,
        ActorKind: evt.ActorKind.ToString(),
        DetailJson: evt.DetailJson,
        PrevHash: evt.PrevHash,
        EntryHash: evt.EntryHash,
        SchemaVersion: evt.SchemaVersion);
}
