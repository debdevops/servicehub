#!/usr/bin/env bash
# Ported-file provenance guard (ADR-0014 D2).
#
# ServiceHub 4.1.0 is grown screen by screen, and code that was proven against real clouds is
# COPIED out of archive/servicehub-4.0.0/ rather than retyped. Every copied file carries a header:
#
#   // Ported from ServiceHub 4.0.0
#   //   source: archive/servicehub-4.0.0/services/api/src/.../RecoveryHashChain.cs
#   //   copied: 2026-09-21 for unit 2.5
#   //   changes: namespace only
#
# This script keeps that header true. For every file declaring `changes: none` or
# `changes: namespace only` it re-derives the file from its source and diffs. Anything else fails.
#
# Why it matters: the copied/written distinction is how we can tell a rewrite from a move
# (ADR-0014's own failure test), and how a copy can be re-diffed against its source years later.
# A header nobody checks is decoration.
#
# Usage: verify-ported-files.sh [root]      (root defaults to the repository root)
set -euo pipefail

ROOT="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
cd "$ROOT"

ARCHIVE_ROOT="archive/servicehub-4.0.0"
SEARCH_PATHS=("services/api/src" "services/api/tests" "apps/servicehub/src")

failures=0
checked=0
unverified=0

fail() { printf '❌ %s\n' "$1"; failures=$((failures + 1)); }

# Files to inspect: anything mentioning the ported-from marker.
# Written for bash 3.2 (the macOS default) — no `mapfile`, no process substitution into an array.
candidates_file="$(mktemp)"
trap 'rm -f "$candidates_file"' EXIT

for path in "${SEARCH_PATHS[@]}"; do
  [ -d "$path" ] || continue
  grep -rl --include='*.cs' --include='*.ts' --include='*.tsx' \
    'Ported from ServiceHub 4\.0\.0' "$path" 2>/dev/null || true
done | sort -u > "$candidates_file"

if [ ! -s "$candidates_file" ]; then
  echo "ℹ️  No ported files found yet. Nothing to verify."
  exit 0
fi

while IFS= read -r file; do
  checked=$((checked + 1))

  header="$(head -n 8 "$file")"
  source_path="$(printf '%s' "$header" | sed -n 's|^//[[:space:]]*source:[[:space:]]*\(.*\)$|\1|p' | head -n 1)"
  changes="$(printf '%s' "$header" | sed -n 's|^//[[:space:]]*changes:[[:space:]]*\(.*\)$|\1|p' | head -n 1)"
  copied="$(printf '%s' "$header" | sed -n 's|^//[[:space:]]*copied:[[:space:]]*\(.*\)$|\1|p' | head -n 1)"

  if [ -z "$source_path" ] || [ -z "$changes" ] || [ -z "$copied" ]; then
    fail "$file: malformed provenance header — it needs 'source:', 'copied:' and 'changes:' lines."
    continue
  fi

  case "$source_path" in
    "$ARCHIVE_ROOT"/*) ;;
    *) fail "$file: source '$source_path' is not inside $ARCHIVE_ROOT/."; continue ;;
  esac

  if [ ! -f "$source_path" ]; then
    fail "$file: source '$source_path' does not exist. The header names a file that is not there."
    continue
  fi

  case "$changes" in
    none|"namespace only")
      # Re-derive: strip our 4-line header + its blank line, then compare ignoring the namespace
      # declaration and any `using` of a namespace that moved with it.
      derived="$(mktemp)"
      expected="$(mktemp)"

      # Drop the header block: everything up to and including the `changes:` line, plus the blank
      # line after it. Found by content rather than a fixed line count, so a header that grows a
      # line does not silently start comparing the wrong thing.
      header_end="$(grep -n '^//[[:space:]]*changes:' "$file" | head -n 1 | cut -d: -f1)"
      skip="$header_end"
      if [ "$(sed -n "$((header_end + 1))p" "$file")" = "" ]; then
        skip=$((header_end + 1))
      fi

      tail -n +$((skip + 1)) "$file" \
        | sed -E 's/^namespace [A-Za-z0-9_.]+;$/namespace <NS>;/; s/^using [A-Za-z0-9_.]+;$/using <NS>;/' \
        > "$derived"
      sed -E 's/^namespace [A-Za-z0-9_.]+;$/namespace <NS>;/; s/^using [A-Za-z0-9_.]+;$/using <NS>;/' \
        "$source_path" > "$expected"

      if ! diff -q "$derived" "$expected" >/dev/null 2>&1; then
        fail "$file: declares 'changes: $changes' but differs from $source_path:"
        diff -u "$expected" "$derived" | sed -n '3,15p' | sed 's/^/      /'
        echo "      → Either restore the file, or change the header to describe what you changed."
      fi

      rm -f "$derived" "$expected"
      ;;
    "")
      fail "$file: empty 'changes:' value."
      ;;
    *)
      # A described adaptation. Not machine-checkable — the header is the record.
      unverified=$((unverified + 1))
      ;;
  esac
done < "$candidates_file"

echo
echo "Checked $checked ported file(s); $unverified carry a described adaptation (recorded, not diffable)."

if [ "$failures" -gt 0 ]; then
  echo
  echo "❌ $failures provenance problem(s). ARCHITECTURE.md §8 has the rules."
  exit 1
fi

echo "✅ Every ported file matches its recorded provenance."
