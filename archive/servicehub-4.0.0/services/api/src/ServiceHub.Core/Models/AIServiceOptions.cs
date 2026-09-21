namespace ServiceHub.Core.Models;

/// <summary>
/// Configuration options for the ServiceHub AI service HTTP client.
/// Bound from the "AI" section of appsettings.json.
/// </summary>
public sealed class AIServiceOptions
{
    /// <summary>Section name in configuration.</summary>
    public const string SectionName = "AI";

    /// <summary>
    /// Whether the AI service integration is enabled. Defaults to false — an operator must
    /// opt in, matching the existing feature-flag discipline for AWS/GCP providers.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Base URL of the AI service (e.g. http://servicehub-ai:8000).</summary>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>Default HTTP client timeout, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 5;
}
