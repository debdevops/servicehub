# Security Hardening

Complete this guide **before** exposing your ServiceHub instance to users or connecting any production Azure Service Bus namespaces. Each item in the checklist has a concrete consequence if skipped.

---

## Table of contents

1. [Generate all secrets](#1-generate-all-secrets)
2. [Pre-production security checklist](#2-pre-production-security-checklist)
3. [What ServiceHub stores and what it never touches](#3-what-servicehub-stores-and-what-it-never-touches)
4. [Rotate secrets after go-live](#4-rotate-secrets-after-go-live)
5. [Recommended Service Bus policy](#5-recommended-service-bus-policy)

---

## 1. Generate all secrets

Run this from the repository root. It produces all four keys at once:

```bash
./scripts/generate-keys.sh
```

Example output:

```
ENCRYPTION_KEY   : a3f9b2c1... (64 hex chars)
SPA_TOKEN_SECRET : d8e4f7a2... (64 hex chars)
ADMIN_API_KEY    : sh_admin_...
READONLY_KEY     : sh_ro_...
```

Save all four values in a password manager (1Password, Azure Key Vault, Bitwarden, etc.) **before** setting them anywhere. If you lose the `ENCRYPTION_KEY`, all saved connection strings become permanently unreadable.

Or generate each one manually:

```bash
# AES-256 encryption key — encrypts Service Bus connection strings at rest
openssl rand -hex 32

# SPA token secret — HMAC key for browser anti-replay tokens
openssl rand -hex 32

# Admin API key — full access to all endpoints
echo "sh_admin_$(openssl rand -hex 32)"

# Read-only API key — browse only, cannot modify or delete
echo "sh_ro_$(openssl rand -hex 32)"
```

**Rules:**
- Each of the four values must be **different**
- Do not reuse values across environments (dev, staging, prod each need their own set)
- Do not store them in Git or in your shell history

---

## 2. Pre-production security checklist

Check every item before allowing users to access your instance.

### Secrets and credentials

- [ ] `Security__EncryptionKey` is set in App Service Application Settings to a random hex string generated above
- [ ] `Security__EncryptionKey` is **not** the placeholder value `CHANGE_THIS_IN_PRODUCTION_USE_ENV_VAR` from `appsettings.json`
- [ ] `Security__EncryptionKey` is different from the value used in any other environment
- [ ] `Security__SpaToken__Secret` is set to a unique random value (different from the encryption key)
- [ ] `Security__Authentication__ScopedApiKeys__0__Key` (admin key) is generated with `openssl rand` — not a guessable short string
- [ ] `Security__Authentication__ScopedApiKeys__1__Key` (read-only key) is a separate randomly generated value

### Authentication and access control

- [ ] `Security__SpaToken__Enabled` is `true`
  - **Why**: When false, any HTTP client (curl, Postman, automated scanner) can call the API without loading the UI. When true, the browser receives a short-lived HMAC-signed token that the API validates on every request.

- [ ] `Security__Authentication__Enabled` is `true`
  - **Why**: When false, the API accepts requests from anyone who can reach the URL. When true, every non-health endpoint requires a valid API key.

- [ ] `ASPNETCORE_ENVIRONMENT` is `Production`
  - **Why**: This activates `appsettings.Production.json` which sets `Swagger__Enabled: false` (disables `/scalar/v1` exposing your full API schema), enforces production log levels, and enables security headers.

### CORS and origin control

- [ ] `Cors__AllowedOrigins__0` is your exact App Service URL — including `https://`, no trailing slash
  - Example: `https://app-servicehub-yourname.azurewebsites.net`
  - **Why**: A wildcard (`*`) would allow any origin to call your API, including malicious scripts on other websites.
- [ ] `Cors__AllowedOrigins__0` is **not** `*`

### Data persistence

- [ ] `NamespaceRepository__DataDirectory` is `/home/data`
  - **Why**: Any path outside `/home/` is on the ephemeral App Service filesystem and is wiped on every restart or deployment.
- [ ] `DlqDatabase__DataDirectory` is `/home/data`
  - Same reason — the DLQ intelligence SQLite database must survive restarts.

### App Service platform

- [ ] **Always On** is enabled (App Service → Configuration → General settings → Always on → On)
  - **Why**: Without Always On, the process is unloaded after idle periods. The next request triggers a cold start that takes 30+ seconds.
- [ ] **HTTPS Only** is enforced (App Service → Settings → TLS/SSL settings → HTTPS Only → On)
- [ ] `Swagger__Enabled` is not explicitly set to `true` in Application Settings
  - The production config sets it to `false` by default. If you override this, anyone can browse your full API schema at `/scalar/v1`.

### Application Insights (only if configured)

- [ ] `ApplicationInsights__ConnectionString` points to **your own** Application Insights resource, not a shared one
- [ ] Or `ApplicationInsights__ConnectionString` is empty (disables all telemetry)

---

## 3. What ServiceHub stores and what it never touches

Understand this before connecting any real namespace.

| Data | What happens |
|---|---|
| **Service Bus connection string** | Encrypted with AES-256-GCM using **your** `Security__EncryptionKey` immediately on receipt — before any other processing. The ciphertext is stored in `/home/data/servicehub-namespaces.json`. The plaintext connection string is discarded — never written to disk, never returned to the browser in any API response, never logged. |
| **Your encryption key** | Lives **only** in your Azure App Service Application Settings. ServiceHub's authors cannot see it. If you delete or rotate the key, all stored connection strings become permanently unreadable. |
| **Message content** | Read transiently from Azure Service Bus to display in your browser. Never written to disk, never logged, never stored in the DLQ database. The DLQ intelligence database stores only aggregated metadata (queue names, error counts, timestamps) — not message bodies. |
| **API requests and response times** | Sent to **your** Application Insights resource only, if you configure one. Zero telemetry if you leave the connection string empty. |
| **API keys and SPA tokens** | Stripped from all log lines by `LogRedactor.cs` before any write operation. They appear as `[REDACTED]` in any log output. |

---

## 4. Rotate secrets after go-live

### Rotate the encryption key

> ⚠️ **Warning**: Rotating the encryption key makes all previously saved namespace connections **permanently unreadable**. Users will need to re-add their connections after a key rotation. Export or note all saved connections before rotating.

```bash
# Step 1: Generate a new key
openssl rand -hex 32

# Step 2: Update the setting
az webapp config appsettings set \
  --name app-servicehub-yourname \
  --resource-group rg-servicehub \
  --settings Security__EncryptionKey="NEW_KEY_VALUE"

# Step 3: Restart the app
az webapp restart \
  --name app-servicehub-yourname \
  --resource-group rg-servicehub
```

After restart, users must re-enter their connection strings.

### Rotate the SPA token secret

Rotating this secret invalidates all active browser sessions. Users will receive a 401 error on their next API call and need to refresh the page.

```bash
openssl rand -hex 32  # Generate new value

az webapp config appsettings set \
  --name app-servicehub-yourname \
  --resource-group rg-servicehub \
  --settings Security__SpaToken__Secret="NEW_SECRET_VALUE"

az webapp restart \
  --name app-servicehub-yourname \
  --resource-group rg-servicehub
```

### Rotate API keys

Add a new key at a new index, distribute it to users, then remove the old key:

```bash
# Add the new key at index 2 (without removing old ones yet)
az webapp config appsettings set \
  --name app-servicehub-yourname \
  --resource-group rg-servicehub \
  --settings "Security__Authentication__ScopedApiKeys__2__Key"="$(openssl rand -hex 32)"

# After confirming all users have switched, remove the old key by deleting the old setting
```

---

## 5. Recommended Service Bus policy

When connecting ServiceHub to a production Azure Service Bus namespace, **do not use `RootManageSharedAccessKey`**. Create a dedicated policy with the minimum permissions.

### Minimum permissions for ServiceHub

| Operation | Permissions needed |
|---|---|
| Browse and peek messages (read-only) | **Listen only** |
| Replay dead-lettered messages back to the active queue | **Listen** + **Send** |
| Purge messages or generate test messages | **Listen** + **Send** + **Manage** |

For read-only DLQ inspection: **Listen only** is sufficient and is the safest option.

### Create a Listen-only policy

#### Option A — Azure Portal

1. Go to your Azure Service Bus namespace in Azure Portal
2. In the left menu, click **Shared access policies**
3. Click **+ Add**
4. Fill in:
   - **Policy name**: `servicehub`
   - **Manage**: *(leave unchecked)*
   - **Send**: *(leave unchecked)*
   - **Listen**: ✅ **Check this**
5. Click **Create**
6. Click the new `servicehub` policy → copy **Primary Connection String**

#### Option B — Azure CLI (one command)

```bash
# Replace with your Service Bus namespace details
az servicebus namespace authorization-rule create \
  --resource-group rg-your-servicebus \
  --namespace-name your-servicebus-namespace \
  --name servicehub \
  --rights Listen
```

Get the connection string:

```bash
az servicebus namespace authorization-rule keys list \
  --resource-group rg-your-servicebus \
  --namespace-name your-servicebus-namespace \
  --name servicehub \
  --query primaryConnectionString \
  --output tsv
```

Use this connection string in ServiceHub. A Listen-only key **cannot delete, send, or modify anything** — even if the key were ever exposed, your Service Bus data remains completely safe.

---

*[← Back to self-hosting index](../README.md)*
