using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Composes a namespace's current DLQ messages, the AI service's raw clustering, and this
/// namespace's signature history into a single, frontend-ready analysis. This is the .NET
/// bridge between the AI service (statistics only) and the frontend (presentation only):
/// identity, history, language, and degradation live here.
/// </summary>
public interface IDlqSignatureAnalysisService
{
    /// <summary>
    /// Analyzes the given namespace's current DLQ messages, scoped to <paramref name="ownerId"/>.
    /// Returns a successful result with <see cref="DlqSignatureAnalysisResult.Available"/> set to
    /// <see langword="false"/> when the AI service is unreachable — that is a normal, expected
    /// outcome, not a failure. A failed <see cref="Result{T}"/> is reserved for genuine
    /// data-access errors (e.g. the DLQ message export itself failing).
    /// </summary>
    Task<Result<DlqSignatureAnalysisResult>> AnalyzeAsync(
        string ownerId,
        Guid namespaceId,
        CancellationToken cancellationToken = default);
}
