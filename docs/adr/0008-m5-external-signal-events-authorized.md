# ADR-0008: M5 (`ExternalSignalEvents`) authorized for C3 — ADR-0006 superseded, narrowly

**Status:** Accepted

## Context

[ADR-0006](0006-rc1-migration-freeze.md) froze all EF Core migrations against `DlqDbContext`
during RC1 stabilization, lifted only by the same explicit, dated user sign-off that confirmed it.
[ADR-0007](0007-persistence-wave-m1-m4-authorized.md) superseded that freeze for exactly four
migration units (M1–M4) and was explicit that it did **not** extend to M5
(`ExternalSignalEvents`, needed for C3 — external-signal correlation): "M5 remains gated behind
ADR-0006 until C3 is actually scheduled and a separate sign-off is sought for it — this decision
does not pre-authorize it."

`docs-private/PERSISTENCE-EVOLUTION-DESIGN-2026-08-29.md` §1.6 already fully designed M5 at the
time ADR-0007 was written — schema, index, and rollback were specified then and deliberately not
built, "flagged so the eventual C3 implementer doesn't have to re-derive this from scratch." That
design has not changed since. The tree was re-verified against it immediately before this decision:
no migration has landed against `DlqDbContext` since ADR-0007's four (confirmed via
`git log -- '**/Migrations/*.cs'`), and §1.6's column list, index, and "no hash chain" rationale
match exactly what this ADR authorizes below.

C3 (external-signal correlation) has now been scheduled and is being implemented alongside this
ADR — the trigger ADR-0007 named as the precondition for revisiting this freeze.

## Decision

**ADR-0006's freeze is superseded for exactly one additional migration unit:**

- **M5** — add the `ExternalSignalEvents` table: `Id` (Guid, PK), `OwnerId` (text), `NamespaceId`
  (Guid?, soft reference — null means fleet-wide), `SignalType` (text enum: `Deploy` /
  `ConfigChange` / `Custom`), `OccurredAt` (DateTimeOffset), `Source` (text), `DetailJson`
  (text?, `LogRedactor`-passed), `IngestedAt` (DateTimeOffset). Non-unique index on
  `(OwnerId, NamespaceId, OccurredAt)`. No FK to `Namespaces` (soft reference, same convention as
  every other `NamespaceId` column in this schema) and no hash chain — this is raw external input,
  not a system claim, so ledger-grade tamper-evidence does not apply the way it does to
  `RecoveryEvent`/`PlaybookEvent`. `Down` is a standard `DROP TABLE`, safe because nothing else in
  the schema references this table (extends `RecoveryLedgerNoForeignKeyTests`' scan).

No other migration is authorized by this decision. This is a narrower lift than ADR-0007: it
authorizes one unit, for one already-scheduled feature (C3), not a wave.

Full schema detail is not restated beyond the summary above; it lives in
`PERSISTENCE-EVOLUTION-DESIGN-2026-08-29.md` §1.6 and is not modified by this ADR.

## Consequences

- [ADR-0006](0006-rc1-migration-freeze.md)'s Status changes to `Superseded for the M1–M4 wave
  (ADR-0007) and M5 (this ADR)`. It remains the operative freeze for any migration not named in
  either decision.
- C3 (external-signal correlation) is unblocked: `IExternalSignalRepository` durably records
  deploy/config-change signals, and `IExternalSignalCorrelationService` correlates anomaly onset
  against them within a bounded window — the last gap in the Correlate pillar's proactive-detection
  story (C1/C2/C4 already shipped).
- Any migration beyond M5 still requires the same explicit, dated sign-off process this ADR and
  ADR-0007 both followed — this decision authorizes one named unit, not a general resumption of
  routine migrations.

## Alternatives considered

- **Treat ADR-0007's M1–M4 authorization as implicitly covering M5 too, since it's the same design
  document.** Rejected: ADR-0007 explicitly excluded M5 by name and required "a separate sign-off"
  for it — inferring coverage from the shared document would be exactly the kind of silent
  extension both ADR-0006 and ADR-0007 were written to prevent.
- **Fold M5 into a broader freeze lift covering "whatever schema C3 eventually needs," ahead of
  implementation.** Rejected: the design was already fixed in §1.6 before this decision: there is
  nothing speculative left to pre-authorize, so a narrow, exact-unit decision (mirroring ADR-0007's
  own discipline) is both sufficient and more auditable than a broader grant.
