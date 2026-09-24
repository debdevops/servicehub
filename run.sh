#!/usr/bin/env bash
# ServiceHub 4.1.0 — development.
#
# Starts the API and the Vite dev server together, and stops both on Ctrl-C. The browser only ever
# talks to Vite, which proxies /api and /health to the API — so there is no CORS to configure, and
# in production they are the same origin anyway (ADR-0014 D3).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

API_PORT="${SERVICEHUB_API_PORT:-5000}"
WEB_PORT="${SERVICEHUB_WEB_PORT:-5173}"

usage() {
  cat <<'USAGE'
Usage: ./run.sh [--api-only | --web-only]

  (no flags)   Start the API and the web dev server together.
  --api-only   Start only the .NET API.
  --web-only   Start only the Vite dev server (expects an API already running).

Environment:
  SERVICEHUB_API_PORT   API port (default 5000)
  SERVICEHUB_WEB_PORT   Dev server port (default 5173)
USAGE
}

MODE="both"
case "${1:-}" in
  --help|-h) usage; exit 0 ;;
  --api-only) MODE="api" ;;
  --web-only) MODE="web" ;;
  "") ;;
  *) echo "Unknown option: $1" >&2; usage; exit 1 ;;
esac

pids=()
cleanup() {
  for pid in "${pids[@]:-}"; do
    [ -n "${pid:-}" ] && kill "$pid" 2>/dev/null || true
  done
}
trap cleanup EXIT INT TERM

if [ "$MODE" != "web" ]; then
  echo "▶ API      http://localhost:${API_PORT}"
  # This script is for development, so the API runs as Development unless you say otherwise: that is what
  # supplies the throw-away dev encryption key. Without it (--no-launch-profile sets no environment) the API
  # starts as Production and, correctly, refuses to run with no key configured.
  ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}" \
  ASPNETCORE_URLS="http://localhost:${API_PORT}" \
    dotnet run --project services/api/src/ServiceHub.Api --no-launch-profile &
  pids+=("$!")
fi

if [ "$MODE" != "api" ]; then
  echo "▶ Web      http://localhost:${WEB_PORT}"
  VITE_PROXY_TARGET="http://localhost:${API_PORT}" \
    npm run dev -w apps/servicehub -- --port "${WEB_PORT}" &
  pids+=("$!")
fi

wait
