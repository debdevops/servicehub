using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// What the Recovery Evidence Ledger says: the summary, the list, one entry opened, and whether the chain
/// verifies (units 2.12–2.13). <b>Read-only.</b> Nothing here executes, writes off or edits anything — acting
/// belongs to the Simple flow, and the ledger is append-only.
/// </summary>
[Route("api/v1/recovery")]
public sealed class RecoveryController : ApiControllerBase
{
    private readonly IRecoveryQueries _queries;
    private readonly IRecoveryLedger _ledger;
    private readonly INamespaceRepository _namespaces;

    /// <summary>Creates the controller.</summary>
    public RecoveryController(IRecoveryQueries queries, IRecoveryLedger ledger, INamespaceRepository namespaces)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _namespaces = namespaces ?? throw new ArgumentNullException(nameof(namespaces));
    }

    /// <summary>How did recoveries end? One computation for every screen that asks.</summary>
    /// <param name="window">24h (default), 7d, 30d or all.</param>
    /// <param name="provider">Only this cloud.</param>
    /// <param name="namespaceId">Only this namespace.</param>
    /// <param name="environment">Only entries made in this environment (dev, uat, prod).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(RecoverySummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Summary(
        [FromQuery] string? window, [FromQuery] CloudProviderType? provider, [FromQuery] Guid? namespaceId, CancellationToken cancellationToken,
        [FromQuery] EnvironmentType? environment = null)
    {
        var w = string.IsNullOrWhiteSpace(window) ? "24h" : window.Trim();
        if (!RecoveryQueries.IsKnownWindow(w))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'window' must be 24h, 7d, 30d or all.");
        }

        if (await ScopeAsync(provider, namespaceId, environment, cancellationToken) is not { } scope)
        {
            return Problem(StatusCodes.Status404NotFound, ErrorCodes.Namespace.NotFound, $"Namespace with ID '{namespaceId}' was not found.");
        }

        return Ok(await _queries.SummariseAsync(scope, w, cancellationToken));
    }

    /// <summary>Ledger entries, newest first, one state at a time if asked.</summary>
    /// <param name="window">24h (default), 7d, 30d or all.</param>
    /// <param name="state">Only entries in this state (the enum's own name, e.g. <c>Unverified</c>).</param>
    /// <param name="provider">Only this cloud.</param>
    /// <param name="namespaceId">Only this namespace.</param>
    /// <param name="environment">Only entries made in this environment (dev, uat, prod).</param>
    /// <param name="page">1-based.</param>
    /// <param name="pageSize">1–100 (default 25).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("entries")]
    [ProducesResponseType(typeof(RecoveryEntryPage), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Entries(
        [FromQuery] string? window, [FromQuery] string? state, [FromQuery] CloudProviderType? provider, [FromQuery] Guid? namespaceId,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken,
        [FromQuery] EnvironmentType? environment = null)
    {
        var w = string.IsNullOrWhiteSpace(window) ? "24h" : window.Trim();
        if (!RecoveryQueries.IsKnownWindow(w))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'window' must be 24h, 7d, 30d or all.");
        }

        RecoveryEntryState? wanted = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            if (!Enum.TryParse<RecoveryEntryState>(state, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, $"'state' must be one of: {string.Join(", ", Enum.GetNames<RecoveryEntryState>())}.");
            }

            wanted = parsed;
        }

        if (page is < 1 || pageSize is < 1 or > 100)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'page' starts at 1 and 'pageSize' must be between 1 and 100.");
        }

        if (await ScopeAsync(provider, namespaceId, environment, cancellationToken) is not { } scope)
        {
            return Problem(StatusCodes.Status404NotFound, ErrorCodes.Namespace.NotFound, $"Namespace with ID '{namespaceId}' was not found.");
        }

        return Ok(await _queries.ListAsync(scope, w, wanted, page ?? 1, pageSize ?? 25, cancellationToken));
    }

    /// <summary>One entry, with its whole history and its place in the hash chain.</summary>
    /// <param name="id">The entry id.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("entries/{id:guid}")]
    [ProducesResponseType(typeof(RecoveryEntryDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Entry(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _queries.GetAsync(new RecoveryScope(OwnerId, AllowedNamespaceIds), id, cancellationToken);
        return detail is null
            ? Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, $"Ledger entry '{id}' was not found.")
            : Ok(detail);
    }

    /// <summary>
    /// Recomputes the owner's hash chain and says whether it holds. It reads and reports — it never repairs.
    /// The same check runs offline with <c>scripts/verify-recovery-chain.py</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("chain")]
    [ProducesResponseType(typeof(ChainVerificationResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Chain(CancellationToken cancellationToken) =>
        Ok(await _ledger.VerifyChainAsync(OwnerId, cancellationToken));

    /// <summary>
    /// The ledger as one file anyone can check offline with <c>scripts/verify-recovery-chain.py</c> (unit 6.12). A read, so
    /// Advanced may offer it (ADR-0016 D3).
    /// </summary>
    /// <remarks>
    /// <para>Events are written exactly as they were hashed — <c>eventType</c> and <c>actorKind</c> by their enum names, not the
    /// API's camelCase — because the verifier recomputes each hash from these fields.</para>
    /// <para>A time range never drops an event from inside it: the ledger is appended in order, so the events in a range are one
    /// unbroken run of the chain. A range that is not the whole chain says <c>partial: true</c> in the manifest, with where it
    /// starts and ends.</para>
    /// <para>The chain is one per owner, across every namespace, so a key limited to some namespaces cannot export it — it would
    /// hand over other namespaces' evidence.</para>
    /// </remarks>
    /// <param name="from">Only events at or after this moment.</param>
    /// <param name="to">Only events at or before this moment.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken)
    {
        if (from is { } f && to is { } t && f > t)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'from' must be before 'to'.");
        }

        if (AllowedNamespaceIds is not null)
        {
            return Problem(StatusCodes.Status403Forbidden, ErrorCodes.PermissionDenied,
                "The evidence ledger is one chain across every namespace, and this key is limited to some of them. Export it with a key that is not limited, or from the browser.");
        }

        var chain = await _queries.ChainAsync(OwnerId, cancellationToken);
        var events = chain.Where(e => (from is null || e.OccurredAt >= from) && (to is null || e.OccurredAt <= to)).ToList();
        var partial = events.Count != chain.Count;
        var now = DateTimeOffset.UtcNow;
        var name = $"servicehub-evidence-{(from ?? events.FirstOrDefault()?.OccurredAt ?? now):yyyyMMdd-HHmm}-to-{(to ?? now):yyyyMMdd-HHmm}.json";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{name}\"";

        return Ok(new
        {
            manifest = new
            {
                kind = "servicehub-recovery-evidence",
                exportedAt = now,
                from,
                to,
                partial,
                note = events.Count == 0
                    ? "Nothing was recorded in this range, so there is nothing to verify."
                    : partial
                        ? "Part of the chain: the first event links to one before it that is not in this file. Every event inside the range is here."
                        : "The whole chain, from its first event.",
                chain = new { firstSeq = events.FirstOrDefault()?.Seq, lastSeq = events.LastOrDefault()?.Seq, eventsInChain = chain.Count },
                verify = $"python3 verify-recovery-chain.py {name}",
            },
            events = events.Select(e => new
            {
                id = e.Id,
                ownerId = e.OwnerId,
                seq = e.Seq,
                entryId = e.EntryId,
                operationId = e.OperationId,
                eventType = e.EventType.ToString(),
                occurredAt = e.OccurredAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                actorIdentity = e.ActorIdentity,
                actorKind = e.ActorKind.ToString(),
                detailJson = e.DetailJson,
                schemaVersion = e.SchemaVersion,
                prevHash = e.PrevHash,
                entryHash = e.EntryHash,
            }),
        });
    }

    private async Task<RecoveryScope?> ScopeAsync(CloudProviderType? provider, Guid? namespaceId, EnvironmentType? environment, CancellationToken cancellationToken)
    {
        if (namespaceId is { } id)
        {
            var visible = await _namespaces.GetByOwnerAsync(OwnerId, AllowedNamespaceIds, cancellationToken);
            if (visible.IsFailure || visible.Value.All(n => n.Id != id))
            {
                // Someone else's namespace looks exactly like one that is not there.
                return null;
            }
        }

        return new RecoveryScope(OwnerId, AllowedNamespaceIds, namespaceId, provider, environment);
    }
}
