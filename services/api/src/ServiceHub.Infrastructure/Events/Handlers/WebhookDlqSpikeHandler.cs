using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Events.Handlers;

/// <summary>
/// Platform Event subscriber that bridges <see cref="EventTypes.DlqSpikeDetected"/>
/// events to the existing <see cref="IWebhookNotifier"/>.
/// <para>
/// Responsibilities:
/// <list type="bullet">
///   <item>Filters the event stream to <see cref="EventTypes.DlqSpikeDetected"/> only.</item>
///   <item>
///     Casts <see cref="PlatformEvent.Payload"/> to <see cref="DlqSpikeDetectedPayload"/>
///     and delegates to <see cref="IWebhookNotifier.NotifyDlqSpikeAsync"/>.
///   </item>
///   <item>No business logic, no retry logic, no threshold evaluation.</item>
/// </list>
/// </para>
/// <para>
/// Lifetime: singleton. <see cref="IWebhookNotifier"/> is scoped (registered via
/// <c>AddHttpClient</c>). This handler creates a DI scope per invocation to safely
/// resolve the scoped notifier — identical to the pattern used by
/// <c>DlqMonitorWorker</c> for per-scan-cycle scope creation.
/// </para>
/// </summary>
public sealed class WebhookDlqSpikeHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebhookDlqSpikeHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="WebhookDlqSpikeHandler"/>.
    /// </summary>
    /// <param name="serviceProvider">
    /// Root service provider. Used to create a per-invocation scope for resolving
    /// the scoped <see cref="IWebhookNotifier"/> dependency.
    /// </param>
    /// <param name="logger">Logger instance.</param>
    public WebhookDlqSpikeHandler(
        IServiceProvider serviceProvider,
        ILogger<WebhookDlqSpikeHandler> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles a <see cref="PlatformEvent"/> from the bus.
    /// Silently ignores events whose <see cref="PlatformEvent.EventType"/> is not
    /// <see cref="EventTypes.DlqSpikeDetected"/>. This handler is registered as a
    /// catch-all subscriber and must not throw for unrelated event types.
    /// </summary>
    /// <param name="platformEvent">The event to handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task HandleAsync(PlatformEvent platformEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(platformEvent);

        // Filter: only handle DLQ spike events.
        if (platformEvent.EventType != EventTypes.DlqSpikeDetected)
            return;

        if (platformEvent.Payload is not DlqSpikeDetectedPayload payload)
        {
            _logger.LogWarning(
                "WebhookDlqSpikeHandler received {EventType} (Id: {EventId}) " +
                "but Payload was not DlqSpikeDetectedPayload. Skipping.",
                platformEvent.EventType,
                platformEvent.Id);
            return;
        }

        _logger.LogDebug(
            "Handled Platform Event {EventType} for NamespaceId {NamespaceId} CorrelationId {CorrelationId}",
            platformEvent.EventType,
            payload.NamespaceId,
            platformEvent.CorrelationId);

        // IWebhookNotifier is scoped (AddHttpClient). Create a scope per invocation.
        using var scope = _serviceProvider.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IWebhookNotifier>();

        await notifier.NotifyDlqSpikeAsync(
            payload.NamespaceId,
            payload.NamespaceName,
            payload.NewMessageCount,
            cancellationToken);
    }
}
