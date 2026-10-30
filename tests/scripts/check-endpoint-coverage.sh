#!/usr/bin/env bash
# Endpoint coverage gate: every API controller endpoint must be called by at least one integration test.
#
# Usage: check-endpoint-coverage.sh [endpoint-coverage.json]
#
# The integration tests write endpoint-coverage.json (see EndpointCoverageRecorder.cs) when their process exits: every
# controller endpoint that exists, and every one a test hit. An endpoint that nothing calls is functionality that could ship
# untested, so it fails here. Endpoints that cannot be called in CI (they need a live cloud) are listed, with the reason, in
# tests/scripts/endpoint-coverage-allowlist.txt — the list can only be shortened by adding a test, never lengthened quietly.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
FILE="${1:-$ROOT/tests/backend/ServiceHub.IntegrationTests/bin/Debug/net10.0/endpoint-coverage.json}"
ALLOW="$ROOT/tests/scripts/endpoint-coverage-allowlist.txt"

[ -f "$FILE" ] || { echo "❌ $FILE not found — run the integration tests first" >&2; exit 1; }

python3 -I - "$FILE" "$ALLOW" <<'PY'
import json, sys
data = json.load(open(sys.argv[1]))
allowed = set()
try:
    for line in open(sys.argv[2]):
        line = line.split('#', 1)[0].strip()
        if line:
            allowed.add(line)
except FileNotFoundError:
    pass
allp, hit = set(data["all"]), set(data["hit"])
missing = sorted(allp - hit - allowed)
stale = sorted(allowed - (allp - hit))
print(f"Endpoints: {len(allp)}   hit by a test: {len(hit & allp)}   allow-listed: {len(allowed & (allp - hit))}")
if stale:
    print("⚠️  allow-list entries that are now covered or no longer exist (remove them):")
    for e in stale: print("   ", e)
if missing:
    print("❌ endpoints no integration test calls:")
    for e in missing: print("   ", e)
    sys.exit(1)
print("✅ every endpoint is exercised by a test")
sys.exit(1 if stale else 0)
PY
