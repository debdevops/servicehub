using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace ServiceHub.Infrastructure.Security;

/// <summary>
/// <see cref="SocketsHttpHandler.ConnectCallback"/> for the webhook-notifier <see cref="HttpClient"/>.
/// Closes a DNS-rebinding gap in <c>WebhookNotifier</c>'s SSRF guard: validating the addresses a
/// hostname resolves to and then letting the framework re-resolve the same hostname at connect
/// time gives a DNS-controlled webhook two lookups to work with — it can return a public address
/// for the first (the guard's check) and a loopback/private/metadata address for the second (the
/// actual connection). This callback connects to the exact address the guard already validated
/// instead, carried on the request via <see cref="PinnedAddressKey"/>. TLS SNI and the Host header
/// are untouched — they still come from the request's original URI — so certificate validation
/// against the real hostname is unaffected; only the TCP-level connection target is pinned.
/// </summary>
public static class WebhookConnectCallback
{
    /// <summary>
    /// Carries the pre-validated address to connect to on the outgoing <see cref="HttpRequestMessage"/>.
    /// Absent for IP-literal webhook URLs, where the "hostname" already is the connect target and
    /// there is no second resolution to diverge from the first.
    /// </summary>
    public static readonly HttpRequestOptionsKey<IPAddress> PinnedAddressKey = new("ServiceHub.PinnedWebhookAddress");

    /// <summary>
    /// Pure selection logic, separated from the actual socket I/O and from
    /// <see cref="SocketsHttpConnectionContext"/> (whose constructor is internal to
    /// <c>System.Net.Http</c> and so cannot be instantiated from a test) so it can be unit-tested
    /// against just the two public inputs it needs: the pinned address when present, otherwise the
    /// framework's own DNS-resolved endpoint (IP-literal URLs, or any future caller that doesn't
    /// set the option).
    /// </summary>
    internal static EndPoint SelectConnectTarget(HttpRequestOptions requestOptions, DnsEndPoint fallback) =>
        requestOptions.TryGetValue(PinnedAddressKey, out var pinned)
            ? new IPEndPoint(pinned, fallback.Port)
            : fallback;

    /// <summary>
    /// <see cref="SocketsHttpHandler.ConnectCallback"/> implementation.
    /// </summary>
    public static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var target = SelectConnectTarget(context.InitialRequestMessage.Options, context.DnsEndPoint);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(target, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
