#!/usr/bin/env bash
# ServiceHub 4.1.0 — development.
#
# Starts the API and the Vite dev server together, and stops both on Ctrl-C. The browser only ever
# talks to Vite, which proxies /api and /health to the API — so there is no CORS to configure, and
# in production they are the same origin anyway (ADR-0014 D3).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

API_PORT="${SERVICEHUB_API_PORT:-5153}"
WEB_PORT="${SERVICEHUB_WEB_PORT:-3000}"

usage() {
  cat <<'USAGE'
Usage: ./run.sh [--api-only | --web-only]

  (no flags)   Start the API and the web dev server together.
  --api-only   Start only the .NET API.
  --web-only   Start only the Vite dev server (expects an API already running).

Environment:
  SERVICEHUB_API_PORT   API port (default 5153)
  SERVICEHUB_WEB_PORT   Dev server port (default 3000)
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

# Start fresh: if a previous ServiceHub API / Vite dev server (from this checkout) still holds a port, stop
# exactly that PID and wait for the port to free. Anything else on the port is not ours, so we refuse.
listener_pids() { { lsof -nP -tiTCP:"$1" -sTCP:LISTEN 2>/dev/null || true; } | sort -u; }
free_port() {
  local port="$1" pattern="$2" label="$3" pid cmd
  local found; found="$(listener_pids "$port")"
  [ -z "$found" ] && return 0
  for pid in $found; do
    cmd="$(ps -o command= -p "$pid" 2>/dev/null || true)"
    if [[ "$cmd" == *"$pattern"* ]]; then
      echo "↻ Stopping previous ${label} on :${port} (pid ${pid})"
      kill "$pid" 2>/dev/null || true
    else
      echo "✖ Port ${port} is held by something else (pid ${pid}: ${cmd:0:100}). Free it or change the port." >&2
      exit 1
    fi
  done
  for _ in $(seq 1 100); do
    [ -z "$(listener_pids "$port")" ] && return 0
    sleep 0.1
  done
  for pid in $(listener_pids "$port"); do kill -9 "$pid" 2>/dev/null || true; done
  sleep 0.5
  [ -z "$(listener_pids "$port")" ] || { echo "✖ Could not free port ${port}." >&2; exit 1; }
}
[ "$MODE" != "web" ] && free_port "$API_PORT" "ServiceHub.Api" "API"
[ "$MODE" != "api" ] && free_port "$WEB_PORT" "$ROOT/node_modules" "dev server"

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
    npm run dev -w apps/servicehub -- --port "${WEB_PORT}" --strictPort &
  pids+=("$!")
fi

# Stop everything as soon as either process exits, so a crashed API is not hidden behind a live dev server.
while :; do
  for pid in "${pids[@]}"; do
    kill -0 "$pid" 2>/dev/null || { wait "$pid" || true; exit 1; }
  done
  sleep 1
done
