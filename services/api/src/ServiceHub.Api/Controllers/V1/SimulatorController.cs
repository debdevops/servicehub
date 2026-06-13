using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Controllers;
using ServiceHub.Core.Enums;
using ServiceHub.Simulator;
using ServiceHub.Simulator.Store;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Management endpoints for the in-memory simulator.
/// Only registered in the middleware pipeline when
/// <c>ASPNETCORE_ENVIRONMENT=Simulator</c>.
/// </summary>
[Route("api/v1/simulator")]
[Tags("Simulator")]
public sealed class SimulatorController : ApiControllerBase
{
    private readonly ISimulatorStore _store;
    private readonly SimulatorClock _clock;
    private readonly SimulatorDataSeeder _seeder;

    /// <summary>Initializes a new instance of <see cref="SimulatorController"/>.</summary>
    public SimulatorController(ISimulatorStore store, SimulatorClock clock, SimulatorDataSeeder seeder)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _seeder = seeder ?? throw new ArgumentNullException(nameof(seeder));
    }

    // ── GET /api/v1/simulator/status ─────────────────────────────────────────

    /// <summary>
    /// Returns the current simulator status including namespace counts,
    /// message counts, active faults, and simulated clock time.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("status")]
    [ProducesResponseType(typeof(SimulatorStatusResponse), StatusCodes.Status200OK)]
    public IActionResult GetStatus(CancellationToken ct)
    {
        var namespaces = _store.GetAllNamespaces();
        var faults = _store.GetActiveFaults();

        var namespaceSummaries = namespaces.Select(ns =>
        {
            var entities = _store.GetEntities(ns.Id);
            return new NamespaceSummary(
                ns.Id,
                ns.Name,
                ns.Provider.ToString(),
                entities.Sum(e => e.GetMessageCount()),
                entities.Sum(e => e.GetDlqCount()),
                entities.Count);
        }).ToList();

        return Ok(new SimulatorStatusResponse(
            Environment: "Simulator",
            SimulatedUtcNow: _clock.UtcNow,
            Namespaces: namespaceSummaries,
            ActiveFaultCount: faults.Count,
            ActiveFaults: faults.Select(f => new FaultSummary(
                f.FaultType, f.TargetEntity, f.NamespaceId, f.Severity, f.ExpiresAt)).ToList()));
    }

    // ── POST /api/v1/simulator/faults ────────────────────────────────────────

    /// <summary>
    /// Injects a fault into the simulator.
    /// </summary>
    /// <param name="request">Fault injection parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("faults")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult InjectFault([FromBody] InjectFaultRequest request, CancellationToken ct)
    {
        if (!IsValidFaultType(request.FaultType))
            return BadRequest($"Invalid fault type '{request.FaultType}'. " +
                "Valid types: MaxDelivery, VisibilityExpiry, AckDeadlineStorm, KmsError, OrderingStall, NetworkTimeout");

        var fault = new SimulatorFault(
            FaultType: request.FaultType,
            TargetEntity: request.TargetEntity ?? string.Empty,
            NamespaceId: request.NamespaceId,
            Severity: request.Severity,
            ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(request.DurationSeconds > 0 ? request.DurationSeconds : 60));

        _store.InjectFault(fault);
        return NoContent();
    }

    // ── DELETE /api/v1/simulator/faults ─────────────────────────────────────

    /// <summary>
    /// Clears all active faults from the simulator.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpDelete("faults")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult ClearFaults(CancellationToken ct)
    {
        _store.ClearFaults();
        return NoContent();
    }

    // ── POST /api/v1/simulator/reset ────────────────────────────────────────

    /// <summary>
    /// Resets all message state and re-seeds the store with the default dataset.
    /// Entity registrations and namespaces are preserved.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Reset(CancellationToken ct)
    {
        _seeder.Seed();
        _clock.Reset();
        _store.ClearFaults();
        return NoContent();
    }

    // ── POST /api/v1/simulator/advance-time ─────────────────────────────────

    /// <summary>
    /// Advances the simulated clock by the specified number of seconds.
    /// Useful for triggering visibility-window and ack-deadline expiry in tests.
    /// </summary>
    /// <param name="request">How many seconds to advance.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("advance-time")]
    [ProducesResponseType(typeof(AdvanceTimeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult AdvanceTime([FromBody] AdvanceTimeRequest request, CancellationToken ct)
    {
        if (request.Seconds <= 0)
            return BadRequest("Seconds must be greater than zero.");

        _clock.Advance(TimeSpan.FromSeconds(request.Seconds));
        return Ok(new AdvanceTimeResponse(_clock.UtcNow));
    }

    // ── POST /api/v1/simulator/inject-dlq-flood ─────────────────────────────

    /// <summary>
    /// Injects a flood of dead-letter messages into the specified entity,
    /// all using the same reason so AI classification can be validated.
    /// </summary>
    /// <param name="request">Flood parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("inject-dlq-flood")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult InjectDlqFlood([FromBody] DlqFloodRequest request, CancellationToken ct)
    {
        if (request.Count <= 0 || request.Count > 500)
            return BadRequest("Count must be between 1 and 500.");

        var entity = _store.GetEntity(request.NamespaceId, request.EntityName);
        if (entity is null)
            return NotFound($"Entity '{request.EntityName}' not found in namespace {request.NamespaceId}.");

        for (var i = 0; i < request.Count; i++)
        {
            var seq = entity.NextSequenceNumber();
            entity.EnqueueDlqMessage(new SimulatorMessage(
                MessageId: $"flood-{seq:D8}",
                SequenceNumber: seq,
                Body: $$$"""{"flood":true,"index":{{{i}}}}""",
                ContentType: "application/json",
                CorrelationId: Guid.NewGuid().ToString(),
                SessionId: null,
                PartitionKey: null,
                Subject: null,
                DeliveryCount: request.DeliveryCount > 0 ? request.DeliveryCount : 10,
                EnqueuedTime: DateTimeOffset.UtcNow.AddMinutes(-i),
                ScheduledEnqueueTime: null,
                IsDeadLettered: true,
                DeadLetterReason: request.Reason,
                DeadLetterErrorDescription: request.ErrorDescription,
                ApplicationProperties: new Dictionary<string, object>(),
                SizeInBytes: 128,
                ReceiptHandle: null,
                VisibilityUntil: null,
                OrderingKey: null,
                DeliveryAttempt: request.DeliveryCount > 0 ? request.DeliveryCount : 10,
                AckDeadline: null,
                IsNacked: false,
                Provider: entity.Provider));
        }

        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly HashSet<string> ValidFaultTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MaxDelivery", "VisibilityExpiry", "AckDeadlineStorm", "KmsError", "OrderingStall", "NetworkTimeout",
    };

    private static bool IsValidFaultType(string? type) =>
        type is not null && ValidFaultTypes.Contains(type);

    // ── Request / Response DTOs (local to this controller) ───────────────────

    /// <summary>Overall simulator status response.</summary>
    public sealed record SimulatorStatusResponse(
        string Environment,
        DateTimeOffset SimulatedUtcNow,
        IReadOnlyList<NamespaceSummary> Namespaces,
        int ActiveFaultCount,
        IReadOnlyList<FaultSummary> ActiveFaults);

    /// <summary>Per-namespace message count summary.</summary>
    public sealed record NamespaceSummary(
        Guid Id,
        string Name,
        string Provider,
        long ActiveMessageCount,
        long DlqMessageCount,
        int EntityCount);

    /// <summary>Summary of a single active fault.</summary>
    public sealed record FaultSummary(
        string FaultType,
        string TargetEntity,
        Guid NamespaceId,
        int Severity,
        DateTimeOffset ExpiresAt);

    /// <summary>Request to inject a fault.</summary>
    public sealed record InjectFaultRequest(
        string FaultType,
        Guid NamespaceId,
        string? TargetEntity = null,
        int Severity = 1,
        int DurationSeconds = 60);

    /// <summary>Request to advance the simulated clock.</summary>
    public sealed record AdvanceTimeRequest(int Seconds);

    /// <summary>Response after advancing the clock.</summary>
    public sealed record AdvanceTimeResponse(DateTimeOffset NewUtcNow);

    /// <summary>Request to inject a DLQ flood.</summary>
    public sealed record DlqFloodRequest(
        Guid NamespaceId,
        string EntityName,
        int Count = 10,
        string Reason = "MaxDeliveryCountExceeded",
        string? ErrorDescription = null,
        int DeliveryCount = 10);
}
