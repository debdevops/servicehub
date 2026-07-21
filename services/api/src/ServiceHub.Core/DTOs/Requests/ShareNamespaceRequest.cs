using System.ComponentModel.DataAnnotations;

namespace ServiceHub.Core.DTOs.Requests;

/// <summary>Request body for <c>POST /api/v1/namespaces/{id}/share</c>.</summary>
/// <param name="OwnerId">
/// The owner identity to grant live operational access to — e.g. <c>oidc:{sub}</c> for an OIDC
/// user (see <c>GET /api/v1/me</c> to discover an owner ID) or <c>key_{hash}</c> for a scoped
/// API key.
/// </param>
public sealed record ShareNamespaceRequest(
    [Required(ErrorMessage = "Owner ID is required")]
    [StringLength(256, MinimumLength = 1, ErrorMessage = "Owner ID must be between 1 and 256 characters")]
    string OwnerId);
