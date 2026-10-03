#!/usr/bin/env bash
# ============================================================================
# ServiceHub regression pack
#
#   ./runtest.sh                 run everything (backend unit + integration,
#                                frontend unit with coverage threshold)
#   ./runtest.sh --backend       backend suites only
#   ./runtest.sh --frontend      frontend suite only
#   ./runtest.sh --quick         backend unit + frontend unit (skips the
#                                slower integration suite)
#
# Contract: if this script exits 0, the core messaging functionality for
# Azure Service Bus, AWS SQS/SNS, and GCP Pub/Sub — plus the security
# invariants around it — is verified by the automated regression pack.
# ============================================================================
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_DIR="$SCRIPT_DIR/services/api"
WEB_DIR="$SCRIPT_DIR/apps/web"

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; BLUE='\033[0;34m'; NC='\033[0m'

RUN_BACKEND=true
RUN_INTEGRATION=true
RUN_FRONTEND=true

case "${1:-}" in
  --backend)  RUN_FRONTEND=false ;;
  --frontend) RUN_BACKEND=false; RUN_INTEGRATION=false ;;
  --quick)    RUN_INTEGRATION=false ;;
  "") ;;
  *) echo "Unknown option: $1 (use --backend | --frontend | --quick)"; exit 2 ;;
esac

declare -a RESULTS=()
FAILED=0
START_TIME=$(date +%s)

section() { printf "\n${BLUE}══════════════════════════════════════════════════════════════${NC}\n${BLUE}  %s${NC}\n${BLUE}══════════════════════════════════════════════════════════════${NC}\n" "$1"; }

record() { # name, exit code
  if [ "$2" -eq 0 ]; then
    RESULTS+=("$(printf "${GREEN}PASS${NC}  %s" "$1")")
  else
    RESULTS+=("$(printf "${RED}FAIL${NC}  %s" "$1")")
    FAILED=1
  fi
}

if $RUN_BACKEND; then
  section "Backend · unit tests (Azure + AWS + GCP providers, rules, security)"
  (cd "$API_DIR" && dotnet test tests/ServiceHub.UnitTests/ServiceHub.UnitTests.csproj --nologo --verbosity quiet)
  record "Backend unit tests" $?
fi

if $RUN_INTEGRATION; then
  section "Backend · integration tests (API pipeline, guards, persistence)"
  (cd "$API_DIR" && dotnet test tests/ServiceHub.IntegrationTests/ServiceHub.IntegrationTests.csproj --nologo --verbosity quiet)
  record "Backend integration tests" $?
fi

if $RUN_FRONTEND; then
  section "Frontend · unit tests (hooks, pages, multi-cloud UI) + coverage gate"
  (cd "$WEB_DIR" && npm run test:coverage --silent)
  record "Frontend unit tests + coverage threshold" $?
fi

ELAPSED=$(( $(date +%s) - START_TIME ))

section "Regression pack summary"
for line in "${RESULTS[@]}"; do printf "  %b\n" "$line"; done
printf "\n  Elapsed: %dm %ds\n" $((ELAPSED / 60)) $((ELAPSED % 60))

if [ "$FAILED" -eq 0 ]; then
  printf "\n${GREEN}✔ All suites green — Azure, AWS, and GCP messaging core is protected.${NC}\n"
else
  printf "\n${RED}✘ Regression pack FAILED — do not ship. See suite output above.${NC}\n"
fi
exit $FAILED
