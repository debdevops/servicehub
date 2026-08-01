using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// AI-powered clustering strategy that groups DLQ messages using statistical analysis
/// and machine learning via the optional AI service. This strategy produces richer,
/// more sophisticated clusters than deterministic approaches but depends on external
/// service availability.
///
/// When the AI service is unavailable, this strategy returns failure, allowing the
/// orchestrator to fall back to <see cref="DeterministicClusteringStrategy"/>.
/// </summary>
public sealed class AIClusteringStrategy : ISignatureAnalysisStrategy
{
    private readonly IAIServiceClient _aiServiceClient;

    public AIClusteringStrategy(IAIServiceClient aiServiceClient)
    {
        _aiServiceClient = aiServiceClient ?? throw new ArgumentNullException(nameof(aiServiceClient));
    }

    public async Task<Result<ClusterAnalysisResult>> AnalyzeAsync(
        IReadOnlyList<DlqMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // Delegate directly to AI service client.
        // This wrapping layer allows AI strategy to be treated like any other strategy
        // without consumers needing to know about AIServiceClient.
        var result = await _aiServiceClient.AnalyzeMessagesAsync(messages, cancellationToken)
            .ConfigureAwait(false);

        return result;
    }

    public async Task<Result<ClusterAnalysisResult>> AnalyzeFingerprintsAsync(
        IReadOnlyList<FailureFingerprint> fingerprints,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fingerprints);

        // Empty batch: return empty result.
        if (fingerprints.Count == 0)
        {
            return Result.Success(new ClusterAnalysisResult([], [], "ai", new Dictionary<string, long>()));
        }

        // For fingerprint-based analysis, delegate to AI service by reconstructing
        // message-like features. This is a bridge implementation for Phase 1.
        // Future improvements could pass fingerprints directly to the AI service.
        var refToMessageId = new Dictionary<string, long>();
        for (var i = 0; i < fingerprints.Count; i++)
        {
            refToMessageId[i.ToString()] = 0; // Placeholder; real message IDs not available from fingerprints
        }

        // For now, fall back to deterministic clustering when using fingerprints.
        // This ensures the fingerprint path is always available and functional.
        var deterministicStrategy = new DeterministicClusteringStrategy();
        return await deterministicStrategy.AnalyzeFingerprintsAsync(fingerprints, cancellationToken)
            .ConfigureAwait(false);
    }
}
