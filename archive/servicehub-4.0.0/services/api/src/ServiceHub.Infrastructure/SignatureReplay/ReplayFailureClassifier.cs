using ServiceHub.Core.Enums;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.SignatureReplay;

/// <summary>
/// Maps a failed replay's <see cref="Error"/> to a <see cref="ReplayFailureReason"/> bucket —
/// see that enum's remarks for why <see cref="ReplayFailureReason.NotFound"/> deliberately does
/// not further distinguish "consumed" from "expired."
/// </summary>
public static class ReplayFailureClassifier
{
    /// <summary>Classifies a failed replay's error into a <see cref="ReplayFailureReason"/>.</summary>
    public static ReplayFailureReason Classify(Error error) => error.Type switch
    {
        ErrorType.NotFound => ReplayFailureReason.NotFound,
        // AWS.SQS.ReplayAmbiguous is the only Conflict this path currently produces (send to the
        // source queue succeeded, delete from the DLQ failed) — see AwsMessageReceiver.
        ErrorType.Conflict => ReplayFailureReason.AmbiguousOutcome,
        ErrorType.Timeout or ErrorType.RateLimited => ReplayFailureReason.Retryable,
        ErrorType.ExternalService or ErrorType.Internal => ReplayFailureReason.ProviderError,
        _ => ReplayFailureReason.Other,
    };
}
