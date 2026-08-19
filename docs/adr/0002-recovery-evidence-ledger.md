# ADR-0002: Recovery Evidence Ledger — append-only hash chain, not a mutable status field

**Status:** Accepted

## Context

Replay and purge are ServiceHub's only mutating operations. Once a message is replayed, the
question an operator or auditor actually needs answered later is not "did the API call succeed"
but "did this specific dead-lettered message genuinely get recovered, and how do we know" — a
question that requires evidence collected *after* the call returns (did it come back to the DLQ?),
not just at the moment it was made. A simple `ReplayHistory.Status` column answers "did the broker
accept the call," which the codebase's own internal documentation explicitly warns is a different,
weaker claim than "was the message actually recovered."

Providers differ in how well they can answer the harder question. Azure can page a DLQ uncapped (up
to 5,000 messages/cycle) and can therefore genuinely prove a message never came back. AWS's
background scanning is off by default and every peek is a destructive, capped receive; GCP's
reconciliation scan is capped per cycle. A capped sample can never prove absence — it can only fail
to find a recurrence, which is a materially weaker claim.

## Decision

Record every replay/purge attempt as a durable, append-only chain of evidence, not a mutable
status:

- `RecoveryLedgerEntry` — one row per (operation, message), with an immutable identity/context
  snapshot (namespace, entity, body hash, failure signature, …) taken at the moment the attempt
  begins, and a small mutable projection (state, disposition, verification result) that can only
  change through `IRecoveryLedger`.
- `RecoveryEvent` — the actual evidence, one row per state transition. Each event's hash is
  `SHA256(canonical(fields excluding EntryHash) || PrevHash)`, chaining every event in an owner's
  history together. Tampering with, or deleting, an earlier event breaks the chain for everything
  after it.
- Append-only is enforced at the persistence layer (a guard inside `DbContext.SaveChangesAsync`),
  not by code review discipline, and a dedicated IL-scanning test (`RecoveryPathCoverageTests`)
  fails the build if any replay/purge code path doesn't also write to the ledger in the same
  method — with an empty exemption list by design.
- Verification consults `ProviderCapabilities.CanProveDlqAbsence` to decide between the honest
  terminal states `Recovered` ("did not return to the DLQ within the observation window — never
  'the business transaction completed'") and `Unverified` (the observation window closed without
  adequate coverage to make that claim) — with the specific reason recorded, not just the state.
  `Unverified` is a first-class, intended result on AWS and GCP today, not a defect to be
  engineered away.
- An independent, dependency-free verifier (`scripts/verify-recovery-chain.py`) recomputes the same
  hash and can validate an exported chain with no ServiceHub server involved, so "tamper-evident"
  is checkable by a third party, not only by the system being audited.

## Alternatives considered

- **A mutable `Status` enum on the replay record, updated in place.** Rejected: this is what a
  simpler design would do, and it cannot represent "verification is still open" versus "verification
  concluded, honestly, that we don't know" without losing the history of how it got there — the
  exact ambiguity this ledger exists to remove.
- **Cryptographic signing / a Merkle tree / WORM storage.** Considered and deferred, not rejected
  outright: signing needs a key-management story (whose key, rotated how, verified against what
  distribution) that no current deployment has asked for; a Merkle tree solves sub-linear
  verification of one leaf among millions, which doesn't match this ledger's actual access pattern
  (per-owner sequential chains, verified in full). "Tamper-evident, not tamper-proof" is treated as
  an honest current position, revisited if a real compliance requirement names a stronger bar.
- **A 3-state absence enum** (`DeterministicAbsence` / `SignatureScopedVerification` /
  `NoSafeObservation`) instead of the boolean `CanProveDlqAbsence`. Deferred: only two real states
  exist among three providers today (Azure proves absence; AWS/GCP cannot). The candidate third
  state — GCP's capped-but-real scan versus AWS's fully-off-by-default scan — is documented as a
  textual distinction in `docs/RECOVERY-EVIDENCE.md` rather than a new enum value, until a provider
  genuinely needs the extra bucket.

## Consequences

- Every recovery claim ServiceHub makes is traceable to a specific, hash-chained event — including
  the honest admission that a claim can't be made for a given provider.
- Exports (`docs/RECOVERY-EVIDENCE.md`) are self-describing about their own limitations by
  construction (`whatServiceHubDoesNotKnow` is non-empty on every export), not by convention.
- The append-only model means correcting a mistaken entry requires a new event (a written-off or
  declined terminal state), never an edit — this is the cost of tamper-evidence, paid deliberately.
- A single-instance, single-owner-chain design is what makes full-chain re-verification tractable
  without a Merkle structure; if ServiceHub ever became multi-instance, this decision would need
  re-examination (see [ADR-0003](0003-single-instance-sqlite.md) for why that isn't planned).
