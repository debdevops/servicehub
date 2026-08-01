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
}
