namespace ServiceHub.Core.Models;

/// <summary>
/// The honesty contract of a Recovery Evidence Ledger export (roadmap §16.3) — what ServiceHub
/// knows, what it observed, and, non-negotiably, what it does not know. Every field here is
/// derived from durable ledger state; nothing is inferred or assumed.
/// </summary>
public sealed class RecoveryEvidenceManifest
{
    /// <summary>Shape version of this manifest. Starts at 1.</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>ServiceHub assembly version at export time.</summary>
    public required string ServiceVersion { get; init; }

    /// <summary>When this export was generated. The one of two fields allowed to differ between
    /// two exports of the same, unchanged operation (see <see cref="ExportedBy"/>).</summary>
    public required DateTimeOffset ExportedAt { get; init; }

    /// <summary>Server-derived identity of the caller who requested this export.</summary>
    public required string ExportedBy { get; init; }

    /// <summary>Owner ID the exported operation belongs to.</summary>
    public required string OwnerId { get; init; }

    /// <summary>The exported operation's ID.</summary>
    public required Guid OperationId { get; init; }

    /// <summary>Hash-chain integrity summary — see <see cref="RecoveryEvidenceChainSummary"/>.</summary>
    public required RecoveryEvidenceChainSummary Chain { get; init; }

    /// <summary>Entry count by <c>RecoveryEntryState</c> name, including zero counts for states
    /// with no entries — so a reader never has to wonder whether a state was omitted or simply
    /// empty.</summary>
    public required IReadOnlyDictionary<string, int> Counts { get; init; }

    /// <summary>What this export can attest to, in ServiceHub's own words.</summary>
    public required IReadOnlyList<string> WhatServiceHubKnows { get; init; }

    /// <summary>What ServiceHub's DLQ scans actually observed for these entries.</summary>
    public required IReadOnlyList<string> WhatServiceHubObserved { get; init; }

    /// <summary>
    /// What this export explicitly does not, and cannot, establish. Never empty — a ServiceHub
    /// evidence package that claimed to know everything would be a worse product than one that
    /// says nothing at all.
    /// </summary>
    public required IReadOnlyList<string> WhatServiceHubDoesNotKnow { get; init; }

    /// <summary>Provider- or coverage-driven limitations that apply to a subset of this
    /// operation's entries. Empty when none apply — unlike <see cref="WhatServiceHubDoesNotKnow"/>,
    /// this list has no non-empty requirement.</summary>
    public required IReadOnlyList<RecoveryEvidenceLimitation> Limitations { get; init; }

    /// <summary>True when this export was generated from Demo Mode fixtures, never from real
    /// ledger evidence. Demo Mode never presents fabricated data as real evidence (roadmap §20).</summary>
    public bool DemoMode { get; init; }
}

/// <summary>Hash-chain integrity summary for one export — see <c>IRecoveryLedger.VerifyChainAsync</c>.</summary>
public sealed class RecoveryEvidenceChainSummary
{
    /// <summary>Always <c>"owner"</c> — the chain is partitioned per owner, not per operation
    /// (roadmap §5.5); verifying one operation's evidence necessarily verifies its owner's whole
    /// chain up to the present.</summary>
    public required string Scope { get; init; }

    /// <summary>The lowest <c>Seq</c> among this operation's events.</summary>
    public required long FirstSeq { get; init; }

    /// <summary>The highest <c>Seq</c> among this operation's events.</summary>
    public required long LastSeq { get; init; }

    /// <summary>Whether the owner's entire chain, recomputed end to end, is intact.</summary>
    public required bool Verified { get; init; }

    /// <summary>Human-readable description of the hashing algorithm, for an auditor working from
    /// this export alone.</summary>
    public required string Algorithm { get; init; }

    /// <summary>Always <c>true</c>. The chain is tamper-EVIDENT, not tamper-PROOF — anyone with
    /// write access to the underlying SQLite file can recompute it. This field ships in the
    /// manifest so the distinction travels with the evidence, not just the documentation.</summary>
    public required bool TamperEvidentNotTamperProof { get; init; }
}

/// <summary>One provider- or coverage-driven limitation affecting a subset of an export's entries.</summary>
public sealed class RecoveryEvidenceLimitation
{
    /// <summary>Short, stable machine-readable code, e.g. <c>AWS_NO_ABSENCE_PROOF</c>.</summary>
    public required string Code { get; init; }

    /// <summary>Number of entries in this export this limitation applies to.</summary>
    public required int AppliesToEntries { get; init; }

    /// <summary>Human-readable explanation, suitable for an auditor with no ServiceHub context.</summary>
    public required string Text { get; init; }
}
