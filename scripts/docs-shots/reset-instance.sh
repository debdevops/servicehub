#!/usr/bin/env bash
# Restart the throw-away docs instance (API :5199 + web :3099) on an EMPTY database. Never touches the dev API on :5153.
set -euo pipefail
S="${DOCS_SCRATCH:?scratch dir}"; ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
for f in api web; do [ -f "$S/$f.pid" ] && kill "$(cat "$S/$f.pid")" 2>/dev/null || true; done
for port in 5199 3099; do for pid in $(lsof -nP -tiTCP:$port -sTCP:LISTEN 2>/dev/null); do kill $pid 2>/dev/null || true; done; done
sleep 2; rm -rf "$S/docsdata"; mkdir -p "$S/docsdata"
cd "$ROOT/services/api/src/ServiceHub.Api"
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5199 ServiceHub__DataDirectory="$S/docsdata" nohup dotnet run --no-launch-profile > "$S/api.log" 2>&1 & echo $! > "$S/api.pid"
cd "$ROOT"; VITE_PROXY_TARGET=http://localhost:5199 nohup npm run dev -w apps/servicehub -- --port 3099 --strictPort > "$S/web.log" 2>&1 & echo $! > "$S/web.pid"
for _ in $(seq 1 60); do curl -sf localhost:5199/api/v1/namespaces >/dev/null && curl -sf localhost:3099 >/dev/null && exit 0; sleep 1; done; exit 1
