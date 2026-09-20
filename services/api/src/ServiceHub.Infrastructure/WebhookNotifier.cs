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
    /// <summary>
    /// A webhook URL that passed the SSRF guard, together with the exact address to connect to
    /// when the host was a hostname (not an IP literal) — see <see cref="TryGetSafeWebhookTargetAsync"/>.
    /// </summary>
    private sealed record SafeWebhookTarget(Uri Uri, System.Net.IPAddress? PinnedAddress);

    private readonly HttpClient _httpClient;
    private readonly WebhookOptions _options;
    private readonly IReadOnlyDictionary<WebhookFormat, IWebhookMessageFormatter> _formatters;
    private readonly ILogger<WebhookNotifier> _logger;
    private readonly Security.IDnsResolver _dnsResolver;

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
        ILogger<WebhookNotifier> logger,
        Security.IDnsResolver? dnsResolver = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dnsResolver = dnsResolver ?? new Security.DnsResolver();

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

        var targetResult = await ResolveSafeWebhookTargetAsync(cancellationToken);
        if (targetResult.IsFailure)
        {
            return Result.Failure(targetResult.Error);
        }

        var target = targetResult.Value;

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

        var sendResult = await PostAsync(target, payload,
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

        var targetResult = await ResolveSafeWebhookTargetAsync(cancellationToken);
        if (targetResult.IsFailure)
        {
            return Result.Failure(targetResult.Error);
        }

        var target = targetResult.Value;

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

        return await PostAsync(target, payload,
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

        var targetResult = await ResolveSafeWebhookTargetAsync(cancellationToken);
        if (targetResult.IsFailure)
        {
            return Result.Failure(targetResult.Error);
        }

        var target = targetResult.Value;

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

        return await PostAsync(target, payload,
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

        var targetResult = await ResolveSafeWebhookTargetAsync(cancellationToken);
        if (targetResult.IsFailure)
        {
            return Result.Failure(targetResult.Error);
        }

        var target = targetResult.Value;

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

        return await PostAsync(target, payload,
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

        var targetResult = await ResolveSafeWebhookTargetAsync(cancellationToken);
        if (targetResult.IsFailure)
        {
            return Result.Failure(targetResult.Error);
        }

        var target = targetResult.Value;

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

        return await PostAsync(target, payload,
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

    /// <summary>
    /// Resolves and validates <see cref="WebhookOptions.Url"/>, converting both an unsafe/invalid
    /// URL and a cancellation during DNS resolution into the same <see cref="Result"/> contract
    /// every caller already expects from <see cref="PostAsync"/> — <see cref="TryGetSafeWebhookTargetAsync"/>
    /// resolves a hostname via <see cref="_dnsResolver"/>, whose real implementation can observe
    /// cancellation and throw <see cref="OperationCanceledException"/> (or the <see cref="TaskCanceledException"/>
    /// subclass) just as <see cref="HttpClient.SendAsync(HttpRequestMessage, CancellationToken)"/>
    /// can; without this, that exception would escape uncaught instead of coming back as a Result.
    /// </summary>
    private async Task<Result<SafeWebhookTarget>> ResolveSafeWebhookTargetAsync(CancellationToken cancellationToken)
    {
        SafeWebhookTarget? target;
        try
        {
            target = await TryGetSafeWebhookTargetAsync(_options.Url, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<SafeWebhookTarget>(Error.Internal("Webhook.Cancelled", "Operation was cancelled"));
        }

        if (target is null)
        {
            // The configured URL is never logged, even redacted: a Slack/Teams webhook URL is a
            // bearer secret in itself, and the rejection reason (non-HTTPS or internal address)
            // is enough for an operator to fix their own configuration without it appearing in
            // plaintext logs.
            _logger.LogWarning("Configured webhook URL is not a permitted destination (must be HTTPS and not an internal address)");
            return Result.Failure<SafeWebhookTarget>(Error.Validation("Webhook.InvalidUrl",
                "Webhook URL must be an HTTPS URL pointing to an external host"));
        }

        return Result.Success(target);
    }

    private async Task<Result> PostAsync(SafeWebhookTarget target, object payload, string logDescription, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Sending {Description}", logDescription);

            using var request = new HttpRequestMessage(HttpMethod.Post, target.Uri)
            {
                Content = JsonContent.Create(payload),
            };

            if (target.PinnedAddress is { } pinnedAddress)
            {
                // Connects the TCP layer to exactly the address TryGetSafeWebhookTargetAsync
                // already validated — see WebhookConnectCallback for why a second, unpinned
                // DNS lookup at connect time would reopen the rebinding gap this closes.
                request.Options.Set(Security.WebhookConnectCallback.PinnedAddressKey, pinnedAddress);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);

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
    /// Returns the URL only for HTTPS URLs that resolve to a non-loopback, non-private-IP host,
    /// together with the exact address to pin the TCP connection to when the host was a hostname
    /// (see <see cref="Security.WebhookConnectCallback"/> — connecting via the framework's own,
    /// separate DNS lookup would let a DNS-controlled webhook answer differently the second time).
    /// A hostname (as opposed to an IP literal) is DNS-resolved and every returned address is
    /// checked — an IP-literal-only check lets a hostname that resolves to a loopback, RFC-1918,
    /// or link-local/cloud-metadata address (e.g. 169.254.169.254) straight through, since
    /// <c>IPAddress.TryParse</c> only ever succeeds for a literal. Every address is also normalized
    /// out of its IPv4-mapped-IPv6 form (e.g. <c>::ffff:127.0.0.1</c>) before classification —
    /// unnormalized, neither <see cref="System.Net.IPAddress.IsLoopback"/> nor the IPv4 branch of
    /// <see cref="IsRfc1918OrLinkLocal"/> recognizes it, letting it pass as a "public" IPv6 address
    /// while actually routing to a local/private IPv4 target.
    /// </summary>
    private async Task<SafeWebhookTarget?> TryGetSafeWebhookTargetAsync(string rawUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            return null;

        // Only HTTPS — no plain HTTP, no file://, no ftp://
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return null;

        var host = uri.Host;

        // Block loopback names
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase))
            return null;

        if (System.Net.IPAddress.TryParse(host, out var literalIp))
        {
            // IP-literal host — validate it directly, no DNS involved, and no second resolution
            // at connect time to diverge from this one, so no pinning is needed either.
            literalIp = NormalizeForClassification(literalIp);
            if (System.Net.IPAddress.IsLoopback(literalIp) || IsRfc1918OrLinkLocal(literalIp))
                return null;

            return new SafeWebhookTarget(uri, PinnedAddress: null);
        }

        // Hostname host — resolve and validate every returned address. Fail closed (reject)
        // on a resolution failure or an empty result rather than letting an unreachable/
        // misconfigured host fall through as "safe." Cancellation during resolution is
        // deliberately left to propagate — ResolveSafeWebhookTargetAsync converts it to the
        // standard Result contract; catching it here as if it were a resolution failure would
        // misreport a cancelled call as "not a permitted destination."
        System.Net.IPAddress[] resolved;
        try
        {
            resolved = await _dnsResolver.ResolveHostAddressesAsync(host, cancellationToken);
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException)
        {
            return null;
        }

        if (resolved.Length == 0)
            return null;

        var normalized = Array.ConvertAll(resolved, NormalizeForClassification);
        if (normalized.Any(a => System.Net.IPAddress.IsLoopback(a) || IsRfc1918OrLinkLocal(a)))
            return null;

        // Pin the connection to the first validated address — every returned address was just
        // proven safe above, and picking one deterministically (rather than letting the
        // framework re-resolve and pick its own) is exactly what closes the rebinding gap.
        return new SafeWebhookTarget(uri, PinnedAddress: normalized[0]);
    }

    /// <summary>
    /// Maps an IPv4-mapped IPv6 address (e.g. <c>::ffff:127.0.0.1</c>) to its IPv4 form before
    /// loopback/private-range classification, so the mapping can't be used to sneak a local or
    /// private IPv4 target past checks written against IPv6 address bytes. A no-op for every other
    /// address.
    /// </summary>
    private static System.Net.IPAddress NormalizeForClassification(System.Net.IPAddress ip) =>
        ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

    /// <summary>
    /// Returns true for RFC-1918 private ranges and link-local addresses:
    /// 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 169.254.0.0/16 (IPv4)
    /// fc00::/7, fe80::/10 (IPv6)
    /// Callers must normalize via <see cref="NormalizeForClassification"/> first — an
    /// IPv4-mapped-IPv6 address is neither recognized by the IPv4 branch (wrong
    /// <see cref="System.Net.Sockets.AddressFamily"/>) nor the IPv6 branch (its bytes don't match
    /// either IPv6 prefix) unless it has already been mapped down to plain IPv4.
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
