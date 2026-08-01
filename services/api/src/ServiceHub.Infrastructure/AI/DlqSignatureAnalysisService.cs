using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Helpers;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// <see cref="IDlqSignatureAnalysisService"/> implementation using a strategy-based
/// architecture for graceful fallback when AI clustering is unavailable.
///
/// Analysis flow:
/// 1. Export active DLQ messages for the namespace
/// 2. Try to cluster using the AI strategy (requires AI service availability)
/// 3. If AI fails or is unavailable, fall back to deterministic strategy
/// 4. Both strategies produce the same <see cref="ClusterAnalysisResult"/> contract
/// 5. Persist signature history (regardless of which strategy was used)
/// 6. Render explanations with confidence levels (hiding which strategy was used)
///
/// This design ensures the feature always works: when AI is unavailable, deterministic
/// clustering provides meaningful results automatically.
/// </summary>
public sealed class DlqSignatureAnalysisService : IDlqSignatureAnalysisService
{
    private readonly IDlqHistoryService _historyService;
    private readonly AIClusteringStrategy _aiStrategy;
    private readonly DeterministicClusteringStrategy _deterministicStrategy;
    private readonly INamespaceSignatureLookupService _signatureLookupService;
    private readonly IFailureFeatureExtractor _featureExtractor;
    private readonly IFailureFingerprintBuilder _fingerprintBuilder;
    private readonly IFailureSignatureRecognitionService _signatureRecognition;
    private readonly ILogger<DlqSignatureAnalysisService> _logger;

    public DlqSignatureAnalysisService(
        IDlqHistoryService historyService,
        AIClusteringStrategy aiStrategy,
        DeterministicClusteringStrategy deterministicStrategy,
        INamespaceSignatureLookupService signatureLookupService,
        IFailureFeatureExtractor featureExtractor,
        IFailureFingerprintBuilder fingerprintBuilder,
        IFailureSignatureRecognitionService signatureRecognition,
        ILogger<DlqSignatureAnalysisService> logger)
    {
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _aiStrategy = aiStrategy ?? throw new ArgumentNullException(nameof(aiStrategy));
        _deterministicStrategy = deterministicStrategy ?? throw new ArgumentNullException(nameof(deterministicStrategy));
        _signatureLookupService = signatureLookupService ?? throw new ArgumentNullException(nameof(signatureLookupService));
        _featureExtractor = featureExtractor ?? throw new ArgumentNullException(nameof(featureExtractor));
        _fingerprintBuilder = fingerprintBuilder ?? throw new ArgumentNullException(nameof(fingerprintBuilder));
        _signatureRecognition = signatureRecognition ?? throw new ArgumentNullException(nameof(signatureRecognition));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<Result<DlqSignatureAnalysisResult>> AnalyzeAsync(
        string ownerId,
        Guid namespaceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);

        var exportResult = await _historyService.ExportAsync(
            ownerId,
            namespaceId,
            status: DlqMessageStatus.Active,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (exportResult.IsFailure)
        {
            return Result.Failure<DlqSignatureAnalysisResult>(exportResult.Error);
        }

        var messages = exportResult.Value;
        if (messages.Count == 0)
        {
            return Result.Success(new DlqSignatureAnalysisResult(
                Available: true,
                Method: null,
                BatchSize: 0,
                Clusters: [],
                Singletons: []));
        }

        // Extract features from all messages (strategy-independent layer).
        var extractResult = await _featureExtractor.ExtractBatchAsync(messages, cancellationToken)
            .ConfigureAwait(false);

        if (extractResult.IsFailure)
        {
            _logger.LogWarning(
                "Feature extraction failed for namespace {NamespaceId}; cannot proceed with signature analysis",
                namespaceId);
            return Result.Failure<DlqSignatureAnalysisResult>(extractResult.Error);
        }

        var features = extractResult.Value;

        // Build fingerprints from extracted features (deterministic, strategy-independent).
        var fingerprintResult = await _fingerprintBuilder.ComputeBatchAsync(features, cancellationToken)
            .ConfigureAwait(false);

        if (fingerprintResult.IsFailure)
        {
            _logger.LogWarning(
                "Fingerprint computation failed for namespace {NamespaceId}; cannot proceed with signature analysis",
                namespaceId);
            return Result.Failure<DlqSignatureAnalysisResult>(fingerprintResult.Error);
        }

        var fingerprints = fingerprintResult.Value;

        // Recognize or create signatures from fingerprints (Phase 2: business-level domain model).
        // This happens before clustering to establish signature identity early.
        var signatureResult = await _signatureRecognition.RecognizeBatchAsync(
            ownerId, namespaceId, fingerprints, cancellationToken)
            .ConfigureAwait(false);

        if (signatureResult.IsFailure)
        {
            _logger.LogWarning(
                "Signature recognition failed for namespace {NamespaceId}; cannot proceed with signature analysis",
                namespaceId);
            return Result.Failure<DlqSignatureAnalysisResult>(signatureResult.Error);
        }

        var signatures = signatureResult.Value;

        // Try AI clustering first; fall back to deterministic if it fails.
        // (Clustering now serves as secondary grouping for UI visualization)
        var analyzeResult = await _aiStrategy.AnalyzeAsync(messages, cancellationToken)
            .ConfigureAwait(false);

        if (analyzeResult.IsFailure)
        {
            _logger.LogInformation(
                "AI clustering unavailable for namespace {NamespaceId}; falling back to deterministic strategy",
                namespaceId);

            analyzeResult = await _deterministicStrategy.AnalyzeAsync(messages, cancellationToken)
                .ConfigureAwait(false);
        }

        if (analyzeResult.IsFailure)
        {
            // Both strategies failed; this should not happen (deterministic has no external dependencies).
            _logger.LogWarning(
                "Both AI and deterministic clustering failed for namespace {NamespaceId}",
                namespaceId);

            return Result.Success(new DlqSignatureAnalysisResult(
                Available: false,
                Method: null,
                BatchSize: messages.Count,
                Clusters: [],
                Singletons: []));
        }

        var analysis = analyzeResult.Value;
        var messagesById = messages.ToDictionary(m => m.Id);

        var observations = analysis.Clusters
            .Select(c => new ClusterSignatureObservation(
                SignatureHash: ClusterSignatureHasher.ComputeHash(c.TopTerms, c.DominantDeadletterReason),
                DominantDeadletterReason: c.DominantDeadletterReason,
                TopTerms: c.TopTerms))
            .ToList();

        var lookupResults = await _signatureLookupService.LookupAndRecordAsync(
            ownerId, namespaceId, observations, cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var clusters = new List<DlqClusterSignature>(analysis.Clusters.Count);

        // Determine confidence level based on which strategy was used.
        // AI → high confidence; Deterministic → medium confidence.
        var isAIResult = string.Equals(analysis.Method, "clustered", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(analysis.Method, "grouped", StringComparison.OrdinalIgnoreCase);
        var confidence = isAIResult ? Confidence.High : Confidence.Medium;

        for (var i = 0; i < analysis.Clusters.Count; i++)
        {
            var cluster = analysis.Clusters[i];
            var signature = lookupResults[observations[i].SignatureHash];

            var messageIds = cluster.MemberRefs
                .Select(r => analysis.RefToMessageId[r])
                .ToList();

            var (windowStart, windowEnd) = ResolveWindow(cluster, analysis.RefToMessageId, messagesById);

            var metadata = new ClusterExplanationRenderer.ClusterMetadata(
                Size: cluster.Size,
                BatchSize: messages.Count,
                DominantEntity: cluster.DominantEntity,
                DominantDeadletterReason: cluster.DominantDeadletterReason,
                DominantDeadletterReasonCount: cluster.DominantDeadletterReasonCount,
                TopTerms: cluster.TopTerms,
                Confidence: confidence);

            var explanation = ClusterExplanationRenderer.Render(metadata, signature, now);

            clusters.Add(new DlqClusterSignature(
                Size: cluster.Size,
                MessageIds: messageIds,
                DominantEntity: cluster.DominantEntity,
                DominantDeadletterReason: cluster.DominantDeadletterReason,
                DominantDeadletterReasonCount: cluster.DominantDeadletterReasonCount,
                TopTerms: cluster.TopTerms,
                IsNew: signature.IsNew,
                FirstSeenAt: signature.FirstSeenAt,
                OccurrenceCount: signature.OccurrenceCount,
                WindowStart: windowStart,
                WindowEnd: windowEnd,
                Explanation: explanation));
        }

        var singletons = analysis.Singletons
            .Select(s => new DlqSingletonSignature(
                MessageId: analysis.RefToMessageId[s.Ref],
                DominantEntity: s.DominantEntity,
                DominantDeadletterReason: s.DominantDeadletterReason))
            .ToList();

        return Result.Success(new DlqSignatureAnalysisResult(
            Available: true,
            Method: analysis.Method,
            BatchSize: messages.Count,
            Clusters: clusters,
            Singletons: singletons));
    }

    /// <summary>
    /// Resolves a cluster's time window from the real messages its <c>FirstOccurrenceRef</c>/
    /// <c>LastOccurrenceRef</c> point to, since those refs are opaque ordering tokens and not
    /// guaranteed to be chronological.
    /// </summary>
    private static (DateTimeOffset Start, DateTimeOffset End) ResolveWindow(
        ClusterSummary cluster,
        IReadOnlyDictionary<string, long> refToMessageId,
        IReadOnlyDictionary<long, DlqMessage> messagesById)
    {
        var firstMessage = messagesById[refToMessageId[cluster.FirstOccurrenceRef]];
        var lastMessage = messagesById[refToMessageId[cluster.LastOccurrenceRef]];

        var firstTime = OccurredAt(firstMessage);
        var lastTime = OccurredAt(lastMessage);

        return firstTime <= lastTime ? (firstTime, lastTime) : (lastTime, firstTime);
    }

    private static DateTimeOffset OccurredAt(DlqMessage message) => message.DeadLetterTimeUtc ?? message.DetectedAtUtc;
}
