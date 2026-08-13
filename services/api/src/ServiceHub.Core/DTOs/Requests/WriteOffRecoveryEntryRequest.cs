using System.ComponentModel.DataAnnotations;

namespace ServiceHub.Core.DTOs.Requests;

/// <summary>Request body for <c>POST /api/v1/recovery/entries/{id}/write-off</c>.</summary>
/// <param name="Reason">
/// Mandatory operator justification for declaring a non-terminal entry unrecoverable. See
/// <c>IRecoveryLedger.SetDispositionAsync</c>.
/// </param>
public sealed record WriteOffRecoveryEntryRequest(
    [Required(ErrorMessage = "Reason is required to write off a recovery ledger entry")]
    [MinLength(1)]
    string Reason);
