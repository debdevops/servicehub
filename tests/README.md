# Tests

Every ServiceHub test lives here, separated by what it proves. CI (`.github/workflows/servicehub-4-1-0.yml`) runs all
of it on pushes and pull requests to `main`, `develop`, `release` and the `feat/`, `feature/`, `fix/`, `bugfix/` and `hotfix/` branches; `./runtest.sh --all` runs the same things locally.

| Folder | What | Tooling | Gate |
|---|---|---|---|
| `backend/ServiceHub.UnitTests` | Domain, providers, persistence, security, recovery ledger, agents, routing. Includes `Architecture/` — the dependency direction, no branching on provider names, the archive staying a parts bin (`Category=Architecture`, reported as its own CI job). | xUnit · FluentAssertions · Moq | ≥ 60 % lines (backend total, generated code excluded) |
| `backend/ServiceHub.IntegrationTests` | The real API in-process over a real SQLite file: every controller, auth/governance, backup and restore, startup. The one live-Azure test skips unless credentials are supplied. | xUnit · `WebApplicationFactory` | counted in the backend total |
| `web/unit` | Components, pages, hooks, API clients, the demo adapter, navigation, design-token contrast. Mirrors `apps/servicehub/src`. `web/support` holds the shared setup and the axe helper. | Vitest · Testing Library · axe-core (jsdom) | ≥ 60 % lines, statements, functions **and** branches |
| `e2e` | A real browser over the production build, in demo mode (no API, no cloud): axe **with colour contrast**, a keyboard-only walk, no side-scroll at 1366×768, Simple's plain words, and whole-screen journeys. | Playwright · Chromium | all specs pass |

## Running

```bash
./runtest.sh --quick      # backend unit tests only — the fast inner loop
./runtest.sh --backend    # backend unit + integration
./runtest.sh --frontend   # tests/web
./runtest.sh --e2e        # tests/e2e (your installed Chrome; PW_CHANNEL= for Playwright's Chromium)
./runtest.sh --all        # everything CI runs, including both coverage floors, lint and type-check
```

Single things:

```bash
dotnet test services/api/ServiceHub.slnx --filter "Category=Architecture"
npm run test -w apps/servicehub -- tests/web/unit/lib          # a folder or file of web tests
npm run e2e -w apps/servicehub -- journeys                      # one Playwright spec
```

## Conventions

- **Where a test goes.** Web unit tests mirror `src/` (`src/lib/format.ts` → `web/unit/lib/format.test.ts`) and import the code
  under test with `@/…`; shared helpers are imported as `@tests/support/…`.
- **Demo mode is the e2e fixture.** Nothing in `e2e` needs an API, a cloud or credentials, so it cannot flake on live traffic.
  Demo answers reads and refuses every write in words — a journey asserts that refusal rather than a fake success.
- **Enums are camelCase on the wire.** A fixture written in PascalCase once hid a real bug; write fixtures in what the API sends.
- **Coverage is a floor, not a goal.** It stops rot. A unit is finished when its behaviour is proven, not when a number moves.
- **Live-cloud checks are not here.** They need real Azure/AWS/GCP and are run by hand (`e2e-verify`), never in CI.

Outputs (`coverage/`, `playwright-report/`, `test-results/`, `TestResults/`) are git-ignored.
