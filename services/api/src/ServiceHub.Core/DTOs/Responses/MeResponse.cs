namespace ServiceHub.Core.DTOs.Responses;

/// <summary>Response DTO for <c>GET /api/v1/me</c> — the caller's own identity.</summary>
/// <param name="OwnerId">
/// The caller's owner ID for tenant isolation — e.g. <c>oidc:{sub}</c>, <c>key_{hash}</c>, or
/// <c>entra:{oid}</c>. Share this value with a namespace owner (via <c>POST
/// /api/v1/namespaces/{id}/share</c>) to request access to a namespace they own.
/// </param>
/// <param name="AuthMethod">
/// How this request authenticated: <c>SpaToken</c>, <c>EasyAuth</c>, <c>Oidc</c>, <c>ApiKey</c>,
/// or null if authentication is disabled on this server.
/// </param>
public sealed record MeResponse(string OwnerId, string? AuthMethod);
