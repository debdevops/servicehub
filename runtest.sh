#!/usr/bin/env bash
# ServiceHub 4.1.0 — the test suites.
#
# Every test lives under tests/ — see tests/README.md. This is the local twin of .github/workflows/servicehub-4-1-0.yml.
#
# ⚠️  This script does NOT type-check the frontend, so local suites can be green while CI is red.
#     Run `npm run typecheck` too — or use --all, which does everything CI does.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

usage() {
  cat <<'USAGE'
Usage: ./runtest.sh [--quick | --backend | --frontend | --e2e | --all]

  (no flags)   Backend + frontend test suites.
  --quick      Backend unit tests only. For a fast inner loop.
  --backend    Backend unit + integration tests.
  --frontend   Frontend unit tests only (tests/web).
  --e2e        Browser tests only (tests/e2e). Uses your installed Chrome; set PW_CHANNEL= to use Playwright's Chromium.
  --all        Everything CI runs: lint, type-check, both suites with their 60% coverage floors, the browser
               tests, and the guards.
USAGE
}

MODE="${1:-default}"
case "$MODE" in
  --help|-h) usage; exit 0 ;;
  --quick|--backend|--frontend|--e2e|--all|default) ;;
  *) echo "Unknown option: $MODE" >&2; usage; exit 1 ;;
esac

SLN="services/api/ServiceHub.slnx"
step() { printf '\n\033[1m▶ %s\033[0m\n' "$1"; }

if [ "$MODE" = "--quick" ]; then
  step "Backend unit tests"
  dotnet test "$SLN" --nologo --filter "FullyQualifiedName!~IntegrationTests"
  exit 0
fi

if [ "$MODE" = "--e2e" ]; then
  step "Browser tests (tests/e2e)"
  PW_CHANNEL="${PW_CHANNEL-chrome}" npm run e2e -w apps/servicehub
  exit 0
fi

if [ "$MODE" != "--frontend" ]; then
  if [ "$MODE" = "--all" ]; then
    step "Backend tests (unit + integration) with the 60% coverage floor"
    COV_DIR="$(mktemp -d)"
    dotnet test "$SLN" --nologo --collect:"XPlat Code Coverage" --results-directory "$COV_DIR"
    tests/scripts/check-backend-coverage.sh "$COV_DIR" 60
    rm -rf "$COV_DIR"
  else
    step "Backend tests (unit + integration)"
    dotnet test "$SLN" --nologo
  fi
fi

if [ "$MODE" != "--backend" ]; then
  if [ "$MODE" = "--all" ]; then
    step "Frontend tests with the 60% coverage floor"
    npm run test:coverage -w apps/servicehub
  else
    step "Frontend tests"
    npm run test -w apps/servicehub
  fi
fi

if [ "$MODE" = "--all" ]; then
  step "Frontend type-check (runtest.sh normally skips this — CI does not)"
  npm run typecheck -w apps/servicehub

  step "Frontend lint (app and tests)"
  npm run lint -w apps/servicehub

  step "Browser tests (tests/e2e)"
  PW_CHANNEL="${PW_CHANNEL-chrome}" npm run e2e -w apps/servicehub

  step "Bundle budget (built into a temp folder, so a running API's files are untouched)"
  BUDGET_DIR="$(mktemp -d)"
  (cd apps/servicehub && npx vite build --outDir "$BUDGET_DIR" --emptyOutDir --logLevel error)
  ./.github/scripts/check-bundle-budget.sh "$BUDGET_DIR"
  rm -rf "$BUDGET_DIR"

  step "Documentation references resolve"
  python3 .github/scripts/check-docs.py

  step "Roadmap board vs task cards"
  ./.github/scripts/verify-roadmap.sh

  step "Archive freeze guard (against origin/main)"
  if git rev-parse --verify --quiet origin/main >/dev/null; then
    ./.github/scripts/archive-freeze-guard.sh origin/main
  else
    echo "  skipped — no origin/main in this clone"
  fi
fi

printf '\n\033[32m✓ Done.\033[0m\n'
