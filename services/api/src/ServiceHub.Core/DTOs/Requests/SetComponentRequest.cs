namespace ServiceHub.Core.DTOs.Requests;

/// <summary>Ticks (<c>true</c>) or unticks (<c>false</c>) one component on one connection.</summary>
/// <param name="Enabled">Whether ServiceHub should read the component on this connection.</param>
public sealed record SetComponentRequest(bool Enabled);
