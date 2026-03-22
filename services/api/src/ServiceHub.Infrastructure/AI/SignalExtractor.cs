using ServiceHub.Core.Entities;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// Extracts normalised signal tokens from a <see cref="DlqMessage"/>
/// for use by the deterministic and heuristic classifiers.
/// </summary>
internal static class SignalExtractor
{
    /// <summary>
    /// Builds a combined, lower-cased text block from the reason,
    /// error description, and body preview of the message.
    /// </summary>
    internal static string CombinedText(DlqMessage msg)
    {
        return $"{msg.DeadLetterReason} {msg.DeadLetterErrorDescription} {msg.BodyPreview}"
            .ToLowerInvariant();
    }
}
