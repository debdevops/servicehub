// Ported from ServiceHub 4.0.0
//   source: archive/servicehub-4.0.0/services/api/src/ServiceHub.Core/Models/RecoveryActor.cs
//   copied: 2026-09-21 for unit 0.5
//   changes: none

using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// A resolved recovery actor — the only way an actor identity enters the Recovery Evidence
/// Ledger. No method on <c>IRecoveryLedger</c> accepts a bare, caller-supplied identity string;
/// every caller must construct this via <c>ActorIdentityResolver</c> first.
/// </summary>
/// <param name="Identity">Server-derived actor identity, e.g. <c>ApiKey:ops-bot</c>,
/// <c>user@example.com</c>, or <c>Rule:42@drain-poison-queue</c>.</param>
/// <param name="Kind">The category of actor.</param>
/// <param name="Scopes">Granted scopes at decision time, for API key actors.</param>
public sealed record RecoveryActor(string Identity, RecoveryActorKind Kind, string? Scopes = null);
