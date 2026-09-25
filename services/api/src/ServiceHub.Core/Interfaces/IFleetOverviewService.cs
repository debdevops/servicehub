using ServiceHub.Core.Models;

namespace ServiceHub.Core.Interfaces;

/// <summary>Reads the cross-cloud fleet view (unit 3.5). Read-only.</summary>
public interface IFleetOverviewService
{
    /// <summary>The owner's fleet. <paramref name="since"/> starts the "new" and "resolved" counts.</summary>
    Task<FleetOverview> GetAsync(string ownerId, IReadOnlySet<Guid>? allowedNamespaceIds, string window, DateTimeOffset since, CancellationToken cancellationToken);
}
