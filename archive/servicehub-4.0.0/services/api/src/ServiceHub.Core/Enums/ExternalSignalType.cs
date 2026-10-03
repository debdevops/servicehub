namespace ServiceHub.Core.Enums;

/// <summary>
/// The kind of external signal an <see cref="Entities.ExternalSignalEvent"/> records (roadmap
/// §5.D, C3 — "External-signal correlation"; persistence design §1.6, M5).
/// </summary>
public enum ExternalSignalType
{
    /// <summary>A code/service deploy.</summary>
    Deploy = 0,

    /// <summary>A configuration change short of a full deploy (feature flag, env var, infra setting).</summary>
    ConfigChange = 1,

    /// <summary>Any other operator-defined signal not covered by <see cref="Deploy"/>/<see cref="ConfigChange"/>.</summary>
    Custom = 2,
}
