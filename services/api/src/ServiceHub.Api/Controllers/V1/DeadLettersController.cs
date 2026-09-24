using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.Constants;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// The durable list of dead letters ServiceHub has seen (unit 2.3) — what is stuck, filterable and
/// paged, newest first. Read-only.
/// </summary>
/// <remarks>
/// Scoped to the caller: only namespaces they may see are ever consulted, and a namespace they may not
/// see is reported exactly like one that does not exist. <b>No provider is named here</b> (rule R4) — a
/// cloud is a filter value the caller chose, never a branch. There is no body search: no cloud supports
/// it without ServiceHub keeping bodies, and promising it would be false (rule R5).
/// </remarks>
[Route("api/v1/dead-letters")]
public sealed class DeadLettersController : ApiControllerBase
{
    private const int DefaultPageSize = 25;
    private const int MaxSearch = 200;

    private readonly INamespaceRepository _namespaces;
    private readonly IDlqMessageReader _reader;

    /// <summary>Creates the controller.</summary>
    public DeadLettersController(INamespaceRepository namespaces, IDlqMessageReader reader)
    {
        _namespaces = namespaces ?? throw new ArgumentNullException(nameof(namespaces));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <summary>Lists dead letters, newest first.</summary>
    /// <param name="namespaceId">One namespace. Give this, or <paramref name="provider"/>, or both.</param>
    /// <param name="provider">One cloud — every namespace the caller has on it. Never mixes clouds.</param>
    /// <param name="status">active (default), resolved or all.</param>
    /// <param name="range">How far back it was first seen: 24h, 7d, 30d, or all (default).</param>
    /// <param name="reason">Only this recorded reason.</param>
    /// <param name="noReason">Only messages with no recorded reason.</param>
    /// <param name="entity">Only this queue or subscription.</param>
    /// <param name="q">Text in the message id, entity name or reason — never the body.</param>
    /// <param name="page">1-based.</param>
    /// <param name="pageSize">1–100 (default 25).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet]
    [ProducesResponseType(typeof(DeadLetterListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? namespaceId, [FromQuery] CloudProviderType? provider, [FromQuery] string? status,
        [FromQuery] string? range, [FromQuery] string? reason, [FromQuery] bool noReason,
        [FromQuery] string? entity, [FromQuery] string? q, [FromQuery] int? page, [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        if (namespaceId is null && provider is null)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed,
                "Say which dead letters: give a 'namespaceId', a 'provider', or both.");
        }

        if (!TryParseStatus(status, out var wantedStatus))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'status' must be active, resolved or all.");
        }

        if (!TryParseRange(range, out var since))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'range' must be 24h, 7d, 30d or all.");
        }

        if (page is < 1 || pageSize is < 1 or > 100)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'page' starts at 1 and 'pageSize' must be between 1 and 100.");
        }

        if (noReason && !string.IsNullOrEmpty(reason))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "Choose either a 'reason' or 'noReason', not both.");
        }

        if (q is { Length: > MaxSearch })
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, $"'q' can be at most {MaxSearch} characters.");
        }

        var visible = await _namespaces.GetByOwnerAsync(OwnerId, AllowedNamespaceIds, cancellationToken);
        if (visible.IsFailure)
        {
            return Problem(visible.Error);
        }

        var candidates = visible.Value.AsEnumerable();
        if (namespaceId is { } id)
        {
            candidates = candidates.Where(n => n.Id == id);
            if (!candidates.Any())
            {
                // Someone else's namespace looks exactly like one that is not there.
                return Problem(StatusCodes.Status404NotFound, ErrorCodes.Namespace.NotFound, $"Namespace with ID '{id}' was not found.");
            }
        }

        if (provider is { } cloud)
        {
            candidates = candidates.Where(n => n.Provider == cloud);
        }

        var result = await _reader.ListAsync(
            new DlqListQuery(
                [.. candidates.Select(n => n.Id)], wantedStatus, since, reason, noReason, entity, q,
                page ?? 1, pageSize ?? DefaultPageSize),
            cancellationToken);

        return Ok(new DeadLetterListResponse(
            result.Items,
            new DeadLetterPaging(result.Total, result.Page, result.PageSize),
            result.Groups,
            result.OtherReasons,
            result.Entities));
    }

    private static bool TryParseStatus(string? value, out DlqMessageStatus? status)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "active":
                status = DlqMessageStatus.Active;
                return true;
            case "resolved":
                status = DlqMessageStatus.Resolved;
                return true;
            case "all":
                status = null;
                return true;
            default:
                status = null;
                return false;
        }
    }

    private static bool TryParseRange(string? value, out DateTimeOffset? since)
    {
        var now = DateTimeOffset.UtcNow;
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "all":
                since = null;
                return true;
            case "24h":
                since = now.AddHours(-24);
                return true;
            case "7d":
                since = now.AddDays(-7);
                return true;
            case "30d":
                since = now.AddDays(-30);
                return true;
            default:
                since = null;
                return false;
        }
    }
}
