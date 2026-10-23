using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Azure.Core;
using Azure.Identity;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Providers.Azure.Components;

/// <summary>Which Azure service a read goes to. Decides the token's audience and the hosts a request may name.</summary>
public enum AzureAudience
{
    /// <summary>Azure Resource Manager (<c>management.azure.com</c>).</summary>
    Management,

    /// <summary>Blob storage (<c>*.blob.core.windows.net</c>).</summary>
    Storage,
}

/// <summary>Azure answered a read with an error status.</summary>
internal sealed class AzureRestException : Exception
{
    public AzureRestException(HttpStatusCode status, string message)
        : base(message) => Status = status;

    /// <summary>The HTTP status Azure answered with.</summary>
    public HttpStatusCode Status { get; }
}

/// <summary>The connection does not sign in with Entra, so it cannot call Azure Resource Manager or Blob storage.</summary>
internal sealed class AzureNotEntraException : Exception
{
    public AzureNotEntraException(ConnectionAuthType authType)
        : base($"This connection signs in with {authType}, which cannot read this component.") => AuthType = authType;

    /// <summary>How the connection signs in.</summary>
    public ConnectionAuthType AuthType { get; }
}

/// <summary>
/// The only way the Azure component sources touch Azure: one authenticated <c>GET</c>. There is no method that sends anything else, so
/// the read-only promise is one small surface to read, and a test can stand in for Azure.
/// </summary>
public interface IAzureRestClient
{
    /// <summary>
    /// The body of a <c>GET</c>, or null when Azure answers 404. Throws for any other error status, and when the connection does not
    /// sign in with Entra.
    /// </summary>
    /// <param name="ns">The connection whose identity signs the request.</param>
    /// <param name="url">An https address on a host the audience allows.</param>
    /// <param name="audience">Which service it is.</param>
    /// <param name="maxBytes">When set, only this many leading bytes are asked for (a range read), so a large blob is never pulled whole.</param>
    /// <param name="ct">Cancellation.</param>
    Task<string?> GetTextAsync(Namespace ns, Uri url, AzureAudience audience, int? maxBytes, CancellationToken ct);
}

/// <summary>
/// Reads Azure over HTTPS with the connection's own Entra identity. Only <c>ManagedIdentity</c> and <c>DefaultAzureCredential</c>
/// connections are accepted (the host's identity is borrowed only when the connection says so): a service-principal secret is not
/// stored anywhere, and a connection string is a Service Bus key, which Resource Manager does not accept.
/// </summary>
internal sealed class AzureRestClient : IAzureRestClient
{
    private const string BlobVersion = "2023-11-03";

    private readonly HttpClient _http;
    private readonly Func<Namespace, TokenCredential> _credentialFor;
    private readonly ConcurrentDictionary<Guid, TokenCredential> _credentials = new();

    public AzureRestClient(HttpClient http)
        : this(http, EntraCredentialFor)
    {
    }

    // The credential is this client's own seam to Entra, so a test can stand in for a token without a network.
    internal AzureRestClient(HttpClient http, Func<Namespace, TokenCredential> credentialFor)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _credentialFor = credentialFor ?? throw new ArgumentNullException(nameof(credentialFor));
    }

    public async Task<string?> GetTextAsync(Namespace ns, Uri url, AzureAudience audience, int? maxBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentNullException.ThrowIfNull(url);
        if (!IsAllowed(url, audience))
        {
            throw new ArgumentException("That address is not one this source may read.", nameof(url));
        }

        var credential = _credentials.GetOrAdd(ns.Id, _ => _credentialFor(ns));
        var scope = audience == AzureAudience.Management ? "https://management.azure.com/.default" : "https://storage.azure.com/.default";
        var token = await credential.GetTokenAsync(new TokenRequestContext([scope]), ct).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        if (audience == AzureAudience.Storage)
        {
            request.Headers.Add("x-ms-version", BlobVersion);
        }

        if (maxBytes is { } limit)
        {
            request.Headers.Range = new RangeHeaderValue(0, limit - 1);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new AzureRestException(response.StatusCode, $"Azure answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        // A range read of a small blob is answered whole; either way no more than the cap is ever held.
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        if (maxBytes is not { } cap)
        {
            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }

        var buffer = new char[cap];
        var read = await reader.ReadBlockAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
        return new string(buffer, 0, read);
    }

    /// <summary>Whether a request may go to this address: https only, and only the host of the audience (never one a caller made up).</summary>
    internal static bool IsAllowed(Uri url, AzureAudience audience) =>
        url.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(url.UserInfo)
        && (audience == AzureAudience.Management
            ? string.Equals(url.Host, "management.azure.com", StringComparison.OrdinalIgnoreCase)
            : url.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase));

    private static TokenCredential EntraCredentialFor(Namespace ns) => ns.AuthType switch
    {
        ConnectionAuthType.ManagedIdentity => new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned),
        ConnectionAuthType.DefaultAzureCredential => new DefaultAzureCredential(),
        _ => throw new AzureNotEntraException(ns.AuthType),
    };
}
