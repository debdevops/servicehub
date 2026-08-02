using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Helpers;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// Recognizes and tracks recurring Failure Signatures.
///
/// When fingerprints are observed, this service:
/// 1. Computes a stable hash of the fingerprint
/// 2. Looks up existing signatures by hash
/// 3. Creates new signatures on first occurrence
/// 4. Updates occurrence count and last-seen on subsequent occurrences
///
/// Recognition is deterministic: same fingerprint always maps to the same signature.
/// </summary>
public sealed class FailureSignatureRecognitionService : IFailureSignatureRecognitionService
{
    private readonly INamespaceSignatureLookupService _lookupService;
    private readonly ILogger<FailureSignatureRecognitionService> _logger;

    public FailureSignatureRecognitionService(
        INamespaceSignatureLookupService lookupService,
        ILogger<FailureSignatureRecognitionService> logger)
    {
        _lookupService = lookupService ?? throw new ArgumentNullException(nameof(lookupService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<FailureSignature>> RecognizeAsync(
        string ownerId,
        Guid namespaceId,
        FailureFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);
        ArgumentNullException.ThrowIfNull(fingerprint);

        var result = await RecognizeBatchAsync(ownerId, namespaceId, [fingerprint], cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result.Failure<FailureSignature>(result.Error);
        }

        return Result.Success(result.Value[0]);
    }

    public async Task<Result<IReadOnlyList<FailureSignature>>> RecognizeBatchAsync(
        string ownerId,
        Guid namespaceId,
        IReadOnlyList<FailureFingerprint> fingerprints,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);
        ArgumentNullException.ThrowIfNull(fingerprints);

        if (fingerprints.Count == 0)
        {
            return Result.Success((IReadOnlyList<FailureSignature>)new List<FailureSignature>());
        }

        // Build observations for lookup (using fingerprint hash as signature hash).
        var observations = fingerprints.Select(fp => new ClusterSignatureObservation(
            SignatureHash: fp.Hash,
            DominantDeadletterReason: fp.Features.DeadLetterReason,
            TopTerms: fp.TopTerms.ToList())).ToList();

        // Look up and record signatures.
        var lookupResult = await _lookupService.LookupAndRecordAsync(
            ownerId, namespaceId, observations, cancellationToken)
            .ConfigureAwait(false);

        // Transform to FailureSignature domain objects.
        var signatures = new List<FailureSignature>(fingerprints.Count);

        for (var i = 0; i < fingerprints.Count; i++)
        {
            var fingerprint = fingerprints[i];
            var lookupData = lookupResult[fingerprint.Hash];

            var displayName = BuildDisplayName(fingerprint);
            var status = ComputeStatus(lookupData.IsNew);
            var confidence = fingerprint.Confidence;

            var signature = new FailureSignature(
                Id: fingerprint.Hash.GetHashCode() & 0x7FFFFFFF, // Positive hash code as ID
                Fingerprint: fingerprint,
                DisplayName: displayName,
                FirstSeen: lookupData.FirstSeenAt,
                LastSeen: lookupData.FirstSeenAt, // Updated by lookup service, use same for consistency
                OccurrenceCount: lookupData.OccurrenceCount,
                Provider: fingerprint.Features.Provider,
                FailureCategory: fingerprint.Features.FailureCategory,
                Confidence: confidence,
                Status: status);

            signatures.Add(signature);
        }

        return Result.Success((IReadOnlyList<FailureSignature>)signatures);
    }

    /// <summary>
    /// Build a human-readable display name from fingerprint characteristics.
    /// Format: "{Provider}: {Category} — {DeadLetterReason}"
    /// </summary>
    private static string BuildDisplayName(FailureFingerprint fingerprint)
    {
        var provider = fingerprint.Features.Provider.ToString();
        var category = fingerprint.Features.FailureCategory ?? "Unknown";
        var reason = fingerprint.Features.DeadLetterReason;

        return $"{provider}: {category} — {reason}";
    }

    /// <summary>
    /// Compute initial status: new failures are Active, recurring are already Active.
    /// </summary>
    private static SignatureStatus ComputeStatus(bool isNew)
    {
        return SignatureStatus.Active;
    }
}
