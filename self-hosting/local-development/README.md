# Run ServiceHub Locally

This guide walks you through running ServiceHub on your own machine for development and testing. No cloud dependencies, no Azure subscription required.

---

## Table of contents

1. [Architecture](#1-architecture)
2. [Quickest way: use run.sh](#2-quickest-way-use-runsh)
3. [Manual setup (step-by-step)](#3-manual-setup-step-by-step)
4. [Frontend environment variables](#4-frontend-environment-variables)
5. [Run tests](#5-run-tests)
6. [Windows support](#6-windows-support)

---

## 1. Architecture

Two processes run simultaneously during local development:

```
  http://localhost:5153           ← .NET 10 API
        ▲
        │  Vite proxy (automatic — no config needed)
        │
  http://localhost:3000           ← React dev server (Vite)
        │
  Your browser
```

The Vite dev server is pre-configured in `apps/web/vite.config.ts` to proxy all `/api` and `/health` requests to `http://localhost:5153`. **You do not need to configure this proxy** — open `http://localhost:3000` and it just works.

---

## 2. Quickest way: use run.sh

The repository includes `run.sh` which automatically installs all prerequisites and starts both services:

```bash
./run.sh
```

This script:
1. Detects your OS (macOS, Ubuntu/Debian, RHEL/CentOS, Arch, openSUSE, or WSL)
2. Installs .NET 10 SDK if missing
3. Installs Node.js 20 if missing
4. Generates local encryption keys (creates `appsettings.Local.json` on first run — git-ignored, never affects Azure)
5. Cleans up any previously running instances on ports 5153 and 3000
6. Starts the API on `http://localhost:5153`
7. Starts the UI on `http://localhost:3000`

Press `Ctrl+C` to stop both services.

> **Note for Windows users**: `run.sh` requires bash. Use WSL (Windows Subsystem for Linux) or see [Windows support](#6-windows-support) for the PowerShell alternative.

---

## 3. Manual setup (step-by-step)

Use this if you prefer full control over each step, or if `run.sh` fails.

### Terminal 1 — Start the API

Open a terminal in the repository root and run these steps in order:

```bash
# Step 1: Navigate to the API project
cd services/api/src/ServiceHub.Api
```

```bash
# Step 2: Create appsettings.Local.json
# This file is git-ignored — safe to put secrets here, it never gets committed.
# NOTE: appsettings.Local.json is only loaded in Development mode.
#       It does NOT affect Azure App Service deployments.
cat > appsettings.Local.json << 'EOF'
{
  "Security": {
    "EncryptionKey": "local-dev-key-minimum-32-characters-long!",
    "EnableConnectionStringEncryption": true,
    "SpaToken": {
      "Enabled": false
    },
    "Authentication": {
      "Enabled": false
    }
  },
  "ApplicationInsights": {
    "ConnectionString": ""
  }
}
EOF
```

```bash
# Step 3: Restore NuGet packages
dotnet restore
```

```bash
# Step 4: Start the API in Development mode
# Development mode: no API key required, Swagger enabled at /scalar/v1
ASPNETCORE_ENVIRONMENT=Development dotnet run --urls "http://localhost:5153"
```

```bash
# Step 5: Verify the API is running (open a new terminal tab to run this)
curl http://localhost:5153/health
# Expected: {"status":"Healthy",...}
```

### Terminal 2 — Start the frontend

Open a **second terminal** in the repository root:

```bash
# Step 1: Navigate to the web app
cd apps/web
```

```bash
# Step 2: Copy the example environment file
cp .env.example .env.local
```

```bash
# Step 3: Leave VITE_API_BASE_URL commented out in .env.local
# The Vite proxy handles routing to localhost:5153 automatically.
# You only need to set VITE_API_BASE_URL if your API runs on a different host.
```

```bash
# Step 4: Install npm dependencies
npm install
```

```bash
# Step 5: Start the Vite dev server
npm run dev
```

```bash
# Step 6: Open in browser
# http://localhost:3000
```

### Expected startup output

**API** (Terminal 1):
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5153
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

**UI** (Terminal 2):
```
  VITE v6.x.x  ready in XXXms

  ➜  Local:   http://localhost:3000/
  ➜  Network: http://0.0.0.0:3000/
```

---

## 4. Frontend environment variables

Create `apps/web/.env.local` (copied from `.env.example`) and customise as needed. All variables are optional for local development.

| Variable | Default | Description |
|---|---|---|
| `VITE_API_BASE_URL` | *(not set — uses Vite proxy)* | Leave commented out for local development. The Vite proxy automatically forwards `/api` requests to `http://localhost:5153`. Only set this if your API runs on a different host or port. |
| `VITE_APPINSIGHTS_CONNECTION_STRING` | *(empty)* | Your Application Insights connection string. Leave empty to disable all telemetry locally — no data is sent anywhere. |
| `VITE_APPINSIGHTS_SAMPLING_PERCENTAGE` | `50` | What percentage of telemetry events to send (1–100). Applies only if `VITE_APPINSIGHTS_CONNECTION_STRING` is set. |
| `VITE_ENABLE_AI_INSIGHTS` | `true` | Show the AI-powered message analysis panel in the UI. |
| `VITE_ENABLE_PERFORMANCE_MONITORING` | `false` | Enable frontend performance tracking (React Query timing, render timing). |
| `VITE_ENABLE_QUERY_DEVTOOLS` | `true` | Show React Query devtools panel (bottom-right corner). Development only — automatically hidden in production builds. |

### Notes on local security settings

The `appsettings.Local.json` template above sets `Authentication.Enabled: false` and `SpaToken.Enabled: false`. This means:

- No API key is required to call the API from localhost
- You can use curl or Postman directly against `http://localhost:5153` without any headers
- The Swagger API explorer is available at `http://localhost:5153/scalar/v1`

**These settings only apply when `ASPNETCORE_ENVIRONMENT=Development`** — which is set automatically by `run.sh` and in the manual Step 4 command above. Azure App Service runs in `Production` mode and uses `appsettings.Production.json` instead, which enforces full authentication.

---

## 5. Run tests

### API unit tests

```bash
dotnet test services/api/tests/ServiceHub.UnitTests
```

### API integration tests

```bash
dotnet test services/api/tests/ServiceHub.IntegrationTests
```

### Frontend tests

```bash
cd apps/web && npm test
```

### Frontend test coverage

```bash
cd apps/web && npm run test:coverage
```

---

## 6. Windows support

`run.sh` requires a bash environment. On Windows, you have two options:

### Option A — WSL (recommended)

WSL (Windows Subsystem for Linux) runs a real Linux distribution inside Windows and is the best experience:

```powershell
# Run in PowerShell (Administrator) to install WSL with Ubuntu
wsl --install
```

After installation and restart, open "Ubuntu" from the Start menu, then run `./run.sh` from the repository root.

### Option B — PowerShell (native Windows)

A PowerShell script is provided for native Windows without WSL:

```powershell
.\run.ps1
```

This starts the API and UI in separate PowerShell windows. See `run.ps1` in the repository root for details.

---

*[← Back to self-hosting index](../README.md)*
