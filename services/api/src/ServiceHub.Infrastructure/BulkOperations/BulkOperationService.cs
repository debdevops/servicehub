using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Constants;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Routing;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.BulkOperations;

/// <summary>
/// <inheritdoc cref="IBulkOperationService"/>
/// </summary>
public sealed class BulkOperationService : IBulkOperationService
{
    private const int MaxSampleSize = 10;

    private readonly DlqDbContext _dbContext;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly CloudProviderRouter _router;
    private readonly IBulkOperationQueue _queue;
    private readonly ILogger<BulkOperationService> _logger;

    /// <summary>Initialises a new instance of <see cref="BulkOperationService"/>.</summary>
    public BulkOperationService(
        DlqDbContext dbContext,
        INamespaceRepository namespaceRepository,
        CloudProviderRouter router,
        IBulkOperationQueue queue,
        ILogger<BulkOperationService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result<BulkOperationPreviewResponse>> PreviewAsync(
        string ownerId, BulkOperationPreviewRequest request, IReadOnlySet<Guid>? allowedNamespaceIds = null,
        CancellationToken cancellationToken = default)
    {
        var nsResult = await ResolveOwnedNamespaceAsync(ownerId, request.Filter.NamespaceId, allowedNamespaceIds, cancellationToken);
        if (nsResult.IsFailure)
            return Result.Failure<BulkOperationPreviewResponse>(nsResult.Error);

        var ns = nsResult.Value;
        var (warnings, canExecute) = EvaluateGuards(ns, request.OperationType);

        var query = BuildMatchQuery(ownerId, request.Filter);
        var totalMatched = await query.CountAsync(cancellationToken);

        if (totalMatched == 0)
        {
            warnings.Add("No DLQ messages match this filter.");
            canExecute = false;
        }

        var sample = await query
            .OrderBy(m => m.DetectedAtUtc)
            .Take(MaxSampleSize)
            .Select(m => new BulkOperationPreviewItem(m.Id, m.MessageId, m.EntityName, m.DeadLetterReason, m.ReplaySafety))
            .ToListAsync(cancellationToken);

        var unsafeReplayCount = 0;
        if (request.OperationType == BulkOperationType.Replay)
        {
            unsafeReplayCount = await query.CountAsync(m => m.ReplaySafety == ReplaySafetyLevels.Unsafe, cancellationToken);
            if (unsafeReplayCount > 0)
            {
                warnings.Add(
                    $"{unsafeReplayCount} of the matched message(s) are flagged 'Unsafe' to replay — review the sample before proceeding.");
            }
        }

        return new BulkOperationPreviewResponse(totalMatched, sample, canExecute, warnings, unsafeReplayCount);
    }

    /// <inheritdoc />
    public async Task<Result<BulkOperationJobResponse>> CreateJobAsync(
        string ownerId, BulkOperationCreateRequest request, string? correlationId,
        RecoveryActor requestedBy,
        IReadOnlySet<Guid>? allowedNamespaceIds = null,
        CancellationToken cancellationToken = default)
    {
        var nsResult = await ResolveOwnedNamespaceAsync(ownerId, request.Filter.NamespaceId, allowedNamespaceIds, cancellationToken);
        if (nsResult.IsFailure)
            return Result.Failure<BulkOperationJobResponse>(nsResult.Error);

        var ns = nsResult.Value;
        var (warnings, canExecute) = EvaluateGuards(ns, request.OperationType);
        if (!canExecute)
        {
            return Result.Failure<BulkOperationJobResponse>(Error.Validation(
                "BulkOperation.NotAllowed", string.Join(' ', warnings)));
        }

        var query = BuildMatchQuery(ownerId, request.Filter);
        var totalMatched = await query.CountAsync(cancellationToken);
        if (totalMatched == 0)
        {
            return Result.Failure<BulkOperationJobResponse>(Error.Validation(
                "BulkOperation.NoMatches", "No DLQ messages match this filter."));
        }

        var job = new BulkOperationJob
        {
            OwnerId = ownerId,
            OperationType = request.OperationType,
            Status = BulkOperationStatus.Pending,
            NamespaceId = ns.Id,
            NamespaceDisplayName = ns.DisplayName ?? ns.Name,
            EntityNameFilter = request.Filter.EntityName,
            StatusFilter = request.Filter.Status,
            CategoryFilter = request.Filter.Category,
            FromFilter = request.Filter.From,
            ToFilter = request.Filter.To,
            TotalMatched = totalMatched,
            CreatedAt = DateTimeOffset.UtcNow,
            CorrelationId = correlationId,
            RequestedByIdentity = requestedBy.Identity,
            RequestedByActorKind = requestedBy.Kind,
            RequestedByScopes = requestedBy.Scopes,
        };

        _dbContext.BulkOperationJobs.Add(job);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _queue.Enqueue(job.Id);

        _logger.LogInformation(
            "Bulk {OperationType} job {JobId} created for namespace {NamespaceId}: {TotalMatched} message(s) matched",
            job.OperationType, job.Id, job.NamespaceId, job.TotalMatched);

        return ToResponse(job);
    }

    /// <inheritdoc />
    public async Task<Result<BulkOperationJobResponse>> GetJobAsync(
        string ownerId, Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _dbContext.BulkOperationJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId && j.OwnerId == ownerId, cancellationToken);

        return job is null
            ? Result.Failure<BulkOperationJobResponse>(Error.NotFound(
                "BulkOperation.NotFound", $"Bulk operation job {jobId} was not found"))
            : ToResponse(job);
    }

    /// <inheritdoc />
    public async Task<Result<PaginatedResponse<BulkOperationJobResponse>>> ListJobsAsync(
        string ownerId, Guid? namespaceId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _dbContext.BulkOperationJobs.AsNoTracking().Where(j => j.OwnerId == ownerId);
        if (namespaceId.HasValue)
            query = query.Where(j => j.NamespaceId == namespaceId.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedResponse<BulkOperationJobResponse>(
            Items: items.Select(ToResponse).ToList(),
            TotalCount: totalCount,
            Page: page,
            PageSize: pageSize,
            HasNextPage: page * pageSize < totalCount,
            HasPreviousPage: page > 1);
    }

    /// <inheritdoc />
    public async Task<Result<BulkOperationJobResponse>> CancelJobAsync(
        string ownerId, Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _dbContext.BulkOperationJobs
            .FirstOrDefaultAsync(j => j.Id == jobId && j.OwnerId == ownerId, cancellationToken);

        if (job is null)
            return Result.Failure<BulkOperationJobResponse>(Error.NotFound(
                "BulkOperation.NotFound", $"Bulk operation job {jobId} was not found"));

        // Idempotent: cancelling a terminal job is a no-op success, not an error — the caller
        // may race a "Cancel" click against the job finishing naturally.
        if (job.Status is BulkOperationStatus.Pending or BulkOperationStatus.Running)
        {
            job.CancellationRequestedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Signals the worker immediately if the job is already running; harmless no-op if
            // it's still Pending (the worker checks CancellationRequestedAt before starting).
            _queue.RequestCancellation(jobId);

            _logger.LogInformation("Cancellation requested for bulk operation job {JobId}", jobId);
        }

        return ToResponse(job);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Result<Namespace>> ResolveOwnedNamespaceAsync(
        string ownerId, Guid namespaceId, IReadOnlySet<Guid>? allowedNamespaceIds, CancellationToken cancellationToken)
    {
        var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        if (nsResult.IsFailure)
            return Result.Failure<Namespace>(nsResult.Error);

        var ns = nsResult.Value;

        // TENANT ISOLATION: return NotFound (not Forbidden) on owner mismatch, matching the
        // convention used by every other namespace-scoped controller in this codebase. Also
        // rejects namespaces outside the caller's allow-list — without this, a key restricted to
        // one namespace could still bulk replay/purge another namespace it truly owns.
        if (!string.Equals(ns.OwnerId, ownerId, StringComparison.Ordinal)
            || (allowedNamespaceIds is not null && !allowedNamespaceIds.Contains(namespaceId)))
        {
            return Result.Failure<Namespace>(Error.NotFound(
                "Namespace.NotFound", $"Namespace {namespaceId} was not found"));
        }

        return ns;
    }

    /// <summary>
    /// Same safety-by-default guards single-message replay/purge already enforce
    /// (<c>MessagesController.ReplayMessage</c>/<c>PurgeMessage</c>), evaluated once for the
    /// whole job rather than per message — production block, Send permission for replay, and
    /// provider capability for purge.
    /// </summary>
    private (List<string> Warnings, bool CanExecute) EvaluateGuards(Namespace ns, BulkOperationType operationType)
    {
        var warnings = new List<string>();
        var canExecute = true;

        if (ns.Environment == Core.Enums.EnvironmentType.Prod)
        {
            warnings.Add("This namespace is Production — bulk operations are blocked. Validate in DEV or UAT first.");
            canExecute = false;
        }

        if (operationType == BulkOperationType.Replay && !ns.HasSendPermission)
        {
            warnings.Add(
                "The configured connection string lacks Send permission, which replay requires to move messages back to the main queue.");
            canExecute = false;
        }

        if (!_router.IsRegistered(ns.Provider))
        {
            warnings.Add($"No provider is registered for '{ns.Provider}' on this server.");
            canExecute = false;
        }
        else if (operationType == BulkOperationType.Purge)
        {
            var capabilities = _router.Resolve(ns.Provider).Capabilities;
            if (!capabilities.SupportsPurge)
            {
                warnings.Add($"{ns.Provider} does not support purge — {capabilities.Notes}");
                canExecute = false;
            }
        }

        return (warnings, canExecute);
    }

    private IQueryable<DlqMessage> BuildMatchQuery(string ownerId, BulkOperationFilterRequest filter) =>
        BulkOperationMatching.BuildQuery(
            _dbContext, ownerId, filter.NamespaceId, filter.EntityName,
            filter.Status, filter.Category, filter.From, filter.To);

    private static BulkOperationJobResponse ToResponse(BulkOperationJob job) => new(
        Id: job.Id,
        OperationType: job.OperationType.ToString(),
        Status: job.Status.ToString(),
        NamespaceId: job.NamespaceId,
        NamespaceDisplayName: job.NamespaceDisplayName,
        EntityNameFilter: job.EntityNameFilter,
        StatusFilter: job.StatusFilter?.ToString(),
        CategoryFilter: job.CategoryFilter?.ToString(),
        From: job.FromFilter,
        To: job.ToFilter,
        TotalMatched: job.TotalMatched,
        ProcessedCount: job.ProcessedCount,
        SuccessCount: job.SuccessCount,
        FailureCount: job.FailureCount,
        SkippedCount: job.SkippedCount,
        FailureSample: DeserializeFailureSample(job.FailureSampleJson),
        ErrorSummary: job.ErrorSummary,
        CreatedAt: job.CreatedAt,
        StartedAt: job.StartedAt,
        CompletedAt: job.CompletedAt,
        IsCancellable: job.Status is BulkOperationStatus.Pending or BulkOperationStatus.Running);

    private static IReadOnlyList<BulkOperationFailureSample>? DeserializeFailureSample(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<BulkOperationFailureSample>>(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
