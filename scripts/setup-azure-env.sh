#!/usr/bin/env bash
# =============================================================================
# setup-azure-env.sh — Generate keys and set Azure App Service env vars
#
# Usage:
#   ./scripts/setup-azure-env.sh <app-name> <resource-group>
#
# Prerequisites:
#   - Azure CLI installed and logged in (az login)
#   - openssl available
# =============================================================================
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <app-service-name> <resource-group>"
  echo "Example: $0 servicehub rg-servicehub"
  exit 1
fi

APP_NAME="$1"
RESOURCE_GROUP="$2"

echo "🔑 Generating keys for Azure App Service: $APP_NAME"
echo ""

ENCRYPTION_KEY=$(openssl rand -hex 32)
SPA_TOKEN_SECRET=$(openssl rand -hex 32)
ADMIN_KEY="sh_admin_$(openssl rand -hex 32)"
READONLY_KEY="sh_ro_$(openssl rand -hex 32)"

echo "Setting Application Settings..."
az webapp config appsettings set \
  --name "$APP_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --settings \
    ASPNETCORE_ENVIRONMENT=Production \
    "Security__EncryptionKey=$ENCRYPTION_KEY" \
    "Security__SpaToken__Enabled=true" \
    "Security__SpaToken__Secret=$SPA_TOKEN_SECRET" \
    "Security__Authentication__Enabled=true" \
    "Security__Authentication__ScopedApiKeys__0__Key=$ADMIN_KEY" \
    "Security__Authentication__ScopedApiKeys__1__Key=$READONLY_KEY" \
    "ApplicationInsights__ConnectionString=SET_YOUR_APPINSIGHTS_CONNECTION_STRING" \
  --output table

echo ""
echo "✅ Azure App Service configured successfully."
echo ""
echo "📋 Save these keys securely (they won't be shown again):"
echo "─────────────────────────────────────────────────"
echo "  Admin API Key     : $ADMIN_KEY"
echo "  Readonly API Key  : $READONLY_KEY"
echo "  Encryption Key    : $ENCRYPTION_KEY"
echo "  SPA Token Secret  : $SPA_TOKEN_SECRET"
echo "─────────────────────────────────────────────────"
