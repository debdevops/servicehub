using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure;

/// <summary>
/// Sends webhook HTTP POST notifications for DLQ spikes and bulk operation completions.
/// Payload shape is delegated to an <see cref="IWebhookMessageFormatter"/> selected by
/// <c>WebhookOptions.Format</c> (generic JSON, Slack, or Teams). Includes a per-namespace
/// cooldown on DLQ spike alerts to prevent alert storms.
/// </summary>
public sealed class WebhookNotifier : IWebhookNotifier
{
    private readonly HttpClient _httpClient;
    private readonly WebhookOptions _options;
    private readonly IReadOnlyDictionary<WebhookFormat, IWebhookMessageFormatter> _formatters;
    private readonly ILogger<WebhookNotifier> _logger;

    // Tracks when the last DLQ-spike notification was sent for each namespace (cooldown).
    // Bulk-operation-completed notifications are not cooled down — see NotifyBulkOperationCompletedAsync.
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastNotified = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookNotifier"/> class.
    /// </summary>
    public WebhookNotifier(
        HttpClient httpClient,
        IOptions<WebhookOptions> options,
        IEnumerable<IWebhookMessageFormatter> formatters,
        ILogger<WebhookNotifier> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ArgumentNullException.ThrowIfNull(formatters);
        _formatters = formatters.ToDictionary(f => f.Format);
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

        if (!TryGetSafeWebhookUri(_options.Url, out var webhookUri))
        {
            // The configured URL is never logged, even redacted: a Slack/Teams webhook URL is a
            // bearer secret in itself, and the rejection reason (non-HTTPS or internal address)
            // is enough for an operator to fix their own configuration without it appearing in
            // plaintext logs.
            _logger.LogWarning("Configured webhook URL is not a permitted destination (must be HTTPS and not an internal address)");
            return Result.Failure(Error.Validation("Webhook.InvalidUrl",
                "Webhook URL must be an HTTPS URL pointing to an external host"));
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

        var notification = new DlqSpikeNotification(
            NamespaceId: namespaceId,
            NamespaceName: namespaceName,
            NewMessageCount: newMessageCount,
            Threshold: _options.DlqSpikeThreshold,
            DetectedAtUtc: now,
            InvestigateUrl: BuildInvestigateUrl(namespaceId));

        var formatter = ResolveFormatter();
        var payload = formatter.BuildDlqSpikePayload(notification);

        var sendResult = await PostAsync(webhookUri, payload,
            $"DLQ spike webhook for namespace {LogRedactor.SanitiseForLog(namespaceName)}",
            cancellationToken);

        if (sendResult.IsSuccess)
        {
            _lastNotified[namespaceId] = now;
        }

        return sendResult;
    }

    /// <inheritdoc />
    public async Task<Result> NotifyBulkOperationCompletedAsync(
        Guid jobId,
        BulkOperationType operationType,
        BulkOperationStatus status,
        Guid namespaceId,
        string namespaceName,
        int totalMatched,
        int successCount,
        int failureCount,
        int skippedCount,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Webhook notifications are disabled, skipping bulk operation alert");
            return Result.Success();
        }

        if (!TryGetSafeWebhookUri(_options.Url, out var webhookUri))
        {
            // The configured URL is never logged, even redacted: a Slack/Teams webhook URL is a
            // bearer secret in itself, and the rejection reason (non-HTTPS or internal address)
            // is enough for an operator to fix their own configuration without it appearing in
            // plaintext logs.
            _logger.LogWarning("Configured webhook URL is not a permitted destination (must be HTTPS and not an internal address)");
            return Result.Failure(Error.Validation("Webhook.InvalidUrl",
                "Webhook URL must be an HTTPS URL pointing to an external host"));
        }

        // No threshold/cooldown gate: a bulk operation is a single, deliberate, human-triggered
        // action, not a recurring scan result — every completion is worth reporting once.
        var notification = new BulkOperationCompletedNotification(
            JobId: jobId,
            OperationType: operationType,
            Status: status,
            NamespaceId: namespaceId,
            NamespaceName: namespaceName,
            TotalMatched: totalMatched,
            SuccessCount: successCount,
            FailureCount: failureCount,
            SkippedCount: skippedCount,
            CompletedAtUtc: DateTimeOffset.UtcNow,
            InvestigateUrl: BuildInvestigateUrl(namespaceId));

        var formatter = ResolveFormatter();
        var payload = formatter.BuildBulkOperationCompletedPayload(notification);

        return await PostAsync(webhookUri, payload,
            $"bulk operation webhook for job {jobId}",
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result> NotifyAutonomyTransitionAsync(
        string signatureHash,
        AutonomyLevel previousLevel,
        AutonomyLevel newLevel,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Webhook notifications are disabled, skipping autonomy transition alert");
            return Result.Success();
        }

        if (!TryGetSafeWebhookUri(_options.Url, out var webhookUri))
        {
            _logger.LogWarning("Configured webhook URL is not a permitted destination (must be HTTPS and not an internal address)");
            return Result.Failure(Error.Validation("Webhook.InvalidUrl",
                "Webhook URL must be an HTTPS URL pointing to an external host"));
        }

        // No threshold/cooldown gate: a grant transition is a single, deliberate,
        // evidence-derived event, not a recurring scan result — every transition is worth
        // reporting once.
        var notification = new AutonomyTransitionNotification(
            SignatureHash: signatureHash,
            PreviousLevel: previousLevel,
            NewLevel: newLevel,
            Reason: reason,
            TransitionedAtUtc: DateTimeOffset.UtcNow,
            InvestigateUrl: BuildSignatureUrl(signatureHash));

        var formatter = ResolveFormatter();
        var payload = formatter.BuildAutonomyTransitionPayload(notification);

        return await PostAsync(webhookUri, payload,
            $"autonomy transition webhook for signature {signatureHash}",
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result> NotifyCircuitBreakerTrippedAsync(
        long ruleId,
        string ruleName,
        int sampleSize,
        double verifiedSuccessRate,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Webhook notifications are disabled, skipping circuit breaker trip alert");
            return Result.Success();
        }

        if (!TryGetSafeWebhookUri(_options.Url, out var webhookUri))
        {
            _logger.LogWarning("Configured webhook URL is not a permitted destination (must be HTTPS and not an internal address)");
            return Result.Failure(Error.Validation("Webhook.InvalidUrl",
                "Webhook URL must be an HTTPS URL pointing to an external host"));
        }

        // No threshold/cooldown gate: a circuit breaker trip is itself already a rare,
        // protective action — every trip is worth reporting once.
        var notification = new CircuitBreakerTrippedNotification(
            RuleId: ruleId,
            RuleName: ruleName,
            SampleSize: sampleSize,
            VerifiedSuccessRate: verifiedSuccessRate,
            TrippedAtUtc: DateTimeOffset.UtcNow,
            InvestigateUrl: BuildRulesUrl());

        var formatter = ResolveFormatter();
        var payload = formatter.BuildCircuitBreakerTrippedPayload(notification);

        return await PostAsync(webhookUri, payload,
            $"circuit breaker webhook for rule {ruleId}",
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result> NotifyInsightDetectedAsync(
        InsightKind kind,
        Guid findingId,
        Guid? namespaceId,
        string? namespaceName,
        string? entityName,
        string description,
        int severity,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Webhook notifications are disabled, skipping insight-detected alert");
            return Result.Success();
        }

        if (!TryGetSafeWebhookUri(_options.Url, out var webhookUri))
        {
            _logger.LogWarning("Configured webhook URL is not a permitted destination (must be HTTPS and not an internal address)");
            return Result.Failure(Error.Validation("Webhook.InvalidUrl",
                "Webhook URL must be an HTTPS URL pointing to an external host"));
        }

        // No threshold/cooldown gate here: the caller (a detection worker) only invokes this for
        // findings that already cleared its own significance threshold, so every call is worth
        // reporting once — same reasoning as NotifyAutonomyTransitionAsync/NotifyCircuitBreakerTrippedAsync.
        var notification = new InsightDetectedNotification(
            Kind: kind,
            FindingId: findingId,
            NamespaceId: namespaceId,
            NamespaceName: namespaceName,
            EntityName: entityName,
            Description: description,
            Severity: severity,
            DetectedAtUtc: DateTimeOffset.UtcNow,
            InvestigateUrl: BuildInsightUrl(namespaceId));

        var formatter = ResolveFormatter();
        var payload = formatter.BuildInsightDetectedPayload(notification);

        return await PostAsync(webhookUri, payload,
            $"insight-detected webhook for {kind} finding {findingId}",
            cancellationToken);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private IWebhookMessageFormatter ResolveFormatter() =>
        _formatters.TryGetValue(_options.Format, out var formatter)
            ? formatter
            : _formatters[WebhookFormat.Generic];

    private string? BuildInvestigateUrl(Guid namespaceId) =>
        string.IsNullOrWhiteSpace(_options.PublicUrl)
            ? null
            : $"{_options.PublicUrl.TrimEnd('/')}/dlq-history?namespace={namespaceId}";

    private string? BuildSignatureUrl(string signatureHash) =>
        string.IsNullOrWhiteSpace(_options.PublicUrl)
            ? null
            : $"{_options.PublicUrl.TrimEnd('/')}/signatures/{Uri.EscapeDataString(signatureHash)}";

    private string? BuildRulesUrl() =>
        string.IsNullOrWhiteSpace(_options.PublicUrl)
            ? null
            : $"{_options.PublicUrl.TrimEnd('/')}/rules";

    private string? BuildInsightUrl(Guid? namespaceId)
    {
        if (string.IsNullOrWhiteSpace(_options.PublicUrl))
        {
            return null;
        }

        var baseUrl = $"{_options.PublicUrl.TrimEnd('/')}/dlq-history";
        return namespaceId is Guid id ? $"{baseUrl}?namespace={id}" : baseUrl;
    }

    private async Task<Result> PostAsync(Uri webhookUri, object payload, string logDescription, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Sending {Description}", logDescription);

            using var response = await _httpClient.PostAsJsonAsync(webhookUri, payload, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("{Description} sent successfully", logDescription);
                return Result.Success();
            }

            _logger.LogWarning("{Description} returned HTTP {StatusCode}", logDescription, (int)response.StatusCode);
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
            _logger.LogError(ex, "Failed to send {Description}", logDescription);
            return Result.Failure(Error.ExternalService(
                "Webhook.Failed",
                $"Webhook notification failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Validates the webhook URL is safe to call (SSRF guard).
    /// Returns true only for HTTPS URLs that resolve to a non-loopback, non-private-IP host.
    /// </summary>
    private static bool TryGetSafeWebhookUri(string rawUrl, out Uri safeUri)
    {
        safeUri = null!;

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            return false;

        // Only HTTPS — no plain HTTP, no file://, no ftp://
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        var host = uri.Host;

        // Block loopback names
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase))
            return false;

        // Block IP-literal hosts that are loopback or RFC-1918 private ranges
        if (System.Net.IPAddress.TryParse(host, out var ip))
        {
            if (System.Net.IPAddress.IsLoopback(ip) || IsRfc1918OrLinkLocal(ip))
                return false;
        }

        safeUri = uri;
        return true;
    }

    /// <summary>
    /// Returns true for RFC-1918 private ranges and link-local addresses:
    /// 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 169.254.0.0/16 (IPv4)
    /// fc00::/7, fe80::/10 (IPv6)
    /// </summary>
    private static bool IsRfc1918OrLinkLocal(System.Net.IPAddress ip)
    {
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254);  // link-local
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            // fc00::/7 — unique local; fe80::/10 — link-local
            return (bytes[0] & 0xFE) == 0xFC   // fc00::/7
                || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80);  // fe80::/10
        }

        return false;
    }
}
