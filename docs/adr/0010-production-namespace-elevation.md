# ADR-0010: Production namespaces — observed by default, recovered only under a recorded elevation

**Status:** Accepted — confirmed by explicit user sign-off on 2026-09-06.

> Recorded the same way ADR-0006, 0007, 0008 and 0009 each were: as a dated decision, not an
> inference. [ADR-0009](0009-next-chapter-migrations-authorized.md) had already lifted the migration
> freeze for this milestone conditional on this acceptance, so **M2 is now unblocked in full** —
> phase 1 and phase 2, including the `ProductionElevation` migration whose shape is fixed in
> §Decision below.
>
> The two constraints in §Decision that read as absolutes are absolutes: **dual control admits no
> self-approval, including for `Admin`**, and **the L0/L1 production ceiling has no configuration
> that raises it**. Both were the basis on which this was accepted. Raising either needs its own
> ADR and its own live observation, exactly as L4 and L5 each did.

## Context

ServiceHub cannot be pointed at a namespace labelled `Prod` at all. `CreateNamespaceRequest.Validate`
refuses registration outright — *"Production namespace connectivity is disabled. Connect in Dev or
UAT instead"* — and five paths deny `EnvironmentType.Prod` independently of one another:
`MessagesController`, `BulkOperationExecutor`, `SignatureReplayExecutor`, `DlqMonitorWorker`'s
`AutoReplayRule` scan, and predicate 2 of `RecoveryEligibilityGate`.

The consequence was named plainly in the 2026-09-01 audit (§14.8) rather than discovered later:
**all recovery, and therefore all autonomy, operates on namespaces an operator has labelled Dev or
UAT.** Whether that label matches reality is the operator's declaration, not something ServiceHub
verifies. This was correct safety-by-default and no reviewer has objected to it.

It is also the largest single gap between a defensible demonstration and a product someone runs in
anger. A dead-letter recovery tool that structurally cannot touch production is a lab instrument:
the failures that cost money are the production ones, and today ServiceHub cannot even *look* at
them.

The audit's own follow-on states the shape of the remedy: *"if production recovery is ever wanted,
predicate 2's 'explicit elevation' needs an actual mechanism, and that is a new safety conversation
with its own ADR."* This is that ADR. The gate already anticipates it — predicate 2's denial reason
is the constant `PRODUCTION_ELEVATION_REQUIRED`, and its own comment says the predicate is
unconditional *"because no elevation-recording mechanism exists yet in v3.7.0."*

### Why now, and not six months ago

The machinery required to survive production access is finished **and observed**, not asserted: the
six-predicate eligibility gate, rehearsal that cannot reach a broker by construction, the circuit
breaker with a production floor, per-namespace and per-pillar RBAC with a CI-proven denial path,
`SqliteInstanceLock`, and a hash chain verified across 500+ events including a deliberate tamper that
was caught. Every one of those was built for the Dev/UAT autonomy ladder and every one of them is
what makes this proposal defensible rather than reckless.

## Decision

**Production is opened in two phases, and the second one never reaches autonomy.**

### Phase 1 — Prod is observed, not touched

`EnvironmentType.Prod` becomes registrable. Investigate, Correlate and Prevent operate against it
without restriction: scanning, peeking, signature clustering, anomaly and drift detection,
correlation, forecasting, narration, and every read surface built on them.

**Every recovery verb stays denied, unconditionally.** All five existing denials remain exactly as
they are. Phase 1 adds no elevation path at all, and is shippable on its own.

This phase carries most of the milestone's value. It is also why
`SERVICEHUB-NEXT-CHAPTER-ROADMAP-2026-09-06.md` orders M1 ahead of M2: those three pillars are
precisely the ones whose findings are currently held in a 24-hour in-process cache, so opening
production to them before their evidence is durable would produce observations nobody can review
the next morning.

### Phase 2 — Recovery under a recorded, time-boxed, two-person elevation

Four constraints, all of which must hold simultaneously for a single production recovery to execute.

**1. An elevation is a subject, a reason, and an expiry — never a flag.**

A `ProductionElevation` record, scoped to one owner and one namespace, carrying: who requested it,
the stated reason, who approved it, when it opens, when it expires, and whether it was revoked
early. Expiry is absolute wall-clock and short by default (a shift, not a sprint). An elevation
cannot be extended — a longer window means a new elevation with a new approval, so "who could act on
production, when, and why" is answerable from the record rather than from an edit history.

Predicate 2 of `RecoveryEligibilityGate` stops being unconditional and instead requires a live,
unexpired, unrevoked elevation covering that exact namespace. It keeps returning
`PRODUCTION_ELEVATION_REQUIRED` when there isn't one.

**2. Dual control, enforced by the grant model that already exists.**

The identity that requests an elevation and the identity that approves it must be distinct, and both
must hold a `GovernanceGrant` on that namespace — `Operator` to request, `Approver` or `Admin` to
approve. This reuses `IGovernanceAccessEvaluator` and adds no second authorization concept.

Self-approval is refused even for `Admin`. An operator who holds every role still cannot act alone
on production, which is the entire point of the control.

**3. Every step is a ledger event.**

`ProductionElevationRequested`, `ProductionElevationApproved`, `ProductionElevationExpired`,
`ProductionElevationRevoked` join `RecoveryEventType`, written through `IRecoveryLedger` into the
same per-owner hash chain as every other recovery event. An auditor holding only the export must be
able to reconstruct who elevated which namespace, on whose approval, for what stated reason, and for
how long — without server access. That is exit statement 9 applied to production access itself.

**4. A hard ceiling: L0/L1 only, forever, in this ADR.**

No `AutonomyGrant` is issued against a Prod namespace. No promotion evaluation runs there. No
`AutoReplayRule` matches there. Production recovery is a human proposing and a second human
approving a specific, bounded action inside a window — every time, with no trust accrual and no
ladder.

Enforced in the eligibility gate and in the grant issuance path, not in the UI. Raising this ceiling
would need its own ADR and its own live observation, exactly as L4 and L5 each did.

### What stays true regardless of phase

The `Prod` label remains the operator's declaration. ServiceHub does not verify it and this ADR does
not change that. `EnvironmentType`'s doc comment — corrected on 2026-09-05 to stop describing
behaviour the code does not have — must be corrected again to describe what these phases actually
implement, at the time they implement it.

## Consequences

- The audit's §14.8 bounded-scope statement is narrowed but not deleted: autonomy still operates
  only on Dev and UAT. Production gains observation and two-person recovery, never unattended
  action.
- `RecoveryEligibilityGate` predicate 2 becomes conditional for the first time. It is the only
  predicate this ADR touches; predicates 0, 1, 3, 4 and 5 are unchanged, and predicate 1 (purge
  origin) remains unconditional and non-overridable in production as everywhere else.
- The migration for the `ProductionElevation` record is authorized by
  [ADR-0009](0009-next-chapter-migrations-authorized.md) conditional on this ADR being Accepted, and
  its shape is fixed here rather than there.
- A CI test proving the same-identity request/approve path is refused becomes a release gate,
  mirroring the Viewer-vs-Operator negative test added in W3.2.
- Phase 1 is independently shippable and independently valuable. If phase 2 is never accepted,
  phase 1 still removes the most visible limitation in the product.

## Alternatives considered

- **A configuration flag — `AllowProd=true`, or `Security:AllowProductionRecovery`.** Rejected, and
  named on the roadmap's never-build list. A single boolean that disables five independent denials
  at once is not a safety model; it is the absence of one, and it leaves no record of who turned it
  on or why. Every reviewer will assume this is what was built unless the elevation record exists.
- **Reuse the existing "explicit intent" header mechanism** (`IntentHeaders`) as the elevation.
  Rejected: intent headers prove a caller meant to do something, which is a different property from
  a second person agreeing they should. Intent stays required in production *in addition to* an
  elevation, not instead of it.
- **Grant production access through an ordinary `GovernanceGrant` with a `Prod`-scoped role.**
  Rejected: grants are current, intentionally mutable configuration about who *may* act, deliberately
  not hash-chained. Production access needs an expiry, a stated reason, and a tamper-evident record
  of the specific approval — properties a grant deliberately does not carry. The two compose instead:
  the grant says who is eligible to be elevated, the elevation says who actually was, when, and on
  whose word.
- **Allow autonomy in production once a signature has earned L5 in UAT.** Rejected: the trust a
  signature earns is trust in a *replay outcome observed in that environment*, and carrying it across
  an environment boundary would be exactly the confidence-transfer the never-build list forbids.
  A UAT L5 grant says nothing about production's blast radius.
- **Verify the `Prod` label against the cloud provider** (resource tags, subscription metadata) so
  the environment type is a fact rather than a declaration. Considered and deliberately not adopted
  here: it is a genuine improvement, but it is a provider-capability question with three different
  answers across Azure, AWS and GCP, and folding it into this decision would make a safety model
  depend on a capability matrix. Worth its own ADR later; the declaration model is unchanged by this
  one.
