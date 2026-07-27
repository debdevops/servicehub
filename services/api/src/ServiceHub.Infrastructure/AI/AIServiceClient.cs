using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// HTTP client for the ServiceHub AI service.
/// <para>
/// <see cref="IsAvailableAsync"/> calls <c>GET /health</c> and <see cref="AnalyzeMessagesAsync"/>
/// calls <c>POST /analyze</c>; the other methods' contracts aren't exposed by the AI service yet
/// and continue to report "not yet implemented" exactly as the previous stub did. ServiceHub
/// makes no external AI API calls of any kind: the AI service is a local, self-hosted container
/// the operator controls, reached only when <see cref="AIServiceOptions.Enabled"/> is explicitly set.
/// </para>
/// <para>
/// Every failure path — unreachable host, timeout, malformed response, non-2xx status —
/// degrades to the same "not available" result. This type never throws into a caller.
/// </para>
/// </summary>
public sealed class AIServiceClient : IAIServiceClient
{
    /// <summary>Name of the named <see cref="HttpClient"/> registered for this client.</summary>
    public const string HttpClientName = "AIService";

    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions HealthResponseSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AIServiceOptions _options;
    private readonly ILogger<AIServiceClient> _logger;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private DateTimeOffset _cacheExpiresAt = DateTimeOffset.MinValue;
    private bool _cachedAvailable;
    private int _unavailabilityLogged;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIServiceClient"/> class.
    /// </summary>
    public AIServiceClient(
        IHttpClientFactory httpClientFactory,
        IOptions<AIServiceOptions> options,
        ILogger<AIServiceClient> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<Result<ClusterAnalysisResult>> AnalyzeMessagesAsync(
        IReadOnlyList<DlqMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ServiceUrl))
        {
            return Result.Failure<ClusterAnalysisResult>(NotAvailableError());
        }

        var refToMessageId = new Dictionary<string, long>(messages.Count);
        var records = new List<FeatureRecordDto>(messages.Count);
        for (var i = 0; i < messages.Count; i++)
        {
            var refKey = i.ToString(CultureInfo.InvariantCulture);
            refToMessageId[refKey] = messages[i].Id;
            records.Add(ToFeatureRecordDto(refKey, SignalExtractor.ExtractFeatures(messages[i])));
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(
                "analyze",
                new AnalyzeRequestDto(records),
                HealthResponseSerializerOptions,
                timeoutCts.Token).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var reason = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                _logger.LogWarning(
                    "AI service rejected the analyze request (422): {Reason}",
                    LogRedactor.SanitiseForLog(reason));
                return Result.Failure<ClusterAnalysisResult>(NotAvailableError());
            }

            if (!response.IsSuccessStatusCode)
            {
                LogUnavailabilityOnce();
                return Result.Failure<ClusterAnalysisResult>(NotAvailableError());
            }

            var analyzeResponse = await response.Content
                .ReadFromJsonAsync<AnalyzeResponseDto>(HealthResponseSerializerOptions, timeoutCts.Token)
                .ConfigureAwait(false);

            if (analyzeResponse?.Clusters is null || analyzeResponse.Singletons is null || analyzeResponse.Method is null)
            {
                LogUnavailabilityOnce();
                return Result.Failure<ClusterAnalysisResult>(NotAvailableError());
            }

            var result = new ClusterAnalysisResult(
                analyzeResponse.Clusters.Select(ToClusterSummary).ToList(),
                analyzeResponse.Singletons.Select(ToClusterSingleton).ToList(),
                analyzeResponse.Method,
                refToMessageId);

            return Result.Success(result);
        }
        catch (OperationCanceledException)
        {
            // Covers both caller cancellation and the configured timeout.
            LogUnavailabilityOnce();
            return Result.Failure<ClusterAnalysisResult>(NotAvailableError());
        }
        catch (HttpRequestException)
        {
            LogUnavailabilityOnce();
            return Result.Failure<ClusterAnalysisResult>(NotAvailableError());
        }
        catch (JsonException)
        {
            LogUnavailabilityOnce();
            return Result.Failure<ClusterAnalysisResult>(NotAvailableError());
        }
    }

    /// <inheritdoc/>
    public Task<Result<string>> GetMessageInsightsAsync(
        Message message,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("AI service is not yet implemented. GetMessageInsightsAsync called for message {MessageId}", message?.MessageId);

        return Task.FromResult(Result.Failure<string>(Error.Internal(
            ErrorCodes.General.ServiceUnavailable,
            "AI message insights service is not yet implemented. This feature will be available in a future release.")));
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Result.Success(false);
        }

        if (DateTimeOffset.UtcNow < _cacheExpiresAt)
        {
            return Result.Success(_cachedAvailable);
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (DateTimeOffset.UtcNow < _cacheExpiresAt)
            {
                return Result.Success(_cachedAvailable);
            }

            var available = await CheckHealthAsync(cancellationToken).ConfigureAwait(false);

            _cachedAvailable = available;
            _cacheExpiresAt = DateTimeOffset.UtcNow.Add(CacheDuration);

            if (!available)
            {
                LogUnavailabilityOnce();
            }

            return Result.Success(available);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Anomaly>>> DetectAnomaliesAsync(
        Guid namespaceId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "AI service is not yet implemented. DetectAnomaliesAsync called for namespace {NamespaceId} from {StartTime} to {EndTime}",
            namespaceId,
            startTime,
            endTime);

        return Task.FromResult(Result.Failure<IReadOnlyList<Anomaly>>(Error.Internal(
            ErrorCodes.General.ServiceUnavailable,
            "AI anomaly detection service is not yet implemented. This feature will be available in a future release.")));
    }

    /// <inheritdoc/>
    public Task<Result<Anomaly>> GetAnomalyByIdAsync(
        Guid anomalyId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("AI service is not yet implemented. GetAnomalyByIdAsync called for anomaly {AnomalyId}", anomalyId);

        return Task.FromResult(Result.Failure<Anomaly>(Error.Internal(
            ErrorCodes.General.ServiceUnavailable,
            "AI anomaly detection service is not yet implemented. This feature will be available in a future release.")));
    }

    private async Task<bool> CheckHealthAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ServiceUrl))
        {
            return false;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(HealthCheckTimeout);

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync("health", timeoutCts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var health = await response.Content
                .ReadFromJsonAsync<HealthResponse>(HealthResponseSerializerOptions, timeoutCts.Token)
                .ConfigureAwait(false);

            return health?.Ready == true;
        }
        catch (OperationCanceledException)
        {
            // Covers both caller cancellation and the 2s health-check timeout.
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Logs the "AI service is unavailable" warning at most once per process, per
    /// <see cref="_unavailabilityLogged"/>. Shared by <see cref="IsAvailableAsync"/> and
    /// <see cref="AnalyzeMessagesAsync"/> so the guard covers both entry points.
    /// </summary>
    private void LogUnavailabilityOnce()
    {
        if (Interlocked.Exchange(ref _unavailabilityLogged, 1) == 0)
        {
            _logger.LogWarning(
                "AI service is unavailable — ServiceHub will continue operating without AI-powered features until it recovers.");
        }
    }

    private static Error NotAvailableError() => Error.Internal(
        ErrorCodes.General.ServiceUnavailable,
        "AI cluster analysis service is currently unavailable.");

    private static FeatureRecordDto ToFeatureRecordDto(string refKey, MessageFeatures features) => new(
        Ref: refKey,
        DeliveryCount: features.DeliveryCount,
        BodySizeBytes: features.BodySizeBytes,
        TimeToDeadletterSeconds: features.TimeToDeadletterSeconds,
        SecondsSinceEnqueued: features.SecondsSinceEnqueued,
        HourOfDay: features.HourOfDay,
        DayOfWeek: features.DayOfWeek,
        PropertyCount: features.PropertyCount,
        Provider: features.Provider.ToString(),
        EntityName: features.EntityName,
        DeadletterReason: features.DeadletterReason,
        ExceptionType: features.ExceptionType,
        ContentType: features.ContentType,
        PayloadShape: features.PayloadShape,
        ErrorTextNormalised: features.ErrorTextNormalised,
        SchemaFingerprint: features.SchemaFingerprint,
        FeatureVersion: features.FeatureVersion);

    private static ClusterSummary ToClusterSummary(ClusterDto dto) => new(
        dto.Size,
        dto.RepresentativeRef,
        dto.TopTerms ?? [],
        dto.FirstOccurrenceRef,
        dto.LastOccurrenceRef,
        dto.DominantEntity,
        dto.DominantDeadletterReason);

    private static ClusterSingleton ToClusterSingleton(SingletonDto dto) => new(
        dto.Ref,
        dto.DominantEntity,
        dto.DominantDeadletterReason);

    private sealed record HealthResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("ready")] bool Ready);

    private sealed record AnalyzeRequestDto(
        [property: JsonPropertyName("records")] IReadOnlyList<FeatureRecordDto> Records);

    private sealed record FeatureRecordDto(
        [property: JsonPropertyName("ref")] string Ref,
        [property: JsonPropertyName("delivery_count")] int DeliveryCount,
        [property: JsonPropertyName("body_size_bytes")] long BodySizeBytes,
        [property: JsonPropertyName("time_to_deadletter_seconds")] double TimeToDeadletterSeconds,
        [property: JsonPropertyName("seconds_since_enqueued")] double SecondsSinceEnqueued,
        [property: JsonPropertyName("hour_of_day")] int HourOfDay,
        [property: JsonPropertyName("day_of_week")] int DayOfWeek,
        [property: JsonPropertyName("property_count")] int PropertyCount,
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("entity_name")] string EntityName,
        [property: JsonPropertyName("deadletter_reason")] string DeadletterReason,
        [property: JsonPropertyName("exception_type")] string ExceptionType,
        [property: JsonPropertyName("content_type")] string ContentType,
        [property: JsonPropertyName("payload_shape")] string PayloadShape,
        [property: JsonPropertyName("error_text_normalised")] string ErrorTextNormalised,
        [property: JsonPropertyName("schema_fingerprint")] string SchemaFingerprint,
        [property: JsonPropertyName("feature_version")] int FeatureVersion);

    private sealed record AnalyzeResponseDto(
        [property: JsonPropertyName("clusters")] List<ClusterDto>? Clusters,
        [property: JsonPropertyName("singletons")] List<SingletonDto>? Singletons,
        [property: JsonPropertyName("method")] string? Method);

    private sealed record ClusterDto(
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("representative_ref")] string RepresentativeRef,
        [property: JsonPropertyName("top_terms")] List<string>? TopTerms,
        [property: JsonPropertyName("first_occurrence_ref")] string FirstOccurrenceRef,
        [property: JsonPropertyName("last_occurrence_ref")] string LastOccurrenceRef,
        [property: JsonPropertyName("dominant_entity")] string DominantEntity,
        [property: JsonPropertyName("dominant_deadletter_reason")] string DominantDeadletterReason);

    private sealed record SingletonDto(
        [property: JsonPropertyName("ref")] string Ref,
        [property: JsonPropertyName("dominant_entity")] string DominantEntity,
        [property: JsonPropertyName("dominant_deadletter_reason")] string DominantDeadletterReason);
}
