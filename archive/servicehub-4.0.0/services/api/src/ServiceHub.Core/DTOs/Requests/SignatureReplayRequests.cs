using System.ComponentModel.DataAnnotations;
using ServiceHub.Core.Enums;

namespace ServiceHub.Core.DTOs.Requests;

/// <summary>
/// Selects the set of a failure signature's member messages a replay targets. The signature's
/// current member messages are resolved server-side (from live clustering); this filter narrows
/// that set further. <see cref="Status"/>/<see cref="From"/>/<see cref="To"/> all null means
/// "every message currently in the signature".
/// </summary>
/// <param name="NamespaceId">Required. The namespace the signature belongs to.</param>
/// <param name="SignatureHash">Required. The failure signature's stable identity hash.</param>
/// <param name="Status">
/// Optional status filter. <see cref="DlqMessageStatus.Active"/> selects "unresolved messages
/// only"; <see cref="DlqMessageStatus.ReplayFailed"/> selects "failed replay attempts only".
/// </param>
/// <param name="From">Optional inclusive lower bound on detection time (date-range scope).</param>
/// <param name="To">Optional inclusive upper bound on detection time (date-range scope).</param>
public sealed record SignatureReplayFilterRequest(
    [Required] Guid NamespaceId,
    [Required] string SignatureHash,
    DlqMessageStatus? Status,
    DateTimeOffset? From,
    DateTimeOffset? To);

/// <summary>Request body for <c>POST .../dlq/signatures/{signatureHash}/replay/preview</c>.</summary>
/// <param name="Filter">The message-selection filter.</param>
public sealed record SignatureReplayPreviewRequest([Required] SignatureReplayFilterRequest Filter);

/// <summary>Request body for <c>POST .../dlq/signatures/{signatureHash}/replay</c>.</summary>
/// <param name="Filter">The message-selection filter, identical to what was used for the preview.</param>
public sealed record SignatureReplayStartRequest([Required] SignatureReplayFilterRequest Filter);

/// <summary>
/// HTTP request body for the signature-replay preview/start endpoints — just the scope, since
/// the namespace and signature are already identified by the route
/// (<c>.../dlq/signatures/{signatureHash}/replay[/preview]</c>). The controller combines this
/// with the route values to build a <see cref="SignatureReplayFilterRequest"/>.
/// </summary>
/// <param name="Status">See <see cref="SignatureReplayFilterRequest.Status"/>.</param>
/// <param name="From">See <see cref="SignatureReplayFilterRequest.From"/>.</param>
/// <param name="To">See <see cref="SignatureReplayFilterRequest.To"/>.</param>
public sealed record SignatureReplayScopeRequest(
    DlqMessageStatus? Status,
    DateTimeOffset? From,
    DateTimeOffset? To);
