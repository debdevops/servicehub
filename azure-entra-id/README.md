# Azure Entra ID Authentication for ServiceHub

ServiceHub supports **Azure Entra ID (formerly Azure Active Directory)** authentication, allowing users to connect to their Service Bus namespaces **without copying or rotating connection strings**.

Instead of a SAS key, users grant ServiceHub's App Registration (or Managed Identity) an Azure RBAC role on their namespace. ServiceHub authenticates as itself — no user login, no token exchange.

---

## How It Works

```
User's Azure Subscription
┌─────────────────────────────────────────┐
│  Service Bus Namespace                  │
│  ┌─────────────────────────────────┐    │
│  │  IAM Role Assignment            │    │
│  │  ServiceHub App Registration    │    │
│  │  → Azure Service Bus Data Owner│    │
│  └─────────────────────────────────┘    │
└──────────────────┬──────────────────────┘
                   │ Role-based access
                   ▼
        ServiceHub API (self-hosted)
        Authenticates via ClientSecretCredential
        using its own App Registration
```

1. **ServiceHub** has its own App Registration (configured by the self-hoster).
2. **You** (the user) grant that App Registration an RBAC role on your namespace via Azure IAM.
3. ServiceHub connects to your namespace using its credential — no SAS key involved.
4. **Revoke** at any time by removing the role assignment from Azure IAM.

> **Note**: Entra ID auth is only available on **self-hosted** instances. The public hosted version uses connection strings only, as each self-hoster configures their own App Registration.

---

## Prerequisites

- A **self-hosted** ServiceHub instance (see [Deployment Guide](../deploy/DEPLOYMENT-GUIDE.md))
- Your ServiceHub instance has `EntraId__ClientId`, `EntraId__ClientSecret`, `EntraId__TenantId` configured
- You have **Owner** or **User Access Administrator** on your Azure Service Bus namespace

---

## Step 1 — Grant ServiceHub Access to Your Namespace

ServiceHub needs the **Azure Service Bus Data Owner** role on your namespace (or Data Receiver for read-only).

### Option A — Azure Portal

1. Go to [portal.azure.com](https://portal.azure.com)
2. Navigate to your **Service Bus namespace**
3. In the left menu, click **Access control (IAM)**
4. Click **+ Add** → **Add role assignment**
5. Search for and select **Azure Service Bus Data Owner**
6. Click **Next** → Members → **+ Select members**
7. Search for ServiceHub's App Registration name (ask your admin for the name or Client ID)
8. Select it and click **Review + assign**

### Option B — Azure CLI

```bash
# Set your values
SERVICE_BUS_NAMESPACE="my-namespace"
RESOURCE_GROUP="my-resource-group"
SERVICEHUB_CLIENT_ID="<ServiceHub App Registration Client ID>"

# Get the namespace resource ID
NAMESPACE_ID=$(az servicebus namespace show \
  --name "$SERVICE_BUS_NAMESPACE" \
  --resource-group "$RESOURCE_GROUP" \
  --query id --output tsv)

# Assign the role
az role assignment create \
  --assignee "$SERVICEHUB_CLIENT_ID" \
  --role "Azure Service Bus Data Owner" \
  --scope "$NAMESPACE_ID"
```

### Option C — PowerShell

```powershell
$namespaceName = "my-namespace"
$resourceGroup = "my-resource-group"
$serviceHubClientId = "<ServiceHub App Registration Client ID>"

$namespace = Get-AzServiceBusNamespace `
  -ResourceGroupName $resourceGroup `
  -Name $namespaceName

New-AzRoleAssignment `
  -ApplicationId $serviceHubClientId `
  -RoleDefinitionName "Azure Service Bus Data Owner" `
  -Scope $namespace.Id
```

---

## Step 2 — Connect in ServiceHub

1. Open ServiceHub → **Connect** page
2. Click the **Azure Entra ID** tab
3. Enter a **Display Name** and your **namespace hostname** (e.g., `my-namespace.servicebus.windows.net`)
4. Click **Connect with Entra ID**

ServiceHub will verify the role assignment is in place before saving the connection. If the role hasn't propagated yet (Azure IAM can take 1–2 minutes), retry after waiting.

---

## Revoking Access

To revoke ServiceHub's access to your namespace:

1. Azure Portal → your Service Bus namespace → **Access control (IAM)**
2. **Role assignments** tab → find the ServiceHub App Registration
3. Click the **⋯** menu → **Remove**

Or via CLI:
```bash
az role assignment delete \
  --assignee "$SERVICEHUB_CLIENT_ID" \
  --role "Azure Service Bus Data Owner" \
  --scope "$NAMESPACE_ID"
```

ServiceHub will immediately lose access. No keys to rotate, no connection strings to invalidate.

---

## Supported Auth Types

| Auth Type | When to Use |
|---|---|
| `ManagedIdentity` | ServiceHub runs in Azure with a Managed Identity configured |
| `ServicePrincipal` | ServiceHub runs anywhere with a Client ID + Secret configured |
| `DefaultAzureCredential` | Local development — picks up `az login`, environment vars, or MSI |

---

## For Self-Hosters: Configuring Entra ID

See [app-registration/README.md](./app-registration/README.md) for how to create and configure the App Registration for your ServiceHub deployment.

See [local-testing/README.md](./local-testing/README.md) for local development options.
