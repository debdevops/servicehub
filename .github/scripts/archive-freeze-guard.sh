#!/usr/bin/env bash
# Archive freeze guard (ADR-0012 D2, ADR-0013 D3).
#
# archive/servicehub-4.0.0/ is the frozen ServiceHub 4.0.0 codebase. Once the archive exists on the
# base branch, NO change under archive/ may be merged into it — not a lint fix, not a dependency
# bump. The one way through is a deliberate, recorded exception: a commit-message trailer
#     Archive-Change-Approved: <ADR or decision that authorises it>
#
# Usage: archive-freeze-guard.sh <base-ref> [<head-ref>]     (head defaults to HEAD)
#
# The change that INTRODUCES the archive (base has no archive/ yet) is allowed — that is what makes
# this safe to run on the local -> develop -> release -> main promotion chain.
set -euo pipefail

BASE="${1:?usage: archive-freeze-guard.sh <base-ref> [<head-ref>]}"
HEAD_REF="${2:-HEAD}"
ARCHIVE_ROOT="archive/servicehub-4.0.0"

if ! git cat-file -e "${BASE}:${ARCHIVE_ROOT}" 2>/dev/null; then
  echo "ℹ️  ${BASE} has no ${ARCHIVE_ROOT}/ yet — this change introduces the archive; nothing to guard."
  exit 0
fi

# --no-renames so a move OUT of archive/ lists both the old and the new path.
CHANGED="$(git diff --no-renames --name-only "${BASE}...${HEAD_REF}" -- archive/)"

if [ -z "${CHANGED}" ]; then
  echo "✅ Nothing under archive/ changed."
  exit 0
fi

if git log --format=%B "${BASE}..${HEAD_REF}" | grep -Eq '^Archive-Change-Approved: .+'; then
  echo "⚠️  archive/ changed, allowed by an Archive-Change-Approved trailer:"
  git log --format=%B "${BASE}..${HEAD_REF}" | grep -E '^Archive-Change-Approved: .+'
  echo "${CHANGED}" | sed 's/^/    /'
  exit 0
fi

echo "❌ archive/ is FROZEN (ADR-0012 D2, ADR-0013 D3), but this change touches it:"
echo "${CHANGED}" | sed 's/^/    /'
echo
echo "The archive is the released ServiceHub 4.0.0 and is never edited. New work belongs outside"
echo "archive/. Copy what you need out of it; do not change it. If it genuinely must change, that is a"
echo "recorded decision: add a commit trailer  'Archive-Change-Approved: <ADR or decision>'."
exit 1
