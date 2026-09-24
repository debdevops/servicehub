# ADR-0015: ServiceHub 4.1.0 starts from a fresh schema; migrations restart at 0001

**Status:** Accepted — 2026-09-21, by the owner.

**Follows:** [ADR-0014](0014-servicehub-4-1-0-architecture.md) — the rewritten stack.
**Clarifies the scope of:** [ADR-0006](0006-rc1-migration-freeze.md) — the RC1 migration freeze.
**Relates to, and does not reopen:** [ADR-0002](0002-recovery-evidence-ledger.md) (the ledger is
append-only and hash-chained), [ADR-0003](0003-single-instance-sqlite.md) (one instance, one SQLite
file), [ADR-0004](0004-self-hosted-security-model.md) (connection strings are encrypted at rest).

---

## Context

ADR-0014 grows a new backend screen by screen. Its database is the one thing that cannot be grown
"screen by screen" without a decision first, because two incompatible answers were both defensible:

- **Carry 4.0.0's schema forward** — 21 migrations, 30 DbSets, a 1,936-line `DbContext` — so an
  existing 4.0.0 SQLite file opens in 4.1.0 and its ledger chain continues unbroken.
- **Start fresh** — only the tables the nine screens need, and no upgrade path.

ADR-0006 (the RC1 migration freeze) sits across this. Its subject is *the 4.0.0 schema*, which is
now frozen inside the archive along with the code that reads it. It cannot govern a schema that did
not exist when it was written, and it says its restriction "cannot be lifted by inference" — so
saying so explicitly is this ADR's job.

## Decision

### D1 — 4.1.0 has its own schema, and its migration history restarts at `0001_Initial`

`services/api/src/ServiceHub.Infrastructure/Persistence/Migrations/` starts empty. The first
migration creates only what the first screens need. **4.0.0's 21 migrations are not copied.**

### D2 — ADR-0006's freeze does not extend to the new tree, and is not thereby lifted

| | |
|---|---|
| **ADR-0006 still binds** | every migration under `archive/` — absolutely, because the archive is never edited at all (ADR-0013 D3) |
| **ADR-0006 does not bind** | `services/api/` in the new tree, which has no RC to protect and no released schema to destabilise |
| **What replaces it there** | **one migration per unit, named for its unit, justified by a screen.** A table with no screen behind it does not get created. No blanket authorisation is granted by this ADR, and none is implied for any table not listed in D3. |

This is narrower than it may read. The freeze existed because 4.0.0 was a release candidate whose
schema people's installations already depended on. 4.1.0 has no installations yet. When 4.1.0 ships,
**a freeze of the same kind applies to it** and will need its own ADR — that is a Wave 7 task, not
an afterthought.

### D3 — The tables 4.1.0 is expected to need, and the unit that creates each

Twelve to fourteen tables, against 4.0.0's thirty. Each arrives with the unit that needs it; a table
listed here is *expected*, not *authorised in advance* — the unit still has to show the screen that
reads it.

| Table | Why 4.1.0 needs it | Arrives in |
|---|---|---|
| `Namespaces` | a connected cloud account; the encrypted connection string | W1 |
| `AuditLogs` | Recent Activity on Home; who did what | W1 |
| `DlqMessages` | the durable record of dead letters — the DLQ screen, the 7-day trend, history | W2 |
| `RecoveryLedgerEntries` | the append-only, hash-chained evidence record (ADR-0002) | W2 |
| `RecoveryEvents` | the events belonging to a ledger entry | W2 |
| `RecoveryOperations` | one recovery action, its scope and its outcome | W2 |
| `ReplayHistories` | what was replayed, when, by whom, with what result | W2 |
| `NamespaceSignatures` | the failure fingerprint messages are grouped by — what the gate and the rules reason about | W3 |
| `AutoReplayRules` | the Auto Replay screen | W3 |
| `BulkOperationJobs` | the Bulk Replay screen's job, preview and progress | W3 |
| `AutonomyGrants` | what a signature has earned; what the agent may do without asking | W4 |
| `DlqObserverAttestations` | the per-namespace proof that lets AWS/GCP claim "Verified" (**R4**) | W4 |
| `GovernanceGrants` | Viewer / Operator / Approver / Admin, per namespace | W5 or W6, with the first screen that must deny an action |
| `WorkerHeartbeats`? | **only if** in-memory heartbeats prove insufficient — 4.0.0 kept them in memory and that was correct | not planned |

**Shapes are copied, not redesigned.** Where a table has a 4.0.0 counterpart, the entity is copied
with its columns, indexes and constraints intact unless a column exists only for a 4.0.0 feature
4.1.0 does not ship. The ledger's columns are copied **exactly**: its hash chain is a wire format
that `scripts/verify-recovery-chain.py` verifies offline, and that script must keep working across
the version boundary (ADR-0014 D9).

### D4 — No upgrade path. 4.1.0 is a fresh install

A 4.0.0 SQLite file will not open in 4.1.0 and no importer is written. The paths are:

- **Stay on 4.0.0** — it is released, supported by its own image, and unchanged.
- **Start 4.1.0 clean** — reconnect namespaces; the new database builds itself from the first run.

The release notes say this in plain words, at the top, and the 4.1.0 container refuses to start
against a database file whose schema it does not recognise rather than attempting anything clever.

**What is actually lost:** a 4.0.0 installation's recovery ledger does not travel. It remains
readable and offline-verifiable in place, by 4.0.0 and by `verify-recovery-chain.py`, which is what
the ledger's tamper-evidence promise requires. It is not destroyed; it simply does not continue into
a new chain.

### D5 — A new chain starts at genesis, and says so

4.1.0's ledger begins with its own genesis entry. The chain does not claim continuity with a 4.0.0
chain it cannot verify, and nothing in 4.1.0 renders a pre-4.1.0 entry as if it were part of its own
chain. Two verifiable chains is honest; one chain with an unverifiable join is not.

## Options considered and rejected

| Rejected | Why |
|---|---|
| **Carry the 4.0.0 schema forward verbatim** | Imports 30 tables into a product that needs about twelve, and with them every 4.0.0 concept the simplification exists to remove — playbooks, pillars, anomalies, drift findings, external signals. The simplification would stop at the UI, which is exactly the failure ADR-0012 named. |
| **Fresh schema plus a one-way importer** | The honest version costs real work: an importer that carries namespaces and a hash-chained ledger across a schema boundary is itself safety-critical code needing its own tests and its own live verification, for a migration path with no users waiting on it today. Revisit if a real 4.0.0 installation needs to move. |
| **Reuse 4.0.0's `DlqDbContext` as a starting point** | 1,936 lines configuring thirty entities. Copying it would decide the schema by inertia rather than by screen. |
| **Keep the same database file and add tables alongside** | Two schemas, two generations of writer, in one file governed by a single-writer invariant (ADR-0003). |

## Consequences

**Positive**

- The schema is small enough to hold in your head, and every table has a screen that reads it.
- No migration in the new tree exists for a feature that was cut.
- The ledger format stays verifiable by the existing offline script, across the version boundary.

**Negative, and accepted**

- **Existing 4.0.0 data does not move.** Anyone running 4.0.0 with a ledger they care about keeps
  running 4.0.0, or accepts that its history stays there.
- **Two verifiable chains instead of one**, for any installation that does move.
- **The freeze question returns at release**: 4.1.0 needs its own migration-freeze ADR at Wave 7,
  and forgetting it would leave a shipped schema unguarded.

## What would make this decision wrong

1. A real 4.0.0 installation needs its ledger in 4.1.0 — then D4 was wrong and the importer that
   was rejected has to be built, deliberately.
2. The table count passes twenty — the new schema is growing into the old one, and each table past
   the D3 list needs its screen named.
3. `verify-recovery-chain.py` cannot verify a 4.1.0 entry — the ledger columns were "improved" when
   D3 said to copy them exactly.
