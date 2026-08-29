namespace ServiceHub.Core.Models;

/// <summary>
/// A producer-facing report packaging one namespace's P2 drift findings as contract violations,
/// worded for the upstream team that can fix the root cause rather than for ServiceHub operators
/// (roadmap §5.D, P3 — "Producer export").
/// </summary>
/// <param name="NamespaceId">The namespace the findings were detected in.</param>
/// <param name="NamespaceName">The namespace's display name.</param>
/// <param name="StartTime">The analysis window start.</param>
/// <param name="EndTime">The analysis window end.</param>
/// <param name="GeneratedAt">When this report was generated.</param>
/// <param name="Violations">The contract violations included in the report, most severe first.</param>
/// <param name="MarkdownReport">The full report rendered as Markdown, ready to hand to a producer team.</param>
public sealed record ContractViolationReport(
    Guid NamespaceId,
    string NamespaceName,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ContractViolationEntry> Violations,
    string MarkdownReport);

/// <summary>
/// One entity's contract violation, described in producer-facing language rather than
/// ServiceHub-internal terms (no raw severity score, no enum names).
/// </summary>
/// <param name="EntityName">The queue, topic, or subscription whose contract changed.</param>
/// <param name="ViolationType">A plain-English description of what kind of change was detected.</param>
/// <param name="Priority">"High", "Medium", or "Low" — banded from the underlying finding's severity.</param>
/// <param name="Evidence">The concrete evidence backing this finding.</param>
/// <param name="SuggestedFixes">Actions the producer team can take to resolve or confirm the change.</param>
public sealed record ContractViolationEntry(
    string EntityName,
    string ViolationType,
    string Priority,
    string Evidence,
    IReadOnlyList<string> SuggestedFixes);
