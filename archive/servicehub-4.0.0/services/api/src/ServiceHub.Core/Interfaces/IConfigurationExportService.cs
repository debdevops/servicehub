using System.Text.Json;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Configuration as code (roadmap next-chapter M5.4) — round-trip export/import of an owner's
/// automation policy and access grants, so an enterprise can keep "who can do what" in git with a
/// pull request on it. Scoped deliberately narrowly:
/// </summary>
/// <remarks>
/// <para><b>In scope:</b> <c>AutoReplayRule</c> (automation policy) and <c>GovernanceGrant</c>
/// (access grants) — both genuinely mutable configuration, the same tier as
/// <c>AutoReplayRule.Enabled</c> or a role assignment.</para>
/// <para><b>Deliberately out of scope, and must stay that way:</b></para>
/// <list type="bullet">
/// <item><description><b>Namespace connection details.</b> <c>Namespace.ConnectionString</c> is an
/// encrypted credential; exporting it into a file meant for git review would be a real secret
/// leak, not a convenience. Namespaces appear here only as a read-only <see cref="NamespaceReference"/>
/// list — enough to make an exported rule/grant's <c>NamespaceId</c> human-readable — and are
/// never created or modified by import. Connecting a namespace stays the existing Connect flow.</description></item>
/// <item><description><b>Ledger events and pillar findings.</b> A <c>PreventionRule</c> is a
/// <c>PlaybookEntry</c> — a hash-chained ledger claim, not configuration — and
/// <c>Anomaly</c>/<c>DriftFinding</c>/etc. are observations. Importing either would fabricate
/// evidence that was never actually observed or disposed, which is on the never-build list. This
/// service touches only <c>AutoReplayRules</c> and <c>GovernanceGrants</c>, never
/// <c>RecoveryEvents</c>, <c>PlaybookEntries</c>/<c>PlaybookEvents</c>, or any pillar finding
/// table.</description></item>
/// </list>
/// <para>Import is additive/upsert only: a rule already present (matched by name) is updated in
/// place; a grant already active (matched by grantee/namespace/pillar/role) is left alone. Nothing
/// present in the live system but absent from the imported file is ever deleted or revoked —
/// removing access is deliberately still a manual, explicit act via the existing revoke
/// endpoint.</para>
/// </remarks>
public interface IConfigurationExportService
{
    /// <summary>Exports every <c>AutoReplayRule</c> and active <c>GovernanceGrant</c> for
    /// <paramref name="ownerId"/>, plus a read-only namespace reference list.</summary>
    Task<ConfigurationBundle> ExportAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a previously-exported (and possibly hand-edited) <see cref="ConfigurationBundle"/>
    /// back to <paramref name="ownerId"/>'s configuration. Every rule referencing a
    /// <c>NamespaceId</c> that does not resolve to a namespace this owner actually has is skipped
    /// with a warning, never applied against a dangling reference.
    /// </summary>
    Task<Result<ConfigurationImportResult>> ImportAsync(
        string ownerId,
        ConfigurationBundle bundle,
        RecoveryActor actor,
        CancellationToken cancellationToken = default);
}

/// <summary>Read-only reference so an exported rule/grant's <c>NamespaceId</c> is human-readable.
/// Never used by import to create or modify a namespace.</summary>
public sealed record NamespaceReference(
    Guid Id,
    string Name,
    string? DisplayName,
    string Environment,
    string Provider);

/// <summary>One <c>AutoReplayRule</c>'s configuration. <c>Conditions</c>/<c>Action</c> are kept as
/// raw <see cref="JsonElement"/>s (not re-typed) so the exported file shows real nested JSON
/// rather than an escaped string blob, and so this service never has to track every condition/
/// action shape <c>RuleEngine</c> understands.</summary>
public sealed record AutoReplayRuleConfig(
    string Name,
    string? Description,
    Guid? NamespaceId,
    bool Enabled,
    int MaxReplaysPerHour,
    JsonElement Conditions,
    JsonElement Action);

/// <summary>One active <c>GovernanceGrant</c>'s configuration.</summary>
public sealed record GovernanceGrantConfig(
    string GranteeIdentity,
    string GranteeKind,
    string Role,
    Guid? NamespaceId,
    string? PillarKind);

/// <summary>The full exportable/importable configuration for one owner.</summary>
public sealed record ConfigurationBundle(
    string OwnerId,
    DateTimeOffset ExportedAtUtc,
    IReadOnlyList<NamespaceReference> Namespaces,
    IReadOnlyList<AutoReplayRuleConfig> AutoReplayRules,
    IReadOnlyList<GovernanceGrantConfig> GovernanceGrants);

/// <summary>Outcome of one <see cref="IConfigurationExportService.ImportAsync"/> call.</summary>
public sealed record ConfigurationImportResult(
    int RulesCreated,
    int RulesUpdated,
    int RulesUnchanged,
    int GrantsCreated,
    int GrantsAlreadyActive,
    IReadOnlyList<string> Warnings);
