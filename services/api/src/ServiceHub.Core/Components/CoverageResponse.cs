namespace ServiceHub.Core.Components;

/// <summary>One coverage row as the API returns it: the catalogue row plus everything derived from it, so no screen derives it again.</summary>
public sealed record CoverageRow(
    string Id,
    string ComponentKey,
    CloudKind Cloud,
    string CloudName,
    string Name,
    string StuckPlace,
    DetectionKind Detection,
    string DetectionWords,
    string MovesOnBy,
    SupportStatus Status,
    string StatusWords,
    string? Note,
    RecoverabilityClass Class,
    bool OffersReplay,
    bool OffersCloudRedrive,
    bool MayEarnAutonomy,
    bool InSidebar,
    IReadOnlyList<string> EntityKinds,
    string? PlanUnit)
{
    /// <summary>Maps a catalogue row. The only mapping; the controller and the snapshot test both use it.</summary>
    public static CoverageRow From(ComponentDescriptor c) => new(
        c.Id, c.ComponentKey, c.Cloud, ComponentCatalog.CloudName(c.Cloud), c.Name, c.StuckPlace, c.Detection,
        ComponentCatalog.DetectionWords(c.Detection), c.MovesOnBy, c.Status, ComponentCatalog.StatusWords(c.Status), c.Note,
        c.Class, c.Class.OffersReplay(), c.Class.OffersCloudRedrive(), c.Class.MayEarnAutonomy(), c.InSidebar,
        c.EntityKinds ?? [], c.PlanUnit);
}

/// <summary><c>GET /api/v1/fleet/coverage</c>.</summary>
public sealed record CoverageResponse(IReadOnlyList<CoverageRow> Rows)
{
    /// <summary>The whole catalogue.</summary>
    public static CoverageResponse Current { get; } = new([.. ComponentCatalog.All.Select(CoverageRow.From)]);
}
