using System.Text.Json;
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
using ServiceHub.Shared.Results;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// P5 <c>PreventionRule</c> proposal and revocation (roadmap §5.C, staged Option B —
/// <c>PREVENTION-RULE-DESIGN-2026-08-29.md</c>). Everything else in the staged lifecycle — Human
/// Review (mark under review / disposition) — reuses the existing generic
/// <see cref="PlaybookController"/> routes unmodified, exactly as the design specifies; this
/// controller only adds the two actions that surface can't express: turning structured fields
/// into a validated <c>PreventionRuleProposal</c> payload (never accepting a caller-supplied
/// <c>Action</c> — it is always <see cref="PreventionRuleActions.ObserveOnly"/>), and revoking an
/// already-promoted rule. Every write here still goes through <see cref="IPlaybookLedger"/> only —
/// nothing in this controller ever mutates a queue, an <c>AutoReplayRule</c>, or the Recovery
/// Evidence Ledger.
/// </summary>
[Route(ApiRoutes.PreventionRules.Base)]
[Tags("Prevention Rules")]
[RequireNamespaceOwnership]
public sealed class PreventionRulesController : ApiControllerBase
{
    // Proposal review window — distinct from a PreventionTrigger's own expiry, and longer than
    // DriftFinding's 7 days: a standing rule proposal is a considered configuration decision, not
    // a single cycle's evidence, so it's reasonable to give an operator more time to review it.
    private static readonly TimeSpan ProposalReviewExpiry = TimeSpan.FromDays(14);

    private static readonly IReadOnlySet<string> AllowedDriftFindingTypeConditions = new HashSet<string>(StringComparer.Ordinal)
    {
        "Any",
        nameof(DriftFindingType.SchemaShapeDrift),
        nameof(DriftFindingType.PayloadFormatDrift),
    };

    private readonly IPlaybookLedger _playbookLedger;
    private readonly IPreventionRuleEvaluationService _evaluationService;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IGovernanceAccessEvaluator _governanceAccessEvaluator;

    /// <summary>Initializes a new instance of the <see cref="PreventionRulesController"/> class.</summary>
    public PreventionRulesController(
        IPlaybookLedger playbookLedger,
        IPreventionRuleEvaluationService evaluationService,
        INamespaceRepository namespaceRepository,
        IGovernanceAccessEvaluator governanceAccessEvaluator)
    {
        _playbookLedger = playbookLedger ?? throw new ArgumentNullException(nameof(playbookLedger));
        _evaluationService = evaluationService ?? throw new ArgumentNullException(nameof(evaluationService));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _governanceAccessEvaluator = governanceAccessEvaluator ?? throw new ArgumentNullException(nameof(governanceAccessEvaluator));
    }

    /// <summary>
    /// Proposes a new P5 <c>PreventionRule</c>, or (when <see cref="ProposePreventionRuleRequest.SupersedesRuleEntryId"/>
    /// is set) a new version of an already-promoted one. Writes a <c>PlaybookEntry</c> —
    /// <c>PillarKind = Prevent</c>, <c>ProposalKind = "PreventionRuleProposal"</c> — in the
    /// <c>Proposed</c> state; a human still has to review and approve it (existing
    /// <see cref="PlaybookController"/> routes) before it becomes an active rule (§8).
    /// </summary>
    /// <param name="request">The candidate rule's structured fields.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.PlaybookWrite)]
    [HttpPost]
    [ProducesResponseType(typeof(PlaybookEntryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlaybookEntryResponse>> Propose(
        [FromBody] ProposePreventionRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateProposeRequest(request);
        if (validationError is not null)
        {
            return ToActionResult<PlaybookEntryResponse>(validationError);
        }

        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, request.NamespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<PlaybookEntryResponse>(namespaceResult.Error);
        }

        var governanceResult = await EvaluatePreventionRuleGovernanceAsync(request.NamespaceId, cancellationToken);
        if (governanceResult.IsFailure)
        {
            return ToActionResult<PlaybookEntryResponse>(governanceResult.Error);
        }

        Guid ruleLineageId;
        int ruleVersion;

        if (request.SupersedesRuleEntryId is { } priorEntryId)
        {
            var priorLineageResult = await ResolvePriorLineageAsync(priorEntryId, request, cancellationToken);
            if (priorLineageResult.IsFailure)
            {
                return ToActionResult<PlaybookEntryResponse>(priorLineageResult.Error);
            }

            (ruleLineageId, ruleVersion) = priorLineageResult.Value;
        }
        else
        {
            ruleLineageId = Guid.NewGuid();
            ruleVersion = 1;
        }

        var rule = new PreventionRuleProposal(
            RuleLineageId: ruleLineageId,
            RuleVersion: ruleVersion,
            Name: request.Name,
            EntityName: request.EntityName,
            Condition: new PreventionRuleCondition(
                DriftFindingType: request.DriftFindingType,
                MinSeverity: request.MinSeverity,
                MinOccurrences: request.MinOccurrences,
                WindowHours: request.WindowHours),
            SupersedesRuleEntryId: request.SupersedesRuleEntryId,
            RuleExpiresAt: request.RuleExpiresAt,
            Action: PreventionRuleActions.ObserveOnly);

        var proposalJson = JsonSerializer.Serialize(rule);
        var evidenceRefJson = JsonSerializer.Serialize(new
        {
            ProposedManually = true,
            Justification = string.IsNullOrWhiteSpace(request.Justification) ? "Proposed via API." : request.Justification,
        });

        var ns = namespaceResult.Value;
        var result = await _playbookLedger.ProposeAsync(new ProposePlaybookEntryRequest
        {
            OwnerId = OwnerId,
            PillarKind = PillarKind.Prevent,
            ProposalKind = "PreventionRuleProposal",
            EvidenceRefJson = evidenceRefJson,
            ProposalJson = proposalJson,
            Proposer = ResolvePlaybookActor(),
            NamespaceId = ns.Id,
            NamespaceNameSnapshot = ns.Name,
            ProviderSnapshot = ns.Provider,
            EnvironmentSnapshot = ns.Environment,
            ExpiresAfter = ProposalReviewExpiry,
        }, cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult<PlaybookEntryResponse>(result.Error);
        }

        return Created(
            $"{ApiRoutes.Playbook.Base}/entries/{result.Value.Id}",
            result.Value.ToResponse());
    }

    /// <summary>
    /// Revokes an already-promoted rule (§9) — the only way to turn off a standing rule short of
    /// letting it lapse at its own <c>RuleExpiresAt</c>. Always requires a reason.
    /// </summary>
    /// <param name="id">The rule's <c>PlaybookEntry.Id</c> (the entry that is currently <c>Approved</c>).</param>
    /// <param name="request">The mandatory revocation reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.PlaybookWrite)]
    [HttpPost("{id:guid}/revoke")]
    [ProducesResponseType(typeof(PlaybookEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlaybookEntryResponse>> Revoke(
        Guid id,
        [FromBody] RevokePreventionRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var entry = await _playbookLedger.GetEntryAsync(id, OwnerId, cancellationToken);

        // Treat an entry that isn't a PreventionRule proposal the same as one that doesn't exist —
        // this controller is scoped to one resource type, and falling through to
        // IPlaybookLedger.RevokeAsync's own allow-list check would still correctly reject it, but
        // only after running a Prevent-pillar governance check against an entry that was never
        // Prevent's to begin with. Checking here keeps the two responsibilities matched: this
        // endpoint only ever reasons about entries it actually owns the concept of.
        if (entry is null || entry.ProposalKind != "PreventionRuleProposal")
        {
            return NotFound();
        }

        var governanceResult = await EvaluatePreventionRuleGovernanceAsync(entry.NamespaceId, cancellationToken);
        if (governanceResult.IsFailure)
        {
            return ToActionResult<PlaybookEntryResponse>(governanceResult.Error);
        }

        var result = await _playbookLedger.RevokeAsync(id, OwnerId, ResolvePlaybookActor(), request.Reason, cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult<PlaybookEntryResponse>(result.Error);
        }

        return Ok(result.Value.ToResponse());
    }

    /// <summary>
    /// Lists the currently active (promoted, reconciled) rules, optionally narrowed to one
    /// namespace — the same read <c>IPreventionRuleEvaluationService.EvaluateAsync</c>
    /// drives each detection cycle, exposed for the UI/an operator to inspect directly.
    /// </summary>
    /// <param name="namespaceId">Optional namespace filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.PlaybookRead)]
    [HttpGet("active")]
    [ProducesResponseType(typeof(IReadOnlyList<PlaybookEntryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlaybookEntryResponse>>> GetActive(
        [FromQuery] Guid? namespaceId = null,
        CancellationToken cancellationToken = default)
    {
        var active = await _evaluationService.GetActiveRulesAsync(OwnerId, namespaceId, cancellationToken);
        return Ok(active.Select(PlaybookEntryResponseMapper.ToResponse).ToList());
    }

    /// <summary>
    /// Requires <see cref="GovernanceRole.Operator"/> scoped to <see cref="PillarKind.Prevent"/> and
    /// the rule's own namespace — mirrors <c>RulesController.EvaluateRuleGovernanceAsync</c>'s
    /// pattern for <c>AutoReplayRule</c>, the design's explicit precedent for why this is
    /// <see cref="GovernanceRole.Operator"/> rather than <see cref="GovernanceRole.Approver"/>:
    /// creating/revoking a standing rule is a config-lifecycle action, not a one-off proposal
    /// disposition (that stays on <see cref="PlaybookController"/>'s <see cref="GovernanceRole.Approver"/> gate).
    /// </summary>
    private async Task<Result> EvaluatePreventionRuleGovernanceAsync(Guid? namespaceId, CancellationToken cancellationToken)
    {
        var granteeIdentity = ResolveGovernanceGranteeIdentity();
        return await _governanceAccessEvaluator.EvaluateAsync(
            OwnerId, granteeIdentity, GovernanceRole.Operator, namespaceId, PillarKind.Prevent, cancellationToken);
    }

    private async Task<Result<(Guid RuleLineageId, int RuleVersion)>> ResolvePriorLineageAsync(
        Guid priorEntryId, ProposePreventionRuleRequest request, CancellationToken cancellationToken)
    {
        var priorEntry = await _playbookLedger.GetEntryAsync(priorEntryId, OwnerId, cancellationToken);
        if (priorEntry is null)
        {
            return Result.Failure<(Guid, int)>(Error.NotFound(
                ErrorCodes.Playbook.NotFound, $"Prior rule entry '{priorEntryId}' was not found."));
        }

        if (priorEntry.ProposalKind != "PreventionRuleProposal" || priorEntry.PillarKind != PillarKind.Prevent)
        {
            return Result.Failure<(Guid, int)>(Error.Validation(
                ErrorCodes.Playbook.ProposalInvalid, $"Entry '{priorEntryId}' is not a PreventionRule proposal."));
        }

        if (priorEntry.State != PlaybookEntryState.Approved)
        {
            return Result.Failure<(Guid, int)>(Error.Conflict(
                ErrorCodes.Playbook.InvalidTransition,
                $"Entry '{priorEntryId}' is not an active (Approved) rule — only a promoted rule can be edited this way."));
        }

        if (priorEntry.NamespaceId != request.NamespaceId)
        {
            return Result.Failure<(Guid, int)>(Error.Validation(
                ErrorCodes.Playbook.ProposalInvalid, "A new rule version must stay in the same namespace as the version it supersedes."));
        }

        PreventionRuleProposal? priorRule;
        try
        {
            priorRule = JsonSerializer.Deserialize<PreventionRuleProposal>(priorEntry.ProposalJson);
        }
        catch (JsonException)
        {
            priorRule = null;
        }

        if (priorRule is null)
        {
            return Result.Failure<(Guid, int)>(Error.Internal(
                ErrorCodes.Playbook.ProposalInvalid, $"Could not parse prior rule entry '{priorEntryId}'."));
        }

        return Result.Success((priorRule.RuleLineageId, priorRule.RuleVersion + 1));
    }

    private static Error? ValidateProposeRequest(ProposePreventionRuleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Error.Validation(ErrorCodes.Playbook.ProposalInvalid, "Name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.EntityName))
        {
            return Error.Validation(ErrorCodes.Playbook.ProposalInvalid, "EntityName is required.");
        }

        if (!AllowedDriftFindingTypeConditions.Contains(request.DriftFindingType))
        {
            return Error.Validation(
                ErrorCodes.Playbook.ProposalInvalid,
                $"DriftFindingType must be one of: {string.Join(", ", AllowedDriftFindingTypeConditions)}.");
        }

        if (request.MinSeverity is < 0 or > 100)
        {
            return Error.Validation(ErrorCodes.Playbook.ProposalInvalid, "MinSeverity must be between 0 and 100.");
        }

        if (request.MinOccurrences < 1)
        {
            return Error.Validation(ErrorCodes.Playbook.ProposalInvalid, "MinOccurrences must be at least 1.");
        }

        if (request.WindowHours is < 1 or > 8760)
        {
            return Error.Validation(ErrorCodes.Playbook.ProposalInvalid, "WindowHours must be between 1 and 8760 (one year).");
        }

        if (request.RuleExpiresAt <= DateTimeOffset.UtcNow)
        {
            return Error.Validation(ErrorCodes.Playbook.ProposalInvalid, "RuleExpiresAt must be in the future.");
        }

        return null;
    }
}
