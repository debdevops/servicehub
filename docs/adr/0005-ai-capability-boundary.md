# ADR-0005: AI is heuristic, client-side, and architecturally forbidden from mutation

**Status:** Accepted

## Context

Pattern detection across dead-lettered messages (clustering by error type, confidence scoring,
suggested auto-replay rules) is a natural fit for AI/ML techniques, and the product includes this
capability. Two distinct risks come with "add AI to a tool that has replay/purge access": data
leaving the operator's environment to reach an external model, and an AI-adjacent component ending
up able to *act* — approve, replay, purge — rather than only *observe and explain*. The second risk
is the more dangerous one: a classification mistake that mislabels a pattern is a UX bug; a
classification mistake that autonomously triggers a purge is a safety incident.

The risk of getting this wrong was named explicitly rather than assumed away: an "AI-generated
auto-replay rule" feature already exists (rules can be AI-authored and are labeled with provenance
in the UI), which means AI output does reach a place that can eventually cause a mutation — through
a rule a human reviews and enables. The boundary this ADR describes is about what AI is permitted
to do *directly*, not about forbidding AI from ever influencing a suggestion a human later approves.

## Decision

Two enforced boundaries, one about data flow and one about capability:

1. **No external AI calls, anywhere, in either direction.** Pattern detection runs as client-side
   heuristics — plain JavaScript in the browser (`packages/servicehub-ui-shared/src/lib/ai/`) —
   mirrored by a backend invariant that the equivalent server-side component never calls out to an
   external AI API. This is the same "data never leaves your environment" claim
   [ADR-0004](0004-self-hosted-security-model.md) makes for the rest of the product, applied to AI
   specifically rather than assumed to already cover it.
2. **AI-adjacent backend types are architecturally forbidden from mutation.** `IRecoveryLedger` and
   the replay/purge execution paths are off-limits to any AI-adjacent component — not by a documented
   convention someone has to remember, but enforced by `AIBoundaryArchitectureTests`, which reflects
   over the relevant interfaces' own method lists to derive the forbidden-member set automatically.
   A future write method added to `IRecoveryLedger` is caught by this test without anyone updating an
   exclusion list. The test also discovers AI-adjacent types by dependency graph, not by a namespace
   naming convention, so a worker that merely resolves an AI-related client through DI is still
   caught even if its class name doesn't obviously say "AI."

AI touches nouns (classification, explanation, a suggested rule shown to a human) and never verbs
(execution) — it can produce a recommendation that a human reviews and enables, but nothing in the
AI-adjacent code path can itself call replay, purge, or write a ledger event.

## Alternatives considered

- **A documented convention ("AI code must not call these methods") enforced by code review.**
  Rejected: this is exactly the kind of rule that erodes under time pressure or staff turnover
  without anyone deciding to weaken it — the actual failure mode this boundary exists to prevent.
- **A runtime permission check** (AI-adjacent services carry a restricted credential/role that the
  ledger checks at call time). Considered, but a compile-time/build-time IL scan is strictly
  stronger for this case: it fails the build the moment a forbidden dependency is introduced, rather
  than waiting to fail a real ledger call at runtime — and it requires no runtime overhead or
  configuration to stay correct.
- **Allow supervised/agentic AI to execute pre-approved actions directly, gated by the same
  eligibility rules as automation.** Rejected, unanimously across every external review of this
  codebase: this would blur exactly the noun/verb line this ADR draws, and there is no named demand
  for it. Explicitly out of scope, not merely deferred.

## Consequences

- Any future AI feature (embeddings, a supervised model, an LLM-backed assistant) that wants to
  reach the recovery/replay path must first cross this ADR, not just pass code review — it names the
  boundary the code already enforces, so a proposal to weaken it is visible as a deliberate
  architectural change rather than an incidental permission a new class happens to acquire.
- The heuristic-only approach means AI pattern detection is bounded by what deterministic clustering
  and rule-based classification can do — no model quality to tune, but also no external inference
  cost, no data-residency question, and no new attack surface from an AI provider integration.
- `AIBoundaryArchitectureTests` and the ledger's own `RecoveryPathCoverageTests` (see
  [ADR-0002](0002-recovery-evidence-ledger.md)) share the same IL-scanning approach so the two
  cannot silently drift apart — a change to one's detection logic is a change worth checking against
  the other.
