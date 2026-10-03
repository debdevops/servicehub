#!/usr/bin/env bash
# Rebuild the Google Cloud screenshot set from scratch: restart the throw-away instance on an empty DB, capture.
# Needs DOCS_SCRATCH (a scratch dir) and GCP_KEY_FILE (the ServiceHub service account's JSON key). GCP_PROJECT defaults to the dev project.
# Google Cloud reads are slow (up to a minute per queue), so a run takes several minutes. A run replays a few dead letters back to the
# orders topic; the dead-letter subscription is left as it is.
set -euo pipefail
cd "$(dirname "$0")"
: "${GCP_KEY_FILE:?JSON key file of the ServiceHub service account}"
export GCP_KEY_FILE
./reset-instance.sh
node gcp.spec.mjs
