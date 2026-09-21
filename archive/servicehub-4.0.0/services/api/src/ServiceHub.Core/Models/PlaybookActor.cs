using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// A resolved actor for the Playbook Ledger — deliberately a separate type from
/// <see cref="RecoveryActor"/>, not a reuse of it: <see cref="Kind"/> is <see cref="PlaybookActorKind"/>,
/// which includes <see cref="PlaybookActorKind.ReasoningAgent"/>, a value <see cref="RecoveryActorKind"/>
/// must never gain. Every <c>IPlaybookLedger</c> method takes one of these, never a bare string —
/// the identity must always be resolved server-side, never caller-supplied.
/// </summary>
public sealed record PlaybookActor(string Identity, PlaybookActorKind Kind);
