using System.Net;

namespace ServiceHub.Infrastructure.Security;

/// <summary>
/// Thin wrapper over <see cref="Dns"/> so SSRF guards that must resolve a hostname before
/// validating it (see <c>WebhookNotifier.TryGetSafeWebhookTargetAsync</c>) can be unit-tested
/// without depending on real DNS.
/// </summary>
public interface IDnsResolver
{
    /// <summary>
    /// Resolves <paramref name="host"/> to its IP addresses. Throws <see cref="System.Net.Sockets.SocketException"/>
    /// on resolution failure, matching <see cref="Dns.GetHostAddressesAsync(string, System.Threading.CancellationToken)"/>.
    /// </summary>
    Task<IPAddress[]> ResolveHostAddressesAsync(string host, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IDnsResolver"/>
public sealed class DnsResolver : IDnsResolver
{
    public Task<IPAddress[]> ResolveHostAddressesAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}
