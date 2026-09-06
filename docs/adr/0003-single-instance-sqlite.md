# ADR-0003: Single-instance process, SQLite persistence

**Status:** Accepted

## Context

ServiceHub needs to persist DLQ scan history, replay/audit history, auto-replay rules, and the
Recovery Evidence Ledger somewhere durable, and needs some way to coordinate the background workers
(DLQ monitor, recovery verification, autonomy evaluation, …) that write to that store. The
conventional default for a growing product is to plan for horizontal scale early: an external
database server, a distributed cache, and a message bus so multiple instances can share state.

ServiceHub's actual constraints point the other way: it is operated by one team against their own
namespaces, self-hosted (see [ADR-0004](0004-self-hosted-security-model.md)), deployed by a single
maintainer's project, and its target onboarding experience is "clone, set two secrets, `docker
compose up`, working in under ten minutes."

## Decision

ServiceHub runs as one process per deployment. One SQLite database (via EF Core, `DlqDbContext`)
holds DLQ history, replay history, auto-replay rules, the audit log, bulk-operation jobs, and the
Recovery Evidence Ledger. One in-process event bus (`PlatformEventStreamBroker`) fans out live
updates to connected SSE clients. There is no shared state between instances and no supported way
to run two instances against the same data directory.

This is treated as a competitive property, not a limitation to apologize for: a ten-minute deploy
with zero external infrastructure dependencies is materially easier to adopt and operate than a
system requiring a database server, a cache tier, and a message broker before it can start.

## Alternatives considered

- **PostgreSQL.** Rejected: SQLite handles the actual workload (single-team DLQ history and audit
  volume, not multi-tenant scale), and a Postgres dependency adds real operational cost — a server
  to provision, secure, back up, and monitor — for no user-visible benefit at this scale. Frozen
  explicitly in `CLAUDE.md`.
- **Kubernetes / high availability / a distributed architecture.** Rejected: nothing in the product
  today needs coordination between processes, and HA is a solution to an availability requirement
  this product doesn't have — a self-hosted forensic tool being briefly restarted for an upgrade is
  a materially different failure mode than a customer-facing service going down.
- **Redis or another external cache/lock service.** Rejected: no named problem requires cross-process
  coordination, because there is deliberately only one process.
- **Kafka or an external message bus for platform events.** Rejected: the in-process event bus
  matches the single-instance model exactly; introducing an external bus would add a dependency to
  solve a problem (cross-process event delivery) that doesn't exist here.

## Consequences

- No horizontal scaling. If a deployment's DLQ volume or team size someday genuinely exceeds what
  one process can serve, that would be a new, deliberate re-architecture decision — not a
  configuration change — and isn't planned pre-emptively.
- Schema evolution uses real EF Core migrations (`Infrastructure/Persistence/Migrations/`) against
  the single SQLite file; there is no cross-shard or cross-replica migration coordination problem
  to solve, because there is only ever one database.
- The Recovery Evidence Ledger's per-owner hash chain (see [ADR-0002](0002-recovery-evidence-ledger.md))
  is tractable to fully re-verify precisely because it lives in one store with one writer — a
  property that would need re-examination if this decision were ever reversed.
- Two persistence stores exist today (the SQLite database and a separate JSON file for namespace
  credentials) for historical reasons, not because of this decision — unifying them is a known,
  deliberately deferred simplification, tracked but not scheduled.
- **The data directory must be local block storage.** SQLite's WAL mode needs a memory-mapped
  `-shm` file, and both SQLite's own locking and the `SqliteInstanceLock` guard below rely on POSIX
  advisory locks; neither is dependable on SMB (Azure Files) or NFS (AWS EFS). This constrains where
  ServiceHub can be hosted — notably it rules out the file-share persistence that Azure App Service
  for Linux documents as its standard answer — and the constraint is a consequence of this decision
  rather than an independent one. A network mount is not made safe by configuration, so none is
  offered; the operator-facing form of this is in
  [self-hosting/README.md](../../self-hosting/README.md#storage-requirement-local-block-storage-not-a-network-share).
- The single-writer assumption is enforced at runtime, not only documented: `SqliteInstanceLock`
  takes an exclusive OS lock on `.instance.lock` in the data directory at startup, so a second
  process against the same directory fails fast instead of corrupting the Recovery Evidence
  Ledger's hash chain. On local block storage this makes the constraint an invariant; the previous
  bullet is why it degrades to an assumption on a network mount.
