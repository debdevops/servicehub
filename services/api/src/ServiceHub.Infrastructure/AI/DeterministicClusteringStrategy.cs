using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Helpers;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// Deterministic clustering strategy that groups DLQ messages by stable, observable fields
/// without requiring an external AI service. This strategy is always available and provides
/// a reliable fallback when AI clustering is unavailable or disabled.
///
/// Grouping criteria (in priority order):
/// 1. DeadLetterReason (e.g., MaxDeliveryCountExceeded, TTLExpiredException)
/// 2. Entity Name (queue, topic, subscription)
/// 3. Exception Type (extracted from error properties)
/// 4. Provider-specific classifications (e.g., AWS error code, GCP reason)
///
/// Each group becomes a cluster. Singletons are messages that don't share any of these
/// characteristics with other messages in the batch.
///
/// The fingerprint is deterministic: the same message properties always produce the same
/// signature hash, enabling signature history tracking even without AI.
/// </summary>
public sealed class DeterministicClusteringStrategy : ISignatureAnalysisStrategy
{
    /// <summary>The strategy name, used in result metadata for observability.</summary>
    private const string StrategyName = "deterministic";

    /// <summary>Minimum cluster size; groups with only 1 member become singletons.</summary>
    private const int MinClusterSize = 2;

    public async Task<Result<ClusterAnalysisResult>> AnalyzeAsync(
        IReadOnlyList<DlqMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // Empty batch: return empty result (no clustering needed).
        if (messages.Count == 0)
        {
            return Result.Success(new ClusterAnalysisResult([], [], StrategyName, new Dictionary<string, long>()));
        }

        // Build ref → messageId map (consistent with AI clustering).
        var refToMessageId = new Dictionary<string, long>(messages.Count);
        for (var i = 0; i < messages.Count; i++)
        {
            refToMessageId[i.ToString()] = messages[i].Id;
        }

        // Group messages by fingerprint (deterministic fields).
        var groupsByFingerprint = messages
            .Select((msg, idx) => (Index: idx.ToString(), Message: msg, Fingerprint: ComputeFingerprint(msg)))
            .GroupBy(x => x.Fingerprint)
            .ToList();

        var clusters = new List<ClusterSummary>();
        var singletons = new List<ClusterSingleton>();

        foreach (var group in groupsByFingerprint)
        {
            var members = group.ToList();

            if (members.Count < MinClusterSize)
            {
                // Singleton: message(s) with unique fingerprint.
                foreach (var member in members)
                {
                    singletons.Add(new ClusterSingleton(
                        Ref: member.Index,
                        DominantEntity: member.Message.EntityName,
                        DominantDeadletterReason: member.Message.DeadLetterReason ?? "Unknown"));
                }
                continue;
            }

            // Cluster: multiple messages with the same fingerprint.
            var representativeMessage = members[0].Message;
            var topTerms = ExtractTopTerms(members.Select(m => m.Message).ToList());

            var cluster = new ClusterSummary(
                Size: members.Count,
                RepresentativeRef: members[0].Index,
                TopTerms: topTerms,
                FirstOccurrenceRef: members[0].Index,
                LastOccurrenceRef: members[^1].Index,
                DominantEntity: representativeMessage.EntityName,
                DominantDeadletterReason: representativeMessage.DeadLetterReason ?? "Unknown",
                MemberRefs: members.Select(m => m.Index).ToList(),
                DominantDeadletterReasonCount: members.Count(m =>
                    string.Equals(
                        m.Message.DeadLetterReason,
                        representativeMessage.DeadLetterReason,
                        StringComparison.OrdinalIgnoreCase)));

            clusters.Add(cluster);
        }

        var result = new ClusterAnalysisResult(clusters, singletons, StrategyName, refToMessageId);
        return await Task.FromResult(Result.Success(result)).ConfigureAwait(false);
    }

    /// <summary>
    /// Computes a stable fingerprint from a message's observable characteristics.
    /// Same input message properties always produce the same fingerprint.
    /// </summary>
    private static string ComputeFingerprint(DlqMessage message)
    {
        var parts = new List<string>
        {
            message.DeadLetterReason ?? "unknown-reason",
            message.EntityName ?? "unknown-entity",
            ExtractExceptionType(message) ?? "unknown-exception",
            message.CloudProvider.ToString().ToLowerInvariant(),
        };

        // Include provider-specific categorization if available (ForensicRootCause
        // was computed earlier by the forensic engine).
        if (!string.IsNullOrWhiteSpace(message.ForensicRootCause))
        {
            parts.Add(message.ForensicRootCause);
        }

        // Create deterministic fingerprint: join normalized parts.
        var canonical = string.Join("|", parts.Select(p => p.ToLowerInvariant().Trim()));
        return canonical;
    }

    /// <summary>Extracts exception type from application properties or error text.</summary>
    private static string? ExtractExceptionType(DlqMessage message)
    {
        // First, try to extract from application properties (if available).
        if (!string.IsNullOrWhiteSpace(message.ApplicationPropertiesJson))
        {
            try
            {
                // Simple heuristic: look for exception type in properties.
                if (message.ApplicationPropertiesJson.Contains("ExceptionType", StringComparison.OrdinalIgnoreCase))
                {
                    // This is a simplified extraction; in production, parse JSON properly.
                    var start = message.ApplicationPropertiesJson.IndexOf("ExceptionType", StringComparison.OrdinalIgnoreCase);
                    if (start >= 0)
                    {
                        var end = message.ApplicationPropertiesJson.IndexOf("\"", start + 15);
                        if (end > start)
                        {
                            return message.ApplicationPropertiesJson.Substring(start + 15, end - start - 15);
                        }
                    }
                }
            }
            catch
            {
                // Parsing failed; fall through to next strategy.
            }
        }

        // Fallback: extract from error description or body preview.
        var errorText = (message.DeadLetterErrorDescription ?? message.BodyPreview ?? string.Empty).ToLowerInvariant();

        // Heuristic patterns (common exception types).
        if (errorText.Contains("timeout")) return "TimeoutException";
        if (errorText.Contains("authentication")) return "AuthenticationException";
        if (errorText.Contains("authorization")) return "AuthorizationException";
        if (errorText.Contains("notfound")) return "NotFoundException";
        if (errorText.Contains("serialization")) return "SerializationException";
        if (errorText.Contains("validation")) return "ValidationException";

        return null;
    }

    /// <summary>
    /// Extracts top distinguishing terms from a cluster of messages.
    /// Uses message properties and error descriptions to identify common characteristics.
    /// </summary>
    private static IReadOnlyList<string> ExtractTopTerms(IReadOnlyList<DlqMessage> clusterMessages)
    {
        var terms = new List<string>();

        // Add entity type/name as term.
        if (!string.IsNullOrWhiteSpace(clusterMessages[0].EntityName))
        {
            terms.Add($"entity:{clusterMessages[0].EntityName}");
        }

        // Add primary deadletter reason.
        if (!string.IsNullOrWhiteSpace(clusterMessages[0].DeadLetterReason))
        {
            terms.Add($"reason:{clusterMessages[0].DeadLetterReason}");
        }

        // Add exception type if extracted.
        var exceptionType = ExtractExceptionType(clusterMessages[0]);
        if (!string.IsNullOrWhiteSpace(exceptionType))
        {
            terms.Add($"exception:{exceptionType}");
        }

        // Add provider.
        terms.Add($"provider:{clusterMessages[0].CloudProvider}");

        // Add forensic classification if available.
        if (!string.IsNullOrWhiteSpace(clusterMessages[0].ForensicRootCause))
        {
            terms.Add($"cause:{clusterMessages[0].ForensicRootCause}");
        }

        // Add delivery count bucket (useful for aggregation).
        var avgDeliveryCount = (int)clusterMessages.Average(m => m.DeliveryCount);
        if (avgDeliveryCount > 0)
        {
            terms.Add($"deliveryAttempts:{avgDeliveryCount}");
        }

        return terms.Take(5).ToList(); // Limit to top 5 terms.
    }
}
