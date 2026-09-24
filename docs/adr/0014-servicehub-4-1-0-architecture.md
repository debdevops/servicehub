# ADR-0014: ServiceHub 4.1.0 — the shape of the rewritten stack

**Status:** Accepted — 2026-09-21, by the owner, answering the four items
[ADR-0013](0013-archive-the-entire-4-0-0-codebase.md) left open.

**Completes:** [ADR-0013](0013-archive-the-entire-4-0-0-codebase.md) — its "not decided here" list.
**Supersedes in part:** [ADR-0012](0012-single-servicehub-application.md) §5 (the `/new` fallback,
the cutover and the `/servicehub-4-0` mount) and its **D3**'s serving model. ADR-0012's one
application / one name principle stands.
**Relates to, and does not reopen:** [ADR-0001](0001-provider-abstraction-and-capabilities.md),
[ADR-0002](0002-recovery-evidence-ledger.md), [ADR-0003](0003-single-instance-sqlite.md),
[ADR-0004](0004-self-hosted-security-model.md), [ADR-0005](0005-ai-capability-boundary.md).
**Followed by:** [ADR-0015](0015-fresh-schema-for-4-1-0.md) — the database.
**Superseded in part by:** [ADR-0016](0016-simple-and-advanced-surfaces-in-4-1-0.md) — **D6**'s
"Advanced is out of 4.1.0". A read-only, five-screen Advanced surface ships in 4.1.0; D4 stands.

---

## Context

ADR-0013 archived the whole 4.0.0 codebase and said the new ServiceHub is rewritten beside it. It
deliberately stopped short of saying *what* the new stack is, and forbade new backend code until an
ADR said so. This is that ADR.

The material facts, measured on 2026-09-21 against `archive/servicehub-4.0.0/`:

| | |
|---|---|
| Backend | 587 C# files, **105,690 lines**, 6 projects, **33 controllers**, 31 entities, 21 migrations, **20 background workers** |
| Frontend | 53,783 lines, 33 pages, 58 components |
| Shared package | 14,536 lines, 29 API modules, 35 hooks |
| What 4.1.0 ships | **nine screens** (`docs-private/servicehub-4.1.0/PLAN.md`) |

The nine screens do not need 33 controllers or 30 tables. But the provider adapters, the
hash-chained ledger, the eligibility gate and the connection-string protection are years of
live-verified work against three real clouds, and rewriting them from memory would discard evidence
that cannot be re-earned cheaply.

## Decision

### D1 — Same stack: .NET 10 + EF Core + SQLite, React 19 + TypeScript + Vite + Tailwind

No new language, framework, runtime, broker or database. The stack is kept **precisely so that the
archive stays a usable parts bin**: a proven file can be copied across and compile, rather than be
translated by hand into a language it has never run in.

### D2 — The new backend is grown screen by screen, never copied wholesale

A new, empty solution at `services/api/`. **Nothing enters it until a screen needs it.** When a unit
needs behaviour that the archive already has, the archive's file is **copied whole** — not retyped,
not summarised — and its tests are copied with it.

This is the opposite of "copy it all and prune later", which is the fork-then-delete failure
ADR-0012 already rejected for the frontend: the pruning pass never finishes, and the old model
survives by inertia.

**Every copied file carries a provenance header**, so the difference between copied and written code
is mechanically auditable and a copy can be re-diffed against its source later:

```csharp
// Ported from ServiceHub 4.0.0
//   source: archive/servicehub-4.0.0/services/api/src/ServiceHub.Infrastructure/RecoveryLedger/RecoveryHashChain.cs
//   copied: 2026-09-21 for unit W2.4
//   changes: namespace only
```

`.github/scripts/verify-ported-files.sh` re-derives every `changes: namespace only` file from its
source and fails CI if the two have drifted apart without the header saying so.

### D3 — The new product owns the repository root; the archive is not served

```
servicehub/
├── apps/servicehub/            the SPA            → served at /
├── services/api/               the API            → /api/v1
├── archive/servicehub-4.0.0/   frozen, self-contained, NOT in the 4.1.0 image
├── docs/ .github/ self-hosting/ …
└── package.json  run.sh  runtest.sh  Dockerfile  .version
```

The 4.1.0 container builds and serves **only** 4.1.0. The archive keeps its own `Dockerfile`,
`run.sh` and `docker-compose.yml` and runs standalone from its folder; it stays in CI as a frozen
regression suite and it is published only under `v4.0.x` tags (ADR-0013 D6).

**This is what makes the serving model simple again.** There is no `/new` prefix, no basename
juggling, no cutover wave, no two-APIs-in-one-image problem, and no router owning half an origin.
The SPA is built into `services/api/src/ServiceHub.Api/wwwroot` and served with one fallback, as
4.0.0 did — but **that `wwwroot` is git-ignored this time**, so a local build no longer dirties
tracked files.

### D4 — The "ServiceHub 4.0" control is withdrawn from 4.1.0

D34 of the transition log gave the old surface a header control named "ServiceHub 4.0". With the
archive out of the image there is nothing for it to navigate to, and a control that 404s is worse
than no control. **4.1.0 ships no link to 4.0.0.** The archive is reached by running it, and the
release notes say so.

The consequence is deliberate and must not be worked around: **Advanced screens cannot "degrade
honestly to 4.0 ↗" any more.** An Advanced screen either exists natively or is not listed. That
removes the crutch that made a twelve-screen Advanced surface look cheap, and it is one reason
Advanced is out of 4.1.0 scope (D6).

### D5 — Four projects, one dependency direction, enforced by a test

```
ServiceHub.Core             contracts, entities, enums, domain rules.
                            Depends on NOTHING — no EF, no cloud SDK, no ASP.NET.
ServiceHub.Infrastructure   EF Core, SQLite, ledger, eligibility gate, agents, events.
                            Depends on Core.
ServiceHub.Providers.Azure  ┐ one project per cloud. Each depends on Core and its own SDK,
ServiceHub.Providers.Aws    │ and NEVER on Infrastructure or on another provider.
ServiceHub.Providers.Gcp    ┘
ServiceHub.Api              controllers, middleware, composition root. Depends on all of the above.
```

**This corrects a real asymmetry in 4.0.0**, where Azure lived *inside* `ServiceHub.Infrastructure`
while AWS and GCP were separate projects — so the "provider-neutral" product had one privileged
provider whose code could reach into infrastructure internals that the other two could not. Here the
three clouds are peers, and adding a fourth is a new project plus one registration line.

The dependency direction is not a convention in a document: `ServiceHub.UnitTests` asserts it over
the loaded assemblies, so a wrong `ProjectReference` fails CI rather than a code review.

### D6 — 4.1.0 is nine screens: the eight-screen product plus Agents, and escalation

**In:** Fleet Overview · Home · DLQ · Active Messages · Replay · Bulk Replay · Auto Replay ·
Agents · Connect, plus Settings and Help; the Outcome Card's capability honesty; the recovery
evidence ledger and the eligibility gate; and the escalation path (pending-work bell, toast, Slack,
Teams) that closes the product's first mandatory gap.

**Out, to 4.2.0 and later:** the Advanced surface (A0–A12), the full autonomy ladder beyond what the
gate needs, Users management, and every 4.0.0 capability not named above. What 4.1.0 does not carry
is **not** deleted from the product's history — it exists, working, in the archive, and returns when
it is rebuilt deliberately.

### D7 — The Agents screen lists the agents that actually run

4.0.0 has twenty background workers, and the transition plan said the Agents screen lists twenty
hand-written descriptors. **In a rewritten backend that would be a lie on day one.** The new backend
starts with no workers and grows them as screens need them, so the screen lists **exactly the agents
registered in this build** — which will be far fewer than twenty for some time.

The mechanism is unchanged and is the point of it: an agent is **one file plus one registration
line**, and it appears on the screen with a name, a purpose, a kind, an authority, health and a
pause control, **without one line of UI being written**.

### D8 — A defect in 4.0.0 is fixed on a branch from `v4.0.0`, or not at all

The archive on `main` is never edited. If a defect in 4.0.0 genuinely must be fixed:

1. Branch from the **tag** `v4.0.0`, fix it there, release `v4.0.1` from that branch.
2. The fix is **never merged back** into `main`; `main`'s archive stays byte-identical to the
   released 4.0.0.
3. If the fix must also appear in `main`'s archive, that is a recorded exception: an ADR plus the
   `Archive-Change-Approved:` commit trailer the freeze guard already understands.

Default answer: **no hotfixes.** 4.0.0 is finished. A security issue is the case that would change
that, and it takes route 1.

### D9 — What replaces the strangler's safety net

ADR-0013 removed R2's guarantee ("existing tests are the contract; a test needing an edit is the
stop signal"). Three mechanisms replace it, and together they are stricter for the code that matters
and looser for the code that does not:

| # | Replacement |
|---|---|
| **R11** | **Copied code arrives with its tests, unedited.** If a copied test must be edited to pass, the behaviour changed — **stop and raise it**. This is R2's stop signal, narrowed to exactly the code that was proven. |
| **R12** | **Nothing enters the new tree until a screen needs it.** Unused code cannot be verified, and unverified code in a safety-critical product is a liability, not an asset. |
| **Ledger parity** | A replay executed by 4.1.0 produces a ledger entry that `scripts/verify-recovery-chain.py` verifies offline, and that is **diffed against an entry produced by the archive** — differing only in expected fields. The evidence format is a contract across the version boundary. |

Newly written code carries no inherited guarantee, so it is held to the live gates instead: every
wave gate in `docs-private/servicehub-4.1.0/PLAN.md` requires real cloud traffic (**R10**).

## Options considered and rejected

| Rejected | Why |
|---|---|
| **Copy the engine wholesale, then prune** | The prune never finishes. 33 controllers and 21 migrations would survive into a product that was meant to be smaller, and every one of them would have to be either verified or carried unverified. |
| **A different stack end to end (Node/TypeScript)** | Discards the Azure, SQS and Pub/Sub adapters, the hash-chained ledger and the eligibility gate — the most live-verified code in the repository — and forces all of it to be re-proven against three real clouds in a language it has never run in. The one language it buys is not worth that. |
| **New UI on the frozen 4.0.0 API** | Cheapest path to nine screens, and it makes R1 permanent: every backend fix for the rest of the product's life would require editing a frozen tree. It also contradicts ADR-0013's decision to rewrite. |
| **Serve both surfaces from one container** | Two APIs, two SQLite files, two static roots, and an archive that cannot be edited to cooperate. The reason to do it — "Opens in ServiceHub 4.0 ↗" fallbacks for unbuilt Advanced screens — evaporates once Advanced is out of 4.1.0 scope. |
| **Keep Azure inside `ServiceHub.Infrastructure`** | It is how 4.0.0 is laid out, and it is the reason "provider-neutral" needed a rule instead of being structural. Three peer projects make neutrality a compile-time fact. |
| **A `packages/` shared library on day one** | There is one consumer. A package boundary between an app and its only consumer buys nothing and costs a build step, a version and an import indirection. It is added the day a second consumer exists, and not before. |

## Consequences

**Positive**

- The serving model is trivial again: one SPA, one API, one container, one origin, no prefixes.
- The parts bin stays usable, because the stack did not change.
- The difference between copied (proven) and written (unproven) code is mechanically visible, and
  CI can tell when a copy drifts.
- Providers are structurally equal, so provider-neutrality stops depending on discipline.
- The Agents screen cannot overstate what the product does, because it renders the registry.

**Negative, and accepted**

- **4.1.0 is less capable than 4.0.0 at release**, by a wide margin. Twenty-four 4.0.0 destinations
  do not exist in it. That is the cost of the simplification, and it is the owner's decision.
- **Newly written code has no inherited test coverage.** The live gates are the compensation, and
  they are slower than a test suite.
- **No upgrade path from a 4.0.0 database** ([ADR-0015](0015-fresh-schema-for-4-1-0.md)).
- **The archive must keep building in CI** for its tests to remain a specification, which means CI
  keeps paying for a product that ships nowhere.

## What would make this decision wrong

1. **If the new backend ends up being the old backend with new paths**, the rewrite was not a
   rewrite — measured by the ratio of copied to written lines, and by whether the controller count
   passes 20.
2. **If a copied test has to be edited to pass**, behaviour changed silently (R11).
3. **If the provenance headers drift from reality**, the copied/written distinction is decoration;
   `verify-ported-files.sh` failing is the early warning.
4. **If the Agents screen ever lists an agent that does not run in that build**, D7 has been
   inverted into the thing it exists to prevent.
5. **If anything outside `archive/` imports from inside it**, the parts bin has become a dependency
   and the freeze is load-bearing for the new product.
