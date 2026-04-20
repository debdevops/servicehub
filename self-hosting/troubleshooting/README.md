# Troubleshooting

This guide covers the 8 most common problems encountered when self-hosting ServiceHub. Each entry includes the exact symptom, the most likely cause, and a concrete fix.

If your issue is not listed here, check the application logs first — they usually contain the exact error message:

```bash
# Azure App Service logs (last 50 lines, errors only)
az webapp log tail \
  --resource-group rg-servicehub \
  --name app-servicehub-yourname \
  --filter Error
```

---

## Table of contents

1. [Health check returns 500 Internal Server Error](#1-health-check-returns-500-internal-server-error)
2. ["Cannot reach the API" toast in the browser](#2-cannot-reach-the-api-toast-in-the-browser)
3. [Connection string saves but connection test fails](#3-connection-string-saves-but-connection-test-fails)
4. [401 Unauthorized on all API calls after deployment](#4-401-unauthorized-on-all-api-calls-after-deployment)
5. [Data disappears after App Service restart](#5-data-disappears-after-app-service-restart)
6. [Frontend shows blank page or 404 on deep links](#6-frontend-shows-blank-page-or-404-on-deep-links)
7. [run.sh fails — "Unsupported operating system"](#7-runsh-fails--unsupported-operating-system)
8. ["Security__EncryptionKey is not configured" exception in logs](#8-securityencryptionkey-is-not-configured-exception-in-logs)

---

## 1. Health check returns 500 Internal Server Error

### Symptom

```bash
curl https://app-servicehub-yourname.azurewebsites.net/health
# Returns HTTP 500 or an HTML error page instead of JSON
```

### Cause

`Security__EncryptionKey` is missing, empty, or still contains the default placeholder value `CHANGE_THIS_IN_PRODUCTION_USE_ENV_VAR` from `appsettings.json`. The API fails to initialise its encryption service when this value is invalid.

### Fix — Step 1: Verify the current value

```bash
az webapp config appsettings list \
  --resource-group rg-servicehub \
  --name app-servicehub-yourname \
  --query "[?name=='Security__EncryptionKey'].{name:name, value:value}"
```

If the value is empty, missing, or shows `CHANGE_THIS_IN_PRODUCTION_USE_ENV_VAR`, proceed to Step 2.

### Fix — Step 2: Generate and set a real key

```bash
# Generate a new key
openssl rand -hex 32

# Set it in App Service (replace NEW_KEY with the generated value)
az webapp config appsettings set \
  --resource-group rg-servicehub \
  --name app-servicehub-yourname \
  --settings Security__EncryptionKey="NEW_KEY"

# Restart the app
az webapp restart \
  --resource-group rg-servicehub \
  --name app-servicehub-yourname
```

Wait 20 seconds, then retry: `curl https://app-servicehub-yourname.azurewebsites.net/health`

---

## 2. "Cannot reach the API" toast in the browser

### Symptom

The ServiceHub UI loads but every action (loading namespaces, connecting, browsing messages) shows a toast: "Cannot reach the API" or the browser Network tab shows failed requests with CORS errors.

### Cause

`Cors__AllowedOrigins__0` does not exactly match the URL you are using in your browser. The browser blocks the API response because of the CORS mismatch.

Common mistakes:
- Trailing slash: `https://app.azurewebsites.net/` ← **wrong**
- Wrong protocol: `http://app.azurewebsites.net` ← **wrong if you're on HTTPS**
- Missing protocol: `app.azurewebsites.net` ← **wrong**

### Fix — Step 1: Check what is configured

```bash
az webapp config appsettings list \
  --resource-group rg-servicehub \
  --name app-servicehub-yourname \
  --query "[?name=='Cors__AllowedOrigins__0'].{name:name, value:value}"
```

### Fix — Step 2: Set the exact URL

Set the value to exactly what appears in your browser's address bar — including `https://`, no trailing slash:

```bash
az webapp config appsettings set \
  --resource-group rg-servicehub \
  --name app-servicehub-yourname \
  --settings "Cors__AllowedOrigins__0"="https://app-servicehub-yourname.azurewebsites.net"

az webapp restart \
  --resource-group rg-servicehub \
  --name app-servicehub-yourname
```

---

## 3. Connection string saves but connection test fails

### Symptom

You paste a connection string, click Connect, see a success toast — but opening the namespace shows "Failed to load queues" or no data appears.

### Cause A: Service Bus network firewall

Your App Service's outbound IP addresses are not allowed through your Service Bus namespace's firewall.

### Fix A — Step 1: Get the App Service outbound IPs

```bash
az webapp show \
  --resource-group rg-servicehub \
  --name app-servicehub-yourname \
  --query outboundIpAddresses \
  --output tsv
```

This outputs a comma-separated list of IP addresses.

### Fix A — Step 2: Add IPs to Service Bus firewall

In Azure Portal → your Service Bus namespace → **Networking** → **Firewalls and virtual networks** → add each IP under "Firewall" → **Save**.

### Cause B: Insufficient SAS policy permissions

The connection string uses a policy without **Listen** permission. ServiceHub needs at least Listen to browse messages.

### Fix B

In Azure Portal → your Service Bus namespace → **Shared access policies** → open the policy used in the connection string → confirm **Listen** is checked. If not, create a new policy with Listen enabled.

---

## 4. 401 Unauthorized on all API calls after deployment

### Symptom

Every API call returns HTTP 401 after deployment. The browser shows "Unauthorized" in the Network tab. The app worked before you scaled to multiple instances or redeployed.

### Cause A: Multiple App Service instances with different SPA token secrets

If you scaled to 2+ instances and `Security__SpaToken__Secret` is not set to a fixed value, each instance generates its own random secret at startup. A SPA token signed by instance A is invalid when the next request routes to instance B.

### Fix A

Set a fixed token secret that all instances share:

```bash
openssl rand -hex 32  # Copy the output

az webapp config appsettings set \
  --resource-group rg-servicehub \
  --name app-servicehub-yourname \
  --settings Security__SpaToken__Secret="PASTE_THE_GENERATED_VALUE"

az webapp restart \
  --resource-group rg-servicehub \
  --name app-servicehub-yourname
```

### Cause B: SPA token expired

Browser sessions that have been open for more than 2 hours without any activity may have an expired SPA token.

### Fix B

Refresh the page. The client refreshes the SPA token automatically every 90 minutes during active use. A hard refresh (`Cmd+Shift+R` / `Ctrl+Shift+R`) always fetches a fresh token.

---

## 5. Data disappears after App Service restart

### Symptom

After restarting the App Service (or deploying an update), all saved namespace connections are gone.

### Cause

`NamespaceRepository__DataDirectory` or `DlqDatabase__DataDirectory` is not set to `/home/data`. The Azure App Service filesystem outside `/home/` is ephemeral and is wiped on every restart, deployment, or instance recycle.

### Fix — Step 1: Check the current values

```bash
az webapp config appsettings list \
  --resource-group rg-servicehub \
  --name app-servicehub-yourname \
  --query "[?name=='NamespaceRepository__DataDirectory' || name=='DlqDatabase__DataDirectory']"
```

### Fix — Step 2: Set both to /home/data

```bash
az webapp config appsettings set \
  --resource-group rg-servicehub \
  --name app-servicehub-yourname \
  --settings \
    NamespaceRepository__DataDirectory="/home/data" \
    DlqDatabase__DataDirectory="/home/data"

az webapp restart \
  --resource-group rg-servicehub \
  --name app-servicehub-yourname
```

After the restart, re-add your connections. They will now persist across future restarts and deployments.

> **Verify it worked**: Add a connection, go to Azure Portal → App Service → Restart (not just reload), come back to ServiceHub — the connection should still be there.

---

## 6. Frontend shows blank page or 404 on deep links

### Symptom

Navigating directly to a URL like `https://app-servicehub-yourname.azurewebsites.net/messages` shows a 404 page or a blank screen. The app only works when you navigate from the root URL.

### Cause

The React build output is missing from `wwwroot/` in the published output. The .NET API has a built-in SPA fallback that serves `index.html` for all non-API routes — but only if `index.html` exists in `wwwroot/`. Without it, the server has nothing to serve.

### Fix — Step 1: Verify wwwroot is in the published output

Check if the file exists using the Azure App Service console:

In Azure Portal → your App Service → Development Tools → **Console**, then:

```bash
ls /home/site/wwwroot/wwwroot/index.html
```

If this file does not exist, the frontend was not built before `dotnet publish`.

### Fix — Step 2: Rebuild in the correct order

The frontend **must** be built before `dotnet publish`, because the Vite build writes its output into `services/api/src/ServiceHub.Api/wwwroot/` which is then picked up by the .NET publish step.

```bash
# Step 1: Build the frontend FIRST
cd apps/web
npm install
npm run build
cd ../..

# Step 2: Verify the index.html exists
ls services/api/src/ServiceHub.Api/wwwroot/index.html

# Step 3: Publish .NET (now includes wwwroot/)
dotnet publish services/api/src/ServiceHub.Api/ServiceHub.Api.csproj \
  --configuration Release \
  --output ./publish

# Step 4: Verify the zip includes wwwroot/
cd publish && zip -r ../servicehub-deploy.zip . && cd ..
unzip -l servicehub-deploy.zip | grep "wwwroot/index.html"

# Step 5: Redeploy
az webapp deploy \
  --resource-group rg-servicehub \
  --name app-servicehub-yourname \
  --src-path servicehub-deploy.zip \
  --type zip
```

---

## 7. run.sh fails — "Unsupported operating system"

### Symptom

Running `./run.sh` on Windows shows:

```
✗ Error: Please use WSL (Windows Subsystem for Linux) on Windows
```

### Cause

`run.sh` requires a bash environment. Native Windows (Command Prompt, PowerShell, Git Bash MINGW/CYGWIN) does not provide a compatible environment.

### Fix A — Use WSL (recommended)

WSL runs a real Linux environment on Windows and is the best experience:

```powershell
# Run in PowerShell (Administrator)
wsl --install
```

After installation and restarting Windows, open "Ubuntu" from the Start menu, navigate to the repository, and run `./run.sh`.

### Fix B — Use the PowerShell script

A native Windows PowerShell alternative is provided in the repository root:

```powershell
.\run.ps1
```

This starts the API and UI in separate PowerShell windows without requiring WSL.

---

## 8. "Security__EncryptionKey is not configured" exception in logs

### Symptom

The app starts but API calls return 500 errors. The App Service logs contain:

```
FATAL: Security:EncryptionKey is not set or contains the placeholder value.
```

Or similar text about the encryption key.

### Cause

The setting is configured in Azure Portal under **Connection strings** instead of **Application settings**, or the value was set but the app was not restarted afterward.

### Fix — Step 1: Confirm the setting is in Application settings (not Connection strings)

In Azure Portal → App Service → Configuration:
- Click **Application settings** tab — `Security__EncryptionKey` must be here
- Click **Connection strings** tab — it must **not** be here

### Fix — Step 2: If missing from Application settings, add it correctly

```bash
az webapp config appsettings set \
  --resource-group rg-servicehub \
  --name app-servicehub-yourname \
  --settings Security__EncryptionKey="$(openssl rand -hex 32)"
```

Then verify the app restarts (it restarts automatically when Application Settings are saved via the Portal, or use the CLI command below):

```bash
az webapp restart \
  --resource-group rg-servicehub \
  --name app-servicehub-yourname
```

---

## Collecting diagnostics for a bug report

If none of the above solves your issue, collect these details before opening a GitHub issue:

```bash
# Recent error logs
az webapp log tail \
  --resource-group rg-servicehub \
  --name app-servicehub-yourname \
  --filter Error

# Health check output
curl -s https://app-servicehub-yourname.azurewebsites.net/health | python3 -m json.tool

# Settings list (redact secret values before sharing publicly)
az webapp config appsettings list \
  --resource-group rg-servicehub \
  --name app-servicehub-yourname \
  --query "[].{name:name}" \
  --output table
```

> ⚠️ Never share the values of `Security__EncryptionKey`, `Security__SpaToken__Secret`, or any API key in a public issue. Share only the setting names, not their values.

---

*[← Back to self-hosting index](../README.md)*
