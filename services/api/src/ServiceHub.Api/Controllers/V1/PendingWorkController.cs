using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Security;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// What is waiting for a person, and the two answers (units 5.1, 5.10). The bell, Home's Needs-you strip and the Ledger's
/// Waiting tab all read <c>GET /pending-work</c> — one query, one truth. There is no read/unread state: an item leaves only
/// when the work is resolved.
/// </summary>
/// <remarks>
/// <b>Approve adds no execution route.</b> It calls the same <see cref="IDlqReplayService.ReplayAsync"/> the single-replay
/// endpoint calls, as the person — through the eligibility gate, one ledger entry, the approver recorded (R6) — and then
/// notes the decision on the waiting entry.
/// </remarks>
[Route("api/v1/pending-work")]
public sealed class PendingWorkController : ApiControllerBase
{
    private const int MaxReason = 500;

    private readonly IPendingWorkService _pending;
    private readonly IRecoveryLedger _ledger;
    private readonly IDlqReplayService _replay;
    private readonly INamespaceRepository _namespaces;
    private readonly IPlatformEventBus? _bus;

    /// <summary>Creates the controller.</summary>
    public PendingWorkController(IPendingWorkService pending, IRecoveryLedger ledger, IDlqReplayService replay, INamespaceRepository namespaces, IPlatformEventBus? bus = null)
    {
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _replay = replay ?? throw new ArgumentNullException(nameof(replay));
        _namespaces = namespaces ?? throw new ArgumentNullException(nameof(namespaces));
        _bus = bus;
    }

    /// <summary>The pending work the caller may see, most urgent first. Narrow with a cloud, namespace, environment or reason code.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PendingWorkPage), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] CloudProviderType? provider, [FromQuery] Guid? namespaceId, [FromQuery] EnvironmentType? environment,
        [FromQuery] string? reason, [FromQuery] int? limit, CancellationToken cancellationToken) =>
        Ok(await _pending.ListAsync(new PendingWorkScope(OwnerId, AllowedNamespaceIds, provider, namespaceId, environment, reason), limit ?? 100, cancellationToken));

    /// <summary>Just the numbers — the bell's badge and "2 waiting, both on AWS".</summary>
    [HttpGet("count")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Count(
        [FromQuery] CloudProviderType? provider, [FromQuery] Guid? namespaceId, [FromQuery] EnvironmentType? environment, CancellationToken cancellationToken)
    {
        var page = await _pending.ListAsync(new PendingWorkScope(OwnerId, AllowedNamespaceIds, provider, namespaceId, environment), 1, cancellationToken);
        return Ok(new { total = page.Total, byProvider = page.ByProvider, agents = page.Agents });
    }

    /// <summary>
    /// Yes: replays the dead letter the agent asked about, as the person, through the one gated route — then records the
    /// decision. A gate refusal is returned as it is and nothing is recorded as approved. Intent <c>approve-escalation</c>.
    /// </summary>
    [HttpPost("{entryId:guid}/approve")]
    [ProducesResponseType(typeof(ReplayOutcome), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Approve(Guid entryId, CancellationToken cancellationToken)
    {
        if (!IntentHeaders.Declares(Request, IntentHeaders.ApproveEscalation))
        {
            return Problem(StatusCodes.Status428PreconditionRequired, ErrorCodes.IntentRequired, IntentHeaders.MissingDetail("approve this", IntentHeaders.ApproveEscalation));
        }

        var item = await WaitingAsync(entryId, cancellationToken);
        if (item?.DlqMessageId is not { } dlqMessageId || item.NamespaceId is not { } nsId)
        {
            return Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "Nothing is waiting under that id — it may already be answered.");
        }

        var ns = await GetVisibleNamespaceAsync(_namespaces, nsId, cancellationToken);
        if (ns.IsFailure)
        {
            return Problem(ns.Error);
        }

        if (await DeniedUnlessAsync(GovernanceRole.Approver, nsId, PillarKind.Recover, "approve what the Agent asked", cancellationToken) is { } denied) return denied;
        var outcome = await _replay.ReplayAsync(dlqMessageId, ns.Value, Actor, IntentHeaders.ReplayMessage, HttpContext.TraceIdentifier, cancellationToken);
        if (outcome.IsFailure)
        {
            return Problem(outcome.Error);
        }

        await _ledger.RecordDecisionAsync(entryId, OwnerId, Actor, approved: true, reason: null, outcome.Value.EntryId, cancellationToken);
        await AnnounceAsync(ns.Value, cancellationToken);
        return Ok(outcome.Value);
    }

    /// <summary>
    /// No: records the person's reason on the waiting entry. Nothing is deleted and nothing reaches a cloud; the agent does not
    /// ask about the same dead letter again. Intent <c>decline-escalation</c>.
    /// </summary>
    [HttpPost("{entryId:guid}/decline")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Decline(Guid entryId, [FromBody] DeclineRequest? body, CancellationToken cancellationToken)
    {
        if (!IntentHeaders.Declares(Request, IntentHeaders.DeclineEscalation))
        {
            return Problem(StatusCodes.Status428PreconditionRequired, ErrorCodes.IntentRequired, IntentHeaders.MissingDetail("decline this", IntentHeaders.DeclineEscalation));
        }

        var reason = body?.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxReason)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, $"Say why, in up to {MaxReason} characters — it is recorded with your name.");
        }

        var item = await WaitingAsync(entryId, cancellationToken);
        if (item is null)
        {
            return Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "Nothing is waiting under that id — it may already be answered.");
        }

        if (await DeniedUnlessAsync(GovernanceRole.Approver, item.NamespaceId, PillarKind.Recover, "decline what the Agent asked", cancellationToken) is { } denied) return denied;
        var recorded = await _ledger.RecordDecisionAsync(entryId, OwnerId, Actor, approved: false, reason, null, cancellationToken);
        if (recorded.IsFailure)
        {
            return Problem(recorded.Error);
        }

        if (item.NamespaceId is { } nsId && (await GetVisibleNamespaceAsync(_namespaces, nsId, cancellationToken)) is { IsSuccess: true } ns)
        {
            await AnnounceAsync(ns.Value, cancellationToken);
        }

        return NoContent();
    }

    // Only an item the caller can currently see is answerable — the same scoping as the list, allow-list included.
    private async Task<PendingWorkItem?> WaitingAsync(Guid entryId, CancellationToken cancellationToken) =>
        (await _pending.ListAsync(new PendingWorkScope(OwnerId, AllowedNamespaceIds), 500, cancellationToken)).Items
            .FirstOrDefault(i => i.EntryId == entryId);

    // Every open screen should look again: the bell and the strip drop the answered item.
    private async Task AnnounceAsync(Namespace ns, CancellationToken cancellationToken)
    {
        if (_bus is not null)
        {
            await _bus.PublishAsync(new PlatformEvent
            {
                Source = "pending-work", Category = EventCategories.Escalation, EventType = EventTypes.ReplayCompleted,
                Actor = OwnerId, NamespaceId = ns.Id, CloudProvider = ns.Provider.ToString().ToLowerInvariant(),
            }, cancellationToken);
        }
    }

    /// <summary>The body of a decline.</summary>
    /// <param name="Reason">Why — required, recorded with the person's name.</param>
    public sealed record DeclineRequest(string? Reason);
}
