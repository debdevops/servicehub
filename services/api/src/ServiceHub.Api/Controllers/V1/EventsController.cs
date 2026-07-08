using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Services;
using ServiceHub.Core.Events;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Streams platform events (DLQ spikes, namespace changes, replays, rule matches)
/// to the browser as Server-Sent Events. Events are scoped to the authenticated
/// owner by <see cref="PlatformEventStreamBroker"/>.
/// </summary>
[Route(ApiRoutes.Events.Base)]
[Tags("Events")]
public sealed class EventsController : ApiControllerBase
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly PlatformEventStreamBroker _broker;
    private readonly ILogger<EventsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventsController"/> class.
    /// </summary>
    public EventsController(
        PlatformEventStreamBroker broker,
        ILogger<EventsController> logger)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Opens a <c>text/event-stream</c> connection that pushes owner-scoped platform
    /// events until the client disconnects. Emits a comment heartbeat every 15 seconds
    /// so intermediaries keep the connection alive. Delivery is best-effort: the
    /// in-process bus and the per-connection buffer both drop oldest under pressure,
    /// so clients should treat events as invalidation hints, not a complete feed.
    /// </summary>
    /// <param name="cancellationToken">Bound to the request abort token.</param>
    [HttpGet("stream")]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task Stream(CancellationToken cancellationToken)
    {
        using var subscription = _broker.Register(OwnerId);
        if (subscription is null)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        Response.Headers.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-store";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        try
        {
            await WriteAndFlushAsync(": connected\n\n", cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                PlatformEvent platformEvent;
                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                heartbeatCts.CancelAfter(HeartbeatInterval);

                try
                {
                    platformEvent = await subscription.Reader.ReadAsync(heartbeatCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await WriteAndFlushAsync(": keepalive\n\n", cancellationToken);
                    continue;
                }

                var payload = JsonSerializer.Serialize(ToStreamItem(platformEvent), SerializerOptions);
                await WriteAndFlushAsync($"data: {payload}\n\n", cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client disconnected — normal SSE lifecycle, not an error.
        }
        catch (ChannelClosedException)
        {
            // Broker completed the channel during shutdown.
        }
    }

    private async Task WriteAndFlushAsync(string frame, CancellationToken cancellationToken)
    {
        await Response.WriteAsync(frame, cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private static EventStreamItem ToStreamItem(PlatformEvent platformEvent) => new(
        platformEvent.Id,
        platformEvent.EventType,
        platformEvent.Category,
        platformEvent.Severity,
        platformEvent.OccurredUtc,
        platformEvent.CloudProvider,
        platformEvent.NamespaceId,
        platformEvent.NamespaceName,
        platformEvent.CorrelationId);

    /// <summary>
    /// Trimmed, client-safe projection of <see cref="PlatformEvent"/> — deliberately
    /// excludes <c>Payload</c>, <c>Metadata</c>, <c>Source</c>, and <c>Actor</c>.
    /// </summary>
    private sealed record EventStreamItem(
        Guid Id,
        string EventType,
        string Category,
        EventSeverity Severity,
        DateTimeOffset OccurredUtc,
        string? CloudProvider,
        Guid? NamespaceId,
        string? NamespaceName,
        string? CorrelationId);
}
