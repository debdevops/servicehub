using System.Text;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.Analytics;

/// <summary>
/// Deterministic, template-based implementation of <see cref="IContractViolationExportService"/>
/// (roadmap §5.D, P3 — "Producer export"). Translates P1/P2's internal drift findings into a
/// report addressed to the upstream producer team that can fix the root cause, in plain language
/// — no ServiceHub-internal jargon (raw severity scores, enum names), no ML, no LLM.
/// </summary>
public sealed class DeterministicContractViolationExportService : IContractViolationExportService
{
    /// <summary>Severity (0-100) at or above which a violation is banded "High" priority.</summary>
    private const int HighPriorityThreshold = 70;

    /// <summary>Severity (0-100) at or above which a violation is banded "Medium" priority.</summary>
    private const int MediumPriorityThreshold = 40;

    /// <inheritdoc />
    public ContractViolationReport BuildReport(
        Namespace @namespace,
        IReadOnlyList<DriftFinding> findings,
        DateTimeOffset startTime,
        DateTimeOffset endTime)
    {
        ArgumentNullException.ThrowIfNull(@namespace);
        ArgumentNullException.ThrowIfNull(findings);

        var generatedAt = DateTimeOffset.UtcNow;

        var violations = findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.EntityName, StringComparer.Ordinal)
            .Select(BuildEntry)
            .ToList();

        var markdown = BuildMarkdown(@namespace, violations, startTime, endTime, generatedAt);

        return new ContractViolationReport(
            @namespace.Id,
            @namespace.Name,
            startTime,
            endTime,
            generatedAt,
            violations,
            markdown);
    }

    private static ContractViolationEntry BuildEntry(DriftFinding finding)
    {
        var violationType = finding.Type switch
        {
            DriftFindingType.SchemaShapeDrift => "Message field shape changed",
            DriftFindingType.PayloadFormatDrift => "Message payload format changed",
            _ => "Message contract changed",
        };

        var priority = finding.Severity switch
        {
            >= HighPriorityThreshold => "High",
            >= MediumPriorityThreshold => "Medium",
            _ => "Low",
        };

        return new ContractViolationEntry(
            finding.EntityName,
            violationType,
            priority,
            finding.Description,
            finding.RecommendedActions);
    }

    private static string BuildMarkdown(
        Namespace @namespace,
        IReadOnlyList<ContractViolationEntry> violations,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        DateTimeOffset generatedAt)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# Contract Violation Report — {@namespace.Name}");
        sb.AppendLine();
        sb.AppendLine($"Generated: {generatedAt:u}");
        sb.AppendLine($"Analysis window: {startTime:u} to {endTime:u}");
        sb.AppendLine();

        if (violations.Count == 0)
        {
            sb.AppendLine("No contract violations were detected in this window.");
            return sb.ToString();
        }

        var entityCount = violations.Select(v => v.EntityName).Distinct(StringComparer.Ordinal).Count();

        sb.AppendLine(
            $"{violations.Count} contract violation(s) detected across {entityCount} " +
            $"entit{(entityCount == 1 ? "y" : "ies")} in this window. Each item below names the " +
            "affected entity, what changed, and a suggested fix for the team that owns the producer.");
        sb.AppendLine();
        sb.AppendLine("## Findings");
        sb.AppendLine();

        foreach (var violation in violations)
        {
            sb.AppendLine($"### {violation.EntityName} — {violation.Priority} priority");
            sb.AppendLine();
            sb.AppendLine($"**Type:** {violation.ViolationType}");
            sb.AppendLine();
            sb.AppendLine($"**Evidence:** {violation.Evidence}");
            sb.AppendLine();

            if (violation.SuggestedFixes.Count > 0)
            {
                sb.AppendLine("**Suggested fix:**");
                foreach (var fix in violation.SuggestedFixes)
                {
                    sb.AppendLine($"- {fix}");
                }

                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
