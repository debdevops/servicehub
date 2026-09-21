using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Events.Handlers;

/// <summary>
/// Platform Event subscriber that bridges <see cref="EventTypes.AutonomyGrantTransitioned"/>
/// events to the existing <see cref="IWebhookNotifier"/> — the autonomy-transition counterpart
/// to <see cref="WebhookDlqSpikeHandler"/>, following the identical pattern.
/// </summary>
public sealed class WebhookAutonomyTransitionHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebhookAutonomyTransitionHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="WebhookAutonomyTransitionHandler"/>.
    /// </summary>
    /// <param name="serviceProvider">
    /// Root service provider. Used to create a per-invocation scope for resolving
    /// the scoped <see cref="IWebhookNotifier"/> dependency.
    /// </param>
    /// <param name="logger">Logger instance.</param>
    public WebhookAutonomyTransitionHandler(
        IServiceProvider serviceProvider,
        ILogger<WebhookAutonomyTransitionHandler> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles a <see cref="PlatformEvent"/> from the bus.
    /// Silently ignores events whose <see cref="PlatformEvent.EventType"/> is not
    /// <see cref="EventTypes.AutonomyGrantTransitioned"/>. This handler is registered as a
    /// catch-all subscriber and must not throw for unrelated event types.
    /// </summary>
    /// <param name="platformEvent">The event to handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task HandleAsync(PlatformEvent platformEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(platformEvent);

        if (platformEvent.EventType != EventTypes.AutonomyGrantTransitioned)
            return;

        if (platformEvent.Payload is not AutonomyGrantTransitionedPayload payload)
        {
            _logger.LogWarning(
                "WebhookAutonomyTransitionHandler received {EventType} (Id: {EventId}) " +
                "but Payload was not AutonomyGrantTransitionedPayload. Skipping.",
                platformEvent.EventType,
                platformEvent.Id);
            return;
        }

        _logger.LogDebug(
            "Handled Platform Event {EventType} for SignatureHash {SignatureHash} CorrelationId {CorrelationId}",
            platformEvent.EventType,
            payload.SignatureHash,
            platformEvent.CorrelationId);

        // IWebhookNotifier is scoped (AddHttpClient). Create a scope per invocation.
        using var scope = _serviceProvider.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IWebhookNotifier>();

        await notifier.NotifyAutonomyTransitionAsync(
            payload.SignatureHash,
            payload.PreviousLevel,
            payload.NewLevel,
            payload.Reason,
            cancellationToken);
    }
}
