using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure;

/// <summary>
/// EF Core-backed implementation of <see cref="INamespaceSignatureLookupService"/>.
/// </summary>
public sealed class NamespaceSignatureLookupService : INamespaceSignatureLookupService
{
    private readonly DlqDbContext _dbContext;

    public NamespaceSignatureLookupService(DlqDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, SignatureLookupResult>> LookupAndRecordAsync(
        string ownerId,
        Guid namespaceId,
        IReadOnlyList<ClusterSignatureObservation> observations,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);
        ArgumentNullException.ThrowIfNull(observations);

        var results = new Dictionary<string, SignatureLookupResult>();
        if (observations.Count == 0)
            return results;

        // De-duplicate by hash so a batch with repeated hashes only upserts once per hash.
        var distinctObservations = observations
            .GroupBy(o => o.SignatureHash)
            .Select(g => g.First())
            .ToList();

        var hashes = distinctObservations.Select(o => o.SignatureHash).ToList();

        var existing = await _dbContext.NamespaceSignatures
            .Where(s => s.OwnerId == ownerId && s.NamespaceId == namespaceId && hashes.Contains(s.SignatureHash))
            .ToDictionaryAsync(s => s.SignatureHash, cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;

        foreach (var observation in distinctObservations)
        {
            if (existing.TryGetValue(observation.SignatureHash, out var signature))
            {
                signature.OccurrenceCount++;
                signature.LastSeenAt = now;

                results[observation.SignatureHash] = new SignatureLookupResult(
                    IsNew: false,
                    FirstSeenAt: signature.FirstSeenAt,
                    OccurrenceCount: signature.OccurrenceCount);
            }
            else
            {
                var newSignature = new NamespaceSignature
                {
                    NamespaceId = namespaceId,
                    OwnerId = ownerId,
                    SignatureHash = observation.SignatureHash,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                    OccurrenceCount = 1,
                    DominantDeadletterReason = observation.DominantDeadletterReason,
                    TopTermsJson = JsonSerializer.Serialize(observation.TopTerms),
                };

                _dbContext.NamespaceSignatures.Add(newSignature);

                results[observation.SignatureHash] = new SignatureLookupResult(
                    IsNew: true,
                    FirstSeenAt: now,
                    OccurrenceCount: 1);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return results;
    }
}
