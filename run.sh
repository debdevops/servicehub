#!/usr/bin/env bash
# ServiceHub 4.1.0 — run from source.
#
# One command for anyone, developer or not:   ./run.sh
#
# It checks the machine, downloads and installs what is missing (the .NET 10 SDK and Node.js — into your home
# folder, no sudo, nothing system-wide is touched), restores and builds everything, starts the API and the web
# server together, tells you when both are actually answering, and stops both (and everything they started) on
# Ctrl-C. The browser only ever talks to Vite, which proxies /api and /health to the API — so there is no CORS
# to configure, and in production they are the same origin anyway (ADR-0014 D3).
#
# Full walkthrough, including troubleshooting: docs/LOCAL-SETUP.md
[ -n "${BASH_VERSION:-}" ] || exec bash "$0" "$@"   # started as `sh run.sh`? re-run under bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

# Ports: if you set one, it is honoured exactly (and we refuse if something else holds it). If you do not,
# the default is tried first and the next free port is used when another program already has it.
API_PORT="${SERVICEHUB_API_PORT:-5153}";  API_PORT_FIXED=0; [ -n "${SERVICEHUB_API_PORT:-}" ] && API_PORT_FIXED=1
WEB_PORT="${SERVICEHUB_WEB_PORT:-3000}";  WEB_PORT_FIXED=0; [ -n "${SERVICEHUB_WEB_PORT:-}" ] && WEB_PORT_FIXED=1
valid_port() { case "$1" in ""|*[!0-9]*) return 1 ;; esac; [ "${#1}" -le 5 ] && [ "$1" -ge 1 ] && [ "$1" -le 65535 ]; }
valid_port "$API_PORT" || { echo "✖ SERVICEHUB_API_PORT='${API_PORT}' is not a valid port (use a number from 1 to 65535)." >&2; exit 1; }
valid_port "$WEB_PORT" || { echo "✖ SERVICEHUB_WEB_PORT='${WEB_PORT}' is not a valid port (use a number from 1 to 65535)." >&2; exit 1; }
READY_TIMEOUT="${SERVICEHUB_READY_TIMEOUT:-180}"   # seconds to wait for startup (after the build)
AUTO_INSTALL="${SERVICEHUB_AUTO_INSTALL:-1}"       # 0 = never download anything, just report what is missing
TOOLS_DIR="${SERVICEHUB_TOOLS_DIR:-${HOME:-$ROOT}/.servicehub/tools}"   # where a missing .NET / Node is installed
NODE_LTS_LINE="v22.x"                              # the Node line downloaded when Node is missing or too old

usage() {
  cat <<'USAGE'
Usage: ./run.sh [--api-only | --web-only | --check] [--no-install]

  (no flags)    Check the machine, install anything missing, then start the API and the web dev server.
  --api-only    Start only the .NET API.
  --web-only    Start only the Vite dev server (expects an API already running).
  --check       Report what is ready and what would be installed, then exit. Starts and installs nothing.
  --no-install  Never download anything; stop with instructions if a tool is missing.

Environment:
  SERVICEHUB_API_PORT       API port (default 5153; if unset and busy, the next free port is used)
  SERVICEHUB_WEB_PORT       Dev server port (default 3000; same rule)
  SERVICEHUB_DATA_DIR       Where the SQLite database lives (default: services/api/src/ServiceHub.Api/data)
  SERVICEHUB_READY_TIMEOUT  Seconds to wait for startup before giving up (default 180)
  SERVICEHUB_AUTO_INSTALL   0 = same as --no-install (default 1)
  SERVICEHUB_TOOLS_DIR      Where a missing .NET SDK / Node.js is installed (default ~/.servicehub/tools)
  ASPNETCORE_ENVIRONMENT    Defaults to Development, which supplies a throw-away dev encryption key
USAGE
}

MODE="both"
for arg in "$@"; do
  case "$arg" in
    --help|-h) usage; exit 0 ;;
    --api-only) MODE="api" ;;
    --web-only) MODE="web" ;;
    --check) MODE="check" ;;
    --no-install) AUTO_INSTALL=0 ;;
    --yes|-y) ;;   # accepted for scripts; installing is already the default
    *) echo "Unknown option: $arg" >&2; usage >&2; exit 1 ;;
  esac
done

# ── Output helpers ──────────────────────────────────────────────────────────────────────────────
die()  { echo "✖ $*" >&2; exit 1; }
warn() { echo "! $*" >&2; }
step() { echo "▶ $*"; }
have() { command -v "$1" >/dev/null 2>&1; }
major() { echo "${1#v}" | cut -d. -f1; }
minor() { echo "${1#v}" | cut -d. -f2; }

TMP_DIR=""
PIDS=""   # space-separated; an empty array trips `set -u` on macOS's bash 3.2
STOPPING=0

kill_children() {   # direct children of $1, printed one per line; works with or without pgrep
  if have pgrep; then pgrep -P "$1" 2>/dev/null || true
  else ps -A -o pid= -o ppid= 2>/dev/null | awk -v p="$1" '$2==p {print $1}' || true; fi
}
# `dotnet run` and `npm run` each start a child. Stopping only the parent leaves the child holding the port,
# so stop the whole tree, children first.
kill_tree() {
  local pid="$1" sig="${2:-TERM}" child
  for child in $(kill_children "$pid"); do kill_tree "$child" "$sig"; done
  kill "-$sig" "$pid" 2>/dev/null || true
}

cleanup() {
  [ -n "$TMP_DIR" ] && rm -rf "$TMP_DIR" 2>/dev/null || true
  [ "$STOPPING" = 1 ] && return 0; STOPPING=1
  [ -n "$PIDS" ] || return 0
  echo; echo "■ Stopping ServiceHub…"
  local pid
  for pid in $PIDS; do kill_tree "$pid" TERM; done
  for _ in $(seq 1 50); do   # up to 5 s to exit gracefully (the API flushes SQLite and releases its instance lock)
    local alive=0
    for pid in $PIDS; do kill -0 "$pid" 2>/dev/null && alive=1; done
    [ "$alive" = 0 ] && return 0
    sleep 0.1
  done
  for pid in $PIDS; do kill_tree "$pid" KILL; done
}
trap cleanup EXIT
trap 'exit 130' INT TERM

# Must be called directly (not inside $(...)): a subshell would set TMP_DIR where cleanup() never sees it.
init_scratch() { [ -n "$TMP_DIR" ] || TMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/servicehub-run.XXXXXX")" || die "Could not create a temporary folder."; }

# ── Platform ────────────────────────────────────────────────────────────────────────────────────
OS="$(uname -s 2>/dev/null || echo unknown)"
case "$OS" in
  Darwin) PLAT="darwin" ;;
  Linux)  PLAT="linux" ;;
  MINGW*|MSYS*|CYGWIN*) die "This is a Windows shell. ServiceHub runs on Windows through WSL 2: open an Ubuntu (WSL) terminal, clone the repo there, and run ./run.sh. Guide: docs/LOCAL-SETUP.md" ;;
  *) die "Unsupported operating system '$OS'. ServiceHub runs on macOS and Linux (and Windows via WSL 2)." ;;
esac
case "$(uname -m 2>/dev/null)" in
  x86_64|amd64) ARCH="x64" ;;
  arm64|aarch64) ARCH="arm64" ;;
  *) ARCH="unsupported" ;;
esac
IS_MUSL=0
if [ "$PLAT" = "linux" ] && { ldd --version 2>&1 || true; } | grep -qi musl; then IS_MUSL=1; fi

# ── Downloading ─────────────────────────────────────────────────────────────────────────────────
need_downloader() { have curl || have wget || die "Neither curl nor wget is installed, and one is needed to download tools. Install curl (Debian/Ubuntu: sudo apt install curl · RHEL/Fedora: sudo dnf install curl · Alpine: apk add curl), or install .NET 10 and Node.js yourself and re-run."; }
fetch() {   # fetch URL DEST — retries, follows redirects, shows a progress bar on a terminal
  local url="$1" dest="$2"
  if have curl; then
    if [ -t 1 ]; then curl -fL --retry 3 --retry-delay 2 --connect-timeout 20 -# -o "$dest" "$url"
    else curl -fsSL --retry 3 --retry-delay 2 --connect-timeout 20 -o "$dest" "$url"; fi
  else
    wget -q --tries=3 --timeout=30 -O "$dest" "$url"
  fi
}
offline_help() {
  cat >&2 <<EOF
  If this is a network problem (offline, behind a proxy, or a firewall in the way):
  • Behind a proxy? Export HTTPS_PROXY (and HTTP_PROXY) in this shell and run ./run.sh again.
  • Offline / locked-down? Install these yourself, then run ./run.sh again:
      .NET 10 SDK  https://dotnet.microsoft.com/download/dotnet/10.0
      Node.js 22   https://nodejs.org
EOF
}
hash_of() {   # hash_of 256|512 FILE — empty output when no hashing tool exists
  local bits="$1" f="$2"
  if have "sha${bits}sum"; then "sha${bits}sum" "$f" | cut -d' ' -f1
  elif have shasum; then shasum -a "$bits" "$f" | cut -d' ' -f1
  elif have openssl; then openssl dgst "-sha${bits}" "$f" | sed 's/.*= *//'
  fi
}
need_hasher() {
  [ -n "$(hash_of 256 /dev/null)" ] && [ -n "$(hash_of 512 /dev/null)" ] \
    || die "Cannot verify downloads: none of sha256sum/sha512sum, shasum or openssl is installed. Install one (Debian/Ubuntu: sudo apt install coreutils openssl), or install .NET 10 and Node.js yourself and re-run."
}
check_disk() {   # check_disk DIR NEEDED_MB WHAT — warn (never block) when space is tight
  local dir="$1" need="$2" what="$3" free_kb
  free_kb="$(df -Pk "$dir" 2>/dev/null | awk 'NR==2 {print $4}' || true)"
  [ -n "$free_kb" ] || return 0
  [ "$((free_kb / 1024))" -ge "$need" ] || warn "Only $((free_kb / 1024)) MB free in $dir; ${what} needs about ${need} MB."
}

# ── .NET SDK ────────────────────────────────────────────────────────────────────────────────────
export DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
DOTNET_FOUND=""
dotnet_ok() {
  # Run from the API folder so global.json is honoured (it pins the 10.0 SDK): an SDK that cannot satisfy it fails here.
  have dotnet || return 1
  local v; v="$(cd "$ROOT/services/api" && dotnet --version 2>/dev/null)" || return 1
  [ -n "$v" ] && [ "$(major "$v")" = 10 ] || return 1
  DOTNET_FOUND="$v"
}
use_local_dotnet() {
  [ -x "$TOOLS_DIR/dotnet/dotnet" ] || return 1
  export DOTNET_ROOT="$TOOLS_DIR/dotnet"
  export PATH="$TOOLS_DIR/dotnet:$PATH"
}
install_dotnet() {
  need_downloader; need_hasher
  have tar || die "tar is required to unpack the .NET SDK. Install it (e.g. sudo apt install tar) and re-run."
  [ "$ARCH" != "unsupported" ] || die "This CPU ($(uname -m)) has no official .NET 10 SDK download. Install .NET 10 yourself: https://dotnet.microsoft.com/download/dotnet/10.0"
  step "Installing the .NET 10 SDK into $TOOLS_DIR/dotnet (about 250 MB; your system is not touched)…"
  mkdir -p "$TOOLS_DIR"; check_disk "$TOOLS_DIR" 1500 "the .NET SDK"
  init_scratch; local tmp="$TMP_DIR" rid="osx-$ARCH" meta line want file got
  [ "$PLAT" = "linux" ] && { rid="linux-$ARCH"; [ "$IS_MUSL" = 1 ] && rid="linux-musl-$ARCH"; }
  # No downloaded script is ever executed. Microsoft publishes the SHA-512 of the latest 10.0 SDK for each platform
  # next to the archive (its first line names the exact file); we download that archive and unpack it only if it matches.
  meta="https://aka.ms/dotnet/10.0/dotnet-sdk-${rid}.tar.gz"
  fetch "${meta}.sha512" "$tmp/sdk.sha512" || { offline_help; die "Could not fetch the .NET SDK checksum from Microsoft."; }
  line="$(head -1 "$tmp/sdk.sha512" | tr -d '\r')"; want="${line%% *}"; file="${line##* }"
  case "$file" in dotnet-sdk-10.0.*-${rid}.tar.gz) ;; *) die "Unexpected .NET SDK checksum file from Microsoft ('${file}'). Nothing was installed.";; esac
  case "$want" in *[!0-9a-f]*|"") die "Malformed .NET SDK checksum from Microsoft. Nothing was installed." ;; esac
  local ver; ver="$(echo "$file" | sed -E 's/^dotnet-sdk-(10\.0\.[0-9]+)-.*/\1/')"
  fetch "https://builds.dotnet.microsoft.com/dotnet/Sdk/${ver}/${file}" "$tmp/$file" || { offline_help; die "Could not download $file."; }
  got="$(hash_of 512 "$tmp/$file")"
  [ "$got" = "$want" ] || die "Checksum mismatch for $file (expected $want, got $got). The download is corrupt or tampered with; nothing was installed. Re-run to retry."
  # Unpack into a side folder first, so an interrupted install never leaves a half-built SDK behind.
  rm -rf "$TOOLS_DIR/dotnet.partial"; mkdir -p "$TOOLS_DIR/dotnet.partial"
  tar -xzf "$tmp/$file" -C "$TOOLS_DIR/dotnet.partial" || { rm -rf "$TOOLS_DIR/dotnet.partial"; die "Could not unpack $file."; }
  rm -rf "$TOOLS_DIR/dotnet"; mv "$TOOLS_DIR/dotnet.partial" "$TOOLS_DIR/dotnet"
  use_local_dotnet
  dotnet_ok || die "The .NET SDK was installed but does not satisfy services/api/global.json (needs 10.0.x)."
}
ensure_dotnet() {
  if dotnet_ok; then echo "✔ .NET SDK $DOTNET_FOUND"; return 0; fi
  # System .NET missing or wrong — try the copy this script installed on a previous run before downloading again.
  if use_local_dotnet && dotnet_ok; then echo "✔ .NET SDK $DOTNET_FOUND (installed by run.sh in $TOOLS_DIR/dotnet)"; return 0; fi
  local why="is not installed"
  have dotnet && why="does not satisfy services/api/global.json (needs 10.0.x). Installed: $(dotnet --list-sdks 2>/dev/null | cut -d' ' -f1 | tr '\n' ' ')"
  if [ "$MODE" = "check" ]; then echo "• .NET 10 SDK $why — ./run.sh will download it to $TOOLS_DIR/dotnet"; return 0; fi
  if [ "$AUTO_INSTALL" != 1 ]; then die "The .NET 10 SDK $why. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 (or drop --no-install to let run.sh do it)."; fi
  echo "• The .NET 10 SDK $why."
  install_dotnet
  echo "✔ .NET SDK $DOTNET_FOUND (installed by run.sh)"
}
# .NET on Linux needs ICU for culture data. Without it the API crashes on start with a cryptic error; invariant
# mode is the supported way to run without it.
check_dotnet_runtime_libs() {
  [ "$PLAT" = "linux" ] || return 0
  local libs; libs="$( { ldconfig -p 2>/dev/null || /sbin/ldconfig -p 2>/dev/null; } || true)"
  [ -n "$libs" ] || return 0
  if ! echo "$libs" | grep -q 'libicu'; then
    warn "libicu is not installed, so .NET will run in invariant-culture mode (fine for ServiceHub). To fix properly: sudo apt install libicu-dev  (or: sudo dnf install libicu)"
    export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
  fi
}

# ── Node.js ─────────────────────────────────────────────────────────────────────────────────────
NODE_FOUND=""
# Matches package.json "engines": ^20.19.0 || >=22.12.0 (Vite 7 needs it).
node_version_ok() {
  local ma mi; ma="$(major "$1")"; mi="$(minor "$1")"
  case "$ma$mi" in *[!0-9]*|"") return 1 ;; esac
  [ "$ma" -ge 23 ] && return 0
  [ "$ma" = 22 ] && [ "$mi" -ge 12 ] && return 0
  [ "$ma" = 20 ] && [ "$mi" -ge 19 ] && return 0
  return 1
}
node_ok() {
  have node && have npm || return 1
  local v; v="$(node --version 2>/dev/null)" || return 1
  node_version_ok "$v" || return 1
  NODE_FOUND="$v"
}
use_local_node() {
  [ -x "$TOOLS_DIR/node/bin/node" ] || return 1
  export PATH="$TOOLS_DIR/node/bin:$PATH"
}
install_node() {
  need_downloader; need_hasher
  have tar || die "tar is required to unpack Node.js. Install it (e.g. sudo apt install tar) and re-run."
  [ "$ARCH" != "unsupported" ] || die "This CPU ($(uname -m)) has no official Node.js download. Install Node 22 LTS yourself: https://nodejs.org"
  [ "$IS_MUSL" = 0 ] || die "This Linux uses musl (Alpine). Official Node builds do not run on it. Install Node with: apk add nodejs npm"
  step "Installing Node.js ${NODE_LTS_LINE%.x} LTS into $TOOLS_DIR/node (about 40 MB; your system is not touched)…"
  mkdir -p "$TOOLS_DIR"; check_disk "$TOOLS_DIR" 600 "Node.js"
  init_scratch; local tmp="$TMP_DIR" base line sum file got
  base="https://nodejs.org/dist/latest-${NODE_LTS_LINE}"
  fetch "$base/SHASUMS256.txt" "$tmp/SHASUMS256.txt" || { offline_help; die "Could not reach nodejs.org."; }
  line="$(grep -E " node-v[0-9.]+-${PLAT}-${ARCH}\.tar\.gz\$" "$tmp/SHASUMS256.txt" | head -1 || true)"
  [ -n "$line" ] || die "nodejs.org lists no Node build for ${PLAT}-${ARCH}. Install Node 22 LTS yourself: https://nodejs.org"
  sum="${line%% *}"; file="${line##* }"
  fetch "$base/$file" "$tmp/$file" || { offline_help; die "Could not download $file."; }
  got="$(hash_of 256 "$tmp/$file")"
  if [ "$got" != "$sum" ]; then die "Checksum mismatch for $file (expected $sum, got $got). The download is corrupt or tampered with; nothing was installed. Re-run to retry."; fi
  mkdir -p "$tmp/nodex"
  tar -xzf "$tmp/$file" -C "$tmp/nodex" --strip-components=1 || die "Could not unpack $file."
  rm -rf "$TOOLS_DIR/node"; mv "$tmp/nodex" "$TOOLS_DIR/node"
  use_local_node
  node_ok || die "Node.js was installed but does not run on this machine."
}
ensure_node() {
  if node_ok; then echo "✔ Node $NODE_FOUND"; return 0; fi
  if use_local_node && node_ok; then echo "✔ Node $NODE_FOUND (installed by run.sh in $TOOLS_DIR/node)"; return 0; fi
  local why="is not installed"
  have node && why="$(node --version 2>/dev/null || echo '?') is too old or too new a line (needs 20.19+ or 22.12+)"
  have node && ! have npm && why="has no npm next to it"
  if [ "$MODE" = "check" ]; then echo "• Node.js $why — ./run.sh will download Node ${NODE_LTS_LINE%.x} LTS to $TOOLS_DIR/node"; return 0; fi
  if [ "$AUTO_INSTALL" != 1 ]; then die "Node.js $why. Install Node 22 LTS from https://nodejs.org (or drop --no-install to let run.sh do it)."; fi
  echo "• Node.js $why."
  install_node
  echo "✔ Node $NODE_FOUND (installed by run.sh)"
}

# ── Ports ───────────────────────────────────────────────────────────────────────────────────────
# Prefer lsof; fall back to ss, then fuser, then a plain connect test — minimal Linux images often have none of the first three.
listener_pids() {
  { if have lsof; then lsof -nP -tiTCP:"$1" -sTCP:LISTEN 2>/dev/null
    elif have ss; then ss -H -ltnp "sport = :$1" 2>/dev/null | grep -o 'pid=[0-9]*' | cut -d= -f2
    elif have fuser; then fuser "$1/tcp" 2>/dev/null | tr ' ' '\n' | grep -E '^[0-9]+$'
    fi; } 2>/dev/null | sort -u || true
}
port_in_use() {
  [ -n "$(listener_pids "$1")" ] && return 0
  (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null && return 0
  return 1
}
proc_cmd() {
  ps -o command= -p "$1" 2>/dev/null || tr '\0' ' ' < "/proc/$1/cmdline" 2>/dev/null || true
}
# Make $2 (API|WEB) usable: stop a previous ServiceHub of ours that still holds it; if something else holds it,
# take the next free port — unless the user pinned the port, in which case refuse rather than guess.
claim_port() {
  local name="$1" pattern="$2" label="$3" var="${1}_PORT" fixedvar="${1}_PORT_FIXED"
  local port="${!var}" tries=0 pid cmd foreign pids other
  other=""; [ "$name" = WEB ] && other="$API_PORT"
  while :; do
    foreign=0
    if [ "$port" = "$other" ]; then foreign=1; elif port_in_use "$port"; then
      pids="$(listener_pids "$port")"
      [ -n "$pids" ] || foreign=1   # in use, but not by anything we can identify as ours
      for pid in $pids; do
        cmd="$(proc_cmd "$pid")"
        if [[ "$cmd" == *"$pattern"* ]]; then
          echo "↻ Stopping previous ${label} on :${port} (pid ${pid})"
          kill "$pid" 2>/dev/null || true
        else
          foreign=1; BLOCKER="pid ${pid}: ${cmd:0:100}"
        fi
      done
      if [ "$foreign" = 0 ]; then
        for _ in $(seq 1 100); do port_in_use "$port" || break; sleep 0.1; done
        if port_in_use "$port"; then
          for pid in $(listener_pids "$port"); do kill -9 "$pid" 2>/dev/null || true; done
          sleep 0.5
        fi
        port_in_use "$port" && foreign=1 && BLOCKER="the previous ${label} would not stop"
      fi
    fi
    if [ "$foreign" = 0 ]; then break; fi
    if [ "${!fixedvar}" = 1 ]; then die "Port ${port} is held by something else (${BLOCKER:-a program this script cannot identify}). Free it, or pick another with SERVICEHUB_${name}_PORT."; fi
    tries=$((tries + 1)); [ "$tries" -le 30 ] || die "Could not find a free port near ${!var} for the ${label}."
    port=$((port + 1))
  done
  if [ "$port" != "${!var}" ]; then echo "• Port ${!var} is taken by another program; the ${label} will use :${port} instead."; fi
  printf -v "$var" '%s' "$port"
}
BLOCKER=""

# ── Preflight ───────────────────────────────────────────────────────────────────────────────────
echo "ServiceHub — checking this machine ($OS/$ARCH)…"
[ "$API_PORT" != "$WEB_PORT" ] || die "SERVICEHUB_API_PORT and SERVICEHUB_WEB_PORT are both ${API_PORT}; they must differ."
[ -f "$ROOT/package.json" ] && [ -d "$ROOT/services/api" ] || die "Run this script from the ServiceHub repository root (package.json and services/api not found next to it)."
[ "${BASH_VERSINFO[0]:-0}" -ge 3 ] || die "bash 3.2 or newer is required."

NEED_API=1; NEED_WEB=1
[ "$MODE" = "web" ] && NEED_API=0
[ "$MODE" = "api" ] && NEED_WEB=0

[ "$NEED_API" = 1 ] && ensure_dotnet
[ "$NEED_WEB" = 1 ] && ensure_node
[ "$NEED_API" = 1 ] && [ "$MODE" != "check" ] && check_dotnet_runtime_libs

# Where the API will keep its database — a read-only or missing parent here fails minutes in, with a stack trace.
if [ "$NEED_API" = 1 ]; then
  DATA_DIR="${SERVICEHUB_DATA_DIR:-$ROOT/services/api/src/ServiceHub.Api/data}"
  if [ "$MODE" != "check" ]; then
    mkdir -p "$DATA_DIR" 2>/dev/null && [ -w "$DATA_DIR" ] || die "The data folder ${DATA_DIR} cannot be created or written to. Fix its permissions, or point SERVICEHUB_DATA_DIR at a folder you own."
  elif [ -d "$DATA_DIR" ] && [ ! -w "$DATA_DIR" ]; then
    echo "✖ The data folder ${DATA_DIR} is not writable."
  else echo "✔ Data folder ${DATA_DIR}"; fi
fi

if [ "$MODE" = "check" ]; then
  for p in "$API_PORT" "$WEB_PORT"; do
    if port_in_use "$p"; then echo "• Port $p is in use (a previous ServiceHub is stopped; for any other program the next free port is used, or run.sh refuses if you pinned the port)."
    else echo "✔ Port $p is free"; fi
  done
  have curl || warn "curl not found — run.sh cannot probe startup (and cannot download tools); install curl for the best experience."
  echo "✔ Check finished. Run ./run.sh"
  exit 0
fi

have curl || echo "• curl not found — startup will not be probed; watch the log for 'Now listening'."

# ── Web dependencies ────────────────────────────────────────────────────────────────────────────
# Install the first time, again whenever the lockfile moves on (a `git pull` that changed it), and again if a
# previous install was cut short (Vite missing). A failed install is retried once from a clean slate.
ensure_node_modules() {
  if [ -d node_modules ] && [ -x node_modules/.bin/vite ] && [ ! package-lock.json -nt node_modules/.package-lock.json ]; then return 0; fi
  step "Installing web dependencies (npm ci)…"
  if ! npm ci --no-audit --no-fund; then
    warn "npm ci failed; retrying once from a clean node_modules…"
    rm -rf "$ROOT/node_modules"
    npm ci --no-audit --no-fund || { offline_help; die "npm ci failed twice (output above). See docs/LOCAL-SETUP.md → Troubleshooting."; }
  fi
  [ -x node_modules/.bin/vite ] || die "Vite is still missing after npm ci. Delete node_modules and re-run."
}
[ "$NEED_WEB" = 1 ] && ensure_node_modules

# ── API restore + build ─────────────────────────────────────────────────────────────────────────
# Done up front, in the open, so a compile or package problem is reported as exactly that — and the readiness
# timeout below measures startup only, not a slow first build.
build_api() {
  local proj="services/api/src/ServiceHub.Api"
  step "Restoring and building the API (first run downloads packages and compiles — a few minutes)…"
  if ! dotnet restore "$proj" --nologo -v q; then
    warn "Package restore failed; retrying once…"
    sleep 3
    dotnet restore "$proj" --nologo -v q || { offline_help; die "dotnet restore failed (needs access to nuget.org). Output above."; }
  fi
  dotnet build "$proj" --no-restore --nologo -v q -clp:ErrorsOnly,Summary \
    || die "The API did not build (errors above). If you changed code, fix it; otherwise open an issue with this output."
}
[ "$NEED_API" = 1 ] && build_api

# ── Free the ports ──────────────────────────────────────────────────────────────────────────────
[ "$NEED_API" = 1 ] && claim_port API "ServiceHub.Api" "API"
[ "$NEED_WEB" = 1 ] && claim_port WEB "$ROOT/node_modules" "dev server"

# ── Start ───────────────────────────────────────────────────────────────────────────────────────
if [ "$NEED_API" = 1 ]; then
  step "Starting the API on http://localhost:${API_PORT}"
  # This script is for development, so the API runs as Development unless you say otherwise: that is what
  # supplies the throw-away dev encryption key. Without it (--no-launch-profile sets no environment) the API
  # starts as Production and, correctly, refuses to run with no key configured.
  [ -n "${SERVICEHUB_DATA_DIR:-}" ] && export ServiceHub__DataDirectory="$SERVICEHUB_DATA_DIR"
  ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}" \
  ASPNETCORE_URLS="http://localhost:${API_PORT}" \
    dotnet run --project services/api/src/ServiceHub.Api --no-build --no-launch-profile &
  PIDS="$PIDS $!"
fi

if [ "$NEED_WEB" = 1 ]; then
  step "Starting the web server on http://localhost:${WEB_PORT}"
  # Vite is started directly rather than through `npm run`, so stopping it does not end in a wall of npm errors.
  ( cd apps/servicehub && VITE_PROXY_TARGET="http://localhost:${API_PORT}" \
      exec "$ROOT/node_modules/.bin/vite" --port "${WEB_PORT}" --strictPort ) &
  PIDS="$PIDS $!"
fi

# ── Wait, announce, supervise ───────────────────────────────────────────────────────────────────
up() { curl -fsS -o /dev/null --max-time 2 "$1" 2>/dev/null; }
announced=0
waited=0
if ! have curl; then announced=1; fi   # cannot probe; do not nag

# Stop everything as soon as either process exits, so a crashed API is not hidden behind a live dev server.
while :; do
  for pid in $PIDS; do
    if ! kill -0 "$pid" 2>/dev/null; then
      wait "$pid" || true
      echo "✖ A ServiceHub process exited — the output above says why. See docs/LOCAL-SETUP.md → Troubleshooting." >&2
      exit 1
    fi
  done

  if [ "$announced" = 0 ]; then
    ok=1
    # API first: probing through Vite before the API answers only fills the log with proxy errors.
    [ "$NEED_API" = 1 ] && ! up "http://localhost:${API_PORT}/health/ready" && ok=0
    [ "$ok" = 1 ] && [ "$NEED_WEB" = 1 ] && ! up "http://localhost:${WEB_PORT}/" && ok=0
    # Through Vite too: proves the proxy reaches the API, which is the path the browser actually uses.
    [ "$ok" = 1 ] && [ "$NEED_WEB" = 1 ] && [ "$NEED_API" = 1 ] && ! up "http://localhost:${WEB_PORT}/health/ready" && ok=0
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
