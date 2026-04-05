# Local Development — Testing Entra ID Auth

Two options for testing Entra ID authentication locally.

---

## Option 1 — DefaultAzureCredential (Simplest)

Use your own Azure CLI login. No App Registration configuration needed locally.

### Setup

```bash
# Log in to Azure
az login

# Verify you are targeting the right tenant
az account show
```

### Configure local ServiceHub

In `services/api/appsettings.Local.json` (git-ignored):

```json
{
  "EntraId": {
    "Enabled": true,
    "ClientId": "",
    "ClientSecret": "",
    "TenantId": ""
  }
}
```

Leave `ClientId`, `ClientSecret`, and `TenantId` empty — `DefaultAzureCredential` will pick up your `az login` session automatically.

### Grant yourself access

```bash
SERVICE_BUS_NAMESPACE="my-namespace"
RESOURCE_GROUP="my-rg"

# Get your current user's object ID
MY_OBJECT_ID=$(az ad signed-in-user show --query id --output tsv)

NAMESPACE_ID=$(az servicebus namespace show \
  --name "$SERVICE_BUS_NAMESPACE" \
  --resource-group "$RESOURCE_GROUP" \
  --query id --output tsv)

az role assignment create \
  --assignee-object-id "$MY_OBJECT_ID" \
  --assignee-principal-type User \
  --role "Azure Service Bus Data Owner" \
  --scope "$NAMESPACE_ID"
```

Wait 1–2 minutes for the role assignment to propagate, then connect in the ServiceHub UI.

---

## Option 2 — Full App Registration (Matches Production)

Use a test App Registration to simulate exactly how production ServiceHub authenticates.

### Step 1 — Create a test App Registration

```bash
APP_NAME="servicehub-local"

az ad app create --display-name "$APP_NAME" --sign-in-audience "AzureADMyOrg"

APP_ID=$(az ad app list --display-name "$APP_NAME" \
  --query "[0].appId" --output tsv)

TENANT_ID=$(az account show --query tenantId --output tsv)

SECRET=$(az ad app credential reset \
  --id "$APP_ID" \
  --display-name "local-dev" \
  --years 1 \
  --query password --output tsv)

echo "Client ID:     $APP_ID"
echo "Tenant ID:     $TENANT_ID"
echo "Client Secret: $SECRET"
```

### Step 2 — Configure local ServiceHub

In `services/api/appsettings.Local.json`:

```json
{
  "EntraId": {
    "Enabled": true,
    "ClientId": "<APP_ID from above>",
    "ClientSecret": "<SECRET from above>",
    "TenantId": "<TENANT_ID from above>"
  }
}
```

### Step 3 — Grant the test App Registration access

```bash
NAMESPACE_ID=$(az servicebus namespace show \
  --name "$SERVICE_BUS_NAMESPACE" \
  --resource-group "$RESOURCE_GROUP" \
  --query id --output tsv)

az role assignment create \
  --assignee "$APP_ID" \
  --role "Azure Service Bus Data Owner" \
  --scope "$NAMESPACE_ID"
```

### Step 4 — Connect in ServiceHub

1. Start the API: `cd services/api && dotnet run --project src/ServiceHub.Api`
2. Start the UI: `cd apps/web && npm run dev`
3. Open `http://localhost:3000` → Connect → Azure Entra ID tab
4. Enter display name + `my-namespace.servicebus.windows.net`
5. Click **Connect with Entra ID**

---

## Troubleshooting

### "Entra ID not configured on this instance"
→ Check `appsettings.Local.json` has `Enabled: true` and the values are non-empty (for Option 2).

### "Failed to create Entra ID Service Bus client"
→ The role assignment may not have propagated yet. Wait 1–2 minutes and retry.
→ Verify the role assignment exists: `az role assignment list --scope "$NAMESPACE_ID"`

### "DefaultAzureCredential failed"
→ Run `az login` again. Your token may have expired.
→ Check `az account show` targets the correct tenant.

### Build errors referencing Azure.Identity
→ Run `dotnet restore` in the `services/api` folder.
