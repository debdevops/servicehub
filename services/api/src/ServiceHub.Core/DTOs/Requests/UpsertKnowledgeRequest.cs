using System.ComponentModel.DataAnnotations;

namespace ServiceHub.Core.DTOs.Requests;

/// <summary>
/// Request to create or update operational knowledge for a failure signature.
/// </summary>
public sealed record UpsertKnowledgeRequest
{
    /// <summary>Root cause analysis. Required — the core of what knowledge is meant to capture.</summary>
    [Required]
    [StringLength(4096, MinimumLength = 1)]
    public required string RootCause { get; init; }

    /// <summary>Resolution notes.</summary>
    [StringLength(4096)]
    public string? ResolutionNotes { get; init; }

    /// <summary>Operational context.</summary>
    [StringLength(4096)]
    public string? OperationalNotes { get; init; }

    /// <summary>Link to runbook. Must be a valid absolute URL when supplied.</summary>
    [StringLength(1024)]
    [Url]
    public string? RunbookLink { get; init; }

    /// <summary>Owner email/identifier.</summary>
    [StringLength(256)]
    public string? Owner { get; init; }

    /// <summary>Replay guidance — one of Safe, Unsafe, or Investigate.</summary>
    [RegularExpression("^(Safe|Unsafe|Investigate)$")]
    public string? ReplayGuidance { get; init; }

    /// <summary>Comma-separated tags.</summary>
    [StringLength(2048)]
    public string? Tags { get; init; }

    /// <summary>When to review this knowledge next.</summary>
    public DateTimeOffset? ReviewDueAt { get; init; }

    /// <summary>Free-text identifier of who is making this edit. Optional.</summary>
    [StringLength(256)]
    public string? ChangedBy { get; init; }
}
