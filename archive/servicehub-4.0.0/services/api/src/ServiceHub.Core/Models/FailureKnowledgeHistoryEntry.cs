namespace ServiceHub.Core.Models;

/// <summary>
/// A prior version of a <see cref="FailureKnowledge"/> record, as it stood before being
/// superseded by a later edit.
/// </summary>
public sealed record FailureKnowledgeHistoryEntry(
    int KnowledgeVersion,
    string? RootCause,
    string? ResolutionNotes,
    string? OperationalNotes,
    string? RunbookLink,
    string? Owner,
    string? ReplayGuidance,
    string? Tags,
    DateTimeOffset? ReviewDueAt,
    string? UpdatedBy,
    DateTimeOffset UpdatedAt);
