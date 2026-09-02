using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Filters;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// The Incident read-model (roadmap W2.1) — one durable, addressable view per failure signature,
/// composing existing recovery and playbook evidence. The keystone W2.2 (attention queue) and
/// W2.3 (incident workspace) build on.
/// </summary>
[Route(ApiRoutes.Incidents.Base)]
[Tags("Incidents")]
public sealed class IncidentsController : ApiControllerBase
{
    private readonly IIncidentReadModelService _incidentReadModel;

    /// <summary>Initializes a new instance of the <see cref="IncidentsController"/> class.</summary>
    public IncidentsController(IIncidentReadModelService incidentReadModel)
    {
        _incidentReadModel = incidentReadModel ?? throw new ArgumentNullException(nameof(incidentReadModel));
    }

    /// <summary>
    /// Gets the full incident view for one failure signature: identity, lifecycle status,
    /// recovery evidence, and every anomaly/drift/correlation/prevention/replay proposal
    /// recorded against it.
    /// </summary>
    /// <param name="namespaceId">The namespace the signature belongs to.</param>
    /// <param name="signatureHash">The signature's stable identity hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("{signatureHash}")]
    [RequireNamespaceOwnership]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(IncidentDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IncidentDetailResponse>> GetIncident(
        Guid namespaceId,
        string signatureHash,
        CancellationToken cancellationToken = default)
    {
        var result = await _incidentReadModel.GetIncidentAsync(OwnerId, namespaceId, signatureHash, cancellationToken);
        return ToActionResult(result);
    }
}
