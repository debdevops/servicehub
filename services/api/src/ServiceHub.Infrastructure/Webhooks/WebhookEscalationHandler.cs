using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Webhooks;

/// <summary>
/// Carries each <c>EscalationRaised</c> to every Slack / Teams / webhook channel the owner set up, and the configured one (units 5.5, 6.3), on 4.0.0's handler pattern.
/// Best-effort: a delivery failure is logged and never fails the escalation — the durable pending item is the truth.
/// </summary>
public sealed class WebhookEscalationHandler
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WebhookEscalationHandler> _logger;

    /// <summary>Creates it.</summary>
    public WebhookEscalationHandler(IServiceScopeFactory scopes, ILogger<WebhookEscalationHandler> logger)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Subscribed to the in-process bus at start-up.</summary>
    public async Task HandleAsync(PlatformEvent platformEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(platformEvent);
        if (platformEvent.EventType != EventTypes.EscalationRaised || platformEvent.Payload is not EscalationRaisedPayload p)
        {
            return;
        }

        try
        {
            // Delivery reads the owner's channels from the database: one scope per escalation.
            using var scope = _scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IEscalationDelivery>()
                .DeliverAsync(p, platformEvent.Actor ?? string.Empty, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Delivery is best-effort by design.
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "An escalation could not be delivered to the webhook");
        }
#pragma warning restore CA1031
    }
}
