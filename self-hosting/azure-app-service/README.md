# Deploy to Azure App Service

This guide walks you through deploying ServiceHub to Azure App Service — the recommended production deployment method. Both Azure Portal (click-by-click) and Azure CLI (one-command) instructions are provided for every step.

---

## Table of contents

1. [Architecture overview](#1-architecture-overview)
2. [What you will create](#2-what-you-will-create)
3. [Step-by-step: build and deploy](#3-step-by-step-build-and-deploy)
4. [Generate your secret keys](#4-generate-your-secret-keys)
5. [Configure Application Settings](#5-configure-application-settings)
6. [Enable persistent storage](#6-enable-persistent-storage)
7. [Verify the deployment](#7-verify-the-deployment)

---

## 1. Architecture overview

ServiceHub is deployed as a **single Azure App Service** — the .NET API and React UI are one deployable unit.

```
Build pipeline (your machine):
  npm run build         ← compiles React → services/api/src/ServiceHub.Api/wwwroot/
  dotnet publish        ← packages API + wwwroot together

Azure App Service (one app, one URL):
  .NET 10 API           ← handles /api/v1/* routes
  React UI              ← served as static files from wwwroot/
  SPA fallback          ← all other routes return index.html (React Router handles them)
```

Everything in one place means:
- One URL — `https://YOUR-APP.azurewebsites.net` serves both the UI and the API
- One App Service plan — no separate frontend hosting cost
- One set of Application Settings — all config in one place

**Your data never leaves your Azure subscription.** All Service Bus connection strings are encrypted with AES-256-GCM using a key you generate and store exclusively in your App Service configuration.

---

## 2. What you will create

| Azure resource | Purpose | Estimated cost |
|---|---|---|
| Resource Group | Logical container for all resources | Free |
| App Service Plan (B1) | The compute — 1 core, 1.75 GB RAM | ~$13/month |
| App Service (Web App) | Hosts ServiceHub (API + UI in one) | Included in plan |
| Application Insights | Optional monitoring in your own subscription | Pay-per-use, very low |

---

## 3. Step-by-step: build and deploy

### Step 1 — Clone the repository

```bash
git clone https://github.com/debdevops/servicehub.git
cd servicehub
```

### Step 2 — Build the frontend

The React build output goes directly into the API's `wwwroot/` folder (configured in `apps/web/vite.config.ts`).

```bash
cd apps/web
npm install
npm run build
cd ../..
```

Verify the output exists:

```bash
ls services/api/src/ServiceHub.Api/wwwroot/index.html
```

If this file does not exist, the build failed — check the npm error output.

### Step 3 — Publish the .NET API

`dotnet publish` packages the API together with the React build from `wwwroot/`.

```bash
dotnet publish services/api/src/ServiceHub.Api/ServiceHub.Api.csproj \
  --configuration Release \
  --output ./publish
```

### Step 4 — Create the deployment zip

```bash
cd publish && zip -r ../servicehub-deploy.zip . && cd ..
```

Verify the zip contains `wwwroot/`:

```bash
unzip -l servicehub-deploy.zip | grep "wwwroot/index.html"
```

### Step 5 — Create an Azure Resource Group

#### Option A — Azure Portal (recommended)

1. Go to [portal.azure.com](https://portal.azure.com)
2. In the top search bar, search for **Resource groups**
3. Click **+ Create**
4. Fill in:
   - **Subscription**: Select your Azure subscription
   - **Resource group name**: `rg-servicehub` *(or any name you prefer)*
   - **Region**: Choose a region close to your users (e.g., `East US`, `West Europe`)
5. Click **Review + create** → **Create**
6. Wait for the notification: "Resource group created"

#### Option B — Azure CLI (one command)

```bash
az group create --name rg-servicehub --location eastus
```

### Step 6 — Create the App Service Plan

The plan defines the compute tier. **B1 (Basic)** is the minimum tier that supports Always On and custom domains.

#### Option A — Azure Portal

1. In the search bar, search for **App Service plans**
2. Click **+ Create**
3. Fill in:
   - **Resource Group**: `rg-servicehub`
   - **Name**: `plan-servicehub`
   - **Operating System**: **Linux**
   - **Region**: Same region as your resource group
   - **Pricing tier**: Click **Explore pricing tiers** → select **B1** (Basic)
4. Click **Review + create** → **Create**

#### Option B — Azure CLI (one command)

```bash
az appservice plan create \
  --name plan-servicehub \
  --resource-group rg-servicehub \
  --sku B1 \
  --is-linux
```

### Step 7 — Create the Web App

> The app name must be **globally unique** across all of Azure — it becomes your URL: `https://YOUR-APP-NAME.azurewebsites.net`.

#### Option A — Azure Portal

1. Search for **App Services** in the search bar
2. Click **+ Create** → **Web App**
3. Fill in:
   - **Resource Group**: `rg-servicehub`
   - **Name**: `app-servicehub-yourname` *(replace `yourname` with something unique)*
   - **Publish**: **Code**
   - **Runtime stack**: **.NET 10 (LTS)**
   - **Operating System**: **Linux**
   - **Region**: Same as your plan
   - **App Service Plan**: Select `plan-servicehub`
4. Click **Review + create** → **Create**
5. Wait ~2 minutes for deployment to complete

<!-- Screenshot: images/azure-portal-webapp-create.png — Add this screenshot after deployment -->

#### Option B — Azure CLI (one command)

```bash
az webapp create \
  --name app-servicehub-yourname \
  --resource-group rg-servicehub \
  --plan plan-servicehub \
  --runtime "DOTNETCORE:10.0"
```

### Step 8 — Deploy the application

#### Option A — Azure Portal

1. Go to your App Service in Azure Portal
2. In the left menu, click **Deployment Center**
3. At the top, select the **FTPS credentials** tab to note your credentials (if using FTP), or scroll to the bottom
4. For ZIP deploy: click **Deploy** in the top bar → select **ZIP Deploy**
5. Drag and drop `servicehub-deploy.zip`, or click to browse and select it
6. Wait for the deployment confirmation

#### Option B — Azure CLI (one command)

```bash
az webapp deploy \
  --name app-servicehub-yourname \
  --resource-group rg-servicehub \
  --src-path servicehub-deploy.zip \
  --type zip
```

---

## 4. Generate your secret keys

**Do this before setting Application Settings.** Run from the repository root:

```bash
./scripts/generate-keys.sh
```

This outputs four values. **Save them in a password manager — they are not stored anywhere else:**

```
ENCRYPTION_KEY   : <64-character hex string>
SPA_TOKEN_SECRET : <64-character hex string>
ADMIN_API_KEY    : sh_admin_<64-character hex string>
READONLY_KEY     : sh_ro_<64-character hex string>
```

Or use the automated script that generates keys **and** sets them in Azure in one step:

```bash
./scripts/setup-azure-env.sh app-servicehub-yourname rg-servicehub
```

---

## 5. Configure Application Settings

These are the environment variables ServiceHub reads at startup. **Every setting marked ✅ Required must be set or the app will not start correctly.**

### Step 1 — Open Configuration

#### Option A — Azure Portal

1. Go to your App Service in Azure Portal
2. In the left menu, click **Configuration** (under Settings)
3. Click the **Application settings** tab
4. Click **+ New application setting** for each setting in the table below
5. After adding all settings, click **Save** at the top → **Continue**
6. The app restarts automatically

<!-- Screenshot: images/azure-portal-appsettings.png — Add this screenshot after deployment -->

#### Option B — Azure CLI (one command — sets all required settings at once)

Replace each `<...>` placeholder with the values generated in Step 4. Replace `app-servicehub-yourname` with your actual app name.

```bash
az webapp config appsettings set \
  --name app-servicehub-yourname \
  --resource-group rg-servicehub \
  --settings \
    ASPNETCORE_ENVIRONMENT="Production" \
    Security__EncryptionKey="<your-ENCRYPTION_KEY>" \
    Security__SpaToken__Enabled="true" \
    Security__SpaToken__Secret="<your-SPA_TOKEN_SECRET>" \
    Security__Authentication__Enabled="true" \
    "Security__Authentication__ScopedApiKeys__0__Key"="<your-ADMIN_API_KEY>" \
    "Security__Authentication__ScopedApiKeys__1__Key"="<your-READONLY_KEY>" \
    "Cors__AllowedOrigins__0"="https://app-servicehub-yourname.azurewebsites.net" \
    NamespaceRepository__DataDirectory="/home/data" \
    DlqDatabase__DataDirectory="/home/data"
```

### Step 2 — Complete settings reference

| Setting name | Required | Description | Example value |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | ✅ Required | Must be `Production`. Activates `appsettings.Production.json` which disables Swagger, enables security headers, and sets production log levels. | `Production` |
| `Security__EncryptionKey` | ✅ Required | The AES-256 key that encrypts all Service Bus connection strings before writing them to disk. Generate with `./scripts/generate-keys.sh`. **Never reuse across environments (dev, staging, prod must each have their own).** | `a3f9...` (64 hex chars) |
| `Security__SpaToken__Enabled` | ✅ Required | Enables SPA token protection. When true, the API validates a short-lived HMAC token on every request. Prevents direct API access without loading the UI. | `true` |
| `Security__SpaToken__Secret` | ✅ Required | The HMAC signing key for SPA tokens. Generate with `./scripts/generate-keys.sh`. **Critical for multi-instance deployments**: each instance must share this value — see the note below. | `b7c2...` (64 hex chars) |
| `Security__Authentication__Enabled` | ✅ Required | Enables API key authentication on all non-health endpoints. | `true` |
| `Security__Authentication__ScopedApiKeys__0__Key` | ✅ Required | Admin API key — full access to all endpoints. Generate with `./scripts/generate-keys.sh`. | `sh_admin_...` |
| `Security__Authentication__ScopedApiKeys__1__Key` | ✅ Required | Read-only API key — can browse messages and queues, cannot modify. Generate with `./scripts/generate-keys.sh`. | `sh_ro_...` |
| `Cors__AllowedOrigins__0` | ✅ Required | Your exact App Service URL. Include `https://`, no trailing slash. A mismatch causes the browser to block all API calls. | `https://app-servicehub-yourname.azurewebsites.net` |
| `NamespaceRepository__DataDirectory` | ✅ Required | Where encrypted namespace connection configs are stored. **Must be `/home/data`** — this is Azure App Service's persistent volume. Any other path is wiped on every restart. | `/home/data` |
| `DlqDatabase__DataDirectory` | ✅ Required | Where the DLQ intelligence SQLite database is stored. **Must be `/home/data`** for the same reason. | `/home/data` |
| `ApplicationInsights__ConnectionString` | Optional | Your Application Insights connection string. Leave empty to disable all telemetry — no data is sent anywhere. See the [Application Insights guide](../application-insights/README.md). | `InstrumentationKey=...;IngestionEndpoint=...` |
| `Webhooks__Enabled` | Optional | Enable outbound webhook notifications when DLQ message counts spike. | `false` |
| `Webhooks__Url` | Optional | Your webhook endpoint. Required only if `Webhooks__Enabled` is `true`. | `https://your-endpoint/hook` |

> ⚠️ **Multi-instance note**: If you scale your App Service to more than 1 instance, you **must** set `Security__SpaToken__Secret` to a fixed generated value. Without it, each instance generates its own random secret at startup, and SPA tokens signed by instance A will be rejected by instance B — causing 401 errors for half of all browser sessions.

---

## 6. Enable persistent storage

### Step 1 — Enable Always On

Always On prevents Azure from unloading the process during idle periods, which would cause 30+ second cold starts on the next request.

#### Option A — Azure Portal

1. Go to your App Service → **Configuration** (left menu)
2. Click the **General settings** tab
3. Under **Platform settings**, toggle **Always on** to **On**
4. Click **Save**

> **Note:** Always On requires the B1 tier (Basic) or higher. It is not available on the Free (F1) or Shared (D1) tiers.

#### Option B — Azure CLI (one command)

```bash
az webapp config set \
  --name app-servicehub-yourname \
  --resource-group rg-servicehub \
  --always-on true
```

### Step 2 — Enable HTTPS only

#### Option A — Azure Portal

1. Go to your App Service → **Settings** → **TLS/SSL settings** (or **Custom domains**)
2. Toggle **HTTPS Only** to **On**

#### Option B — Azure CLI (one command)

```bash
az webapp update \
  --name app-servicehub-yourname \
  --resource-group rg-servicehub \
  --https-only true
```

The `/home` directory on Azure App Service is automatically backed by persistent storage. Because you set `NamespaceRepository__DataDirectory` and `DlqDatabase__DataDirectory` to `/home/data`, your namespace connections and DLQ history survive all restarts and redeployments.

---

## 7. Verify the deployment

### Step 1 — Check the health endpoint

```bash
curl https://app-servicehub-yourname.azurewebsites.net/health
```

Expected response:

```json
{"status":"Healthy","totalDuration":"...","entries":{"self":{"status":"Healthy","description":"API is running","duration":"..."}}}
```

If you get HTTP 500, see [Troubleshooting — Health check returns 500](../troubleshooting/README.md#1-health-check-returns-500-internal-server-error).

### Step 2 — Open the UI

Open `https://app-servicehub-yourname.azurewebsites.net` in your browser.

You should see the **ServiceHub Connect page**. If you see a 404 or blank page, see [Troubleshooting — Blank page or 404 on deep links](../troubleshooting/README.md#6-frontend-shows-blank-page-or-404-on-deep-links).

<!-- Screenshot: images/servicehub-connect-success.png — Add this screenshot after deployment -->

### Step 3 — Test a connection

1. On the Connect page, enter a Display Name (e.g., `My Service Bus`)
2. Enter a Service Bus connection string (Listen-only policy recommended)
3. Click **Connect** — you should see a success toast and the namespace appear in **Saved Connections**

If the connection saves but queues fail to load, see [Troubleshooting — Connection string saves but test fails](../troubleshooting/README.md#3-connection-string-saves-but-connection-test-fails).

---

## Next steps

| Guide | Purpose |
|---|---|
| [Security Hardening](../security-hardening/README.md) | Required before production: complete the pre-launch security checklist |
| [Application Insights](../application-insights/README.md) | Optional: monitor performance in your own Azure subscription |
| [Troubleshooting](../troubleshooting/README.md) | If anything is not working |

---

*[← Back to self-hosting index](../README.md)*
