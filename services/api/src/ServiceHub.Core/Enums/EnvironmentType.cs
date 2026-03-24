namespace ServiceHub.Core.Enums;

/// <summary>
/// Represents the deployment environment of a Service Bus namespace.
/// Controls safety guards and feature availability per namespace.
/// </summary>
public enum EnvironmentType
{
    /// <summary>Development environment — all features enabled, no restrictions.</summary>
    Dev = 0,

    /// <summary>User Acceptance Testing — test message generation disabled.</summary>
    Uat = 1,

    /// <summary>Production — send/dead-letter disabled, replay requires confirmation.</summary>
    Prod = 2
}
