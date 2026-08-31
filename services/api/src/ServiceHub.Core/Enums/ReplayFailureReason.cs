namespace ServiceHub.Core.Enums;

/// <summary>
/// Provider-agnostic classification of why a single message replay failed, derived from the
/// underlying provider call's <c>Error.Type</c> — the same category every Azure/AWS/GCP
/// provider already reports failures through, so this requires no new provider-side detection.
/// Exists so the UI can distinguish "this message is genuinely gone" from "this failed for a
/// reason worth retrying" instead of one generic failure bucket.
/// </summary>
/// <remarks>
/// None of the three providers' APIs expose *why* a message is no longer in the DLQ (consumed
/// by another process, replayed earlier, or expired past its TTL) — only that it can no longer
/// be found there. <see cref="NotFound"/> intentionally covers all three; further splitting
/// them would require inventing a distinction the provider APIs do not make.
/// </remarks>
public enum ReplayFailureReason
{
    /// <summary>
    /// The message could no longer be found in the DLQ — already consumed, replayed, or
    /// expired; the provider APIs do not distinguish which.
    /// </summary>
    NotFound = 0,

    /// <summary>
    /// The provider accepted part of the operation but not all of it (e.g. AWS: send to the
    /// source queue succeeded but delete from the DLQ failed) — outcome is genuinely uncertain,
    /// and a blind retry risks delivering the message twice.
    /// </summary>
    AmbiguousOutcome = 1,

    /// <summary>A transient provider condition (timeout, rate limit) — safe to retry.</summary>
    Retryable = 2,

    /// <summary>An unexpected provider/service failure not covered by the categories above.</summary>
    ProviderError = 3,

    /// <summary>Doesn't fit any of the above (e.g. a configuration/validation error).</summary>
    Other = 4,
}
