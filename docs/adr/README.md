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

Each ADR states its Status. An ADR marked `Accepted` is currently in force — do not propose
reversing it without the same level of justification the ADR itself required. `CLAUDE.md`'s
"Frozen" list names the exact set of architectural changes considered out of scope; these ADRs are
the reasoning behind several of those entries.
