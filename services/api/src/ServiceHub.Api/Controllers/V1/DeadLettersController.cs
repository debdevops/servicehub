using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.Constants;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Api.Security;
using ServiceHub.Core.Models;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// The durable list of dead letters ServiceHub has seen (unit 2.3) — what is stuck, filterable and
/// paged, newest first. Read-only.
/// </summary>
/// <remarks>
/// Scoped to the caller: only namespaces they may see are ever consulted, and a namespace they may not
/// see is reported exactly like one that does not exist. <b>No provider is named here</b> (rule R4) — a
/// cloud is a filter value the caller chose, never a branch. There is no body search: no cloud supports
/// it without ServiceHub keeping bodies, and promising it would be false (rule R5).
/// </remarks>
[Route("api/v1/dead-letters")]
public sealed class DeadLettersController : ApiControllerBase
{
    private const int DefaultPageSize = 25;
    private const int MaxSearch = 200;

    private readonly INamespaceRepository _namespaces;
    private readonly IDlqMessageReader _reader;
    private readonly IDlqReplayService _replay;

    /// <summary>Creates the controller.</summary>
    public DeadLettersController(INamespaceRepository namespaces, IDlqMessageReader reader, IDlqReplayService replay)
    {
        _namespaces = namespaces ?? throw new ArgumentNullException(nameof(namespaces));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _replay = replay ?? throw new ArgumentNullException(nameof(replay));
    }

    /// <summary>Lists dead letters, newest first.</summary>
    /// <param name="namespaceId">One namespace. Give this, or <paramref name="provider"/>, or both.</param>
    /// <param name="provider">One cloud — every namespace the caller has on it. Never mixes clouds.</param>
    /// <param name="environment">Only namespaces of this environment (dev, uat, prod) — the Environment level of Cloud → Environment → Namespace.</param>
    /// <param name="status">active (default), resolved or all.</param>
    /// <param name="range">How far back it was first seen: 24h, 7d, 30d, or all (default).</param>
    /// <param name="reason">Only this recorded reason.</param>
    /// <param name="noReason">Only messages with no recorded reason.</param>
    /// <param name="entity">Only this queue or subscription.</param>
    /// <param name="q">Text in the message id, entity name or reason — never the body.</param>
    /// <param name="page">1-based.</param>
    /// <param name="pageSize">1–100 (default 25).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet]
    [ProducesResponseType(typeof(DeadLetterListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? namespaceId, [FromQuery] CloudProviderType? provider, [FromQuery] EnvironmentType? environment, [FromQuery] string? status,
        [FromQuery] string? range, [FromQuery] string? reason, [FromQuery] bool noReason,
        [FromQuery] string? entity, [FromQuery] string? q, [FromQuery] int? page, [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        if (namespaceId is null && provider is null)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed,
                "Say which dead letters: give a 'namespaceId', a 'provider', or both.");
        }

        if (!TryParseStatus(status, out var wantedStatus))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'status' must be active, resolved or all.");
        }

        if (!TryParseRange(range, out var since))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'range' must be 24h, 7d, 30d or all.");
        }

        if (page is < 1 || pageSize is < 1 or > 100)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'page' starts at 1 and 'pageSize' must be between 1 and 100.");
        }

        if (noReason && !string.IsNullOrEmpty(reason))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "Choose either a 'reason' or 'noReason', not both.");
        }

        if (q is { Length: > MaxSearch })
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, $"'q' can be at most {MaxSearch} characters.");
        }

        var visible = await _namespaces.GetByOwnerAsync(OwnerId, AllowedNamespaceIds, cancellationToken);
        if (visible.IsFailure)
        {
            return Problem(visible.Error);
        }

        var candidates = visible.Value.AsEnumerable();
        if (namespaceId is { } id)
        {
            candidates = candidates.Where(n => n.Id == id);
            if (!candidates.Any())
            {
                // Someone else's namespace looks exactly like one that is not there.
                return Problem(StatusCodes.Status404NotFound, ErrorCodes.Namespace.NotFound, $"Namespace with ID '{id}' was not found.");
            }
        }

        if (provider is { } cloud)
        {
            candidates = candidates.Where(n => n.Provider == cloud);
        }

        if (environment is { } env)
        {
            candidates = candidates.Where(n => n.Environment == env);
        }

        var result = await _reader.ListAsync(
            new DlqListQuery(
                [.. candidates.Select(n => n.Id)], wantedStatus, since, reason, noReason, entity, q,
                page ?? 1, pageSize ?? DefaultPageSize),
            cancellationToken);

        return Ok(new DeadLetterListResponse(
            result.Items,
            new DeadLetterPaging(result.Total, result.Page, result.PageSize),
            result.Groups,
            result.OtherReasons,
            result.Entities));
    }

    /// <summary>
    /// New vs resolved dead letters per day for the last <paramref name="days"/> days — the Home chart. Computed
    /// from what ServiceHub stored; there is no hourly series and none is faked.
    /// </summary>
    /// <param name="days">1–30 (default 7).</param>
    /// <param name="namespaceId">One namespace.</param>
    /// <param name="provider">One cloud — every namespace the caller has on it.</param>
    /// <param name="environment">Only namespaces of this environment (dev, uat, prod).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("trend")]
    [ProducesResponseType(typeof(DeadLetterTrendResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Trend(
        [FromQuery] int? days, [FromQuery] Guid? namespaceId, [FromQuery] CloudProviderType? provider, [FromQuery] EnvironmentType? environment,
        CancellationToken cancellationToken)
    {
        if (namespaceId is null && provider is null)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed,
                "Say which dead letters: give a 'namespaceId', a 'provider', or both.");
        }

        if (days is < 1 or > 30)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'days' must be between 1 and 30.");
        }

        var visible = await _namespaces.GetByOwnerAsync(OwnerId, AllowedNamespaceIds, cancellationToken);
        if (visible.IsFailure)
        {
            return Problem(visible.Error);
        }

        var candidates = visible.Value.AsEnumerable();
        if (namespaceId is { } id)
        {
            candidates = candidates.Where(n => n.Id == id);
            if (!candidates.Any())
            {
                return Problem(StatusCodes.Status404NotFound, ErrorCodes.Namespace.NotFound, $"Namespace with ID '{id}' was not found.");
            }
        }

        if (provider is { } cloud)
        {
            candidates = candidates.Where(n => n.Provider == cloud);
        }

        if (environment is { } env)
        {
            candidates = candidates.Where(n => n.Environment == env);
        }

        var span = days ?? 7;
        var series = await _reader.GetTrendAsync([.. candidates.Select(n => n.Id)], span, DateTimeOffset.UtcNow, cancellationToken);
        return Ok(new DeadLetterTrendResponse(span, series));
    }

    /// <summary>One dead letter, opened — the row, its stored body preview and its properties.</summary>
    /// <param name="id">The row id from the list.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(DlqDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken)
    {
        var visible = await _namespaces.GetByOwnerAsync(OwnerId, AllowedNamespaceIds, cancellationToken);
        if (visible.IsFailure)
        {
            return Problem(visible.Error);
        }

        var detail = await _reader.GetAsync(id, [.. visible.Value.Select(n => n.Id)], cancellationToken);
        return detail is null
            ? Problem(StatusCodes.Status404NotFound, ErrorCodes.Message.NotFound, $"Dead letter '{id}' was not found.")
            : Ok(detail);
    }

    /// <summary>
    /// Asks the eligibility gate whether the caller may act on one dead letter — read-only, nothing is
    /// executed. The verdict comes with its reason <b>code</b>.
    /// </summary>
    /// <param name="id">The row id from the list.</param>
    /// <param name="action">replay (default) or purge.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("{id:long}/eligibility")]
    [ProducesResponseType(typeof(EligibilityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Eligibility(long id, [FromQuery] string? action, CancellationToken cancellationToken)
    {
        var kind = action?.Trim().ToLowerInvariant() switch
        {
            null or "" or "replay" => RecoveryOperationKind.Replay,
            "purge" => RecoveryOperationKind.Purge,
            _ => (RecoveryOperationKind?)null,
        };
        if (kind is null)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'action' must be replay or purge.");
        }

        var visible = await _namespaces.GetByOwnerAsync(OwnerId, AllowedNamespaceIds, cancellationToken);
        if (visible.IsFailure)
        {
            return Problem(visible.Error);
        }

        var detail = await _reader.GetAsync(id, [.. visible.Value.Select(n => n.Id)], cancellationToken);
        var ns = detail is null ? null : visible.Value.FirstOrDefault(n => n.Id == detail.Item.NamespaceId);
        if (detail is null || ns is null)
        {
            return Problem(StatusCodes.Status404NotFound, ErrorCodes.Message.NotFound, $"Dead letter '{id}' was not found.");
        }

        var decision = await _replay.CheckEligibilityAsync(id, ns, Actor, kind.Value, cancellationToken);
        if (decision is null)
        {
            return Problem(StatusCodes.Status404NotFound, ErrorCodes.Message.NotFound, $"Dead letter '{id}' was not found.");
        }

        return Ok(new EligibilityResponse(
            kind.Value.ToString().ToLowerInvariant(), decision.Verdict.ToString(), decision.ReasonCode,
            decision.MatchedCount, decision.Verdict == EligibilityVerdict.Escalate));
    }

    /// <summary>
    /// What replaying one dead letter would do — scope, risk, the checks and what happens after — shown
    /// before anything runs. Read-only.
    /// </summary>
    /// <param name="id">The row id from the list.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("{id:long}/replay-proposal")]
    [ProducesResponseType(typeof(ReplayProposal), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplayProposal(long id, CancellationToken cancellationToken)
    {
        var ns = await VisibleNamespaceOfAsync(id, cancellationToken);
        if (ns is null)
        {
            return Problem(StatusCodes.Status404NotFound, ErrorCodes.Message.NotFound, $"Dead letter '{id}' was not found.");
        }

        var proposal = await _replay.ProposeAsync(id, ns, Actor, cancellationToken);
        return proposal.IsFailure ? Problem(proposal.Error) : Ok(proposal.Value);
    }

    /// <summary>
    /// Replays one dead letter: through the eligibility gate, recorded in the ledger before and after the
    /// cloud is touched, and never retried. The single execution route for replaying one message. Needs
    /// <c>X-ServiceHub-Intent: replay-message</c>.
    /// </summary>
    /// <param name="id">The row id from the list.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpPost("{id:long}/replay")]
    [ProducesResponseType(typeof(ReplayOutcome), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Replay(long id, CancellationToken cancellationToken)
    {
        if (!IntentHeaders.Declares(Request, IntentHeaders.ReplayMessage))
        {
            return Problem(
                StatusCodes.Status400BadRequest, ErrorCodes.IntentRequired,
                IntentHeaders.MissingDetail("replay this message", IntentHeaders.ReplayMessage));
        }

        var ns = await VisibleNamespaceOfAsync(id, cancellationToken);
        if (ns is null)
        {
            return Problem(StatusCodes.Status404NotFound, ErrorCodes.Message.NotFound, $"Dead letter '{id}' was not found.");
        }

        if (await DeniedUnlessAsync(GovernanceRole.Operator, ns.Id, PillarKind.Recover, "replay this message", cancellationToken) is { } denied) return denied;
        var outcome = await _replay.ReplayAsync(id, ns, Actor, IntentHeaders.ReplayMessage, HttpContext.TraceIdentifier, cancellationToken);
        return outcome.IsFailure ? Problem(outcome.Error) : Ok(outcome.Value);
    }

    /// <summary>What a purge carries: why. It is kept with the evidence.</summary>
    /// <param name="Reason">Why this message is being deleted for good.</param>
    public sealed record PurgeRequest(string? Reason);

    /// <summary>
    /// Deletes one dead letter for good (unit 6.15), through the same gate and ledger as a replay — never an un-gated delete.
    /// Only where the cloud can delete one message; a reason is required. Needs <c>X-ServiceHub-Intent: purge-message</c>.
    /// </summary>
    /// <param name="id">The row id from the list.</param>
    /// <param name="request">Why.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpPost("{id:long}/purge")]
    [ProducesResponseType(typeof(ReplayOutcome), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Purge(long id, [FromBody] PurgeRequest? request, CancellationToken cancellationToken)
    {
        if (!IntentHeaders.Declares(Request, IntentHeaders.PurgeMessage))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.IntentRequired, IntentHeaders.MissingDetail("purge this message", IntentHeaders.PurgeMessage));
        }

        var ns = await VisibleNamespaceOfAsync(id, cancellationToken);
        if (ns is null)
        {
            return Problem(StatusCodes.Status404NotFound, ErrorCodes.Message.NotFound, $"Dead letter '{id}' was not found.");
        }

        if (await DeniedUnlessAsync(GovernanceRole.Operator, ns.Id, PillarKind.Recover, "purge this message", cancellationToken) is { } denied) return denied;
        var outcome = await _replay.PurgeAsync(id, ns, Actor, request?.Reason ?? "", IntentHeaders.PurgeMessage, HttpContext.TraceIdentifier, cancellationToken);
        return outcome.IsFailure ? Problem(outcome.Error) : Ok(outcome.Value);
    }

    private async Task<Namespace?> VisibleNamespaceOfAsync(long id, CancellationToken cancellationToken)
    {
        var visible = await _namespaces.GetByOwnerAsync(OwnerId, AllowedNamespaceIds, cancellationToken);
        if (visible.IsFailure)
        {
            return null;
        }

        var detail = await _reader.GetAsync(id, [.. visible.Value.Select(n => n.Id)], cancellationToken);
        return detail is null ? null : visible.Value.FirstOrDefault(n => n.Id == detail.Item.NamespaceId);
    }

    private static bool TryParseStatus(string? value, out DlqMessageStatus? status)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "active":
                status = DlqMessageStatus.Active;
                return true;
            case "resolved":
                status = DlqMessageStatus.Resolved;
                return true;
            case "all":
                status = null;
                return true;
            default:
                status = null;
                return false;
        }
    }

    private static bool TryParseRange(string? value, out DateTimeOffset? since)
    {
        var now = DateTimeOffset.UtcNow;
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "all":
                since = null;
                return true;
            case "24h":
                since = now.AddHours(-24);
                return true;
            case "7d":
                since = now.AddDays(-7);
                return true;
            case "30d":
                since = now.AddDays(-30);
                return true;
            default:
                since = null;
                return false;
        }
    }
}
