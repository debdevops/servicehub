using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Services;
using ServiceHub.Core.Events;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Streams platform events to the browser as Server-Sent Events (unit 2.11), scoped to the caller's owner and to
/// the caller's namespace allow-list on every connection.
/// </summary>
/// <remarks>
/// <b>A hint, not a feed.</b> Delivery is best-effort: the in-process bus and each connection's buffer both drop
/// the oldest event under pressure, and nothing is replayed to a client that reconnects. A client must treat an
/// event as "something changed — look again" and read the durable tables for the truth. Events carry no payload:
/// a stream is not a second way to read data, only a nudge to read it.
/// </remarks>
[Route("api/v1/events")]
[Tags("Events")]
public sealed class EventsController : ApiControllerBase
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly PlatformEventStreamBroker _broker;
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>Creates the controller.</summary>
    public EventsController(PlatformEventStreamBroker broker, IHostApplicationLifetime lifetime)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    }

    /// <summary>
    /// Opens a <c>text/event-stream</c> that pushes owner-scoped events until the client disconnects, with a
    /// comment heartbeat every 15 seconds so intermediaries keep it open.
    /// </summary>
    /// <param name="requestAborted">Bound to the request abort token.</param>
    [HttpGet("stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task Stream(CancellationToken requestAborted)
    {
        // A stream never ends on its own, so it must also end when the host is stopping — otherwise one open browser tab
        // holds the whole application up until the shutdown timeout (30 s).
        using var stopOrLeave = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, _lifetime.ApplicationStopping);
        var cancellationToken = stopOrLeave.Token;
        using var subscription = _broker.Register(OwnerId, AllowedNamespaceIds);
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
            // The client left — the normal end of a stream, not an error.
        }
        catch (ChannelClosedException)
        {
            // The broker closed the channel at shutdown.
        }
    }

    private async Task WriteAndFlushAsync(string frame, CancellationToken cancellationToken)
    {
        await Response.WriteAsync(frame, cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private static EventStreamItem ToStreamItem(PlatformEvent e) =>
        new(e.Id, e.EventType, e.Category, e.Severity, e.OccurredUtc, e.CloudProvider, e.NamespaceId);

    /// <summary>What a client is told: that something changed, where, and when. No payload, no actor.</summary>
    private sealed record EventStreamItem(
        Guid Id, string EventType, string Category, EventSeverity Severity, DateTimeOffset OccurredUtc, string? CloudProvider, Guid? NamespaceId);
}
