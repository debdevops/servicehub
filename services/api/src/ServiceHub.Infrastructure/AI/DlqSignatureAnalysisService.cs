using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Helpers;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// <see cref="IDlqSignatureAnalysisService"/> implementation. Lives in
/// <c>ServiceHub.Infrastructure</c> (rather than a separate assembly) specifically so it can
/// call the <see langword="internal"/> <see cref="ClusterExplanationRenderer"/> directly instead
/// of duplicating its logic.
/// </summary>
public sealed class DlqSignatureAnalysisService : IDlqSignatureAnalysisService
{
    private readonly IDlqHistoryService _historyService;
    private readonly IAIServiceClient _aiServiceClient;
    private readonly INamespaceSignatureLookupService _signatureLookupService;

    public DlqSignatureAnalysisService(
        IDlqHistoryService historyService,
        IAIServiceClient aiServiceClient,
        INamespaceSignatureLookupService signatureLookupService)
    {
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _aiServiceClient = aiServiceClient ?? throw new ArgumentNullException(nameof(aiServiceClient));
        _signatureLookupService = signatureLookupService ?? throw new ArgumentNullException(nameof(signatureLookupService));
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

        var analyzeResult = await _aiServiceClient.AnalyzeMessagesAsync(messages, cancellationToken).ConfigureAwait(false);
        if (analyzeResult.IsFailure)
        {
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
                TopTerms: cluster.TopTerms);

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
