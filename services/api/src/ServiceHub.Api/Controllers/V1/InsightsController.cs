using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// The Insights tab on Advanced Overview (unit 6.18): what the Insights agents noticed. Read-only — a finding never acts,
/// opens a rule or changes authority. Narrations are templated text, returned with <c>suggestion: true</c> (R3).
/// </summary>
[Route("api/v1/insights")]
public sealed class InsightsController : ApiControllerBase
{
    private readonly ServiceHubDbContext _db;
    private readonly INamespaceRepository _namespaces;

    /// <summary>Creates the controller.</summary>
    public InsightsController(ServiceHubDbContext db, INamespaceRepository namespaces)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _namespaces = namespaces ?? throw new ArgumentNullException(nameof(namespaces));
    }

    /// <summary>Current findings, most severe first; with <paramref name="cleared"/>, also the last 50 that stopped being true.</summary>
    /// <param name="provider">Only findings about this cloud's namespaces (correlations that touch it included).</param>
    /// <param name="cleared">Also recently cleared findings.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] CloudProviderType? provider, [FromQuery] bool cleared, CancellationToken cancellationToken)
    {
        var visible = await _namespaces.GetByOwnerAsync(OwnerId, AllowedNamespaceIds, cancellationToken);
        var namespaces = visible.IsSuccess ? visible.Value.ToDictionary(n => n.Id) : [];

        var rows = await _db.InsightFindings.AsNoTracking().Where(f => f.OwnerId == OwnerId).ToListAsync(cancellationToken);
        bool Visible(Core.Entities.InsightFinding f)
        {
            if (f.NamespaceId is { } id)
            {
                return namespaces.TryGetValue(id, out var ns) && (provider is null || ns.Provider == provider);
            }

            // A correlation names its members in its key: every one must be visible, and one must be in the chosen cloud.
            var members = f.Key.Split(',').Select(k => Guid.TryParse(k.Split('|')[0], out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
            return members.All(namespaces.ContainsKey) && (provider is null || members.Any(m => namespaces[m].Provider == provider));
        }

        object Shape(Core.Entities.InsightFinding f) => new
        {
            f.Id, f.Kind, f.Severity, f.What, f.EntityName, f.NamespaceId,
            namespaceName = f.NamespaceId is { } id && namespaces.TryGetValue(id, out var ns) ? ns.DisplayName ?? ns.Name : null,
            provider = f.NamespaceId is { } p && namespaces.TryGetValue(p, out var pns) ? pns.Provider.ToString().ToLowerInvariant() : null,
            metrics = f.MetricsJson is null ? (System.Text.Json.JsonElement?)null : System.Text.Json.JsonDocument.Parse(f.MetricsJson).RootElement,
            f.FirstSeenAt, f.LastSeenAt, f.ClearedAt,
            suggestion = f.Kind == "narration",
        };

        var shown = rows.Where(Visible).ToList();
        return Ok(new
        {
            current = shown.Where(f => f.ClearedAt == null).OrderByDescending(f => f.Severity).ThenByDescending(f => f.LastSeenAt).Select(Shape),
            cleared = cleared ? shown.Where(f => f.ClearedAt != null).OrderByDescending(f => f.ClearedAt).Take(50).Select(Shape) : [],
            lastLookedAt = rows.Count == 0 ? (DateTimeOffset?)null : rows.Max(f => f.LastSeenAt),
        });
    }
}
