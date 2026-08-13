using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Filters;
using ServiceHub.Api.Security;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Surface over the Recovery Evidence Ledger: read queries, evidence export, chain verification,
/// and write-off — the one <c>recovery:write</c>-gated action, for a non-terminal entry an
/// operator declares unrecoverable. Every write remains inside <see cref="IRecoveryLedger"/>;
/// this controller only translates HTTP concerns (scopes, intent headers, actor resolution) and
/// never mutates ledger state itself.
/// </summary>
[Route(ApiRoutes.Recovery.Base)]
[Tags("Recovery")]
[RequireNamespaceOwnership]
public sealed class RecoveryController : ApiControllerBase
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;
    private const int MaxDetailEntries = 5000;

    private readonly IRecoveryLedger _recoveryLedger;
    private readonly IRecoveryEvidenceExporter _evidenceExporter;

    /// <summary>Initializes a new instance of the <see cref="RecoveryController"/> class.</summary>
    public RecoveryController(IRecoveryLedger recoveryLedger, IRecoveryEvidenceExporter evidenceExporter)
    {
        _recoveryLedger = recoveryLedger ?? throw new ArgumentNullException(nameof(recoveryLedger));
        _evidenceExporter = evidenceExporter ?? throw new ArgumentNullException(nameof(evidenceExporter));
    }

    /// <summary>
    /// Lists recovery operations for the caller, most recently opened first.
    /// </summary>
    /// <param name="namespaceId">Optional namespace filter.</param>
    /// <param name="limit">Maximum number of operations to return (1-500, default 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.RecoveryRead)]
    [HttpGet("operations")]
    [ProducesResponseType(typeof(IReadOnlyList<RecoveryOperationResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RecoveryOperationResponse>>> GetOperations(
        [FromQuery] Guid? namespaceId = null,
        [FromQuery] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var operations = await _recoveryLedger.QueryOperationsAsync(
            OwnerId, namespaceId, ClampLimit(limit), cancellationToken);

        return Ok(operations.Select(MapToResponse).ToList());
    }

    /// <summary>
    /// Gets one recovery operation by ID, scoped to the caller — the header plus every entry
    /// begun under it and its full event chain, for the operation detail page.
    /// </summary>
    /// <param name="id">The operation ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.RecoveryRead)]
    [HttpGet("operations/{id:guid}")]
    [ProducesResponseType(typeof(RecoveryOperationDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecoveryOperationDetailResponse>> GetOperationById(
        Guid id, CancellationToken cancellationToken = default)
    {
        var operation = await _recoveryLedger.GetOperationAsync(id, OwnerId, cancellationToken);
        if (operation is null)
        {
            return NotFound();
        }

        var entries = await _recoveryLedger.QueryEntriesAsync(new RecoveryEntryQuery
        {
            OwnerId = OwnerId,
            OperationId = id,
            Limit = MaxDetailEntries,
        }, cancellationToken);

        var events = await _recoveryLedger.GetEventsForOperationAsync(id, OwnerId, cancellationToken);

        return Ok(new RecoveryOperationDetailResponse(
            MapToResponse(operation),
            entries.Select(MapToResponse).ToList(),
            events.Select(MapToResponse).ToList()));
    }

    /// <summary>
    /// Exports one recovery operation's evidence — the manifest, operation header, entries, and
    /// full event chain. <c>format=json</c> (default) returns a single combined document;
    /// <c>format=csv</c> returns the entry flattening alone; <c>format=package</c> returns a zip
    /// containing all five files described in the roadmap's export package structure.
    /// </summary>
    /// <param name="id">The operation to export.</param>
    /// <param name="format">Export format: <c>json</c> (default), <c>csv</c>, or <c>package</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.RecoveryRead)]
    [HttpGet("operations/{id:guid}/export")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(
        Guid id,
        [FromQuery] string format = "json",
        CancellationToken cancellationToken = default)
    {
        var actor = ResolveRecoveryActor();
        var result = await _evidenceExporter.ExportAsync(id, OwnerId, actor.Identity, cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(Result.Failure(result.Error));
        }

        var export = result.Value;
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var baseName = $"recovery-evidence-{id}-{timestamp}";

        return format.ToLowerInvariant() switch
        {
            "csv" => File(Encoding.UTF8.GetBytes(export.EntriesCsv), "text/csv", $"{baseName}.csv"),
            "package" => File(BuildPackageZip(export), "application/zip", $"{baseName}.zip"),
            _ => File(Encoding.UTF8.GetBytes(export.BundleJson), "application/json", $"{baseName}.json"),
        };
    }

    /// <summary>
    /// Recomputes and compares the caller's Recovery Evidence Ledger hash chain — the chain is
    /// partitioned per owner, not per operation (roadmap §5.5), so this necessarily verifies the
    /// owner's entire chain, not only the events belonging to <paramref name="id"/>. Tamper-EVIDENT,
    /// not tamper-PROOF: it detects casual or partial alteration of the underlying SQLite file,
    /// nothing more.
    /// </summary>
    /// <param name="id">An operation belonging to the caller — used only to authorize the call
    /// and to confirm the operation exists; verification itself is owner-wide.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.RecoveryRead)]
    [HttpPost("operations/{id:guid}/verify")]
    [ProducesResponseType(typeof(ChainVerificationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChainVerificationResult>> VerifyChain(
        Guid id, CancellationToken cancellationToken = default)
    {
        var operation = await _recoveryLedger.GetOperationAsync(id, OwnerId, cancellationToken);
        if (operation is null)
        {
            return NotFound();
        }

        var result = await _recoveryLedger.VerifyChainAsync(OwnerId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Lists recovery ledger entries for the caller, optionally filtered by operation,
    /// namespace, or source DLQ message, most recently begun first. The
    /// <paramref name="dlqMessageId"/> filter is the message-detail "recovery record" lookup
    /// (roadmap §13.2) — it matches <c>RecoveryLedgerEntry.DlqMessageId</c>, a soft reference, so
    /// it still returns evidence for a message ServiceHub has since deleted.
    /// </summary>
    /// <param name="operationId">Optional operation filter.</param>
    /// <param name="namespaceId">Optional namespace filter.</param>
    /// <param name="dlqMessageId">Optional source DLQ message filter.</param>
    /// <param name="limit">Maximum number of entries to return (1-500, default 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.RecoveryRead)]
    [HttpGet("entries")]
    [ProducesResponseType(typeof(IReadOnlyList<RecoveryLedgerEntryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RecoveryLedgerEntryResponse>>> GetEntries(
        [FromQuery] Guid? operationId = null,
        [FromQuery] Guid? namespaceId = null,
        [FromQuery] long? dlqMessageId = null,
        [FromQuery] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var entries = await _recoveryLedger.QueryEntriesAsync(new RecoveryEntryQuery
        {
            OwnerId = OwnerId,
            OperationId = operationId,
            NamespaceId = namespaceId,
            DlqMessageId = dlqMessageId,
            Limit = ClampLimit(limit),
        }, cancellationToken);

        return Ok(entries.Select(MapToResponse).ToList());
    }

    /// <summary>
    /// Declares a non-terminal recovery ledger entry unrecoverable. Requires a mandatory reason
    /// and the explicit-intent headers, since this is the one way an operator asserts a terminal
    /// outcome without ServiceHub having observed it.
    /// </summary>
    /// <param name="id">The entry to write off.</param>
    /// <param name="request">The mandatory reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="428">Missing explicit-intent headers.</response>
    [RequireScope(ApiKeyScopes.RecoveryWrite)]
    [HttpPost("entries/{id:guid}/write-off")]
    [ProducesResponseType(typeof(RecoveryLedgerEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<RecoveryLedgerEntryResponse>> WriteOff(
        Guid id,
        [FromBody] WriteOffRecoveryEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IntentHeaders.HasExplicitIntent(HttpContext, IntentHeaders.IntentWriteOffRecovery))
        {
            return Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "Explicit Intent Required",
                detail: IntentHeaders.BuildIntentRequiredDetail("writing off a recovery ledger entry"));
        }

        var actor = ResolveRecoveryActor();
        var result = await _recoveryLedger.SetDispositionAsync(id, OwnerId, actor, request.Reason, cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult<RecoveryLedgerEntryResponse>(result.Error);
        }

        return Ok(MapToResponse(result.Value));
    }

    /// <summary>
    /// Gets the caller's currently open (non-terminal) recovery ledger entries, oldest first —
    /// the falsifiable form of "nothing is silently lost." Entries past the ageing threshold
    /// carry an <c>AgeingFlagged</c> event in their history (see <see cref="GetOperationById"/>'s
    /// event chain, or the <c>RecoveryAgeingWorker</c> class docs) but remain on this list, still
    /// non-terminal, until they either resolve normally or expire.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.RecoveryRead)]
    [HttpGet("ageing")]
    [ProducesResponseType(typeof(IReadOnlyList<RecoveryLedgerEntryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RecoveryLedgerEntryResponse>>> GetAgeing(
        CancellationToken cancellationToken = default)
    {
        var entries = await _recoveryLedger.GetAgeingAsync(OwnerId, cancellationToken);
        return Ok(entries.Select(MapToResponse).ToList());
    }

    private static int ClampLimit(int limit) => Math.Clamp(limit, 1, MaxLimit);

    private static byte[] BuildPackageZip(RecoveryEvidenceExport export)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(archive, "manifest.json", export.ManifestJson);
            AddZipEntry(archive, "operation.json", export.OperationJson);
            AddZipEntry(archive, "entries.json", export.EntriesJson);
            AddZipEntry(archive, "events.json", export.EventsJson);
            AddZipEntry(archive, "entries.csv", export.EntriesCsv);
        }

        return stream.ToArray();
    }

    private static void AddZipEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Encoding.UTF8);
        writer.Write(content);
    }

    private static RecoveryOperationResponse MapToResponse(RecoveryOperation operation) => new(
        Id: operation.Id,
        Kind: operation.Kind.ToString(),
        Trigger: operation.Trigger.ToString(),
        ActorIdentity: operation.ActorIdentity,
        ActorKind: operation.ActorKind.ToString(),
        Reason: operation.Reason,
        NamespaceId: operation.NamespaceId,
        NamespaceNameSnapshot: operation.NamespaceNameSnapshot,
        ProviderSnapshot: operation.ProviderSnapshot?.ToString(),
        EnvironmentSnapshot: operation.EnvironmentSnapshot?.ToString(),
        ScopeDescription: operation.ScopeDescription,
        SourceRuleId: operation.SourceRuleId,
        SourceJobId: operation.SourceJobId,
        ServiceVersion: operation.ServiceVersion,
        OpenedAt: operation.OpenedAt,
        TargetCount: operation.TargetCount);

    private static RecoveryLedgerEntryResponse MapToResponse(RecoveryLedgerEntry entry) => new(
        Id: entry.Id,
        OperationId: entry.OperationId,
        DlqMessageId: entry.DlqMessageId,
        NamespaceId: entry.NamespaceId,
        NamespaceNameSnapshot: entry.NamespaceNameSnapshot,
        ProviderSnapshot: entry.ProviderSnapshot?.ToString(),
        EnvironmentSnapshot: entry.EnvironmentSnapshot?.ToString(),
        EntityNameSnapshot: entry.EntityNameSnapshot,
        EntityTypeSnapshot: entry.EntityTypeSnapshot,
        TopicNameSnapshot: entry.TopicNameSnapshot,
        BodyHash: entry.BodyHash,
        FailureCategorySnapshot: entry.FailureCategorySnapshot?.ToString(),
        DeadLetterReasonSnapshot: entry.DeadLetterReasonSnapshot,
        SignatureHashSnapshot: entry.SignatureHashSnapshot,
        TargetEntity: entry.TargetEntity,
        BegunAt: entry.BegunAt,
        MarkerApplied: entry.MarkerApplied,
        State: entry.State.ToString(),
        Disposition: entry.Disposition?.ToString(),
        VerificationResult: entry.VerificationResult?.ToString(),
        VerificationConfidence: entry.VerificationConfidence?.ToString(),
        ObservationWindowEndsAt: entry.ObservationWindowEndsAt,
        ClosedAt: entry.ClosedAt);

    private static RecoveryEventResponse MapToResponse(RecoveryEvent evt) => new(
        Id: evt.Id,
        OwnerId: evt.OwnerId,
        Seq: evt.Seq,
        EntryId: evt.EntryId,
        OperationId: evt.OperationId,
        EventType: evt.EventType.ToString(),
        OccurredAt: evt.OccurredAt,
        ActorIdentity: evt.ActorIdentity,
        ActorKind: evt.ActorKind.ToString(),
        DetailJson: evt.DetailJson,
        PrevHash: evt.PrevHash,
        EntryHash: evt.EntryHash,
        SchemaVersion: evt.SchemaVersion);
}
