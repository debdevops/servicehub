# App Registration Setup (Self-Hosters Only)

This guide is for **administrators deploying ServiceHub** who want to enable Azure Entra ID authentication for their users.

If you are a **user** connecting your namespace, see [../README.md](../README.md) instead.

---

## What You're Creating

A single Azure App Registration that **ServiceHub itself** uses to authenticate against users' Service Bus namespaces. Users grant this App Registration an IAM role on their own namespaces.

```
ServiceHub App Registration
├── Client ID     → SERVICEHUB_CLIENT_ID (share with users so they can grant access)
├── Client Secret → kept secret, configured in App Service / Key Vault
└── Tenant ID     → your Azure AD tenant
```

---

## Step 1 — Create the App Registration

### Option A — Azure Portal

1. Go to [portal.azure.com](https://portal.azure.com) → **Entra ID** → **App registrations**
2. Click **+ New registration**
3. Name: `servicehub` (or `ServiceHub - Production`)
4. Supported account types: **Accounts in this organizational directory only**
5. Redirect URI: leave blank (this is a service, not a user-facing app)
6. Click **Register**
7. Copy the **Application (client) ID** and **Directory (tenant) ID** — you'll need these

### Option B — Azure CLI

```bash
APP_NAME="servicehub"

az ad app create \
  --display-name "$APP_NAME" \
  --sign-in-audience "AzureADMyOrg"

# Get the app ID
APP_ID=$(az ad app list --display-name "$APP_NAME" \
  --query "[0].appId" --output tsv)

echo "Client ID: $APP_ID"
echo "Tenant ID: $(az account show --query tenantId --output tsv)"
```

### Option C — PowerShell

```powershell
$app = New-AzADApplication -DisplayName "servicehub"
Write-Host "Client ID: $($app.AppId)"
Write-Host "Tenant ID: $((Get-AzContext).Tenant.Id)"
```

---

## Step 2 — Create a Client Secret

### Azure Portal

1. In your App Registration → **Certificates & secrets**
2. **Client secrets** → **+ New client secret**
3. Description: `servicehub-production`
4. Expiry: 24 months (set a calendar reminder to rotate)
5. Click **Add** → copy the **Value** immediately (it won't be shown again)

### Azure CLI

```bash
SECRET=$(az ad app credential reset \
  --id "$APP_ID" \
  --display-name "servicehub-production" \
  --years 2 \
  --query password --output tsv)

echo "Client Secret: $SECRET"
```

### PowerShell

```powershell
$secret = New-AzADAppCredential -ApplicationId $app.AppId -EndDate (Get-Date).AddYears(2)
Write-Host "Client Secret: $($secret.SecretText)"
```

---

## Step 3 — Configure ServiceHub

Set these values in your Azure App Service → **Configuration** → **Application settings**:

| Setting | Value |
|---|---|
| `EntraId__ClientId` | Application (client) ID from Step 1 |
| `EntraId__ClientSecret` | Secret value from Step 2 |
| `EntraId__TenantId` | Directory (tenant) ID from Step 1 |
| `EntraId__Enabled` | `true` |

Or via CLI:
```bash
az webapp config appsettings set \
  --resource-group "$RESOURCE_GROUP" \
  --name "$APP_SERVICE_NAME" \
  --settings \
    "EntraId__ClientId=$CLIENT_ID" \
    "EntraId__ClientSecret=$CLIENT_SECRET" \
    "EntraId__TenantId=$TENANT_ID" \
    "EntraId__Enabled=true"
```

---

## Step 4 — Share the Client ID with Users

Users need to know your App Registration's **Client ID** so they can grant it access to their namespaces. The `GET /namespaces/entra-id/status` API endpoint returns this automatically — the ServiceHub Connect page displays it when Entra ID is enabled.

You can also find it in: Azure Portal → Entra ID → App registrations → your app → **Overview** → Application (client) ID.

---

## Secret Rotation

When your client secret expires:

1. Create a new secret (Step 2 above)
2. Update `EntraId__ClientSecret` in App Service settings
3. Restart the App Service
4. Delete the old secret from the App Registration

Rotate before expiry — set a calendar reminder when you create the secret.

---

## Security Notes

- The App Registration has **no permissions** configured — it only gains access to namespaces where users explicitly grant it an IAM role
- Never commit the client secret to source control — use App Service settings or Key Vault references
- Consider using a **Managed Identity** instead for Azure-hosted ServiceHub deployments (see notes below)

### Using Managed Identity Instead (Recommended for Azure-hosted)

If your ServiceHub runs in Azure App Service, you can use a **system-assigned managed identity** instead of a client secret:

1. App Service → **Identity** → **System assigned** → Turn **On**
2. Users grant the managed identity's principal ID (not a client ID) an IAM role on their namespace
3. Set `EntraId__Enabled=true` and `ConnectionAuthType=DefaultAzureCredential` — no client secret needed
