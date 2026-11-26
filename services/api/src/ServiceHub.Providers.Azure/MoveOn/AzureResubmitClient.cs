using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Azure.Core;
using ServiceHub.Core.Entities;
using ServiceHub.Providers.Azure.Components;

namespace ServiceHub.Providers.Azure.MoveOn;

/// <summary>
/// The one door through which ServiceHub sends anything but a read to Azure: a Logic Apps trigger-history <c>resubmit</c> (build-plan
/// unit R2). It is a <b>separate</b> interface from <see cref="IAzureRestClient"/> on purpose, so a read-only source can never reach it,
/// and the read door stays GET-only (a test holds that).
/// </summary>
internal interface IAzureResubmitClient
{
    /// <summary>
    /// Sends one <c>POST</c> with no body to a Logic Apps <c>.../histories/{run}/resubmit</c> address. Returns when Azure answers a success
    /// status (202). Throws <see cref="AzureRestException"/> for any other status, and <see cref="AzureNotEntraException"/> when the
    /// connection does not sign in with Entra. Never retries: Azure has no idempotency key, so a second POST would start a second run.
    /// </summary>
    Task PostAsync(Namespace ns, Uri url, CancellationToken ct);
}

/// <summary>Resubmits over HTTPS with the connection's own Entra identity, to Azure Resource Manager only, and only to a resubmit address.</summary>
internal sealed class AzureResubmitClient : IAzureResubmitClient
{
    private const string Scope = "https://management.azure.com/.default";

    private readonly HttpClient _http;
    private readonly Func<Namespace, TokenCredential> _credentialFor;
    private readonly ConcurrentDictionary<Guid, TokenCredential> _credentials = new();

    public AzureResubmitClient(HttpClient http)
        : this(http, AzureRestClient.EntraCredentialFor)
    {
    }

    // The credential is this client's own seam to Entra, so a test can stand in for a token without a network.
    internal AzureResubmitClient(HttpClient http, Func<Namespace, TokenCredential> credentialFor)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _credentialFor = credentialFor ?? throw new ArgumentNullException(nameof(credentialFor));
    }

    public async Task PostAsync(Namespace ns, Uri url, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentNullException.ThrowIfNull(url);
        if (!IsResubmitAddress(url))
        {
            throw new ArgumentException("That is not a Logic Apps resubmit address on Azure Resource Manager.", nameof(url));
        }

        var credential = _credentials.GetOrAdd(ns.Id, _ => _credentialFor(ns));
        var token = await credential.GetTokenAsync(new TokenRequestContext([Scope]), ct).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AzureRestException(response.StatusCode, $"Azure answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }

    /// <summary>Whether a request may go to this address: Resource Manager over https, a Logic Apps workflow trigger history, ending in <c>/resubmit</c>.</summary>
    internal static bool IsResubmitAddress(Uri url) =>
        AzureRestClient.IsAllowed(url, AzureAudience.Management)
        && string.IsNullOrEmpty(url.Fragment)
        && url.AbsolutePath.Contains("/providers/Microsoft.Logic/workflows/", StringComparison.Ordinal)
        && url.AbsolutePath.Contains("/triggers/", StringComparison.Ordinal)
        && url.AbsolutePath.Contains("/histories/", StringComparison.Ordinal)
        && url.AbsolutePath.EndsWith("/resubmit", StringComparison.Ordinal)
        && url.Port == 443;
}
