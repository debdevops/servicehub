using System.ComponentModel.DataAnnotations;
using ServiceHub.Core.Enums;

namespace ServiceHub.Core.DTOs.Requests;

/// <summary>Request body for <c>POST /api/v1/external-signals</c> — an operator's manual
/// annotation, or a webhook's ingest, of a deploy/config-change timestamp (roadmap §5.D, C3).</summary>
/// <param name="NamespaceId">Soft reference to the namespace this signal concerns. Null means
/// fleet-wide (e.g. a platform-wide deploy).</param>
/// <param name="SignalType">The kind of signal.</param>
/// <param name="OccurredAt">When the signal actually happened — not when it was ingested.</param>
/// <param name="Source">Where the signal came from, e.g. <c>"webhook:github-actions"</c>,
/// <c>"manual"</c>.</param>
/// <param name="DetailJson">Free-form detail (e.g. commit SHA, deploy ID) — never a message body.</param>
public sealed record RecordExternalSignalHttpRequest(
    Guid? NamespaceId,
    ExternalSignalType SignalType,
    DateTimeOffset OccurredAt,
    [Required(ErrorMessage = "Source is required to record an external signal")]
    [MinLength(1)]
    string Source,
    string? DetailJson);
