using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Filters;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Configuration and status surface for the infrastructure-attested DLQ observer
/// (`cloud-platform-infra` ADR-004; ADR-0011) — an operator sets this once the corresponding
/// Terraform observer module has actually been applied for a namespace's DLQ.
/// <see cref="IDlqObserverAttestationService"/> owns every write; this controller only translates
/// HTTP concerns.
/// </summary>
[Route(ApiRoutes.DlqObserverAttestation.Base)]
[Tags("DlqObserverAttestation")]
[RequireNamespaceOwnership]
public sealed class DlqObserverAttestationController : ApiControllerBase
{
    private readonly IDlqObserverAttestationService _attestationService;

    /// <summary>Initializes a new instance of the <see cref="DlqObserverAttestationController"/> class.</summary>
    public DlqObserverAttestationController(IDlqObserverAttestationService attestationService)
    {
        _attestationService = attestationService ?? throw new ArgumentNullException(nameof(attestationService));
    }

    /// <summary>Gets one namespace's attestation configuration and current liveness.</summary>
    /// <param name="namespaceId">The namespace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.NamespacesRead)]
    [HttpGet]
    [ProducesResponseType(typeof(DlqObserverAttestationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DlqObserverAttestationResponse>> Get(
        Guid namespaceId, CancellationToken cancellationToken = default)
    {
        var attestation = await _attestationService.GetAsync(OwnerId, namespaceId, cancellationToken);
        if (attestation is null)
        {
            return NotFound();
        }

        return Ok(MapToResponse(attestation));
    }

    /// <summary>
    /// Creates or updates one namespace's attestation configuration. Requires
    /// <see cref="GovernanceRole.Admin"/>, scoped to the namespace/<see cref="PillarKind.Recover"/>
    /// — the same privilege level required to change what a namespace's own eligibility gate
    /// trusts, not a lesser one an Operator role could set unilaterally.
    /// </summary>
    /// <param name="namespaceId">The namespace.</param>
    /// <param name="request">The attestation configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.NamespacesWrite)]
    [RequireGovernanceRole(GovernanceRole.Admin, PillarKind.Recover)]
    [HttpPut]
    [ProducesResponseType(typeof(DlqObserverAttestationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DlqObserverAttestationResponse>> Configure(
        Guid namespaceId, [FromBody] ConfigureDlqObserverAttestationRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _attestationService.ConfigureAsync(
            OwnerId, namespaceId, request.Enabled, request.ObserverReference, request.DlqEntityName,
            request.StalenessBoundMinutes, cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult<DlqObserverAttestationResponse>(result.Error);
        }

        return Ok(MapToResponse(result.Value));
    }

    private static DlqObserverAttestationResponse MapToResponse(DlqObserverAttestation attestation) => new(
        NamespaceId: attestation.NamespaceId,
        Enabled: attestation.Enabled,
        ObserverReference: attestation.ObserverReference,
        DlqEntityName: attestation.DlqEntityName,
        LastCanarySentAt: attestation.LastCanarySentAt,
        LastConfirmedAt: attestation.LastConfirmedAt,
        StalenessBoundMinutes: attestation.StalenessBoundMinutes,
        IsLive: attestation.IsLiveAt(DateTimeOffset.UtcNow));
}
