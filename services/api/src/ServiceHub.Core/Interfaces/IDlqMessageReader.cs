using ServiceHub.Core.Models;

namespace ServiceHub.Core.Interfaces;

/// <summary>Reads the durable list of dead letters ServiceHub has seen (unit 2.1's table).</summary>
public interface IDlqMessageReader
{
    /// <summary>One filtered, paged, newest-first page — plus the reason groups of the set it came from.</summary>
    /// <param name="query">What to list.</param>
    /// <param name="ct">Cancellation.</param>
    Task<DlqPage> ListAsync(DlqListQuery query, CancellationToken ct);

    /// <summary>One stored dead letter, or null when it is not in one of the given namespaces.</summary>
    /// <param name="id">The row id.</param>
    /// <param name="namespaceIds">The namespaces the caller may see; a row elsewhere is reported as absent.</param>
    /// <param name="ct">Cancellation.</param>
    Task<DlqDetail?> GetAsync(long id, IReadOnlyCollection<Guid> namespaceIds, CancellationToken ct);

    /// <summary>
    /// New and resolved dead letters per UTC day for the last <paramref name="days"/> days, oldest first, every day
    /// present (zeros included). Computed from what ServiceHub stored — never estimated, and not hourly: no hourly
    /// series exists.
    /// </summary>
    Task<IReadOnlyList<DlqTrendDay>> GetTrendAsync(IReadOnlyCollection<Guid> namespaceIds, int days, DateTimeOffset nowUtc, CancellationToken ct);
}
