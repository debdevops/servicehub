using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Constants;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Routing;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Helpers;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.SignatureReplay;

/// <summary>
/// <inheritdoc cref="ISignatureReplayService"/>
/// </summary>
public sealed class SignatureReplayService : ISignatureReplayService
{
    private const int MaxSampleSize = 10;

    private readonly DlqDbContext _dbContext;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly CloudProviderRouter _router;
    private readonly IDlqSignatureAnalysisService _signatureAnalysisService;
    private readonly IDlqHistoryService _historyService;
    private readonly ISignatureReplayQueue _queue;
    private readonly ILogger<SignatureReplayService> _logger;

    /// <summary>Initialises a new instance of <see cref="SignatureReplayService"/>.</summary>
    public SignatureReplayService(
        DlqDbContext dbContext,
        INamespaceRepository namespaceRepository,
        CloudProviderRouter router,
        IDlqSignatureAnalysisService signatureAnalysisService,
        IDlqHistoryService historyService,
        ISignatureReplayQueue queue,
        ILogger<SignatureReplayService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _signatureAnalysisService = signatureAnalysisService ?? throw new ArgumentNullException(nameof(signatureAnalysisService));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result<BulkOperationPreviewResponse>> PreviewAsync(
        string ownerId, SignatureReplayPreviewRequest request, CancellationToken cancellationToken = default)
    {
        var nsResult = await ResolveOwnedNamespaceAsync(ownerId, request.Filter.NamespaceId, cancellationToken);
        if (nsResult.IsFailure)
            return Result.Failure<BulkOperationPreviewResponse>(nsResult.Error);

        var (warnings, canExecute) = EvaluateGuards(nsResult.Value);

        var messagesResult = await ResolveSignatureMessagesAsync(
            ownerId, request.Filter, cancellationToken);
        if (messagesResult.IsFailure)
            return Result.Failure<BulkOperationPreviewResponse>(messagesResult.Error);

        var matched = messagesResult.Value;
        if (matched.Count == 0)
        {
            warnings.Add("No DLQ messages match this signature and filter.");
            canExecute = false;
        }

        var sample = matched
            .OrderBy(m => m.DetectedAtUtc)
            .Take(MaxSampleSize)
            .Select(m => new BulkOperationPreviewItem(m.Id, m.MessageId, m.EntityName, m.DeadLetterReason, m.ReplaySafety))
            .ToList();

        var unsafeReplayCount = matched.Count(m => m.ReplaySafety == ReplaySafetyLevels.Unsafe);
        if (unsafeReplayCount > 0)
        {
            warnings.Add(
                $"{unsafeReplayCount} of the matched message(s) are flagged 'Unsafe' to replay — review the sample before proceeding.");
        }

        return new BulkOperationPreviewResponse(matched.Count, sample, canExecute, warnings, unsafeReplayCount);
    }

    /// <inheritdoc />
    public async Task<Result<BulkOperationJobResponse>> StartAsync(
        string ownerId, SignatureReplayStartRequest request, CancellationToken cancellationToken = default)
    {
        var nsResult = await ResolveOwnedNamespaceAsync(ownerId, request.Filter.NamespaceId, cancellationToken);
        if (nsResult.IsFailure)
            return Result.Failure<BulkOperationJobResponse>(nsResult.Error);

        var ns = nsResult.Value;
        var (warnings, canExecute) = EvaluateGuards(ns);
        if (!canExecute)
        {
            return Result.Failure<BulkOperationJobResponse>(Error.Validation(
                "SignatureReplay.NotAllowed", string.Join(' ', warnings)));
        }

        var messagesResult = await ResolveSignatureMessagesAsync(ownerId, request.Filter, cancellationToken);
        if (messagesResult.IsFailure)
            return Result.Failure<BulkOperationJobResponse>(messagesResult.Error);

        var matched = messagesResult.Value;
        if (matched.Count == 0)
        {
            return Result.Failure<BulkOperationJobResponse>(Error.Validation(
                "SignatureReplay.NoMatches", "No DLQ messages match this signature and filter."));
        }

        var job = new SignatureReplayJob
        {
            OwnerId = ownerId,
            NamespaceId = ns.Id,
            NamespaceDisplayName = ns.DisplayName ?? ns.Name,
            SignatureHash = request.Filter.SignatureHash,
            StatusFilter = request.Filter.Status,
            FromFilter = request.Filter.From,
            ToFilter = request.Filter.To,
            MessageIdsJson = JsonSerializer.Serialize(matched.Select(m => m.Id)),
            TotalMatched = matched.Count,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _dbContext.SignatureReplayJobs.Add(job);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _queue.Enqueue(job.Id);

        _logger.LogInformation(
            "Signature replay job {JobId} created for namespace {NamespaceId}, signature {SignatureHash}: {TotalMatched} message(s) matched",
            job.Id, job.NamespaceId, LogRedactor.SanitiseForLog(job.SignatureHash), job.TotalMatched);

        return ToResponse(job);
    }

    /// <inheritdoc />
    public async Task<Result<BulkOperationJobResponse>> GetJobAsync(
        string ownerId, Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _dbContext.SignatureReplayJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId && j.OwnerId == ownerId, cancellationToken);

        return job is null
            ? Result.Failure<BulkOperationJobResponse>(Error.NotFound(
                "SignatureReplay.NotFound", $"Signature replay job {jobId} was not found"))
            : ToResponse(job);
    }

    /// <inheritdoc />
    public async Task<Result<PaginatedResponse<BulkOperationJobResponse>>> ListJobsAsync(
        string ownerId, Guid namespaceId, string signatureHash, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _dbContext.SignatureReplayJobs.AsNoTracking().Where(j =>
            j.OwnerId == ownerId && j.NamespaceId == namespaceId && j.SignatureHash == signatureHash);

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
        var job = await _dbContext.SignatureReplayJobs
            .FirstOrDefaultAsync(j => j.Id == jobId && j.OwnerId == ownerId, cancellationToken);

        if (job is null)
        {
            return Result.Failure<BulkOperationJobResponse>(Error.NotFound(
                "SignatureReplay.NotFound", $"Signature replay job {jobId} was not found"));
        }

        // Idempotent: cancelling a terminal job is a no-op success, not an error — the caller
        // may race a "Cancel" click against the job finishing naturally.
        if (job.Status is BulkOperationStatus.Pending or BulkOperationStatus.Running)
        {
            job.CancellationRequestedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Signals the worker immediately if the job is already running; harmless no-op if
            // it's still Pending (the worker checks CancellationRequestedAt before starting).
            _queue.RequestCancellation(jobId);

            _logger.LogInformation("Cancellation requested for signature replay job {JobId}", jobId);
        }

        return ToResponse(job);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Result<Namespace>> ResolveOwnedNamespaceAsync(
        string ownerId, Guid namespaceId, CancellationToken cancellationToken)
    {
        var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        if (nsResult.IsFailure)
            return Result.Failure<Namespace>(nsResult.Error);

        var ns = nsResult.Value;

        // TENANT ISOLATION: return NotFound (not Forbidden) on owner mismatch, matching the
        // convention used by every other namespace-scoped controller/service in this codebase.
        if (!string.Equals(ns.OwnerId, ownerId, StringComparison.Ordinal))
        {
            return Result.Failure<Namespace>(Error.NotFound(
                "Namespace.NotFound", $"Namespace {namespaceId} was not found"));
        }

        return ns;
    }

    /// <summary>
    /// Same safety-by-default guards single-message replay and bulk replay already enforce
    /// (<c>MessagesController.ReplayMessage</c>, <c>BulkOperationService.EvaluateGuards</c>) —
    /// production block and Send permission.
    /// </summary>
    private (List<string> Warnings, bool CanExecute) EvaluateGuards(Namespace ns)
    {
        var warnings = new List<string>();
        var canExecute = true;

        if (ns.Environment == EnvironmentType.Prod)
        {
            warnings.Add("This namespace is Production — signature replay is blocked. Validate in DEV or UAT first.");
            canExecute = false;
        }

        if (!ns.HasSendPermission)
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

        return (warnings, canExecute);
    }

    /// <summary>
    /// Resolves a failure signature's current member messages — the same clustering + hash
    /// match <c>DlqHistoryController.GetSignatureDetail</c> performs — then applies the
    /// requested status/date filter. A signature no longer in the live cluster window (see
    /// <see cref="IDlqSignatureAnalysisService"/>) resolves to zero messages rather than an
    /// error, matching how the signature detail page already surfaces that state.
    /// </summary>
    private async Task<Result<IReadOnlyList<DlqMessage>>> ResolveSignatureMessagesAsync(
        string ownerId, SignatureReplayFilterRequest filter, CancellationToken cancellationToken)
    {
        var analysisResult = await _signatureAnalysisService.AnalyzeAsync(ownerId, filter.NamespaceId, cancellationToken);
        if (analysisResult.IsFailure)
            return Result.Failure<IReadOnlyList<DlqMessage>>(analysisResult.Error);

        var cluster = analysisResult.Value.Clusters.FirstOrDefault(c =>
            ClusterSignatureHasher.ComputeHash(c.TopTerms, c.DominantDeadletterReason) == filter.SignatureHash);

        if (cluster is null)
            return Result.Success<IReadOnlyList<DlqMessage>>([]);

        var messagesResult = await _historyService.GetByIdsAsync(ownerId, cluster.MessageIds, cancellationToken);
        if (messagesResult.IsFailure)
            return Result.Failure<IReadOnlyList<DlqMessage>>(messagesResult.Error);

        IEnumerable<DlqMessage> filtered = messagesResult.Value;
        if (filter.Status.HasValue)
            filtered = filtered.Where(m => m.Status == filter.Status.Value);
        if (filter.From.HasValue)
            filtered = filtered.Where(m => m.DetectedAtUtc >= filter.From.Value);
        if (filter.To.HasValue)
            filtered = filtered.Where(m => m.DetectedAtUtc <= filter.To.Value);

        return Result.Success<IReadOnlyList<DlqMessage>>(filtered.ToList());
    }

    private static BulkOperationJobResponse ToResponse(SignatureReplayJob job) => new(
        Id: job.Id,
        OperationType: "Replay",
        Status: job.Status.ToString(),
        NamespaceId: job.NamespaceId,
        NamespaceDisplayName: job.NamespaceDisplayName,
        EntityNameFilter: null,
        StatusFilter: job.StatusFilter?.ToString(),
        CategoryFilter: null,
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
            return JsonSerializer.Deserialize<List<BulkOperationFailureSample>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
