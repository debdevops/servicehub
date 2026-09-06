# ADR-0009: Next-chapter migrations authorized (M1 pillar evidence, M2 production elevation)

**Status:** Accepted — 2026-09-06

## Context

[ADR-0006](0006-rc1-migration-freeze.md) froze all EF Core migrations against `DlqDbContext` during
RC1 stabilization, liftable only by the same explicit, dated user sign-off that confirmed it — never
by inference, and never by a migration being judged safe on its own merits.
[ADR-0007](0007-persistence-wave-m1-m4-authorized.md) superseded it for exactly four units;
[ADR-0008](0008-m5-external-signal-events-authorized.md) for exactly one more. Both were narrow,
both named their units exactly, and both stated that they did not pre-authorize anything else.

`docs-private/SERVICEHUB-NEXT-CHAPTER-ROADMAP-2026-09-06.md` defines the chapter that follows the
now-closed autonomy work. Two of its five milestones require schema changes:

- **M1** makes the Investigate, Correlate and Prevent pillars' evidence durable. Six
  `IXxxResultCache` interfaces are today implemented by process-local, TTL-bounded dictionaries
  (`Analytics/InMemory*ResultCache.cs`). The consequence is not merely that findings are lost on
  restart: `PreventionRuleEvaluationService` writes `DriftFindingId` into `PlaybookEntry.EvidenceRefJson`,
  which is append-only and permanent, so the hash-chained Playbook Ledger already contains citations
  to rows that stop resolving after 24 hours. That is an evidence-integrity defect in the one
  property the architecture exists to protect, and it cannot be fixed without tables.
- **M1.4** additionally splits the two vocabularies currently sharing
  `NamespaceSignatures.SignatureHash` — the trust fingerprint and the cluster hash — which the
  2026-09-01 audit left open as a decision (§14.4) and which the 2026-09-05 L5 run hit concretely
  when a replay operation's `signatureHashSnapshot` differed from the cluster hash used to target it.
- **M2** opens production namespaces under an elevation model, which needs a durable record of who
  elevated what, why, and until when.

The freeze status and both open decisions were put to the user on 2026-09-06 and answered
explicitly: sign off now, scoped to M1 and M2; resolve the identity spaces with a `HashKind`
discriminator.

## Decision

**ADR-0006's freeze is superseded for the migration units named below, and for nothing else.**

### Authorized now — M1

1. **Six findings tables**, one per currently-ephemeral pillar entity: `Anomaly`, `DriftFinding`,
   `CorrelationFinding`, `Narration`, `BacklogForecast`, `ExternalSignalCorrelation`. Each gains a
   `DbSet` on `DlqDbContext` and a table keyed on its existing `Id` (Guid, PK).
   - **`OwnerId` (text) is added to `Anomaly`, `DriftFinding` and `BacklogForecast`**, which carry
     `NamespaceId` but no owner today. Every other table in this schema is owner-partitioned and
     every read path filters on it; a findings table without it is a tenant-isolation defect.
     `CorrelationFinding` already carries owner scoping.
     > **Correction, recorded during M1.1's implementation (2026-09-06):** this line originally
     > also named `Narration` as already owner-scoped. That was wrong — `Narration` has no
     > `OwnerId` property at all, on any commit. Its own tenant-isolation key is
     > `AccessNamespaceIds` (a cross-namespace correlation narration has no single owner, per the
     > entity's own remarks), so no `OwnerId` column was added to it in the migration this ADR
     > authorizes — adding one that no read path would ever use would be schema without a purpose.
   - Collection properties (`Metrics`, `RecommendedActions`, `Providers`, `Members`, and
     `Narration`'s three `Contributing*Ids` lists) are stored as JSON columns via EF value
     converters — the same treatment `RecoveryEvent.DetailJson` and `PlaybookEntry.EvidenceRefJson`
     already get. No child tables: these are read whole and never queried into.
   - `NamespaceId` stays a **soft reference with no FK**, matching every other ledger-adjacent
     `NamespaceId` in this schema (`NamespaceSharedOwners` remains the one deliberate real FK).
     Extends `RecoveryLedgerNoForeignKeyTests`' scan.
   - **No hash chain.** These are observations the system made, not claims about what happened. The
     ledger-grade tamper-evidence that applies to `RecoveryEvent` and `PlaybookEvent` does not apply
     here, and adding it would misrepresent a detector's output as a disposition.
   - Non-unique index on `(OwnerId, NamespaceId, DetectedAt)` per table (`GeneratedAt` for
     `Narration`), serving both the read paths and the M1.2 retention sweep.
   - `Down` is a standard `DROP TABLE` per unit, safe because nothing else in the schema references
     these tables.

2. **`NamespaceSignatures.HashKind`** (int, non-nullable, enum `Fingerprint | Cluster`) — the
   discriminator resolving §14.4's two identity spaces. Backfilled by re-deriving each existing
   row's hash with both `FailureFingerprintBuilder` and `ClusterSignatureHasher`; a row matching
   neither is written as `Fingerprint` and logged at warning, never silently dropped.

   **This migration must not change fingerprint identity.** Every earned `AutonomyGrant` is keyed on
   it and all of them orphan if it shifts. The pinned regression on
   `aac7b240aa9569aacc7798817065e2f8a9dd24f176083c50f0ea869a19f64b05` stays unmodified and must pass
   after the migration. `Down` drops the column; the two spaces re-conflate, which is the current
   state, so the rollback is lossy in precision but not in data.

### Authorized conditionally — M2

3. **The production elevation record**, as specified in
   [ADR-0010](0010-production-namespace-elevation.md) §Decision.

   **No migration may be written for this unit while ADR-0010 reads `Proposed`.** This ADR lifts the
   freeze for it so that accepting ADR-0010 is the only remaining gate — it does not authorize a
   schema nobody has fixed yet. Pre-authorizing a shape ahead of its design is exactly what
   ADR-0008's second rejected alternative refused to do, and the same reasoning applies here.

   > **Condition satisfied 2026-09-06.** ADR-0010 was accepted by explicit user sign-off the same
   > day this ADR was written. Unit 3 is now authorized unconditionally, bounded by the schema fixed
   > in ADR-0010 §Decision — the elevation's subject, namespace, stated reason, requester, approver,
   > opening and absolute expiry, and revocation state, plus the four `RecoveryEventType` members
   > that record its lifecycle in the existing per-owner hash chain. Nothing beyond that shape is
   > authorized by this note.

No other migration is authorized by this decision. M3, M4 and M5 are all deliberately outside it:
none of the three needs a schema change as scoped, and if one turns out to (M5.2's epoch sealing is
the likely candidate), it requires its own sign-off recorded the same way.

## Consequences

- [ADR-0006](0006-rc1-migration-freeze.md)'s Status gains this ADR alongside 0007 and 0008. It
  remains the operative freeze for every migration not named in one of the three.
- M1 is unblocked and can begin immediately. M2's schema work is unblocked only once ADR-0010 is
  Accepted.
- The Playbook Ledger stops carrying citations that dangle, which is the specific condition that
  makes exit statement 9 — *an external auditor, given only the exports, can reconstruct why the
  system trusted what it trusted* — currently true for the Recover pillar and false for the other
  three.
- Any migration beyond the units above still requires the same explicit, dated sign-off this ADR,
  ADR-0007 and ADR-0008 all followed. This is a scoped lift for two milestones, not a general
  resumption of routine migrations.

## Alternatives considered

- **Keep the freeze and make findings durable without a schema change** — a JSON sidecar file, or
  reusing `PlaybookEntry` rows as the storage for findings. Rejected: the first re-introduces the
  two-store split that M2 of the persistence wave existed to remove (`NamespaceStoreImporter`), and
  the second would put detector output into a hash-chained append-only ledger, which
  misrepresents an observation as a disposition and permanently inflates a structure that cannot be
  pruned.
- **Lift the freeze generally, since RC1 has long shipped.** Rejected on the same grounds ADR-0006
  itself rejected it: the freeze does not lapse by the passage of time or by a reading of "probably
  fine". A scoped, dated, unit-naming lift is auditable; a general resumption is not, and the
  discipline is the reason the last two waves landed without schema surprises.
- **Fold M2's elevation record into this decision with a schema sketched now.** Rejected — see the
  conditional clause above. ADR-0010 is where that shape gets fixed, under its own safety review.
- **Resolve the identity spaces by unifying on the fingerprint hash instead of adding a
  discriminator** (§14.4 option 2). Considered and put to the user: it needs no schema change, but it
  changes every signature URL and settles "what is a signature" as a product noun in a way that is
  hard to walk back. The discriminator keeps both vocabularies addressable and leaves fingerprint
  identity — the thing autonomy grants depend on — provably untouched.
