using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// The Incident and Trace tabs on Failure Signatures (unit 6.19). <b>Read-only, and from recorded tables only.</b>
/// </summary>
/// <remarks>
/// 4.0.0's incident read-model and cross-cloud trace are not copied: they read tables 4.1.0 does not have, and the trace
/// peeked live into every cloud — on a cloud without a repeatable peek every look is a delivery attempt, so a search could
/// dead-letter the very message it was looking for. Here a trace is what ServiceHub already recorded, and says so.
/// </remarks>
public sealed class IncidentTraceController : ApiControllerBase
{
    private const int MaxItems = 200;

    private readonly ServiceHubDbContext _db;
    private readonly INamespaceRepository _namespaces;

    /// <summary>Creates the controller.</summary>
    public IncidentTraceController(ServiceHubDbContext db, INamespaceRepository namespaces)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _namespaces = namespaces ?? throw new ArgumentNullException(nameof(namespaces));
    }

    /// <summary>One signature's story: first seen, each day it came back, every replay or purge and how it ended — newest first.</summary>
    /// <param name="hash">The signature.</param>
    /// <param name="provider">Which cloud's signature (the same failure can exist in two).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("api/v1/signatures/{hash}/incident")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Incident(string hash, [FromQuery] CloudProviderType? provider, CancellationToken cancellationToken)
    {
        if (provider is not { } cloud)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "Say which cloud: give a 'provider'.");
        }

        var visible = await VisibleAsync(cancellationToken);
        var ids = visible.Where(n => n.Value.Provider == cloud).Select(n => n.Key).ToList();
        var messages = await _db.DlqMessages.AsNoTracking()
            .Where(m => m.OwnerId == OwnerId && m.SignatureHash == hash && ids.Contains(m.NamespaceId))
            .Select(m => new { m.Id, m.NamespaceId, m.EntityName, m.DetectedAtUtc, m.Status, m.DeadLetterReason })
            .ToListAsync(cancellationToken);
        if (messages.Count == 0)
        {
            return Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, $"No signature '{hash}' was found in that cloud.");
        }

        var entries = await _db.RecoveryLedgerEntries.AsNoTracking()
            .Where(e => e.OwnerId == OwnerId && e.SignatureHashSnapshot == hash && e.NamespaceId != null && ids.Contains(e.NamespaceId.Value))
            .ToListAsync(cancellationToken);
        var kinds = await _db.RecoveryOperations.AsNoTracking()
            .Where(o => entries.Select(e => e.OperationId).Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Kind, cancellationToken);

        string Where(Guid ns) => visible.TryGetValue(ns, out var n) ? n.DisplayName ?? n.Name : "a namespace";
        var first = messages.MinBy(m => m.DetectedAtUtc)!;
        var items = new List<TimelineItem> { new(first.DetectedAtUtc, "first_seen", $"First seen dead-lettered on {first.EntityName} in {Where(first.NamespaceId)}.", null, first.Id) };
        items.AddRange(messages.Where(m => m.Id != first.Id)
            .GroupBy(m => (Day: m.DetectedAtUtc.UtcDateTime.Date, m.NamespaceId))
            .Select(g => new TimelineItem(g.Max(m => m.DetectedAtUtc), "came_back", $"{g.Count()} more dead-lettered in {Where(g.Key.NamespaceId)}.", null, null)));
        foreach (var e in entries)
        {
            var purge = kinds.GetValueOrDefault(e.OperationId) == RecoveryOperationKind.Purge;
            items.Add(new TimelineItem(e.BegunAt, purge ? "purged" : "replayed", $"{(purge ? "Purged" : "Replayed")} one from {e.EntityNameSnapshot} — {Outcome(e.State)}.", e.Id, e.DlqMessageId));
        }

        return Ok(new
        {
            signatureHash = hash,
            provider = cloud.ToString().ToLowerInvariant(),
            reason = first.DeadLetterReason,
            firstSeenAt = first.DetectedAtUtc,
            lastSeenAt = messages.Max(m => m.DetectedAtUtc),
            messages = messages.Count,
            stillStuck = messages.Count(m => m.Status == DlqMessageStatus.Active),
            replays = entries.Count(e => kinds.GetValueOrDefault(e.OperationId) == RecoveryOperationKind.Replay),
            stayedFixed = entries.Count(e => e.State == RecoveryEntryState.Recovered),
            cameBackAfterReplay = entries.Count(e => e.State == RecoveryEntryState.Returned),
            timeline = items.OrderByDescending(i => i.At).Take(MaxItems),
        });
    }

    /// <summary>
    /// Where a message went: every dead letter ServiceHub recorded with this correlation id, across every connected cloud, and each
    /// replay or purge of one — oldest first. Only what was recorded; a message never seen dead-lettered is not here.
    /// </summary>
    /// <param name="correlationId">The correlation id.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("api/v1/trace")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Trace([FromQuery] string? correlationId, CancellationToken cancellationToken)
    {
        var id = correlationId?.Trim();
        if (string.IsNullOrEmpty(id) || id.Length > 256)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "Give a 'correlationId' of up to 256 characters.");
        }

        var visible = await VisibleAsync(cancellationToken);
        var ids = visible.Keys.ToList();
        var sightings = await _db.DlqMessages.AsNoTracking()
            .Where(m => m.OwnerId == OwnerId && m.CorrelationId == id && ids.Contains(m.NamespaceId))
            .Take(MaxItems).ToListAsync(cancellationToken);
        var messageIds = sightings.Select(m => (long?)m.Id).ToList();
        var entries = await _db.RecoveryLedgerEntries.AsNoTracking()
            .Where(e => e.OwnerId == OwnerId && messageIds.Contains(e.DlqMessageId)).ToListAsync(cancellationToken);
        var kinds = await _db.RecoveryOperations.AsNoTracking()
            .Where(o => entries.Select(e => e.OperationId).Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Kind, cancellationToken);

        object Place(Guid ns) => visible.TryGetValue(ns, out var n)
            ? new { provider = n.Provider.ToString().ToLowerInvariant(), namespaceName = n.DisplayName ?? n.Name, environment = n.Environment.ToString().ToLowerInvariant() }
            : new { provider = "unknown", namespaceName = "a namespace", environment = "unknown" };

        var hops = sightings.Select(m => new
        {
            at = m.DetectedAtUtc, kind = "dead_lettered", place = Place(m.NamespaceId), entity = m.EntityName, dlqMessageId = (long?)m.Id,
            m.NamespaceId, detail = m.DeadLetterReason, entryId = (Guid?)null,
        }).Concat(entries.Select(e => new
        {
            at = e.BegunAt, kind = kinds.GetValueOrDefault(e.OperationId) == RecoveryOperationKind.Purge ? "purged" : "replayed",
            place = Place(e.NamespaceId ?? Guid.Empty), entity = e.EntityNameSnapshot ?? e.TargetEntity, dlqMessageId = e.DlqMessageId,
            NamespaceId = e.NamespaceId ?? Guid.Empty, detail = (string?)Outcome(e.State), entryId = (Guid?)e.Id,
        })).OrderBy(h => h.at).ToList();

        return Ok(new
        {
            correlationId = id,
            clouds = sightings.Select(m => visible.TryGetValue(m.NamespaceId, out var n) ? n.Provider.ToString().ToLowerInvariant() : "unknown").Distinct(),
            hops,
            note = "Only what ServiceHub recorded: dead letters it saw, and what was done to them. A message that was never dead-lettered is not here.",
        });
    }

    /// <summary>One line of a signature's story.</summary>
    public sealed record TimelineItem(DateTimeOffset At, string Kind, string Text, Guid? EntryId, long? DlqMessageId);

    private async Task<Dictionary<Guid, Namespace>> VisibleAsync(CancellationToken cancellationToken)
    {
        var visible = await _namespaces.GetByOwnerAsync(OwnerId, AllowedNamespaceIds, cancellationToken);
        return visible.IsSuccess ? visible.Value.ToDictionary(n => n.Id) : [];
    }

    private static string Outcome(RecoveryEntryState state) => state switch
    {
        RecoveryEntryState.Recovered => "stayed fixed",
        RecoveryEntryState.Returned => "came back",
        RecoveryEntryState.Unverified => "sent; the cloud can’t prove it stayed fixed",
        RecoveryEntryState.Observing => "being watched",
        RecoveryEntryState.ExecutionFailed => "the cloud refused it",
        RecoveryEntryState.ExecutionUnknown => "outcome not known",
        RecoveryEntryState.Discarded => "deleted for good",
        _ => state.ToString(),
    };
}
