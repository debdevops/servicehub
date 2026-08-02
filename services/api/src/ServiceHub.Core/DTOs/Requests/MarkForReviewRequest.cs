using System.ComponentModel.DataAnnotations;

namespace ServiceHub.Core.DTOs.Requests;

/// <summary>Request to mark a failure signature's knowledge as needing review by a given date.</summary>
public sealed record MarkForReviewRequest(
    [property: Required] DateTimeOffset ReviewDueAt) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ReviewDueAt == default)
        {
            yield return new ValidationResult(
                "ReviewDueAt is required.",
                [nameof(ReviewDueAt)]);
        }
    }
}
