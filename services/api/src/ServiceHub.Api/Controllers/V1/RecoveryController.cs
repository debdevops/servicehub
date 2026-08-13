using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Filters;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Read-only surface over the Recovery Evidence Ledger. Export, chain verification, and
/// write-off — the <c>recovery:write</c>-gated actions — are a later phase; every action here is
/// <c>recovery:read</c>.
/// </summary>
[Route(ApiRoutes.Recovery.Base)]
[Tags("Recovery")]
[RequireNamespaceOwnership]
public sealed class RecoveryController : ApiControllerBase
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    private readonly IRecoveryLedger _recoveryLedger;

    /// <summary>Initializes a new instance of the <see cref="RecoveryController"/> class.</summary>
    public RecoveryController(IRecoveryLedger recoveryLedger)
    {
        _recoveryLedger = recoveryLedger ?? throw new ArgumentNullException(nameof(recoveryLedger));
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
    /// Gets one recovery operation by ID, scoped to the caller.
    /// </summary>
    /// <param name="id">The operation ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.RecoveryRead)]
    [HttpGet("operations/{id:guid}")]
    [ProducesResponseType(typeof(RecoveryOperationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecoveryOperationResponse>> GetOperationById(
        Guid id, CancellationToken cancellationToken = default)
    {
        var operation = await _recoveryLedger.GetOperationAsync(id, OwnerId, cancellationToken);
        return operation is null ? NotFound() : Ok(MapToResponse(operation));
    }

    /// <summary>
    /// Lists recovery ledger entries for the caller, optionally filtered by operation or
    /// namespace, most recently begun first.
    /// </summary>
    /// <param name="operationId">Optional operation filter.</param>
    /// <param name="namespaceId">Optional namespace filter.</param>
    /// <param name="limit">Maximum number of entries to return (1-500, default 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.RecoveryRead)]
    [HttpGet("entries")]
    [ProducesResponseType(typeof(IReadOnlyList<RecoveryLedgerEntryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RecoveryLedgerEntryResponse>>> GetEntries(
        [FromQuery] Guid? operationId = null,
        [FromQuery] Guid? namespaceId = null,
        [FromQuery] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var entries = await _recoveryLedger.QueryEntriesAsync(new RecoveryEntryQuery
        {
            OwnerId = OwnerId,
            OperationId = operationId,
            NamespaceId = namespaceId,
            Limit = ClampLimit(limit),
        }, cancellationToken);

        return Ok(entries.Select(MapToResponse).ToList());
    }

    /// <summary>
    /// Gets the caller's currently open (non-terminal) recovery ledger entries, oldest first —
    /// the falsifiable form of "nothing is silently lost." No ageing threshold or flagging is
    /// applied yet; that is the ageing worker's responsibility, added in a later phase.
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
}
