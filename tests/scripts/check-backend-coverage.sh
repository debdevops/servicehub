#!/usr/bin/env bash
# Backend line-coverage gate: fails if coverage is below the floor.
#
# Usage: check-backend-coverage.sh <results-dir> [floor-percent]      (floor defaults to 60, as in 4.0.0)
#
# <results-dir> holds the coverage.cobertura.xml files that `dotnet test --collect:"XPlat Code Coverage"
# --results-directory <results-dir>` wrote — one per test project; they are merged.
#
# Generated code (obj/, regex and OpenAPI source generators, EF migrations) is left out of the count: nobody writes it, and it
# is what makes a percentage say something other than "how much of what we wrote is tested". A floor to stop rot — never the
# reason a change is finished.
set -euo pipefail

RESULTS="${1:?usage: check-backend-coverage.sh <results-dir> [floor-percent]}"
FLOOR="${2:-60}"
OUT="$RESULTS/report"

command -v reportgenerator >/dev/null || dotnet tool install --global dotnet-reportgenerator-globaltool >/dev/null
export PATH="$PATH:$HOME/.dotnet/tools"

reportgenerator \
  -reports:"$RESULTS/**/coverage.cobertura.xml" \
  -targetdir:"$OUT" \
  -reporttypes:"TextSummary;MarkdownSummaryGithub;Cobertura" \
  "-filefilters:-*/obj/*;-*.g.cs;-*.generated.cs;-*/Migrations/*" >/dev/null

cat "$OUT/Summary.txt"
[ -n "${GITHUB_STEP_SUMMARY:-}" ] && { echo "### Backend coverage"; cat "$OUT/SummaryGithub.md"; } >> "$GITHUB_STEP_SUMMARY" || true

COVERAGE="$(sed -n 's/^ *Line coverage: *\([0-9.]*\)%.*/\1/p' "$OUT/Summary.txt" | head -1)"
[ -n "$COVERAGE" ] || { echo "❌ could not read the line coverage from the report" >&2; exit 1; }

if awk "BEGIN {exit !($COVERAGE < $FLOOR)}"; then
  echo "❌ Backend line coverage ${COVERAGE}% is below the ${FLOOR}% floor"
  exit 1
fi
echo "✅ Backend line coverage ${COVERAGE}% meets the ${FLOOR}% floor"
