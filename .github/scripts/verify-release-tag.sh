#!/usr/bin/env bash
# Pick the codebase a release tag publishes, and refuse any tag that does not match it (ADR-0013 D6).
#
# Two generations live in this repo: the frozen ServiceHub 4.0.x archive (archive/servicehub-4.0.0/) and
# ServiceHub 4.1.x at the repo root. A v4.0.x tag builds the archive; a v4.1.x tag builds the root. The tag
# must equal the .version of the codebase it selects — otherwise one generation would be published under the
# other's version (e.g. archived 4.0.0 code as ":4.1.0" and ":latest").
#
# Usage: verify-release-tag.sh <ref-type> <ref-name>
# Prints the chosen build context; when $GITHUB_OUTPUT is set, also writes context= and dockerfile= to it.
set -euo pipefail

REF_TYPE="${1:?ref type (github.ref_type)}"
REF_NAME="${2:?ref name (github.ref_name)}"

if [ "${REF_TYPE}" != "tag" ]; then
  echo "❌ This workflow publishes a release image and must run from a version tag (got ${REF_TYPE} '${REF_NAME}')."
  echo "   Publishing from a branch would overwrite ':latest' with an unreleased codebase."
  exit 1
fi

TAG_VERSION="${REF_NAME#v}"
case "${TAG_VERSION}" in
  4.0.*) CONTEXT="archive/servicehub-4.0.0" ;;
  4.1.*|4.2.*) CONTEXT="." ;;
  *)
    echo "❌ Tag '${REF_NAME}' is not a release line this workflow knows (v4.0.x → archive, v4.1.x and v4.2.x → root)."
    echo "   A new release line needs its own build path (ADR-0013 D6)."
    exit 1
    ;;
esac

if [ ! -f "${CONTEXT}/.version" ] || [ ! -f "${CONTEXT}/Dockerfile" ]; then
  echo "❌ ${CONTEXT} has no .version/Dockerfile — nothing to publish for '${REF_NAME}'."
  exit 1
fi

CODE_VERSION="$(tr -d '[:space:]' < "${CONTEXT}/.version")"
if [ "${TAG_VERSION}" != "${CODE_VERSION}" ]; then
  echo "❌ Tag '${REF_NAME}' (${TAG_VERSION}) does not match the version ${CODE_VERSION} of the codebase it builds (${CONTEXT})."
  echo "   Bump ${CONTEXT}/.version first, or tag the right version (ADR-0013 D6)."
  exit 1
fi

echo "✅ Tag ${REF_NAME} matches ${CONTEXT}/.version (${CODE_VERSION}); building ${CONTEXT}."
if [ -n "${GITHUB_OUTPUT:-}" ]; then
  { echo "context=${CONTEXT}"; echo "dockerfile=${CONTEXT}/Dockerfile"; } >> "${GITHUB_OUTPUT}"
fi
