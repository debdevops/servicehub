#!/usr/bin/env bash
# Refuse to publish the ARCHIVED ServiceHub 4.0.0 codebase under any other version (ADR-0013 D6).
#
# The release-image workflow builds archive/servicehub-4.0.0/. Without this check, tagging v4.1.0 for
# the new ServiceHub would publish the frozen 4.0.0 code as ":4.1.0" and ":latest" — the two
# generations mixed under one version. The tag must equal the archive's own .version.
#
# Usage: verify-release-tag.sh <ref-type> <ref-name> <archive-version-file>
set -euo pipefail

REF_TYPE="${1:?ref type (github.ref_type)}"
REF_NAME="${2:?ref name (github.ref_name)}"
VERSION_FILE="${3:?path to the archive .version file}"

if [ "${REF_TYPE}" != "tag" ]; then
  echo "❌ This workflow publishes a release image and must run from a version tag (got ${REF_TYPE} '${REF_NAME}')."
  echo "   Publishing from a branch would overwrite ':latest' with the archived 4.0.0 codebase."
  exit 1
fi

TAG_VERSION="${REF_NAME#v}"
ARCHIVE_VERSION="$(tr -d '[:space:]' < "${VERSION_FILE}")"

if [ "${TAG_VERSION}" != "${ARCHIVE_VERSION}" ]; then
  echo "❌ Tag '${REF_NAME}' (${TAG_VERSION}) does not match the archived codebase version ${ARCHIVE_VERSION}."
  echo "   This workflow builds archive/servicehub-4.0.0/ — the frozen ServiceHub ${ARCHIVE_VERSION}. It must not be"
  echo "   published as ${TAG_VERSION}. A ${TAG_VERSION} release needs its own build path (ADR-0013 D6)."
  exit 1
fi

echo "✅ Tag ${REF_NAME} matches the archived codebase version ${ARCHIVE_VERSION}."
