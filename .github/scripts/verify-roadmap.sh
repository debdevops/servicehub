#!/usr/bin/env bash
# Roadmap/task-card consistency guard.
#
# ROADMAP.md owns the ORDER, the DEPENDENCIES and the STATUS of every unit. TASKS.md owns the CARD
# that says how to do it. Two files describing the same list is exactly the drift that produced real
# bugs in 4.0.0's navigation, so it is checked rather than trusted:
#
#   1. every unit on the board has a card, and every card is on the board
#   2. every dependency names a unit that exists
#   3. the per-wave counts in ROADMAP.md §4 match the board
#   4. the done count in §4 matches the number of ✅ rows
#
# Usage: verify-roadmap.sh [docs-dir]
set -euo pipefail

DIR="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/docs-private/servicehub-4.1.0}"
ROADMAP="$DIR/ROADMAP.md"
TASKS="$DIR/TASKS.md"

for f in "$ROADMAP" "$TASKS"; do
  [ -f "$f" ] || { echo "❌ Missing $f"; exit 1; }
done

failures=0
fail() { printf '❌ %s\n' "$1"; failures=$((failures + 1)); }

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# Board rows look like:  | `1.1` | What | deps | M | ⬜ | notes |
grep -oE '^\| `[0-9]+\.[0-9]+` \|' "$ROADMAP" \
  | grep -oE '[0-9]+\.[0-9]+' | sort -u > "$tmp/board.txt"

# Cards look like:  ## `1.1` — Title
grep -oE '^## `[0-9]+\.[0-9]+`' "$TASKS" \
  | grep -oE '[0-9]+\.[0-9]+' | sort -u > "$tmp/cards.txt"

if [ ! -s "$tmp/board.txt" ]; then
  fail "No unit rows found in ROADMAP.md — has the board's table format changed?"
fi
if [ ! -s "$tmp/cards.txt" ]; then
  fail "No unit cards found in TASKS.md — has the card heading format changed?"
fi

missing_cards="$(comm -23 "$tmp/board.txt" "$tmp/cards.txt")"
missing_rows="$(comm -13 "$tmp/board.txt" "$tmp/cards.txt")"

if [ -n "$missing_cards" ]; then
  fail "On the ROADMAP board but with no card in TASKS.md:"
  echo "$missing_cards" | sed 's/^/      /'
fi
if [ -n "$missing_rows" ]; then
  fail "Has a card in TASKS.md but is not on the ROADMAP board:"
  echo "$missing_rows" | sed 's/^/      /'
fi

# Every dependency must name a unit that exists. Column 3 of a board row.
while IFS= read -r line; do
  unit="$(printf '%s' "$line" | grep -oE '^\| `[0-9]+\.[0-9]+`' | grep -oE '[0-9]+\.[0-9]+')"
  deps="$(printf '%s' "$line" | awk -F'|' '{print $4}')"

  for dep in $(printf '%s' "$deps" | grep -oE '[0-9]+\.[0-9]+' || true); do
    if ! grep -qx "$dep" "$tmp/board.txt"; then
      fail "Unit $unit depends on $dep, which is not on the board."
    fi
  done
done < <(grep -E '^\| `[0-9]+\.[0-9]+` \|' "$ROADMAP")

# The §4 counts must match the board.
total_board="$(wc -l < "$tmp/board.txt" | tr -d ' ')"
done_board="$(grep -cE '^\| `[0-9]+\.[0-9]+` \|.*\| ✅' "$ROADMAP" || true)"

total_claimed="$(grep -oE '^\*\*[0-9]+ units\.' "$ROADMAP" | grep -oE '[0-9]+' | head -1 || true)"
done_claimed="$(grep -oE '^\*\*[0-9]+ units\. [0-9]+ done' "$ROADMAP" | grep -oE '[0-9]+' | sed -n 2p || true)"

if [ -n "$total_claimed" ] && [ "$total_claimed" != "$total_board" ]; then
  fail "ROADMAP §4 claims $total_claimed units; the board has $total_board."
fi
if [ -n "$done_claimed" ] && [ "$done_claimed" != "$done_board" ]; then
  fail "ROADMAP §4 claims $done_claimed done; the board has $done_board ✅ rows."
fi

echo
echo "Board: $total_board units, $done_board done. Cards: $(wc -l < "$tmp/cards.txt" | tr -d ' ')."

if [ "$failures" -gt 0 ]; then
  echo
  echo "❌ $failures roadmap problem(s). TASKS.md → 'Adding a unit' has the rules."
  exit 1
fi

echo "✅ The roadmap board and the task cards agree."
