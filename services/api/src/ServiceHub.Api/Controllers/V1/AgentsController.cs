using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Security;
using ServiceHub.Core.Constants;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Security;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// The agents that run in this build, what each may do, how each is doing, and the one control over them: pause
/// (unit 4.4). It reads <see cref="IAgentRegistry"/> and nothing else, so it cannot report an agent that is not running.
/// </summary>
/// <remarks>
/// <b>Generic by construction.</b> No agent is named here; a new agent arrives through its descriptor alone. Its timeline
/// is the cycles this process saw, the ledger events it recorded and the pauses people set — no table of its own.
/// </remarks>
[Route("api/v1/agents")]
public sealed class AgentsController : ApiControllerBase
{
    private const int TimelineLimit = 40;

    private readonly IAgentRegistry _registry;
    private readonly IAuditTrail _audit;
    private readonly IRecoveryQueries _queries;
    private readonly TimeProvider _time;

    /// <summary>Creates the controller.</summary>
    public AgentsController(IAgentRegistry registry, IAuditTrail audit, IRecoveryQueries queries, TimeProvider? time = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Every registered agent, in registration order.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AgentResponse>), StatusCodes.Status200OK)]
    public IActionResult List() => Ok(_registry.All().Select(ToResponse).ToList());

    /// <summary>One agent.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(AgentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult Get(string id) =>
        _registry.StateOf(id) is { } state ? Ok(ToResponse(state)) : NotFound(id);

    /// <summary>One agent's timeline, newest first: its recent cycles, the ledger events it recorded, and pauses.</summary>
    [HttpGet("{id}/activity")]
    [ProducesResponseType(typeof(AgentActivityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activity(string id, CancellationToken cancellationToken)
    {
        if (_registry.StateOf(id) is not { } state)
        {
            return NotFound(id);
        }

        var items = new List<AgentActivityItem>();
        var cycles = _registry.RecentCycles(id);
        items.AddRange(cycles.Select(c => c.Result is { } r
            ? new AgentActivityItem(c.RunUtc, "cycle", CycleKind(r, state.Descriptor.CanAct), $"examined {r.Examined} · {r.Summary}", null)
            : new AgentActivityItem(c.RunUtc, "cycle", "failed", c.Failure ?? "the cycle failed", null)));

        if (state.Descriptor.LedgerActor is { } actor)
        {
            var events = await _queries.EventsByActorAsync(OwnerId, actor, TimelineLimit, cancellationToken);
            items.AddRange(events.Select(e => new AgentActivityItem(e.OccurredAt, "ledger", e.EventType.ToString(), LedgerWords(e.EventType), null)));
        }

        foreach (var action in new[] { AuditActions.AgentPause, AuditActions.AgentResume })
        {
            var page = await _audit.QueryAsync(new AuditQuery(OwnerId, AllowedNamespaceIds, Action: action, PageSize: TimelineLimit), cancellationToken);
            items.AddRange(page.Items.Where(a => a.ResourceName == id && a.Outcome == AuditActions.Success).Select(a => new AgentActivityItem(
                a.Timestamp, "audit", action == AuditActions.AgentPause ? "paused" : "resumed",
                action == AuditActions.AgentPause ? "Paused — it will not run until resumed" : "Resumed", RecoveryActorLabel.For(a.UserIdentity ?? "unknown"))));
        }

        return Ok(new AgentActivityResponse(
            id, cycles.Count > 0 ? cycles[^1].RunUtc : null,
            [.. items.OrderByDescending(i => i.At).Take(TimelineLimit)]));
    }

    /// <summary>
    /// Pauses an agent: its loop keeps running and its cycles are skipped, so an acting agent <b>will not act</b>. Removes
    /// authority only (R8). Recorded on the audit trail, which is also how a pause survives a restart. Intent <c>pause-agent</c>.
    /// </summary>
    [HttpPost("{id}/pause")]
    [ProducesResponseType(typeof(AgentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public Task<IActionResult> Pause(string id, CancellationToken cancellationToken) =>
        SetPausedAsync(id, true, IntentHeaders.PauseAgent, AuditActions.AgentPause, "pause this agent", cancellationToken);

    /// <summary>Resumes a paused agent. It gives authority back, so it needs intent <c>resume-agent</c> too, and is audited.</summary>
    [HttpPost("{id}/resume")]
    [ProducesResponseType(typeof(AgentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public Task<IActionResult> Resume(string id, CancellationToken cancellationToken) =>
        SetPausedAsync(id, false, IntentHeaders.ResumeAgent, AuditActions.AgentResume, "resume this agent", cancellationToken);

    private async Task<IActionResult> SetPausedAsync(string id, bool paused, string intent, string action, string words, CancellationToken cancellationToken)
    {
        if (!IntentHeaders.Declares(Request, intent))
        {
            return Problem(StatusCodes.Status428PreconditionRequired, ErrorCodes.IntentRequired, IntentHeaders.MissingDetail(words, intent));
        }

        // Pausing only takes authority away (Operator); resuming gives it back, so it is an Admin's call.
        if (await DeniedUnlessAsync(paused ? GovernanceRole.Operator : GovernanceRole.Admin, null, null, words, cancellationToken) is { } denied) return denied;

        if (!_registry.SetPaused(id, paused))
        {
            return NotFound(id);
        }

        await _audit.RecordAsync(new AuditLog
        {
            Id = Guid.NewGuid(), Timestamp = _time.GetUtcNow(), OwnerId = OwnerId, UserIdentity = Actor.Identity,
            Action = action, Outcome = AuditActions.Success, ResourceName = id,
            CorrelationId = HttpContext.TraceIdentifier, HttpMethod = Request.Method, HttpPath = Request.Path.Value,
        }, cancellationToken);

        return Ok(ToResponse(_registry.StateOf(id)!));
    }

    private ObjectResult NotFound(string id) =>
        Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, $"No agent '{LogRedactor.SanitiseForLog(id)}' runs in this build.");

    private AgentResponse ToResponse(AgentRuntimeState s)
    {
        var d = s.Descriptor;
        // Late = no cycle for three cadences (and at least a minute): a loop that stopped reporting is not "healthy".
        var late = s.IsLate(_time.GetUtcNow());
        return new AgentResponse(
            d.Id, d.Name, d.Purpose, Camel(d.Kind.ToString()), Camel(d.Authority.ToString()), d.CanAct, d.Cadence.TotalSeconds, d.Notes,
            d.May ?? [], d.MayNot ?? [], Camel((late && s.Health == AgentHealth.Healthy ? AgentHealth.Failing : s.Health).ToString()), late, s.IsPaused,
            s.LastRunUtc, s.LastResult is { } r ? new AgentCycleResponse(r.Examined, r.Changed, r.Summary, r.Degraded) : null,
            s.LastFailure, s.ConsecutiveFailures);
    }

    private static string CycleKind(AgentCycleResult r, bool canAct) =>
        r.Degraded ? "degraded" : r.Changed > 0 && canAct ? "acted" : r.Examined == 0 ? "idle" : "looked";

    private static string LedgerWords(RecoveryEventType type) => type switch
    {
        RecoveryEventType.OperationOpened => "Opened a recovery",
        RecoveryEventType.ProviderAccepted => "The cloud accepted a replay",
        RecoveryEventType.ProviderRejected => "The cloud refused a replay",
        RecoveryEventType.ExecutionUnknown => "Lost contact during a replay — outcome unknown",
        RecoveryEventType.RecurrenceObserved => "A replayed message came back",
        RecoveryEventType.NoRecurrenceObserved => "A replay stayed fixed",
        RecoveryEventType.ObservationUnavailable => "A replay's outcome could not be proven",
        RecoveryEventType.EligibilityDeclined => "The safety checks said no — it asked a person",
        RecoveryEventType.AutonomyGrantPromoted => "A failure earned replay without asking",
        RecoveryEventType.AutonomyGrantDemoted => "A failure lost replay without asking",
        RecoveryEventType.AutoReplayRuleCircuitBreakerTripped => "A rule stopped itself — too few replays stayed fixed",
        _ => type.ToString(),
    };

    private static string Camel(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
