using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.DlqObserver;

/// <inheritdoc cref="IDlqObserverAttestationService"/>
public sealed class DlqObserverAttestationService : IDlqObserverAttestationService
{
    private readonly DlqDbContext _dbContext;

    /// <summary>Initialises a new instance of <see cref="DlqObserverAttestationService"/>.</summary>
    public DlqObserverAttestationService(DlqDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task<DlqObserverAttestation?> GetAsync(
        string ownerId, Guid namespaceId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.DlqObserverAttestations
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.OwnerId == ownerId && a.NamespaceId == namespaceId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsLiveAsync(
        string ownerId, Guid namespaceId, CancellationToken cancellationToken = default)
    {
        var attestation = await GetAsync(ownerId, namespaceId, cancellationToken);
        // Fail-closed: no row at all is indistinguishable from "never live" (ADR-004 item 4) —
        // never treated as an unconfigured-but-fine default.
        return attestation?.IsLiveAt(DateTimeOffset.UtcNow) ?? false;
    }

    /// <inheritdoc />
    public async Task<Result<DlqObserverAttestation>> ConfigureAsync(
        string ownerId, Guid namespaceId, bool enabled, string? observerReference, string? dlqEntityName,
        int stalenessBoundMinutes, CancellationToken cancellationToken = default)
    {
        if (stalenessBoundMinutes <= 0)
        {
            return Result<DlqObserverAttestation>.Failure(Error.Validation(
                "DlqObserverAttestation.StalenessBoundInvalid", "stalenessBoundMinutes must be positive."));
        }

        if (enabled && string.IsNullOrWhiteSpace(dlqEntityName))
        {
            return Result<DlqObserverAttestation>.Failure(Error.Validation(
                "DlqObserverAttestation.DlqEntityNameRequired",
                "dlqEntityName is required to enable attestation — the liveness canary needs a send target."));
        }

        var attestation = await _dbContext.DlqObserverAttestations
            .FirstOrDefaultAsync(a => a.OwnerId == ownerId && a.NamespaceId == namespaceId, cancellationToken);

        if (attestation is null)
        {
            attestation = new DlqObserverAttestation
            {
                OwnerId = ownerId,
                NamespaceId = namespaceId,
                StalenessBoundMinutes = stalenessBoundMinutes,
            };
            _dbContext.DlqObserverAttestations.Add(attestation);
        }

        attestation.Enabled = enabled;
        attestation.ObserverReference = observerReference;
        attestation.DlqEntityName = dlqEntityName;
        attestation.StalenessBoundMinutes = stalenessBoundMinutes;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<DlqObserverAttestation>.Success(attestation);
    }

    /// <inheritdoc />
    public async Task<Result<DlqObserverAttestation>> RecordCanarySentAsync(
        string ownerId, Guid namespaceId, string canaryMessageId, CancellationToken cancellationToken = default)
    {
        var attestation = await _dbContext.DlqObserverAttestations
            .FirstOrDefaultAsync(a => a.OwnerId == ownerId && a.NamespaceId == namespaceId, cancellationToken);

        if (attestation is null)
        {
            return Result<DlqObserverAttestation>.Failure(Error.NotFound(
                "DlqObserverAttestation.NotFound", "Attestation not configured for this namespace."));
        }

        attestation.LastCanarySentAt = DateTimeOffset.UtcNow;
        attestation.LastCanaryMessageId = canaryMessageId;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<DlqObserverAttestation>.Success(attestation);
    }

    /// <inheritdoc />
    public async Task<Result<DlqObserverAttestation>> RecordCanaryConfirmedAsync(
        string ownerId, Guid namespaceId, CancellationToken cancellationToken = default)
    {
        var attestation = await _dbContext.DlqObserverAttestations
            .FirstOrDefaultAsync(a => a.OwnerId == ownerId && a.NamespaceId == namespaceId, cancellationToken);

        if (attestation is null)
        {
            return Result<DlqObserverAttestation>.Failure(Error.NotFound(
                "DlqObserverAttestation.NotFound", "Attestation not configured for this namespace."));
        }

        attestation.LastConfirmedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<DlqObserverAttestation>.Success(attestation);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DlqObserverAttestation>> GetAllEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DlqObserverAttestations
            .AsNoTracking()
            .Where(a => a.Enabled)
            .ToListAsync(cancellationToken);
    }
}
