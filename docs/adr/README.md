# Architecture Decision Records

Concise records of the decisions in ServiceHub that are genuinely load-bearing — the ones where a
plausible alternative exists, was considered, and rejected for a stated reason. This is not a
record of every implementation choice; see [`docs/ARCHITECTURE.md`](../ARCHITECTURE.md) for how
the system is built, and these documents for *why* a handful of its foundational choices were made
the way they were.

| ADR | Decision |
|---|---|
| [0001](0001-provider-abstraction-and-capabilities.md) | Provider abstraction: one interface, per-provider declared capabilities |
| [0002](0002-recovery-evidence-ledger.md) | Recovery Evidence Ledger: append-only hash chain, not a mutable status field |
| [0003](0003-single-instance-sqlite.md) | Single-instance process, SQLite persistence |
| [0004](0004-self-hosted-security-model.md) | Self-hosted-only; no multi-tenant SaaS mode |
| [0005](0005-ai-capability-boundary.md) | AI is heuristic, client-side, and architecturally forbidden from mutation |
| [0006](0006-rc1-migration-freeze.md) | RC1 migration freeze: active, lifted only by explicit dated sign-off |
| [0007](0007-persistence-wave-m1-m4-authorized.md) | Persistence wave M1–M4 authorized: NamespaceId scoping, namespace consolidation, Governance/RBAC, Playbook Ledger |
| [0008](0008-m5-external-signal-events-authorized.md) | M5 (`ExternalSignalEvents`) authorized for C3: narrow, single-unit freeze lift distinct from ADR-0007 |
| [0009](0009-next-chapter-migrations-authorized.md) | Next-chapter migrations authorized: pillar-evidence tables and the `HashKind` discriminator (M1), production elevation record (M2, conditional on 0010) |
| [0010](0010-production-namespace-elevation.md) | Production namespaces: observed by default, recovered only under a time-boxed two-person elevation, never autonomously |
| [0011](0011-dlq-observer-attestation-table-authorized.md) | DLQ observer attestation table authorized (M3.2): durable per-namespace liveness-canary state |

Each ADR states its Status. An ADR marked `Accepted` is currently in force — do not propose
reversing it without the same level of justification the ADR itself required. An ADR marked
`Proposed` is a design that has been written but **not** authorized: it is safe to read and to plan
against, and implementing it requires the same explicit, dated sign-off an `Accepted` one already
received. `CLAUDE.md`'s
"Frozen" list, where an operator maintains one, names further architectural changes considered out
of scope; these ADRs are the reasoning behind several of those entries, and are the tracked,
authoritative source for the ones — like the RC1 migration freeze — that also live here.
