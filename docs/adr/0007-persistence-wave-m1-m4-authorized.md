# ADR-0007: Persistence wave M1–M4 authorized — ADR-0006 superseded

**Status:** Accepted

## Context

[ADR-0006](0006-rc1-migration-freeze.md) froze all EF Core migrations against `DlqDbContext` during
RC1 stabilization, lifted only by the same explicit, dated user sign-off that confirmed it — never
by inference from elapsed time, RC1 "probably" shipping, or a migration being judged low-risk on its
own merits.

`docs-private/PERSISTENCE-EVOLUTION-DESIGN-2026-08-29.md` (gitignored, local planning document; not
tracked in this repository) consolidated every durable-state evolution then known — `AutoReplayRule`
namespace scoping (M1), namespace/provider JSON→SQLite consolidation (M2), Governance/RBAC (M3), and
the Playbook Ledger (M4) — into one design, and its own §20 defined the concrete, non-inferable
criterion for when ADR-0006 could legitimately be revisited. That criterion was applied in full on
2026-08-29:

1. The design document was presented to the user in full (not summarized or excerpted) immediately
   before the sign-off conversation.
2. The tree was re-verified against the document's own inventory at that moment: 16 `DbSet`s across
   12 migrations in `DlqDbContext`, unchanged from the document's stated baseline. Everything shipped
   since the document's authoring (I4 narration, I5 proactive push, P3 contract-violation export, P4
   backlog forecasting, C2 cross-cloud correlation) was confirmed to have added zero migrations —
   cache/read-side only, consistent with the document's own §1.7 classification.
3. The user's approval explicitly named **all of M1–M4** as authorized (not M5, which the design
   document itself defers until C3 is scheduled).
4. This ADR is that dated decision, recorded per point 4 of the design document's §20.
5. The migration order to be followed is the design document's own §4 ordering: **M1 → M2 → M3 → M4**,
   with no deviation approved.

## Decision

**ADR-0006's freeze is superseded for exactly the following four migration units, in exactly this
order:**

- **M1** — add nullable `AutoReplayRule.NamespaceId` (Guid?).
- **M2** — add `Namespaces` and `NamespaceSharedOwners` tables; one-shot, forward-only cutover from
  `servicehub-namespaces.json` to SQLite via a new `SqliteNamespaceRepository`; source JSON renamed
  to `.migrated`, never deleted.
- **M3** — add `GovernanceGrants` table (per-user/API-key, per-namespace, per-pillar role model);
  seeded from existing owner/sharing data so no account loses access it has today.
- **M4** — add `PlaybookEntries` and `PlaybookEvents` tables (the Playbook Ledger), append-only,
  independently hash-chained from the Recovery Evidence Ledger, with its own append-only guard and
  no-foreign-key architecture tests.

No other migration is authorized by this decision. In particular, **M5** (`ExternalSignalEvents`,
for C3) remains gated behind ADR-0006 until C3 is actually scheduled and a separate sign-off is
sought for it — this decision does not pre-authorize it.

Full schema, index, backfill, rollback, and testing detail for M1–M4 is not restated here; it lives
in the design document referenced above and is not modified by this ADR. Implementation must follow
that document's specification for each migration unit unless a future dated decision explicitly
revises it.

## Consequences

- [ADR-0006](0006-rc1-migration-freeze.md)'s Status changes to `Superseded`, linking here, for the
  four migration units named above. ADR-0006 remains the operative freeze for any migration *not*
  named in this decision — including M5 and any future schema change not yet designed.
- Work previously blocked on ADR-0006 (`AutoReplayRule.NamespaceId` scoping, Governance/RBAC, the
  Playbook Ledger, and everything downstream of the Playbook Ledger — C4 correlation accountability,
  counterfactual backtesting, and the reasoning companion's only legal write surface per ADR-0005) is
  now unblocked, in the order M1 → M2 → M3 → M4.
- A successful, integrity-checked backup (`POST /api/v1/admin/backup`) is a mandatory precondition
  before this wave is applied to any real operator's data directory, per the design document's §9 —
  this ADR does not waive that gate.
- Any migration beyond these four still requires the same explicit, dated sign-off process this ADR
  itself followed — this decision authorizes this wave, not a general resumption of routine
  migrations.

## Alternatives considered

- **Authorize a subset (e.g. M1–M3, deferring M4).** Rejected by the user's explicit choice — "all of
  M1–M4" was the sign-off given, not a partial one.
- **Treat this as a blanket freeze lift.** Rejected: the design document's §20 point 3 requires naming
  exactly which units are authorized; M5 is deliberately excluded here and stays gated.
