using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Linq;

namespace ServiceHub.Infrastructure.Security;

/// <summary>
/// <see cref="SocketsHttpHandler.ConnectCallback"/> for the webhook-notifier <see cref="HttpClient"/>.
/// Closes a DNS-rebinding gap in <c>WebhookNotifier</c>'s SSRF guard: validating the addresses a
/// hostname resolves to and then letting the framework re-resolve the same hostname at connect
/// time gives a DNS-controlled webhook two lookups to work with — it can return a public address
/// for the first (the guard's check) and a loopback/private/metadata address for the second (the
/// actual connection). This callback connects to one of the exact addresses the guard already
/// validated instead, carried on the request via <see cref="PinnedAddressesKey"/>. TLS SNI and the
/// Host header are untouched — they still come from the request's original URI — so certificate
/// validation against the real hostname is unaffected; only the TCP-level connection target is
/// pinned. When a hostname resolved to more than one validated address, every one of them was
/// already proven safe by the guard, so this callback tries them in order and only fails if all
/// of them are unreachable — otherwise a single unlucky address (e.g. an advertised route that's
/// actually down) would fail the whole notification even though another validated address works.
/// </summary>
public static class WebhookConnectCallback
{
    /// <summary>
    /// Carries the ordered, pre-validated addresses to try connecting to on the outgoing
    /// <see cref="HttpRequestMessage"/>. Absent (or empty) for IP-literal webhook URLs, where the
    /// "hostname" already is the connect target and there is no second resolution to diverge from
    /// the first.
    /// </summary>
    public static readonly HttpRequestOptionsKey<IReadOnlyList<IPAddress>> PinnedAddressesKey = new("ServiceHub.PinnedWebhookAddresses");

    /// <summary>
    /// Pure selection logic, separated from the actual socket I/O and from
    /// <see cref="SocketsHttpConnectionContext"/> (whose constructor is internal to
    /// <c>System.Net.Http</c> and so cannot be instantiated from a test) so it can be unit-tested
    /// against just the two public inputs it needs: the pinned addresses when present, otherwise the
    /// framework's own DNS-resolved endpoint (IP-literal URLs, or any future caller that doesn't
    /// set the option) as the sole candidate.
    /// </summary>
    internal static IReadOnlyList<EndPoint> SelectConnectTargets(HttpRequestOptions requestOptions, DnsEndPoint fallback) =>
        requestOptions.TryGetValue(PinnedAddressesKey, out var pinned) && pinned.Count > 0
            ? pinned.Select(address => (EndPoint)new IPEndPoint(address, fallback.Port)).ToArray()
            : [fallback];

    /// <summary>
    /// <see cref="SocketsHttpHandler.ConnectCallback"/> implementation. Extracts the candidate
    /// targets from the framework-owned <see cref="SocketsHttpConnectionContext"/> and delegates
    /// the actual connect-with-failover to <see cref="ConnectToFirstAvailableAsync"/>, which — like
    /// <see cref="SelectConnectTargets"/> — is kept independently testable against real sockets
    /// without needing a <see cref="SocketsHttpConnectionContext"/> instance.
    /// </summary>
    public static ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken) =>
        ConnectToFirstAvailableAsync(
            SelectConnectTargets(context.InitialRequestMessage.Options, context.DnsEndPoint),
            cancellationToken);

    /// <summary>
    /// Tries each candidate target in order, moving on to the next only when the connection
    /// attempt itself fails — a cancellation is propagated immediately rather than treated as "try
    /// the next address." Separated from <see cref="ConnectAsync"/> so the failover behavior can be
    /// unit-tested against real listening sockets directly.
    /// </summary>
    internal static async ValueTask<Stream> ConnectToFirstAvailableAsync(
        IReadOnlyList<EndPoint> targets, CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;

        foreach (var target in targets)
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(target, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                socket.Dispose();
                lastFailure = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw lastFailure ?? new SocketException((int)SocketError.HostUnreachable);
    }
}
