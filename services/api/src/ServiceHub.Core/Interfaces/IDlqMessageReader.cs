using ServiceHub.Core.Models;

namespace ServiceHub.Core.Interfaces;

/// <summary>Reads the durable list of dead letters ServiceHub has seen (unit 2.1's table).</summary>
public interface IDlqMessageReader
{
    /// <summary>One filtered, paged, newest-first page — plus the reason groups of the set it came from.</summary>
    /// <param name="query">What to list.</param>
    /// <param name="ct">Cancellation.</param>
    Task<DlqPage> ListAsync(DlqListQuery query, CancellationToken ct);
}
