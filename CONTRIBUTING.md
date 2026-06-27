# Contributing to ServiceHub

Thank you for your interest in contributing to ServiceHub! This document explains how to get started, what to expect, and how to report issues.

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

---

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating you agree to abide by its terms.

---

## Reporting Security Issues

**Do NOT open a public GitHub issue for security vulnerabilities.**

Please read [SECURITY.md](SECURITY.md) for instructions on responsible disclosure. We aim to respond within 72 hours.

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

See [self-hosting/README.md](self-hosting/README.md) for full local development instructions.

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
cd apps/web
npm run test:coverage
```

### End-to-End (Playwright)

```bash
./run.sh --simulator   # starts API in simulator mode + Vite
cd apps/web
npm run test:e2e
```

**Coverage threshold:** Both backend and frontend must maintain ≥60% line coverage. CI enforces this automatically.

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
- Hooks live in `src/hooks/`; API calls live in `src/lib/api/`
- Do not add new `any` types — use proper generics or `unknown`
- Run `npx tsc -b` before committing to catch type errors

### General

- No secrets, API keys, or credentials in source code — ever.
- Comments should explain **why**, not **what**. The code already shows what.

---

## Architecture Overview

```
servicehub/
├── apps/web/              # React 19 SPA (Vite + TypeScript)
│   └── src/
│       ├── components/    # Reusable UI components
│       ├── hooks/         # React Query hooks
│       ├── lib/api/       # API client and typed wrappers
│       └── pages/         # Route-level page components
│
├── services/api/          # .NET 10 Web API
│   └── src/
│       ├── ServiceHub.Api/            # Controllers, middleware, DI
│       ├── ServiceHub.Core/           # Domain entities, interfaces, DTOs
│       ├── ServiceHub.Infrastructure/ # Azure Service Bus, persistence
│       ├── ServiceHub.Infrastructure.Aws/  # AWS SQS/SNS
│       ├── ServiceHub.Infrastructure.Gcp/  # GCP Pub/Sub
│       └── ServiceHub.Shared/         # Result<T>, constants, helpers
│
├── self-hosting/          # Deployment guides (local, Azure, AWS, GCP)
└── run.sh / run.ps1       # One-command local dev launcher
```

The API uses a **Result/Error pattern** (no exceptions for business logic), **AES-256-GCM** for connection string encryption, and **tenant isolation** via `OwnerId` on every data access. The SPA authenticates via an ephemeral SPA token injected into `<meta>` at page load time.
