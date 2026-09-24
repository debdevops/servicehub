#!/usr/bin/env bash
# ServiceHub 4.1.0 — the test suites.
#
# ⚠️  This script does NOT type-check the frontend, so local suites can be green while CI is red.
#     Run `npm run typecheck` too — or use --all, which does everything CI does.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

usage() {
  cat <<'USAGE'
Usage: ./runtest.sh [--quick | --backend | --frontend | --all]

  (no flags)   Backend + frontend test suites.
  --quick      Backend unit tests only. For a fast inner loop.
  --backend    Backend unit + integration tests.
  --frontend   Frontend tests only.
  --all        Everything CI runs: lint, type-check, both suites, and the guards.
USAGE
}

MODE="${1:-default}"
case "$MODE" in
  --help|-h) usage; exit 0 ;;
  --quick|--backend|--frontend|--all|default) ;;
  *) echo "Unknown option: $MODE" >&2; usage; exit 1 ;;
esac

SLN="services/api/ServiceHub.slnx"
step() { printf '\n\033[1m▶ %s\033[0m\n' "$1"; }

if [ "$MODE" = "--quick" ]; then
  step "Backend unit tests"
  dotnet test "$SLN" --nologo --filter "FullyQualifiedName!~IntegrationTests"
  exit 0
fi

if [ "$MODE" != "--frontend" ]; then
  step "Backend tests (unit + integration)"
  dotnet test "$SLN" --nologo
fi

if [ "$MODE" != "--backend" ]; then
  step "Frontend tests"
  npm run test -w apps/servicehub
fi

if [ "$MODE" = "--all" ]; then
  step "Frontend type-check (runtest.sh normally skips this — CI does not)"
  npm run typecheck -w apps/servicehub

  step "Frontend lint"
  npm run lint -w apps/servicehub

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
