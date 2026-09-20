using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// The one <see cref="PlaybookEntry"/> → <see cref="PlaybookEntryResponse"/> mapping, shared by
/// every controller that surfaces raw <see cref="PlaybookEntry"/> rows (<see cref="PlaybookController"/>,
/// <see cref="PreventionRulesController"/>) — previously duplicated verbatim in both, which risked
/// one copy silently falling out of sync with the other if <see cref="PlaybookEntry"/> or
/// <see cref="PlaybookEntryResponse"/> ever gained or renamed a field.
/// </summary>
internal static class PlaybookEntryResponseMapper
{
    public static PlaybookEntryResponse ToResponse(this PlaybookEntry entry) => new(
        Id: entry.Id,
        PillarKind: entry.PillarKind.ToString(),
        ProposalKind: entry.ProposalKind,
        EvidenceRefJson: entry.EvidenceRefJson,
        ProposalJson: entry.ProposalJson,
        ProposedAt: entry.ProposedAt,
        ProposerIdentity: entry.ProposerIdentity,
        ProposerKind: entry.ProposerKind.ToString(),
        SignatureHashSnapshot: entry.SignatureHashSnapshot,
        NamespaceId: entry.NamespaceId,
        NamespaceNameSnapshot: entry.NamespaceNameSnapshot,
        ProviderSnapshot: entry.ProviderSnapshot?.ToString(),
        EnvironmentSnapshot: entry.EnvironmentSnapshot?.ToString(),
        RelatedRecoveryOperationId: entry.RelatedRecoveryOperationId,
        ExpiresAt: entry.ExpiresAt,
        State: entry.State.ToString(),
        Disposition: entry.Disposition?.ToString(),
        ClosedAt: entry.ClosedAt);
}
