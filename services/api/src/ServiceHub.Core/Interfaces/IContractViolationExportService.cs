using ServiceHub.Core.Entities;
using ServiceHub.Core.Models;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Builds a producer-facing contract-violation report from P2 drift findings (roadmap §5.D, P3 —
/// "Producer export"). Pure formatting over data <see cref="IDriftDetectionService"/> already
/// computed — no new detection logic, no new data source.
/// </summary>
public interface IContractViolationExportService
{
    /// <summary>
    /// Builds a contract-violation report for one namespace's drift findings over the analysis
    /// window, worded for the upstream producer team that owns the entities involved.
    /// </summary>
    /// <param name="namespace">The namespace the findings were detected in.</param>
    /// <param name="findings">The drift findings to include, typically from a single detection call.</param>
    /// <param name="startTime">The analysis window start.</param>
    /// <param name="endTime">The analysis window end.</param>
    /// <returns>The assembled report.</returns>
    ContractViolationReport BuildReport(
        Namespace @namespace,
        IReadOnlyList<DriftFinding> findings,
        DateTimeOffset startTime,
        DateTimeOffset endTime);
}
