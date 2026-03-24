using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure;

/// <summary>
/// Sends webhook HTTP POST notifications when DLQ activity exceeds the configured threshold.
/// Includes a per-namespace cooldown to prevent alert storms.
/// </summary>
public sealed class WebhookNotifier : IWebhookNotifier
{
    private readonly HttpClient _httpClient;
    private readonly WebhookOptions _options;
    private readonly ILogger<WebhookNotifier> _logger;

    // Tracks when the last notification was sent for each namespace (cooldown).
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastNotified = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookNotifier"/> class.
    /// </summary>
    public WebhookNotifier(
        HttpClient httpClient,
        IOptions<WebhookOptions> options,
        ILogger<WebhookNotifier> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result> NotifyDlqSpikeAsync(
        Guid namespaceId,
        string namespaceName,
        int newMessageCount,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Webhook notifications are disabled, skipping DLQ spike alert");
            return Result.Success();
        }

        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            _logger.LogWarning("Webhook URL is not configured, skipping DLQ spike alert");
            return Result.Failure(Error.Validation("Webhook.NoUrl", "Webhook URL is not configured"));
        }

        if (newMessageCount < _options.DlqSpikeThreshold)
        {
            return Result.Success();
        }

        // Cooldown check — prevent alert storms
        var now = DateTimeOffset.UtcNow;
        if (_lastNotified.TryGetValue(namespaceId, out var lastSent) &&
            (now - lastSent).TotalSeconds < _options.CooldownSeconds)
        {
            _logger.LogDebug(
                "Cooldown active for namespace {NamespaceId}, skipping notification",
                namespaceId);
            return Result.Success();
        }

        var payload = new DlqSpikePayload
        {
            NamespaceId = namespaceId,
            NamespaceName = namespaceName,
            NewMessageCount = newMessageCount,
            Threshold = _options.DlqSpikeThreshold,
            DetectedAtUtc = now,
            Source = "ServiceHub"
        };

        try
        {
            _logger.LogInformation(
                "Sending DLQ spike webhook for namespace {Namespace}: {Count} new messages (threshold: {Threshold})",
                LogRedactor.SanitiseForLog(namespaceName),
                newMessageCount,
                _options.DlqSpikeThreshold);

            using var response = await _httpClient.PostAsJsonAsync(
                _options.Url, payload, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _lastNotified[namespaceId] = now;
                _logger.LogInformation(
                    "DLQ spike webhook sent successfully for namespace {Namespace}",
                    LogRedactor.SanitiseForLog(namespaceName));
                return Result.Success();
            }

            _logger.LogWarning(
                "DLQ spike webhook returned {StatusCode} for namespace {Namespace}",
                (int)response.StatusCode,
                LogRedactor.SanitiseForLog(namespaceName));

            return Result.Failure(Error.ExternalService(
                "Webhook.HttpError",
                $"Webhook returned HTTP {(int)response.StatusCode}"));
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(Error.Internal("Webhook.Cancelled", "Operation was cancelled"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send DLQ spike webhook for namespace {Namespace}",
                LogRedactor.SanitiseForLog(namespaceName));

            return Result.Failure(Error.ExternalService(
                "Webhook.Failed",
                $"Webhook notification failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Payload sent to the webhook endpoint.
    /// </summary>
    internal sealed class DlqSpikePayload
    {
        [JsonPropertyName("namespaceId")]
        public Guid NamespaceId { get; init; }

        [JsonPropertyName("namespaceName")]
        public string NamespaceName { get; init; } = string.Empty;

        [JsonPropertyName("newMessageCount")]
        public int NewMessageCount { get; init; }

        [JsonPropertyName("threshold")]
        public int Threshold { get; init; }

        [JsonPropertyName("detectedAtUtc")]
        public DateTimeOffset DetectedAtUtc { get; init; }

        [JsonPropertyName("source")]
        public string Source { get; init; } = "ServiceHub";
    }
}
