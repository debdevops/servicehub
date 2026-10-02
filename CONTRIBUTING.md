# Contributing to ServiceHub

**ServiceHub** is a self-hosted, open-source forensic debugger for cloud message queues (Azure
Service Bus, AWS SQS/SNS, GCP Pub/Sub). Thank you for your interest in contributing! This document
explains how to get started, what to expect, and how to report issues.

---

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Reporting Security Issues](#reporting-security-issues)
- [Reporting Bugs](#reporting-bugs)
- [Requesting Features](#requesting-features)
- [Development Setup](#development-setup)
- [Running Tests](#running-tests)
- [Pull Request Process](#pull-request-process)
- [Code Style](#code-style)
- [Architecture Overview](#architecture-overview)
- [Contributing a Provider](#contributing-a-provider)
- [Safety Requirements](#safety-requirements)

---

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating you agree to abide by its terms.

---

## Reporting Security Issues

**Do NOT open a public GitHub issue for security vulnerabilities.**

Please read [SECURITY.md](SECURITY.md) for instructions on responsible disclosure. We aim to respond within 48 hours.

---

## Reporting Bugs

Before filing a bug:

1. Search [existing issues](https://github.com/debdevops/servicehub/issues) to avoid duplicates.
2. Collect the relevant logs (redact any connection strings or secrets).
3. Note your OS, .NET version, and Node version.

Then open a [GitHub Issue](https://github.com/debdevops/servicehub/issues/new) with the **Bug report** template.

---

## Requesting Features

Open a [GitHub Issue](https://github.com/debdevops/servicehub/issues/new) with the **Feature request** template. Describe the use-case, not just the solution.

---

## Development Setup

### Prerequisites

| Tool | Version |
|---|---|
| .NET SDK | 10.0 or later (`services/api/global.json`) |
| Node.js | 22.x |
| npm | 10.x or later |

### Quick start

```bash
git clone https://github.com/debdevops/servicehub.git
cd servicehub
npm ci
./run.sh       # API on http://localhost:5153, web on http://localhost:3000 (proxied by Vite)
```

`./run.sh --check` verifies your tools first; [docs/LOCAL-SETUP.md](docs/LOCAL-SETUP.md) has the full walkthrough and troubleshooting.

`/demo/azure` runs the whole UI on made-up data — no API, no cloud, nothing sent — which is the quickest way to try a UI change.

The frozen 4.0.0 codebase in `archive/servicehub-4.0.0/` is a parts bin: read it, copy from it, **never edit it and never import it**
(an architecture test fails the build if you do).

---

## Running Tests

Every test lives under [`tests/`](tests/README.md): `tests/backend` (xUnit), `tests/web` (Vitest) and `tests/e2e` (Playwright).

```bash
./runtest.sh --quick      # backend unit tests only — the fast inner loop
./runtest.sh              # backend + frontend suites
./runtest.sh --e2e        # browser tests (uses your Chrome; PW_CHANNEL= for Playwright's Chromium)
./runtest.sh --all        # everything CI runs: lint, type-check, both suites with coverage floors, browser tests, guards
```

Plain `./runtest.sh` does **not** type-check the frontend; CI does (`npm run typecheck -w apps/servicehub`), so run it before pushing.

**Coverage floor: 60 %** — backend lines, and frontend lines, statements, functions and branches. CI enforces it. It is a floor to stop rot,
not a goal: a change is finished when its behaviour is proven.

---

## Pull Request Process

1. **Fork** the repository and create a feature branch from `main`.
2. Make your changes with clear, focused commits.
3. Write or update tests to cover your changes.
4. Run `./runtest.sh --all` — everything must pass, with zero build warnings (warnings are errors).
5. Open a PR against `main` with a clear description of what changed and why.
6. Address any CI failures or review feedback promptly.

**Branch naming convention:**
- `feature/<short-description>` — new features
- `fix/<short-description>` — bug fixes
- `hotfix/<short-description>` — urgent production fixes
- `docs/<short-description>` — documentation only

---

## Code Style

### C# (Backend)

- File-scoped namespaces (`namespace Foo.Bar;`) and `sealed` on classes that are not meant to be inherited.
- Use `Result<T>` / `Result` for fallible operations — do **not** throw business exceptions.
- Sanitise any user-supplied string before logging with `LogRedactor.SanitiseForLog()`.
- Guard constructor arguments (`ArgumentNullException.ThrowIfNull`).
- Never branch on a provider's name — ask `ProviderCapabilities`. An architecture test fails the build if you do.

### TypeScript / React (Frontend)

- API calls live in `apps/servicehub/src/lib/api/` (one module per controller); screens use hooks over them.
- A screen is added in one place, `apps/servicehub/src/nav/navigation.ts`; the router, sidebar and command palette all read it.
- No new `any` — use generics or `unknown`. Colours come from the design tokens in `styles/index.css`, never a literal.
- Simple screens use Simple's words: no *signature*, *ledger*, *autonomy level* or *grant* (a browser test reads the screen and fails on them).

### General

- No secrets, API keys, or credentials in source code — ever.
- Comments explain **why**, not **what**.

---

## Architecture Overview

```
services/api/src/
  ServiceHub.Core            entities, enums, interfaces, Result<T> — depends on nothing
  ServiceHub.Infrastructure  EF Core/SQLite, RecoveryLedger (hash chain + eligibility gate), Agents, Rules, Events, Security, Routing
  ServiceHub.Providers.Azure / .Aws / .Gcp   one adapter per cloud, peers of each other
  ServiceHub.Api             controllers, middleware, DI root, wwwroot (the built SPA — git-ignored)
apps/servicehub/src          React 19 · Vite · TypeScript: nav/navigation.ts → router → pages; lib/api/* → hooks
tests/                       backend · web · e2e (tests/README.md)
archive/servicehub-4.0.0/    frozen 4.0.0 — a parts bin, never edited, never imported
```

The dependency direction is `Api → Infrastructure → Core` and `Providers → Core`; nothing depends on a provider adapter but the DI root.
`DependencyDirectionTests` fails the build if that changes. Five places you extend the product without touching the rest: a **provider**
(implement `ICloudMessagingProvider`, declare `ProviderCapabilities`), an **agent** (one class + one `AddAgent` line in `Program.cs`), an
**event** handler (`IPlatformEventBus`), a **screen** (one entry in `nav/navigation.ts`), and an **API module** (`lib/api/<controller>.ts`).
The source cites decision records as `ADR-00NN` in comments; the maintainers keep those records outside this repository, so ask in the PR
or issue if a comment's reasoning is not clear from the code.

---

## Contributing a Provider

Step-by-step: [Adding a messaging provider](docs/extending/adding-a-provider.md) and [Adding an agent](docs/extending/adding-an-agent.md).

`ICloudMessagingProvider` (`ServiceHub.Core.Interfaces`) is the extension point for a new messaging backend. Look at the Azure, AWS and
GCP adapters under `services/api/src/ServiceHub.Providers.*` — registration is one `Add<Cloud>Provider` extension and one line in
`Program.cs` — and at `CapabilityHonestyTests`, which fails the build if any code outside an adapter branches on a provider's name.

The most important rule: **declare `ProviderCapabilities` honestly.** A capability your provider cannot safely support is `false`, never
approximated as `true` and left to fail at call time. That keeps backend gating and UI copy from drifting apart. In particular
`CanProveDlqAbsence` gates unattended replay and whether a recovery can ever read *Verified*; declare it `true` only if a background scan can
page the entire entity, so "no recurrence found" really means absent.

---

## Safety Requirements

ServiceHub is a safe tool around destructive operations. A change to replay, purge, send, bulk operations, the Recovery Ledger or the
eligibility gate (`RecoveryEligibilityGate`) must preserve these invariants:

- **No fabricated recovery evidence.** A provider that cannot prove a message stayed off the DLQ (`CanProveDlqAbsence == false`) closes the
  ledger entry `Unverified` with a real reason — never `Recovered`. Do not "fix" an `Unverified` result by relaxing the check.
- **One gated route.** Every replay, by a person or an agent, goes through the eligibility gate, which **fails closed**: a check that cannot
  run blocks the replay. Dangerous requests carry an intent header (`X-ServiceHub-Intent`) so a stray request cannot trigger them.
- **No unattended purge, ever.** Unconditional, not configurable.
- **Every replay or purge writes the ledger in the same code path that performs it**, and the ledger is append-only and hash-chained
  (enforced in the DbContext).
- **No unsafe polling.** Check `ProviderCapabilities.SupportsRepeatablePeek` before adding auto-refresh, live tail or polling against a
  provider whose peek is a real receive — AWS SQS's counts toward the redelivery limit.
- **Automatic demotion on verified failure is never behind a configuration flag.**
- **AI only recommends** — a person or the gate decides.

If a change to safety-critical code does not fit these, open an issue and discuss the design first; do not work around a failing test.
