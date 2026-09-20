using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>Input for <see cref="Interfaces.IExternalSignalRepository.RecordAsync"/>.</summary>
public sealed class RecordExternalSignalRequest
{
    /// <summary>Owner recording the signal.</summary>
    public required string OwnerId { get; init; }

    /// <summary>Soft reference to the namespace this signal concerns. Null means fleet-wide.</summary>
    public Guid? NamespaceId { get; init; }

    /// <summary>The kind of signal.</summary>
    public required ExternalSignalType SignalType { get; init; }

    /// <summary>When the signal actually happened.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Where the signal came from, e.g. <c>"webhook:github-actions"</c>, <c>"manual"</c>.</summary>
    public required string Source { get; init; }

    /// <summary>Free-form detail (e.g. commit SHA, deploy ID) — never a message body.</summary>
    public string? DetailJson { get; init; }
}
