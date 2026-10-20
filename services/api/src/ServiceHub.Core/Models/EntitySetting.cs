namespace ServiceHub.Core.Models;

/// <summary>
/// One read-only limit or setting of a messaging entity, in the cloud's own words ("Lock duration", "Maximum receives"),
/// as the adapter read it. A list of these is display data: nothing branches on a label.
/// </summary>
/// <param name="Label">What the cloud calls the setting.</param>
/// <param name="Value">The value as plain text, including its unit ("30 seconds", "10").</param>
public sealed record EntitySetting(string Label, string Value);
