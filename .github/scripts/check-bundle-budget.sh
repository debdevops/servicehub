#!/usr/bin/env bash
# Bundle budget (4.1.0 unit 6.7): the initial chunk — the one script index.html loads first — must stay below 4.0.0's ~460 kB.
# It is checked on the real build output, by CI, rather than by hand. Vendors split out on purpose (react, query, charts) are
# reported, not budgeted: they change on their own schedule and cache across releases.
#
# Usage: check-bundle-budget.sh [build-dir]   (default: the API's wwwroot, where `npm run build` writes)
set -euo pipefail

DIR="${1:-services/api/src/ServiceHub.Api/wwwroot}"
BUDGET_BYTES=$((460 * 1000))

if [ ! -f "$DIR/index.html" ]; then
  echo "❌ No build found at $DIR/index.html — run the frontend build first." >&2
  exit 1
fi

ENTRY=$(grep -o '<script[^>]*src="[^"]*"' "$DIR/index.html" | head -1 | sed 's/.*src="\/\{0,1\}\([^"]*\)"/\1/')
if [ -z "$ENTRY" ] || [ ! -f "$DIR/$ENTRY" ]; then
  echo "❌ Could not find the entry script named in $DIR/index.html." >&2
  exit 1
fi

size=$(wc -c < "$DIR/$ENTRY" | tr -d ' ')
echo "Initial chunk: $ENTRY — $((size / 1000)) kB (budget $((BUDGET_BYTES / 1000)) kB)"
for f in "$DIR"/assets/vendor-*.js; do
  [ -f "$f" ] && echo "  vendor (reported, not budgeted): $(basename "$f") — $(( $(wc -c < "$f" | tr -d ' ') / 1000 )) kB"
done

if [ "$size" -gt "$BUDGET_BYTES" ]; then
  echo "❌ The initial chunk is over budget. Lazy-load a screen that is not the core loop, or split a vendor out." >&2
  exit 1
fi
echo "✅ Within the bundle budget."
