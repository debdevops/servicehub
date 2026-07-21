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

> ⚠️ **The SPA token is anti-replay, not per-user authentication.** When `Security__SpaToken__Enabled` is `true` and there is no platform identity layer (Azure Easy Auth or equivalent) in front of ServiceHub, **any user who can load the web UI receives a full-scope admin session.** The SPA token — a short-lived HMAC-signed token embedded in the served HTML — only proves a request came from a browser that loaded the page. It does **not** identify or authorize an individual user: all SPA sessions share the single built-in admin owner (`__spa__`) and bypass API-key scope checks entirely.
>
> **This is by design for single-operator self-hosting** — the SPA-token path assumes one trust boundary, where everyone who can reach the UI is trusted as the instance administrator.
>
> **If more than one trust level must be isolated, or you expose ServiceHub beyond a single operator:**
> - **On Azure App Service**: enable **Easy Auth**. ServiceHub's `EasyAuthMiddleware` reads the injected `X-MS-CLIENT-PRINCIPAL-ID` and assigns each user a distinct owner ID (`entra:{oid}`) for per-user tenant isolation; Easy Auth requests bypass the shared SPA-owner path.
> - **On AWS, GCP, Docker Compose, on-prem, or any other host**: enable **OIDC Bearer authentication** (`Security:Oidc:*`) instead — see below. Easy Auth only works behind Azure's own auth proxy; OIDC is the portable equivalent for every other deployment target.
> - Restrict who can reach the URL at the network layer (App Service Access Restrictions, private endpoints, or VPN).
> - For headless/API automation, issue a **scoped API key** rather than relying on the SPA token — scoped keys get their own isolated owner ID and least-privilege scopes.

- [ ] If more than one person or role must be isolated from one another, **Azure Easy Auth or OIDC Bearer authentication (or an equivalent identity layer) is enabled** — the SPA token alone grants a shared, full-scope admin session to anyone who can load the UI.

### OIDC Bearer authentication (any host, any standards-compliant IdP)

> ⚠️ Same trust-model caveat as Easy Auth by default: a validated OIDC identity gets a
> **full-scope session**, isolated from other identities only by its own `oidc:{sub}` owner ID.
> If your IdP's app registration is configured to emit an OAuth2 `scope` claim on the token
> (literal scopes like `dlq:read`, or role names — see [Roles](#roles-scope-bundles-for-api-keys-and-oidc)
> below), ServiceHub enforces it exactly like a scoped API key instead. Without that claim,
> every validated OIDC identity is unrestricted (but still isolated by owner ID) — use scoped API
> keys if you need least-privilege enforcement without configuring your IdP's claim mapping.

Unlike Easy Auth, which only works behind Azure App Service's built-in authentication proxy, OIDC
Bearer authentication works identically regardless of where ServiceHub runs — AWS ECS, GCP Cloud
Run, Docker Compose, bare metal, or Azure App Service itself. It validates `Authorization: Bearer
<JWT>` tokens against your identity provider's own signing keys (fetched from
`{Authority}/.well-known/openid-configuration` and auto-rotated), so there is no upstream-header
trust assumption to get wrong — the token's signature is the proof.

**When to use it**: your organization's own reverse proxy or API gateway already performs the
interactive OIDC login and forwards a validated Bearer token to ServiceHub, or your CLI/automation
obtains a token from your IdP via `client_credentials` and passes it directly. ServiceHub does not
implement an interactive login UI itself — exactly like Easy Auth, where Azure's own infrastructure
handles the login redirect and ServiceHub only trusts the resulting identity signal.

**Setup**:

1. Register ServiceHub as an application/client with your identity provider (Entra ID, Okta,
   Auth0, Ping, Google Workspace, or any other OIDC-compliant IdP) and note its **Authority**
   (issuer URL) and the **Audience** (your app's client ID).
2. Set:
   ```bash
   Security__Oidc__Enabled=true
   Security__Oidc__Authority=https://your-idp.example.com   # must be HTTPS
   Security__Oidc__Audience=your-servicehub-client-id
   ```
3. Restart. ServiceHub fails fast at startup if `Enabled=true` with a non-HTTPS or missing
   `Authority`, or a missing `Audience` — see `docs/CONFIGURATION.md`.
4. Callers present `Authorization: Bearer <token>` on every request. A validated token is
   isolated under owner ID `oidc:{sub}` — that identity's namespaces, DLQ history, and audit
   trail are separate from every other identity (SPA, other OIDC subjects, API keys).
5. **Optional** — to enforce least-privilege instead of full access, configure your IdP's app
   registration to emit an OAuth2 `scope` claim on the token (a space-delimited string, e.g.
   `"dlq:read dlq:write"` or a role name like `"Viewer"` — see Roles below). Most IdPs don't do
   this without deliberate configuration, so existing OIDC deployments are unaffected until you
   opt in.

A missing, expired, or invalid Bearer token is never a hard rejection by itself — the request
simply falls through to the SPA token or API key path, same as an untrusted Easy Auth header.

### Roles (scope bundles for API keys and OIDC)

A role is a named bundle of scopes, so an operator doesn't have to enumerate individual scopes by
hand for every key (or every IdP group-to-scope mapping). Use a role name anywhere a scope is
accepted — a scoped API key's `Scopes` array, or an OIDC token's `scope` claim:

| Role | Grants |
|---|---|
| `Viewer` | Browse namespaces, entities, and messages — read-only |
| `Operator` | Viewer + send messages, replay/purge DLQ messages |
| `Auditor` | Viewer + audit trail access, for compliance review without operational access |

```json
"ScopedApiKeys": [
  { "Key": "...", "Scopes": ["Viewer"], "Description": "Read-only key" },
  { "Key": "...", "Scopes": ["Operator"], "Description": "On-call ops key" }
]
```

There's no separate `Admin` role — an API key or OIDC token with no scopes at all (or the literal
`admin` scope) already has full access. Role names and literal scopes can be mixed freely in the
same list.

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
| **OIDC Bearer tokens** | Never logged, in full or in part — only the HTTP method, path, and (on failure) the validation exception type are logged. Signing keys are fetched from your IdP's own discovery document and cached in memory; the raw token itself is never persisted anywhere. |

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
