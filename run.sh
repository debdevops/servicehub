#!/usr/bin/env bash
# ServiceHub 4.1.0 — development.
#
# Starts the API and the Vite dev server together, tells you when both are actually ready, and stops both
# (and everything they started) on Ctrl-C. The browser only ever talks to Vite, which proxies /api and /health
# to the API — so there is no CORS to configure, and in production they are the same origin anyway (ADR-0014 D3).
#
# Full walkthrough, including troubleshooting: docs/LOCAL-SETUP.md
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

API_PORT="${SERVICEHUB_API_PORT:-5153}"
WEB_PORT="${SERVICEHUB_WEB_PORT:-3000}"
READY_TIMEOUT="${SERVICEHUB_READY_TIMEOUT:-180}"   # seconds; the first run compiles the API, which takes a while

usage() {
  cat <<'USAGE'
Usage: ./run.sh [--api-only | --web-only | --check]

  (no flags)   Start the API and the web dev server together.
  --api-only   Start only the .NET API.
  --web-only   Start only the Vite dev server (expects an API already running).
  --check      Verify your machine is ready (tools, versions, ports) and exit. Starts nothing.

Environment:
  SERVICEHUB_API_PORT       API port (default 5153)
  SERVICEHUB_WEB_PORT       Dev server port (default 3000)
  SERVICEHUB_DATA_DIR       Where the SQLite database lives (default: services/api/src/ServiceHub.Api/data)
  SERVICEHUB_READY_TIMEOUT  Seconds to wait for startup before giving up (default 180)
  ASPNETCORE_ENVIRONMENT    Defaults to Development, which supplies a throw-away dev encryption key
USAGE
}

MODE="both"
case "${1:-}" in
  --help|-h) usage; exit 0 ;;
  --api-only) MODE="api" ;;
  --web-only) MODE="web" ;;
  --check) MODE="check" ;;
  "") ;;
  *) echo "Unknown option: $1" >&2; usage; exit 1 ;;
esac

die() { echo "✖ $*" >&2; exit 1; }

# ── Preflight ───────────────────────────────────────────────────────────────────────────────────
# Fail here, in plain words, rather than three minutes in with a stack trace.
major() { echo "${1#v}" | cut -d. -f1; }

preflight() {
  local need_api=1 need_web=1
  [ "$MODE" = "web" ] && need_api=0
  [ "$MODE" = "api" ] && need_web=0

  command -v lsof >/dev/null 2>&1 || die "lsof is required (it is how this script avoids port clashes). macOS has it; on Linux: sudo apt install lsof"

  if [ "$need_api" = 1 ]; then
    command -v dotnet >/dev/null 2>&1 || die "The .NET SDK is not installed. Get .NET 10: https://dotnet.microsoft.com/download/dotnet/10.0"
    # Run from the API folder so global.json is honoured: a SDK that cannot satisfy it makes this fail.
    local sdk
    sdk="$(cd services/api && dotnet --version 2>/dev/null)" \
      || die "The installed .NET SDK does not satisfy services/api/global.json (needs 10.0.x). Installed: $(dotnet --list-sdks 2>/dev/null | tr '\n' ' ')"
    [ "$(major "$sdk")" -ge 10 ] || die ".NET SDK $sdk is too old; ServiceHub needs .NET 10."
    echo "✔ .NET SDK $sdk"
  fi

  if [ "$need_web" = 1 ]; then
    command -v node >/dev/null 2>&1 || die "Node.js is not installed. Get Node 22 LTS: https://nodejs.org"
    command -v npm  >/dev/null 2>&1 || die "npm is not installed (it ships with Node.js)."
    local node_v; node_v="$(node --version)"
    [ "$(major "$node_v")" -ge 20 ] || die "Node $node_v is too old; ServiceHub needs Node 20 or newer (22 LTS recommended)."
    echo "✔ Node $node_v"
  fi

  command -v curl >/dev/null 2>&1 || echo "• curl not found — startup will not be probed; watch the log for 'Now listening'."
}

preflight

# Install web dependencies the first time, and again whenever the lockfile moves on (a `git pull` that changed it).
ensure_node_modules() {
  if [ ! -d node_modules ] || [ package-lock.json -nt node_modules/.package-lock.json ]; then
    echo "▶ Installing web dependencies (npm ci)…"
    npm ci --no-audit --no-fund || die "npm ci failed. Delete node_modules and retry; see docs/LOCAL-SETUP.md → Troubleshooting."
  fi
}
[ "$MODE" != "api" ] && [ "$MODE" != "check" ] && ensure_node_modules

# ── Ports ───────────────────────────────────────────────────────────────────────────────────────
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
      die "Port ${port} is held by something else (pid ${pid}: ${cmd:0:100}). Free it, or set SERVICEHUB_API_PORT / SERVICEHUB_WEB_PORT."
    fi
  done
  for _ in $(seq 1 100); do
    [ -z "$(listener_pids "$port")" ] && return 0
    sleep 0.1
  done
  for pid in $(listener_pids "$port"); do kill -9 "$pid" 2>/dev/null || true; done
  sleep 0.5
  [ -z "$(listener_pids "$port")" ] || die "Could not free port ${port}."
}

if [ "$MODE" = "check" ]; then
  for p in "$API_PORT" "$WEB_PORT"; do
    if [ -n "$(listener_pids "$p")" ]; then echo "• Port $p is in use (run.sh will stop it if it is a previous ServiceHub, otherwise refuse)."
    else echo "✔ Port $p is free"; fi
  done
  echo "✔ Ready. Run ./run.sh"
  exit 0
fi

[ "$MODE" != "web" ] && free_port "$API_PORT" "ServiceHub.Api" "API"
[ "$MODE" != "api" ] && free_port "$WEB_PORT" "$ROOT/node_modules" "dev server"

# ── Process management ──────────────────────────────────────────────────────────────────────────
# `dotnet run` and `npm run` each start a child. Stopping only the parent leaves the child holding the port,
# so stop the whole tree, children first.
kill_tree() {
  local pid="$1" sig="${2:-TERM}" child
  for child in $(pgrep -P "$pid" 2>/dev/null || true); do kill_tree "$child" "$sig"; done
  kill "-$sig" "$pid" 2>/dev/null || true
}

pids=()
STOPPING=0
cleanup() {
  [ "$STOPPING" = 1 ] && return; STOPPING=1
  [ "${#pids[@]}" -gt 0 ] || return 0
  echo; echo "■ Stopping ServiceHub…"
  for pid in "${pids[@]}"; do kill_tree "$pid" TERM; done
  for _ in $(seq 1 50); do   # up to 5 s to exit gracefully (the API flushes SQLite and releases its instance lock)
    local alive=0
    for pid in "${pids[@]}"; do kill -0 "$pid" 2>/dev/null && alive=1; done
    [ "$alive" = 0 ] && return 0
    sleep 0.1
  done
  for pid in "${pids[@]}"; do kill_tree "$pid" KILL; done
}
trap cleanup EXIT
trap 'exit 130' INT TERM

if [ "$MODE" != "web" ]; then
  echo "▶ API      http://localhost:${API_PORT}   (first run compiles it — give it a minute)"
  # This script is for development, so the API runs as Development unless you say otherwise: that is what
  # supplies the throw-away dev encryption key. Without it (--no-launch-profile sets no environment) the API
  # starts as Production and, correctly, refuses to run with no key configured.
  [ -n "${SERVICEHUB_DATA_DIR:-}" ] && export ServiceHub__DataDirectory="$SERVICEHUB_DATA_DIR"
  ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}" \
  ASPNETCORE_URLS="http://localhost:${API_PORT}" \
    dotnet run --project services/api/src/ServiceHub.Api --no-launch-profile &
  pids+=("$!")
fi

if [ "$MODE" != "api" ]; then
  echo "▶ Web      http://localhost:${WEB_PORT}"
  # Vite is started directly rather than through `npm run`, so stopping it does not end in a wall of npm errors.
  [ -x "$ROOT/node_modules/.bin/vite" ] || die "Vite is missing from node_modules. Run: npm ci"
  ( cd apps/servicehub && VITE_PROXY_TARGET="http://localhost:${API_PORT}" \
      exec "$ROOT/node_modules/.bin/vite" --port "${WEB_PORT}" --strictPort ) &
  pids+=("$!")
fi

# ── Wait, announce, supervise ───────────────────────────────────────────────────────────────────
up() { curl -fsS -o /dev/null --max-time 2 "$1" 2>/dev/null; }
announced=0
waited=0
if ! command -v curl >/dev/null 2>&1; then announced=1; fi   # cannot probe; do not nag

# Stop everything as soon as either process exits, so a crashed API is not hidden behind a live dev server.
while :; do
  for pid in "${pids[@]}"; do
    if ! kill -0 "$pid" 2>/dev/null; then
      wait "$pid" || true
      echo "✖ A ServiceHub process exited — the output above says why. See docs/LOCAL-SETUP.md → Troubleshooting." >&2
      exit 1
    fi
  done

  if [ "$announced" = 0 ]; then
    ok=1
    [ "$MODE" != "web" ] && ! up "http://localhost:${API_PORT}/health/ready" && ok=0
    [ "$MODE" != "api" ] && ! up "http://localhost:${WEB_PORT}/" && ok=0
    if [ "$ok" = 1 ]; then
      announced=1
      echo
      echo "✔ ServiceHub is ready"
      if [ "$MODE" = "api" ]; then echo "   API  http://localhost:${API_PORT}   (health: /health/ready)"
      else echo "   Open http://localhost:${WEB_PORT}      (try /demo/azure first: made-up data, nothing is sent anywhere)"; fi
      echo "   Ctrl-C to stop."
      echo
    elif [ "$waited" -ge "$READY_TIMEOUT" ]; then
      echo "✖ Not ready after ${READY_TIMEOUT}s. Look for an error above; see docs/LOCAL-SETUP.md → Troubleshooting." >&2
      exit 1
    fi
  fi
  sleep 1
  waited=$((waited + 1))
done
