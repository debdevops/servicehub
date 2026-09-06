# ADR-0011: DLQ observer attestation table authorized (M3.2)

**Status:** Accepted — 2026-09-06

## Context

[ADR-0006](0006-rc1-migration-freeze.md) froze all EF Core migrations against `DlqDbContext` during
RC1 stabilization, liftable only by the same explicit, dated user sign-off that confirmed it — never
by inference, and never by a migration being judged safe on its own merits. [ADR-0009](0009-next-chapter-migrations-authorized.md)
lifted it for two named units (M1's pillar-evidence tables and `HashKind` discriminator; M2's
`ProductionElevation` record) and said plainly that M3, M4 and M5 were deliberately outside that
lift: *"if one turns out to [need a schema change], it requires its own sign-off recorded the same
way."*

`SERVICEHUB-NEXT-CHAPTER-ROADMAP-2026-09-06.md`'s M3.2 — consuming the infrastructure-attested DLQ
observer `cloud-platform-infra` ADR-004 defines — is exactly that case. ADR-004 item 4 requires:
*"Before ServiceHub's application layer may trust an observer's log as authoritative for a given
namespace, it must independently confirm the observer is live and actually attached: e.g. a periodic
synthetic canary message sent to the DLQ, cleared once the observer's log records its arrival within
a bounded window. A namespace whose observer cannot be verified this way stays at
`CanProveDlqAbsence: false`."* That liveness state — which namespace has an observer, when a canary
was last sent, when it was last confirmed, and the staleness bound it is judged against — is
trust-relevant state a restart must not lose, for the same reason M1 made the four detection
pillars' findings durable instead of process-local: an in-memory cache here would mean
`CanProveDlqAbsence` silently reverts to `false` for every attested namespace on every deploy,
re-capping AWS/GCP at L3 until the next canary cycle happens to confirm it again — a stale-looking
outage with no cause a reader could find without knowing this implementation detail.

The freeze status was put to the user on 2026-09-06 and answered explicitly: authorize this one
table now, scoped to nothing else.

## Decision

**ADR-0006's freeze is superseded for the migration unit named below, and for nothing else.**

One table, `DlqObserverAttestations`, one row per `(OwnerId, NamespaceId)`:

- `Id` (Guid, PK), `OwnerId` (text), `NamespaceId` (Guid) — soft reference, no FK, matching every
  other ledger-adjacent `NamespaceId` in this schema. Unique index on `(OwnerId, NamespaceId)`.
- `Enabled` (bool) — opt-in per namespace; an operator sets this only once `cloud-platform-infra`'s
  observer module has actually been applied for that namespace's DLQ.
- `ObserverReference` (text, nullable) — the DynamoDB table name (AWS) or Firestore collection name
  (GCP) the liveness canary and log-resolution reads target. Config the operator supplies, not
  anything this migration infers.
- `LastCanarySentAt` (DateTimeOffset, nullable), `LastCanaryMessageId` (text, nullable) — the most
  recent synthetic canary dispatched to the namespace's DLQ.
- `LastConfirmedAt` (DateTimeOffset, nullable) — when the observer's log last recorded that canary's
  arrival within the bound window. Null means never confirmed.
- `StalenessBoundMinutes` (int) — how old `LastConfirmedAt` may be before liveness is judged false.
- `DlqEntityName` (text, nullable) — the DLQ entity the liveness canary is sent to, via the existing
  provider-neutral `IMessageOperationsService.SendAsync`. Not named in this ADR's original decision
  text; added during M3.2's implementation once it became clear the canary needs an explicit send
  target rather than assuming one — the same kind of correction ADR-0009's M1.1 implementation note
  recorded. Operator-supplied, exactly like `ObserverReference`: the operator already knows their
  own DLQ's name from having wired the `cloud-platform-infra` observer to it.
- **No hash chain.** This is infrastructure-liveness telemetry, not a claim about what ServiceHub
  did — the same tier `NamespaceSignature`/`AutonomyGrant` occupy, not the tier `RecoveryEvent`/
  `PlaybookEvent` occupy. Mutable in place; nothing here needs append-only history.

`Down` is a standard `DROP TABLE`, safe because nothing else in the schema references it.

## Consequences

- [ADR-0006](0006-rc1-migration-freeze.md)'s Status gains this ADR alongside 0007, 0008, 0009 and
  0010. It remains the operative freeze for every migration not named in one of the five.
- M3.2 is unblocked: the liveness-canary mechanism and the per-namespace `CanProveDlqAbsence`
  override it feeds can now persist durably instead of resetting on restart.
- Any migration beyond this table still requires the same explicit, dated sign-off this ADR and its
  four predecessors all followed. M3.3 (conformance evidence trust-root labeling) needs no schema
  change and is not covered or implied by this ADR.

## Alternatives considered

- **Keep it process-local (an in-memory dictionary).** Rejected for the reason stated in Context: it
  reintroduces the exact defect M1 existed to fix, one milestone after M1 fixed it, for state that
  gates a real autonomy ceiling change (AWS/GCP reaching L4/L5).
- **Fold attestation state into the existing `Namespace` row instead of a new table.** Rejected:
  `Namespace` is the connection-registration record: adding canary/confirmation timestamps to it
  conflates "how do I connect" with "is this namespace's optional add-on infrastructure currently
  proven live," and would force every future observer-unrelated `Namespace` change to reason about
  attestation columns it never touches. A dedicated table keeps the concern separable and its own
  `Down` migration lossless.
