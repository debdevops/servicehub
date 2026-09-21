using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Events.Handlers;

/// <summary>
/// Platform Event subscriber that bridges <see cref="EventTypes.InsightDetected"/> events to the
/// existing <see cref="IWebhookNotifier"/> (roadmap §5, I5 — "Push") — the insight-detected
/// counterpart to <see cref="WebhookDlqSpikeHandler"/>, following the identical pattern.
/// </summary>
public sealed class WebhookInsightDetectedHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebhookInsightDetectedHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="WebhookInsightDetectedHandler"/>.
    /// </summary>
    /// <param name="serviceProvider">
    /// Root service provider. Used to create a per-invocation scope for resolving
    /// the scoped <see cref="IWebhookNotifier"/> dependency.
    /// </param>
    /// <param name="logger">Logger instance.</param>
    public WebhookInsightDetectedHandler(
        IServiceProvider serviceProvider,
        ILogger<WebhookInsightDetectedHandler> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles a <see cref="PlatformEvent"/> from the bus.
    /// Silently ignores events whose <see cref="PlatformEvent.EventType"/> is not
    /// <see cref="EventTypes.InsightDetected"/>. This handler is registered as a catch-all
    /// subscriber and must not throw for unrelated event types.
    /// </summary>
    /// <param name="platformEvent">The event to handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task HandleAsync(PlatformEvent platformEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(platformEvent);

        if (platformEvent.EventType != EventTypes.InsightDetected)
            return;

        if (platformEvent.Payload is not InsightDetectedPayload payload)
        {
            _logger.LogWarning(
                "WebhookInsightDetectedHandler received {EventType} (Id: {EventId}) " +
                "but Payload was not InsightDetectedPayload. Skipping.",
                platformEvent.EventType,
                platformEvent.Id);
            return;
        }

        _logger.LogDebug(
            "Handled Platform Event {EventType} for {Kind} finding {FindingId} CorrelationId {CorrelationId}",
            platformEvent.EventType,
            payload.Kind,
            payload.FindingId,
            platformEvent.CorrelationId);

        // IWebhookNotifier is scoped (AddHttpClient). Create a scope per invocation.
        using var scope = _serviceProvider.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IWebhookNotifier>();

        await notifier.NotifyInsightDetectedAsync(
            payload.Kind,
            payload.FindingId,
            platformEvent.NamespaceId,
            platformEvent.NamespaceName,
            payload.EntityName,
            payload.Description,
            payload.Severity,
            cancellationToken);
    }
}
