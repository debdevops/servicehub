using System.ComponentModel.DataAnnotations;

namespace ServiceHub.Core.DTOs.Requests;

/// <summary>Request to mark a failure signature's knowledge as needing review by a given date.</summary>
public sealed record MarkForReviewRequest(
    [property: Required] DateTimeOffset ReviewDueAt);
