using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;

namespace ServiceHub.Core.Interfaces;

/// <summary>Failure signatures (unit 3.8): read-only. Rules are created on Auto Replay, never here.</summary>
public interface ISignaturesService
{
    /// <summary>
    /// The owner's signatures, newest activity first by default. <paramref name="namespaceId"/> and <paramref name="environment"/> narrow
    /// what is counted to one namespace, or to the namespaces of one environment — the counts are recomputed over them, not filtered afterwards.
    /// </summary>
    Task<SignaturePage> ListAsync(
        string ownerId, IReadOnlySet<Guid>? allowed, CloudProviderType? provider, int days, string? tab, string sort, int page, int pageSize, CancellationToken ct,
        Guid? namespaceId = null, EnvironmentType? environment = null);

    /// <summary>One signature by hash (per cloud), or null.</summary>
    Task<SignatureSummary?> GetAsync(string ownerId, IReadOnlySet<Guid>? allowed, string hash, CloudProviderType provider, int days, CancellationToken ct);
}
