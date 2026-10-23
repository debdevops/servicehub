using ServiceHub.Core.Entities;
using ServiceHub.Providers.Azure.Components;

namespace ServiceHub.UnitTests.Components.Sources;

/// <summary>
/// Azure's HTTP door as the readers see it: answers from a table of address-to-body, records every address asked for, and can fail.
/// An address with no answer is a 404 (null), exactly as the real client reports it.
/// </summary>
internal sealed class FakeAzureRest : IAzureRestClient
{
    private readonly List<(Uri Url, AzureAudience Audience, int? MaxBytes)> _asked = [];

    /// <summary>Answers by the address's path and query, as Azure would see it (<c>/subscriptions?api-version=...</c>).</summary>
    public Dictionary<string, string?> Answers { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, answers with this instead of the table.</summary>
    public Func<Uri, Exception?>? Fail { get; set; }

    public IReadOnlyList<(Uri Url, AzureAudience Audience, int? MaxBytes)> Asked => _asked;

    public IEnumerable<string> Urls => _asked.Select(a => a.Url.AbsoluteUri);

    public Task<string?> GetTextAsync(Namespace ns, Uri url, AzureAudience audience, int? maxBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_asked)
        {
            _asked.Add((url, audience, maxBytes));
        }

        if (Fail?.Invoke(url) is { } error)
        {
            return Task.FromException<string?>(error);
        }

        return Task.FromResult(Answers.TryGetValue(url.PathAndQuery, out var body) ? body : null);
    }
}
