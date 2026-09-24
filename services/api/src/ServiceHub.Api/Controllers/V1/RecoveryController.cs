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
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(RecoverySummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Summary(
        [FromQuery] string? window, [FromQuery] CloudProviderType? provider, [FromQuery] Guid? namespaceId, CancellationToken cancellationToken)
    {
        var w = string.IsNullOrWhiteSpace(window) ? "24h" : window.Trim();
        if (!RecoveryQueries.IsKnownWindow(w))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'window' must be 24h, 7d, 30d or all.");
        }

        if (await ScopeAsync(provider, namespaceId, cancellationToken) is not { } scope)
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
    /// <param name="page">1-based.</param>
    /// <param name="pageSize">1–100 (default 25).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("entries")]
    [ProducesResponseType(typeof(RecoveryEntryPage), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Entries(
        [FromQuery] string? window, [FromQuery] string? state, [FromQuery] CloudProviderType? provider, [FromQuery] Guid? namespaceId,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
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

        if (await ScopeAsync(provider, namespaceId, cancellationToken) is not { } scope)
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

    private async Task<RecoveryScope?> ScopeAsync(CloudProviderType? provider, Guid? namespaceId, CancellationToken cancellationToken)
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

        return new RecoveryScope(OwnerId, AllowedNamespaceIds, namespaceId, provider);
    }
}
