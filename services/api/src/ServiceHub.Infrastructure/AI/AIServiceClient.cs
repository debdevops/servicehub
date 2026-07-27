using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// HTTP client for the ServiceHub AI service.
/// <para>
/// Only <see cref="IsAvailableAsync"/> makes a real call (<c>GET /health</c>) — the AI service
/// does not yet expose endpoints matching the other methods' contracts, so they continue to
/// report "not yet implemented" exactly as the previous stub did. ServiceHub makes no external
/// AI API calls of any kind: the AI service is a local, self-hosted container the operator
/// controls, reached only when <see cref="AIServiceOptions.Enabled"/> is explicitly set.
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
    public Task<Result<IReadOnlyList<AnomalyType>>> AnalyzeMessagesAsync(
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("AI service is not yet implemented. AnalyzeMessagesAsync called with {MessageCount} messages", messages?.Count ?? 0);

        return Task.FromResult(Result.Failure<IReadOnlyList<AnomalyType>>(Error.Internal(
            ErrorCodes.General.ServiceUnavailable,
            "AI anomaly detection service is not yet implemented. This feature will be available in a future release.")));
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

            if (!available && Interlocked.Exchange(ref _unavailabilityLogged, 1) == 0)
            {
                _logger.LogWarning(
                    "AI service is unavailable — ServiceHub will continue operating without AI-powered features until it recovers.");
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

    private sealed record HealthResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("ready")] bool Ready);
}
