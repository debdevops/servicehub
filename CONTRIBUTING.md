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
| .NET SDK | 10.0 or later |
| Node.js | 22.x or later |
| npm | 10.x or later |

### Quick start

```bash
# Clone
git clone https://github.com/debdevops/servicehub.git
cd servicehub

# Start everything (API + React dev server + hot reload)
./run.sh
# or on Windows
./run.ps1
```

The React UI is served at **http://localhost:3000** and the API at **http://localhost:5153** (proxied via Vite).

---

## Running Tests

### Backend (xUnit + coverage)

```bash
cd services/api
dotnet test tests/ServiceHub.UnitTests --configuration Release

# With coverage report (requires reportgenerator):
dotnet test tests/ServiceHub.UnitTests \
  --settings coverlet.runsettings \
  --results-directory TestResults
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" \
  -targetdir:"TestResults/CoverageReport" -reporttypes:Html
```

### Frontend (Vitest + coverage)

```bash
npm run -w apps/web test:coverage

# The shared hooks/API-client package (packages/servicehub-ui-shared) has its own suite —
# most API and hook logic lives here, not in apps/web:
npm run -w packages/servicehub-ui-shared test
```

### Lint and typecheck

```bash
npm run -w apps/web lint
npm exec -w apps/web -- tsc -b
```

### End-to-End (Playwright)

```bash
cd apps/web
npm run test:e2e   # starts its own dev server against client-side Demo Mode — no backend needed
```

**Coverage threshold:** Both backend and `apps/web` must maintain ≥60% line coverage. CI enforces this automatically.

---

## Pull Request Process

1. **Fork** the repository and create a feature branch from `main`.
2. Make your changes with clear, focused commits.
3. Write or update tests to cover your changes.
4. Run `dotnet build --configuration Release` and `npm run build` locally — both must pass with zero warnings.
5. Run the full test suites (see above) — all must pass.
6. Open a PR against `main` with a clear description of what changed and why.
7. Address any CI failures or review feedback promptly.

**Branch naming convention:**
- `feature/<short-description>` — new features
- `fix/<short-description>` — bug fixes
- `hotfix/<short-description>` — urgent production fixes
- `docs/<short-description>` — documentation only

---

## Code Style

### C# (Backend)

- Follow existing file-scoped namespace conventions (`namespace Foo.Bar;`)
- Use `sealed` on non-inheritable classes
- All public and protected members must have XML doc-comments
- Use `Result<T>` / `Result` pattern for fallible operations — do **not** throw business exceptions
- Sanitise any user-supplied string before logging with `LogRedactor.SanitiseForLog()`
- Use `ArgumentNullException.ThrowIfNull()` or explicit null guard in constructors
- No `string.Format` — use interpolated strings or structured logging parameters

### TypeScript / React (Frontend)

- All exported components must have a JSDoc comment
- Hooks live in `packages/servicehub-ui-shared/src/hooks/`; API calls live in
  `packages/servicehub-ui-shared/src/lib/api/` — not under `apps/web/src/`
- Do not add new `any` types — use proper generics or `unknown`
- Run `npx tsc -b` before committing to catch type errors

### General

- No secrets, API keys, or credentials in source code — ever.
- Comments should explain **why**, not **what**. The code already shows what.

---

## Architecture Overview

```
servicehub/
├── apps/web/                        # React 19 SPA (Vite + TypeScript) — the supported product surface
│   └── src/
│       ├── components/              # Reusable UI components
│       ├── hooks/                   # One app-specific hook (useQuickAccessHistory) — see packages/servicehub-ui-shared below
│       └── pages/                   # Route-level page components, registered in router.tsx
│
├── apps/demo/, apps/sandbox/        # Experimental, standalone exploratory apps — not CI-tested beyond
│                                     # lint/typecheck/build. See apps/demo/README.md, apps/sandbox/README.md.
│
├── packages/servicehub-ui-shared/   # Every TanStack Query hook, the Axios API client (lib/api/),
│   └── src/                         # client-side AI heuristics (lib/ai/), and Demo Mode fixtures
│       ├── hooks/                   # (lib/demo/, lib/*MockData.ts). Consumed by apps/web, apps/demo,
│       ├── lib/api/                 # and apps/sandbox as @servicehub/ui-shared. This is the only place
│       └── lib/demo/, lib/ai/       # API calls should originate from — not apps/web/src.
│
├── services/api/                    # .NET 10 Web API
│   └── src/
│       ├── ServiceHub.Api/            # Controllers, middleware, DI
│       ├── ServiceHub.Core/           # Domain entities, interfaces, DTOs — no external deps
│       ├── ServiceHub.Infrastructure/ # Azure Service Bus, SQLite persistence, encryption, rule engine
│       ├── ServiceHub.Infrastructure.Aws/  # AWS SQS/SNS
│       ├── ServiceHub.Infrastructure.Gcp/  # GCP Pub/Sub
│       └── ServiceHub.Shared/         # Result<T>, constants, helpers
│
└── run.sh / run.ps1       # One-command local dev launcher
```

The API uses a **Result/Error pattern** (no exceptions for business logic), **AES-256-GCM** for connection string encryption, and owner-scoped isolation via `OwnerId` on every data access. The SPA authenticates via an ephemeral SPA token injected into `<meta>` at page load time.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full picture (provider abstraction, the
Recovery Evidence Ledger, autonomy/safety model, persistence, SSE) and [`docs/adr/`](docs/adr/) for
why the foundational decisions (provider model, ledger design, single-instance + SQLite,
self-hosted-only) were made the way they were.

---

## Contributing a Provider

`ICloudMessagingProvider` (`ServiceHub.Core.Interfaces`) is the extension point for a new messaging
backend — Kafka, RabbitMQ, IBM MQ, ActiveMQ, Pulsar, NATS, or a fourth cloud provider.
[`docs/extending/adding-a-provider.md`](docs/extending/adding-a-provider.md) is the practical
how-to, written from what the Azure/AWS/GCP providers actually needed, not a theoretical design. It
covers registration, the `ProviderCapabilities` declaration every provider must make, the friction
points the first three providers hit, and the test/security checklist.

The single most important rule for a new provider: **declare `ProviderCapabilities` honestly.** A
capability your provider cannot safely support must be declared `false`, never approximated as
`true` and left to fail (or silently misbehave) at call time. This is what keeps backend gating and
UI copy from ever drifting apart — see [ADR-0001](docs/adr/0001-provider-abstraction-and-capabilities.md).

---

## Safety Requirements

ServiceHub's core value is being a safe forensic tool around destructive operations. Any PR that
touches replay, purge, send, bulk operations, the Recovery Evidence Ledger, or autonomy/eligibility
logic (`RecoveryEligibilityGate`, `AutonomyEvaluationWorker`) must preserve these invariants — see
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md#5-recovery-evidence-ledger-and-verification) and
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md#6-autonomy-and-safety-model) for the full model:

- **No fabricated recovery evidence.** A provider that cannot prove a message stayed off the DLQ
  (`ProviderCapabilities.CanProveDlqAbsence == false`) must close the ledger entry `Unverified` with
  a real reason — never `Recovered`. Don't "fix" an `Unverified` result by relaxing this check.
- **No unattended purge, ever, at any autonomy level.** This is unconditional, not configurable.
- **Every replay/purge attempt writes the ledger in the same code path that performs the operation.**
  `RecoveryPathCoverageTests` enforces this by IL scan with an empty exemption list — if your change
  needs an exemption, the design is wrong, not the test.
- **AI-adjacent code never calls a mutating `IRecoveryLedger` method or the replay/purge paths.**
  Enforced by `AIBoundaryArchitectureTests`. AI produces recommendations a human reviews — it does
  not execute anything itself.
- **No unsafe polling of a provider whose peek is destructive.** Check
  `ProviderCapabilities.SupportsRepeatablePeek` before adding any auto-refresh, live-tail, or
  polling behavior against a provider — AWS SQS's peek is a real receive that counts toward the
  queue's redelivery limit.
- **Automatic demotion on verified failure cannot be gated behind a configuration flag.**

If a change to safety-critical code doesn't fit these invariants, that's a sign to open an issue and
discuss the design before implementing, not to work around a failing architecture test.
