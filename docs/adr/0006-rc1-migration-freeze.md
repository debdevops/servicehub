# ADR-0006: RC1 migration freeze — active, and confirmed by explicit sign-off only

**Status:** Superseded for the M1–M4 persistence wave by
[ADR-0007](0007-persistence-wave-m1-m4-authorized.md) (2026-08-29). This ADR remains the operative
freeze for any migration not explicitly named in ADR-0007 — including M5 and any future schema
change not yet designed.

## Context

EF Core migrations against the single SQLite store (`DlqDbContext`, see
[ADR-0003](0003-single-instance-sqlite.md)) are otherwise routine in this repository — eleven were
added in about three weeks of active development, auto-applied via `Database.MigrateAsync()` at
startup with no manual step. During an RC1 release cycle that routineness is a liability: a schema
change landing late in the cycle risks destabilizing the exact thing a release candidate is supposed
to hold still.

Prior sessions treated an RC1 "frozen — never propose these" list, naming EF Core migrations among
other items, as authoritative and documented in `CLAUDE.md`. That file does not exist anywhere in
this working tree — it is git-ignored (`.gitignore`) and was never committed, so nothing tracked in
the repository actually states the freeze. Several tracked documents reference it anyway
(`docs/ARCHITECTURE.md`, this ADR index, [ADR-0003](0003-single-instance-sqlite.md)), which meant the
freeze rested on a file a fresh checkout, a new contributor, or a future session would never see.

Because a plausible alternative reading — "the file is just absent, so there's nothing stopping a
migration" — would have unblocked real, already-designed work (`AutoReplayRule.NamespaceId`, see the
`autoreplayrule-namespace-scope-deferred` project note and roadmap item 3), that reading was
explicitly *not* assumed. The freeze status was instead confirmed directly with the user rather than
inferred from the missing file.

## Decision

**The RC1 migration freeze is active**, confirmed by explicit user sign-off on 2026-08-28. While it
is in effect:

- No EF Core migration may be authored or applied against `DlqDbContext` / the SQLite schema,
  regardless of how small, additive, or low-risk it appears (an additive nullable column carries the
  same restriction as a destructive one — the freeze is about not touching the schema during RC1
  stabilization, not about risk-grading individual migrations).
- This restriction cannot be lifted by inference — not from `CLAUDE.md`'s absence, not from RC1
  "probably" having shipped, not from a migration being judged safe on its own merits. Lifting it
  requires the same explicit user sign-off that confirmed it, recorded the same way: as a dated
  decision, not a silent resumption.
- This ADR is the authoritative, tracked record of that status — superseding any expectation that
  `CLAUDE.md` is where it lives, since that file is not part of this repository.

Work that depends on a migration (roadmap item 3, `AutoReplayRule.NamespaceId` scoping, and anything
gated behind it — items 4 and 6) stays blocked until a follow-up decision explicitly lifts this
freeze, at which point this ADR's Status changes to `Superseded` with a link to that decision.

## Alternatives considered

- **Infer the freeze is lifted because `CLAUDE.md` doesn't exist in the tree.** Rejected: file
  absence is not a decision, and this project's own precedent (`rc1-freeze-strict-enforcement`) is to
  stop and ask rather than reason around a frozen item even when the technical case for proceeding is
  strong. Treating silence as consent here would have been exactly that mistake.
- **Work around the freeze by encoding new schema-shaped data into existing JSON blob columns
  instead of a real migration.** Rejected for the concrete case this freeze currently blocks
  (`AutoReplayRule.NamespaceId`) in the design work already on record — it forfeits SQL-level
  filterability, conflates shared DTO shapes, and needs hand-rolled backward-compatible envelope
  parsing across every serialize/deserialize call site with no compiler-enforced guarantee against
  silently dropping the new field. A workaround that trades a clean additive column for that is worse
  than waiting.
- **Auto-expire the freeze once RC1 ships.** Rejected: "shipped" is not a single unambiguous event
  observable from the repository (tag, merge, deploy, and announcement can all disagree on the date),
  so tying a schema-safety gate to it would reintroduce exactly the inference problem this decision
  exists to close.

## Consequences

- Roadmap item 3 (`AutoReplayRule.NamespaceId`) and everything downstream of it (items 4 and 6, see
  the roadmap's dependency graph) remain gated behind this ADR's Status, not behind a separate
  tracking mechanism.
- Any future schema change — not only the currently-designed one — requires the same explicit,
  dated user approval before a migration is authored, for as long as this ADR's Status reads
  `Accepted`.
- `docs/ARCHITECTURE.md`'s reference to a "Frozen" list in `CLAUDE.md` (§7) is stale for the
  migration-freeze item specifically: this ADR is now that item's source of truth. `CLAUDE.md` may
  still exist as an operator's local, git-ignored working file with its own additional rules; it is
  simply not where this particular decision is recorded going forward.
