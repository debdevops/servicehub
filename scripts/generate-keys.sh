#!/usr/bin/env bash
# =============================================================================
# generate-keys.sh — Generate ServiceHub encryption + API keys
#
# Usage:
#   ./scripts/generate-keys.sh              # Print keys to stdout
#   ./scripts/generate-keys.sh --local      # Write to appsettings.Local.json
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
LOCAL_SETTINGS="$REPO_ROOT/services/api/src/ServiceHub.Api/appsettings.Local.json"

# Generate keys
ENCRYPTION_KEY=$(openssl rand -hex 32)
SPA_TOKEN_SECRET=$(openssl rand -hex 32)
ADMIN_KEY="sh_admin_$(openssl rand -hex 32)"
READONLY_KEY="sh_ro_$(openssl rand -hex 32)"

if [[ "${1:-}" == "--local" ]]; then
  if [[ -f "$LOCAL_SETTINGS" ]]; then
    echo "⚠️  $LOCAL_SETTINGS already exists — not overwriting."
    echo "   Delete it first if you want fresh keys."
    exit 0
  fi

  cat > "$LOCAL_SETTINGS" <<EOF
{
  "Security": {
    "EncryptionKey": "$ENCRYPTION_KEY",
    "SpaToken": {
      "Enabled": false,
      "Secret": "$SPA_TOKEN_SECRET"
    }
  }
}
EOF

  echo "\u2705 Created $LOCAL_SETTINGS with encryption key and SPA token secret."
  echo ""
  echo "\ud83d\udd11 Encryption Key    : $ENCRYPTION_KEY"
  echo "\ud83d\udd11 SPA Token Secret  : $SPA_TOKEN_SECRET"
else
  # Print for manual use (e.g., setting Azure env vars)
  echo "🔑 Generated ServiceHub Keys"
  echo "─────────────────────────────────────────────────"
  echo "ENCRYPTION_KEY   : $ENCRYPTION_KEY"
  echo "SPA_TOKEN_SECRET : $SPA_TOKEN_SECRET"
  echo "ADMIN_API_KEY    : $ADMIN_KEY"
  echo "READONLY_KEY     : $READONLY_KEY"
  echo "─────────────────────────────────────────────────"
  echo ""
  echo "Azure App Service env vars:"
  echo "  Security__EncryptionKey=$ENCRYPTION_KEY"
  echo "  Security__SpaToken__Enabled=true"
  echo "  Security__SpaToken__Secret=$SPA_TOKEN_SECRET"
  echo "  Security__Authentication__Enabled=true"
  echo "  Security__Authentication__ScopedApiKeys__0__Key=$ADMIN_KEY"
  echo "  Security__Authentication__ScopedApiKeys__1__Key=$READONLY_KEY"
fi
