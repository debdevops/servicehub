using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Core.DTOs.Requests;

/// <summary>Request body for <c>POST /api/v1/playbook/entries/{id}/disposition</c> — a human's
/// terminal decision on a proposal. A missing <see cref="Reason"/> on
/// <see cref="PlaybookDisposition.Rejected"/> is enforced authoritatively by
/// <see cref="IPlaybookLedger.DispositionAsync"/> itself, not this DTO.</summary>
public sealed record DispositionPlaybookEntryRequest(
    PlaybookDisposition Disposition,
    string? Reason);
