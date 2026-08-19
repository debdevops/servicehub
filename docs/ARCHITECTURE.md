# ServiceHub Architecture

This document describes the system as it exists today, grounded in the source tree — not a
5-year vision (see [`docs/architecture/ARCHITECTURE_VISION.md`](architecture/ARCHITECTURE_VISION.md)
for that) and not a decision log (see [`docs/adr/`](adr/) for that). Read this to understand how
the pieces fit together before making a change.

---

## 1. What ServiceHub is

ServiceHub is a self-hosted forensic debugger for cloud message queues — Azure Service Bus (GA),
AWS SQS/SNS (preview), GCP Pub/Sub (preview). It answers the question a cloud portal can't: *what
is actually inside these 5,000 dead-lettered messages, why did they fail, and what happens if I
replay them?*

It is read-only by default. Every mutating operation (replay, purge, send) is explicit, capability-
gated per provider, and disabled entirely against namespaces marked `Production`. It is a single
process per deployment — see [§7](#7-persistence-and-single-instance-architecture) for why.

## 2. High-level architecture

Two independently deployable pieces talk over HTTP: a React SPA and an ASP.NET Core API. Nothing
else is required to run — no external database server, no message broker of its own, no cache
tier.

```
┌─────────────────────────────────────────────────────────────────────┐
│  Browser — React 19 SPA (apps/web)                                  │
│    TanStack Query hooks + Axios client (packages/servicehub-ui-shared)│
└───────────────────────────────┬─────────────────────────────────────┘
                                 │ HTTPS (Vite dev proxy in dev; same-origin in prod)
┌───────────────────────────────▼─────────────────────────────────────┐
│  ASP.NET Core 10 API (services/api/src/ServiceHub.Api)              │
│    Middleware pipeline → Controllers/V1                             │
└───────────────────────────────┬─────────────────────────────────────┘
                                 │
┌───────────────────────────────▼─────────────────────────────────────┐
│  ServiceHub.Core — interfaces, entities, DTOs (no external deps)    │
└───────────────────────────────┬─────────────────────────────────────┘
                                 │ implemented by
┌───────────────────────────────▼─────────────────────────────────────┐
│  ServiceHub.Infrastructure(.Aws / .Gcp) — provider SDKs, SQLite,     │
│  encryption, rule engine, background workers                        │
└───────────────────────────────────────────────────────────────────────┘
```

Four backend layers, dependencies flowing inward only (`Shared` → `Core` → `Infrastructure*` →
`Api`), enforced by `TreatWarningsAsErrors=true` and an architecture-conformance test suite
(`ApiLayerBoundaryTests`, `RecoveryPathCoverageTests`, `AIBoundaryArchitectureTests` — see
[§6a](#6a-the-ai-capability-boundary)) rather than by convention alone. Controllers depend on
`ServiceHub.Core` interfaces (`ICloudMessagingProvider`, `IMessageOperationsService`,
`IRecoveryLedger`, …), never on concrete `Infrastructure` types.

## 3. Frontend/backend relationship

The frontend is a monorepo of npm workspaces:

| Workspace | Role |
|---|---|
| `apps/web` | The production SPA. One page component per route (`pages/`), registered in `router.tsx`. |
| `packages/servicehub-ui-shared` | Every TanStack Query hook, the Axios API client (`lib/api/`), the client-side AI heuristics (`lib/ai/`), and the Demo Mode fixtures (`lib/demo/`, `lib/*MockData.ts`). `apps/web` and the experimental apps below all consume this as `@servicehub/ui-shared`. |
| `apps/demo`, `apps/sandbox` | Experimental, standalone exploratory surfaces — see [`apps/demo/README.md`](../apps/demo/README.md) / [`apps/sandbox/README.md`](../apps/sandbox/README.md). Not part of the supported product surface. |

A component never calls the API directly — it goes through a hook in `packages/servicehub-ui-shared/src/hooks/`, which wraps `lib/api/`'s Axios client. This is the only place API calls originate, which is what makes the client-side Demo Mode (`/demo/azure`, `/demo/aws`, `/demo/gcp` — fixture data, zero backend calls) and the real mode swappable at the hook layer.

Requests cross a Vite dev-server proxy in development and are same-origin in production (the API
serves the built SPA's static assets directly — one container, one process, one port).

### Live operations: SSE, not polling

Where the UI needs to react to server-side state changes without the user refreshing — an
auto-replay rule firing, a circuit breaker tripping, a bulk operation progressing — the API pushes
Server-Sent Events rather than the client polling. `PlatformEventStreamBroker`
(`ServiceHub.Api/Services/PlatformEventStreamBroker.cs`) is an in-process pub/sub broker: workers
and controllers publish typed platform events (`servicehub.rule.circuitbreaker.tripped.v1`, DLQ
scan completions, bulk-operation progress, …), the broker fans them out to connected SSE clients,
scoped to the owner that can see them (`IsVisibleToOwnerAsync`). The frontend's `useEventStream()`
hook consumes the stream and invalidates the relevant TanStack Query cache keys, so a change on the
server shows up without a manual refresh. This is in-process by design — it matches the
single-instance architecture ([§7](#7-persistence-and-single-instance-architecture)); there is no
external message bus to keep in sync, because there is only ever one process to notify.

Live Tail (a `tail -f` for a queue) is a separate, narrower SSE stream gated by
`ProviderCapabilities.SupportsRepeatablePeek` — see [§4](#4-provider-abstraction).

### Middleware pipeline

Requests pass through, in order (and back out in reverse): `SecurityHeadersMiddleware` →
`ErrorHandlingMiddleware` → `CorrelationIdMiddleware` → `RequestLoggingMiddleware` →
`ApiKeyAuthenticationMiddleware` → `RateLimitingMiddleware` (production only) →
compression/CORS/Swagger → routing → controller.

## 4. Provider abstraction

Every cloud integration implements one interface, `ICloudMessagingProvider`
(`ServiceHub.Core/Interfaces`), and is resolved through `CloudProviderRouter`
(`ServiceHub.Infrastructure/Routing/CloudProviderRouter.cs`). The router takes
`IEnumerable<ICloudMessagingProvider>` from DI, groups by `CloudProviderType`, and exposes
`Resolve(type)` / `IsRegistered(type)`. Adding a provider requires **zero changes to the router
itself** — a new provider implements the interface, registers via its own `AddXProvider()` DI
extension method (the pattern `AddAzureProvider()` / `AddAwsProvider()` / `AddGcpProvider()`
already follow), and the router picks it up automatically. Every consumer
(`IMessageOperationsService`, `CrossCloudTraceController`, `DlqMonitorService`, …) codes against
the interface, never a concrete provider list — enforced by `ApiLayerBoundaryTests`.

Azure, AWS, and GCP registration is independent and flag-gated: Azure is always live;
`CloudProviders:Aws:Enabled` / `CloudProviders:Gcp:Enabled` both default `false` in
`appsettings.json`. Enabling a flag registers that provider's `ICloudMessagingProvider`, its
client factory, and a connectivity health check; registration is inert until a namespace for that
provider actually exists. This is why AWS/GCP are labeled **preview**, not GA: the flows are
implemented and unit-tested end-to-end, but not exercised against live AWS/GCP infrastructure in
this project's own CI, and are provably asymmetric in capability (below) — not because anything is
half-built.

### ProviderCapabilities: the mechanism for honest asymmetry

Azure, AWS, and GCP are not interchangeable — SQS has no non-destructive peek; Pub/Sub has no
message-count API; Azure has no reliable single-message delete by sequence number. Rather than
scatter `if (provider == "aws")` checks across the codebase, `ICloudMessagingProvider.Capabilities`
(`ServiceHub.Core.Models.ProviderCapabilities`, a `sealed record`) declares this once, per
provider, as data:

| Capability | Azure | AWS | GCP |
|---|:---:|:---:|:---:|
| `SupportsMessageCounts` | ✅ | ✅ | ❌ |
| `SupportsManualDeadLetter` | ✅ | ✅ | ❌ |
| `SupportsPurge` | ❌ | ✅ | ✅ |
| `SupportsScheduledMessages` | ✅ | ❌ | ❌ |
| `SupportsRepeatablePeek` | ✅ | ❌ | ✅ |
| `SupportsRecoveryMarker` | ✅ | ✅¹ | ✅ |
| `CanProveDlqAbsence` | ✅ | ❌ | ❌ |

¹ AWS additionally enforces SQS's 10-attribute cap per message at replay time; when a message is
already at the cap, the marker is skipped for that message even though the provider generally
supports it.

Every `false` is a genuine platform constraint (see each preset's `Notes` field for the specific
reason), not a missing feature. `GET /api/v1/cloud-bridge/capabilities` exposes the same record to
the frontend's `useProviderCapabilities()` hook, so backend gating and UI copy can never drift
apart — one fact, two consumers. `SupportsRepeatablePeek` in particular gates Live Tail and
auto-refresh: AWS SQS's peek is actually a receive that increments `ReceiveCount`, so polling it on
a timer can silently push a message toward its own dead-letter threshold — this is why AWS declares
`SupportsRepeatablePeek: false` while Azure and GCP (whose peek implementations genuinely re-queue)
declare `true`. See [ADR-0001](adr/0001-provider-abstraction-and-capabilities.md) and
[`docs/extending/adding-a-provider.md`](extending/adding-a-provider.md) for how a new provider
declares its own preset.

`CanProveDlqAbsence` feeds directly into recovery verification, next.

## 5. Recovery Evidence Ledger and verification

Replay and purge are the only mutating operations, and every attempt at either is recorded in the
**Recovery Evidence Ledger** — an append-only, per-owner hash chain, not a mutable status field.

- `RecoveryLedgerEntry` — one row per (operation, message): identity/context fields snapshotted at
  the moment the attempt begins (namespace, entity, body hash, failure signature, …), plus a small
  mutable projection (`State`, `Disposition`, `VerificationResult`, …) that can only change through
  `IRecoveryLedger`, and only alongside the event that justifies the change.
- `RecoveryEvent` — the actual evidence. Each event's hash is
  `SHA256(canonical(fields excluding EntryHash) || PrevHash)` (`RecoveryHashChain.ComputeEntryHash`),
  chaining every event in an owner's history together. Append-only is enforced at the persistence
  layer (`RecoveryLedgerAppendOnlyGuard` inside `DlqDbContext.SaveChangesAsync`), not by review
  discipline, and `RecoveryPathCoverageTests` IL-scans for any replay/purge caller that doesn't
  also write the ledger in the same method — with an empty exemption list by design.
- An entry moves through `RecoveryEntryState` (`Executing` → `Observing` → a terminal state:
  `Recovered`, `Returned`, `Unverified`, `Discarded`, `ExecutionFailed`, `ExecutionUnknown`,
  `WrittenOff`, `Expired`, or `Declined`). `Recovered` means *"did not return to the DLQ within the
  observation window"* — never *"the business transaction completed,"* which ServiceHub cannot
  observe.

**Why `Unverified` exists, and is not a bug:** `RecoveryVerificationWorker` decides between
`Recovered` and `Unverified` using `ProviderCapabilities.CanProveDlqAbsence`. Only Azure can page an
entity uncapped (up to 5,000 messages/cycle) and so can actually *prove* a message never came back.
AWS's background scanning is off by default and every peek is a destructive 100-message receive;
GCP's reconciliation is capped at a 100-message batch per cycle. A capped sample can never prove
absence — so AWS and GCP replays close `Unverified` with a recorded reason
(`AWS_NO_ABSENCE_PROOF`, `GCP_NO_ABSENCE_PROOF`, or an operational reason like
`NAMESPACE_DEREGISTERED`), rather than a fabricated `Recovered`. See
[`docs/RECOVERY-EVIDENCE.md`](RECOVERY-EVIDENCE.md) for the full model, the export format, and
[`scripts/verify-recovery-chain.py`](../scripts/verify-recovery-chain.py) for independently
verifying an exported chain without trusting the ServiceHub server that produced it.

## 6. Autonomy and safety model

Auto-replay rules can act without a human clicking Replay, but only within an earned-autonomy
ladder (`AutonomyLevel`, `ServiceHub.Core.Enums`):

| Level | Name | What happens |
|---|---|---|
| L0 | Observe | Failure recorded; nothing else. |
| L1 | Explain | Signature classified, evidence assembled. |
| L2 | Recommend | A recovery plan is generated and shown; display only. |
| L3 | Approve | A human approves each instance before it executes. **Permanent floor** — not a rung to graduate past. |
| L4 | Standing | A pre-approved recipe executes without per-instance approval, budget-bounded. |
| L5 | Unattended | Same execution path as L4; differs in accumulated evidence and demotion sensitivity. |

Only L4/L5 let `RecoveryActorKind.Automation` execute without a human in the loop, and only when
the provider can prove DLQ absence (`CanProveDlqAbsence`) — AWS and GCP are structurally blocked
from unattended replay today, independently enforced twice: once when `AutonomyEvaluationWorker`
decides whether to promote a signature, and again at execution time by
`RecoveryEligibilityGate.ReasonProviderCannotVerifyAbsence`, so a stale grant can't bypass the
current provider reality.

`RecoveryEligibilityGate` is the single ordered-predicate authority for every replay/purge
attempt, automated or manual: emergency stop first, purge-by-automation unconditionally denied,
production namespaces require explicit elevation, a fleet-wide replay-velocity cap, a per-rule
success-rate circuit breaker (last 20 verified dispositions, floor configurable, default 50%), and
the provider-capability re-check above. Every query in the chain fails closed. Demotion — dropping
a signature back down the ladder after a verified `Returned` — fires synchronously on the 2nd
consecutive verified recurrence and cannot be disabled by configuration.

## 6a. The AI capability boundary

AI/DLQ pattern detection is heuristic — no calls to any third-party or cloud AI API, in either
direction. The primary pattern-detection surface (the Messages page's "AI Findings" panel) runs as
client-side heuristics in the browser (`packages/servicehub-ui-shared/src/lib/ai/`). A backend
AI-adjacent path also exists for richer failure-signature clustering and (stubbed) anomaly
detection: it can optionally call `services/ai/` — a local, self-hosted, disabled-by-default
companion container the operator runs on their own network, never an external service — and always
falls back to a purely local, in-process deterministic strategy when that container is disabled,
absent, or unreachable (see [`services/ai/README.md`](../services/ai/README.md)). Every AI-adjacent
backend component, this clustering path and anomaly detection alike, is architecturally forbidden
from calling any mutating method on `IRecoveryLedger` or the replay/purge paths:
`AIBoundaryArchitectureTests` reflects over each relevant interface's own method list to derive the
forbidden-member set automatically, so a future write method added to `IRecoveryLedger` is caught
without anyone remembering to update an exclusion list. AI touches nouns (classification,
explanation), never verbs (execution) — see [ADR-0005](adr/0005-ai-capability-boundary.md).

## 7. Persistence and single-instance architecture

ServiceHub is one process per deployment: one SQLite database, one in-process event bus, both
scoped to that process's lifetime. There is no shared state between instances and no supported way
to run two instances against the same data directory. This is deliberate, not an omission — see
[ADR-0003](adr/0003-single-instance-sqlite.md).

Two stores, for historical reasons:

| Store | Backing | Contents |
|---|---|---|
| SQLite (`DlqDbContext`, EF Core) | `DlqDatabase:DataDirectory` | DLQ history, replay history, auto-replay rules, audit log, bulk-operation jobs, failure signatures, the Recovery Evidence Ledger |
| Namespace credential store | `NamespaceRepository:DataDirectory` (JSON file, crash-safe temp-file-then-atomic-rename) | Encrypted connection strings / auth config per namespace |

Schema changes to the SQLite store ship as real EF Core migrations under
`Infrastructure/Persistence/Migrations/`. The namespace store predates the SQLite database and was
never migrated into it — unifying them is a known, deliberately deferred simplification, not an
active defect (see the "Frozen" list in [`CLAUDE.md`](../CLAUDE.md)).

## 8. Authentication and security boundaries

Four independent authentication paths compose, all off unless configured:
`EasyAuthMiddleware` (Azure App Service), `OidcBearerAuthenticationMiddleware` (any standards-
compliant IdP), `ApiKeyAuthenticationMiddleware` (scoped API keys), and `SpaTokenInjectionMiddleware`
(an ephemeral token injected into the served HTML at response time, confirming a request came from
the page ServiceHub served — not user identity). `Security:Authentication:Enabled` defaults `true`
with an **empty** key list rather than a shipped default key; the Docker Quick Start additionally
binds to `127.0.0.1` only, so a fresh deployment is safe by default even before an operator
configures real identity.

Connection strings are AES-GCM-256 encrypted at rest (`ENC[v1]:` prefix; legacy `ENC:V2:` values
are transparently decrypted and re-encrypted on read), with the encryption key derived via
HKDF/PBKDF2 from an operator-supplied master key — never generated or stored by ServiceHub itself,
and **not rotatable**: losing it, or changing it after namespaces are saved, makes every stored
connection string permanently undecryptable. There is no default; a placeholder value is rejected
outright outside `Development`. Any user-controlled value written to a log line is routed through
`LogRedactor.SanitiseForLog()`, enforced in CI by CodeQL (`cs/log-forging`). Message *bodies* are
never persisted in full — only a SHA-256 hash plus a capped preview — so investigation never
requires retaining the sensitive payload itself.

Owner-scoping (`OwnerId`) threads through the ledger, the eligibility gate, and SSE visibility
checks, so one authenticated identity can never see another's data even though the process itself
is single-tenant per deployment.

## 9. Self-hosting model, and why it's intentional

ServiceHub is self-hosted, single-instance software for one team — not a multi-tenant SaaS
platform. See [ADR-0004](adr/0004-self-hosted-security-model.md) for the full reasoning; in short:
message queue contents are frequently sensitive (payment payloads, PII, internal system state), and
the only architecture that makes a blanket "your data never leaves your network" claim true is one
where ServiceHub runs inside *your* network, using *your* cloud credentials, writing to *your*
disk. A hosted multi-tenant version would need a fundamentally different trust model (customer data
crossing into a vendor-operated environment) that this project deliberately does not build toward.
The cost of that choice is real — no built-in horizontal scaling, no cross-team sharing beyond
per-owner API/OIDC scoping — and is documented as a trade-off, not hidden as a limitation. See
[`self-hosting/README.md`](../self-hosting/README.md) for the operational side of running it for
real: persistent storage, least-privilege cloud credentials, and what to change before exposing it
beyond `localhost`.

## 10. Where to go next

- [`docs/adr/`](adr/) — why these specific decisions were made, and what was rejected.
- [`docs/extending/adding-a-provider.md`](extending/adding-a-provider.md) — add a new
  `ICloudMessagingProvider`.
- [`docs/RECOVERY-EVIDENCE.md`](RECOVERY-EVIDENCE.md) — the ledger's evidence model and export
  format, for auditors and integrators.
- [`CONTRIBUTING.md`](../CONTRIBUTING.md) — development setup, tests, PR process.
- [`CLAUDE.md`](../CLAUDE.md) — the file governing AI-assisted development in this repository;
  useful as a dense summary of invariants even if you're not using an AI assistant.
