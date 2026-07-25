# Security Hardening

Complete this guide **before** exposing your ServiceHub instance to users or connecting any production Azure Service Bus namespaces. Each item in the checklist has a concrete consequence if skipped.

---

## Table of contents

1. [Threat model and non-goals](#1-threat-model-and-non-goals)
2. [Generate all secrets](#2-generate-all-secrets)
3. [Pre-production security checklist](#3-pre-production-security-checklist)
4. [What ServiceHub stores and what it never touches](#4-what-servicehub-stores-and-what-it-never-touches)
5. [Rotate secrets after go-live](#5-rotate-secrets-after-go-live)
6. [Recommended Service Bus policy](#6-recommended-service-bus-policy)

---

## 1. Threat model and non-goals

Read this before deciding whether ServiceHub's defaults are appropriate for your environment.

### What ServiceHub defends against by default
- **Accidental data modification** — read-only (`Peek`) access by default; replay/send/purge are
  explicit, gated actions, blocked entirely on production namespaces.
- **Casual/automated abuse of an exposed endpoint** — the SPA token (above) stops zero-effort
  scanners and cross-site requests from hitting the API without first fetching the page.
- **Secrets at rest** — connection strings are AES-256-GCM encrypted; the plaintext never touches
  disk or logs.
- **Accidental secret leakage in logs** — `LogRedactor` strips known secret patterns from log
  output (see [T6 redaction coverage](#redaction-coverage-and-its-limits) below — best-effort, not
  a guarantee).
- **Cross-tenant data leakage between distinct owner identities** — API keys, OIDC subjects, and
  Easy Auth users are isolated from each other's namespaces, DLQ history, and audit trail (subject
  to the namespace-sharing feature, which is opt-in and explicit).

### What ServiceHub explicitly does NOT defend against
- **A hostile network peer.** ServiceHub assumes TLS termination and network transport security
  are handled by your infrastructure (a reverse proxy, load balancer, or platform TLS). It does not
  encrypt traffic itself.
- **A malicious operator.** Whoever controls the encryption key, the host filesystem, or the
  process's environment variables can read everything ServiceHub can read. There is no
  operator-proof secret storage — this is the same trust model as any self-hosted application
  holding its own encryption key.
- **Multi-tenant SaaS isolation.** ServiceHub isolates data *between owner identities* (API keys,
  OIDC subjects), but it is not hardened as a platform for hosting mutually-distrusting tenants who
  each need protection from a compromised or misbehaving co-tenant, a noisy-neighbor resource
  exhaustion attacker, or side-channel attacks. If you need that isolation model, run separate
  ServiceHub instances per tenant rather than relying on owner-ID scoping alone.
- **A user who can already reach the URL and is willing to act as the instance administrator.**
  With no OIDC/Easy Auth configured, anyone who reaches the instance is the admin — see the SPA
  token explanation below.

---

## 2. Generate all secrets

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

## 3. Pre-production security checklist

Check every item before allowing users to access your instance.

### Secrets and credentials

- [ ] `Security__EncryptionKey` is set in App Service Application Settings to a random hex string generated above
- [ ] `Security__EncryptionKey` is **not** the placeholder value `CHANGE_THIS_IN_PRODUCTION_USE_ENV_VAR` from `appsettings.json`
- [ ] `Security__EncryptionKey` is different from the value used in any other environment
- [ ] `Security__SpaToken__Secret` is set to a unique random value (different from the encryption key)
- [ ] `Security__Authentication__ScopedApiKeys__0__Key` (admin key) is generated with `openssl rand` — not a guessable short string
- [ ] `Security__Authentication__ScopedApiKeys__1__Key` (read-only key) is a separate randomly generated value

> ⚠️ **Key rotation is NOT SUPPORTED, plainly.** There is no rotate-and-re-encrypt tool.
> `Security__EncryptionKey` derives the AES-GCM key for every stored connection string via
> HKDF/PBKDF2; if you change it after connections have been saved, **every previously stored
> connection string becomes permanently unreadable** — there is no way to recover them with the
> old key discarded. Treat this key as fixed for the lifetime of your deployment's stored data.
> This is the first question most enterprise reviewers ask, and the honest answer is: back up the
> key itself (in your secret manager) rather than planning to rotate it. ServiceHub also refuses
> to start outside Development if `Security__EncryptionKey` is left at the shipped placeholder
> value — a fail-fast check, not a silent fallback to an insecure default.

### Authentication and access control

- [ ] `Security__SpaToken__Enabled` is `true`
  - **Why**: this is a CSRF and casual-automation mitigation, not an authentication boundary — see below for exactly what it does and doesn't protect against.

> ⚠️ **What the SPA token actually is: a CSRF and casual-automation mitigation, not an
> authentication boundary.** `Security__SpaToken__Enabled=true` makes the API require a
> short-lived HMAC-signed token on every request, which the server embeds directly in the HTML it
> serves at `/`. This raises the bar above zero-effort automation — a browser extension, a
> cross-site request, or a scanner that blindly fires requests at the API without first fetching
> the page will not have a valid token — but it is **not** proof that a human is driving a real
> browser, and it is **not per-user identity**. The token is obtainable by anyone who can fetch the
> index page: `curl https://your-instance/ | grep spaToken` (or equivalent) retrieves it exactly
> as a browser would, because the token is static content in the response, not something that
> requires executing JavaScript or passing a challenge. Every request carrying a valid SPA token —
> from a real user's browser or from that `curl` command — is treated as the same single built-in
> admin owner (`__spa__`), with every scope, on every namespace.
>
> **This is by design for single-operator self-hosting**, not an oversight: the SPA-token path
> assumes one trust boundary, where everyone who can reach the URL is already trusted as the
> instance administrator (the same trust assumption as, say, a database admin console bound to
> localhost). It stops accidental/automated abuse of an exposed instance; it does not stop a
> deliberate visitor from acting as admin.
>
> **The supported path to real per-user identity is OIDC** (any standards-compliant identity
> provider — Entra ID, Okta, Auth0, Ping, Google Workspace, ...), documented in full below. On
> Azure App Service specifically, Easy Auth is also available as a platform-native alternative.
> Neither is required to run ServiceHub; both are required before more than one trust level needs
> isolating.
>
> **If more than one trust level must be isolated, or you expose ServiceHub beyond a single operator:**
> - Enable **OIDC Bearer authentication** (`Security:Oidc:*`, below) — works on any host: AWS, GCP,
>   Docker Compose, on-prem, or Azure App Service itself.
> - **On Azure App Service specifically**: Easy Auth is also available. ServiceHub's
>   `EasyAuthMiddleware` reads the injected `X-MS-CLIENT-PRINCIPAL-ID` and assigns each user a
>   distinct owner ID (`entra:{oid}`); Easy Auth requests bypass the shared SPA-owner path. Easy
>   Auth only works behind Azure's own auth proxy, unlike OIDC above.
> - Restrict who can reach the URL at the network layer (firewall rules, private endpoints, VPN).
> - For headless/API automation, issue a **scoped API key** rather than relying on the SPA token —
>   scoped keys get their own isolated owner ID and least-privilege scopes.

- [ ] If more than one person or role must be isolated from one another, **OIDC Bearer authentication (or, on Azure App Service, Easy Auth)** is enabled — the SPA token alone grants a shared, full-scope admin session to anyone who reaches the URL, not just anyone who "loads the UI" in a literal browser.

### Failed-authentication throttling and its shared-IP-behind-proxy caveat

`AuthFailureThrottle` locks out a client after repeated invalid API-key attempts within a sliding
window, closing the gap left by `RateLimitingMiddleware` (which only sees requests that already
authenticated successfully). It keys on `HttpContext.Connection.RemoteIpAddress` specifically to
avoid trusting an easily-spoofed header.

**Caveat**: `Program.cs` clears `ForwardedHeadersOptions.KnownProxies`/`KnownIPNetworks` so
`X-Forwarded-For` is accepted from any immediate connection (needed for hosts like Azure App
Service, whose front-end IPs aren't fixed in advance). That means `RemoteIpAddress` has already
been overwritten by whatever the client's `X-Forwarded-For` claims before the throttle ever runs.
Behind most reverse proxies, load balancers, or NATs, this means:
- Every user sharing that proxy/NAT shares one lockout bucket — one user's failed attempts can
  lock out everyone else behind the same address.
- An attacker can rotate the header value to dodge the throttle entirely.

**Fix for your deployment**: restrict `ForwardedHeadersOptions.KnownProxies`/`KnownIPNetworks` (in
`Program.cs`) to your actual front-end proxy's address(es) so only headers from a trusted hop are
honored. Not restricted by default because the correct value is deployment-specific. See
[docs/KNOWN-LIMITATIONS.md](../../docs/KNOWN-LIMITATIONS.md) for the same caveat as it affects
audit log `ClientIp`.

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

### Namespace sharing (Preview)

A namespace's owner can grant another owner identity live operational access — browse, peek,
replay/purge, Live Tail — via `POST /api/v1/namespaces/{id}/share` (both this and the revoke
endpoint require `X-ServiceHub-Intent: namespaces:share`). Use `GET /api/v1/me` to discover the
exact owner ID string a colleague needs to share with them (e.g. `oidc:{sub}` for an OIDC
identity, `key_{hash}` for a scoped API key).

> ⚠️ Sharing grants the same full live-access trust the namespace's own owner has — it is not
> scope-restricted. Only the true owner can share or revoke (a shared collaborator cannot
> re-share or delete the namespace), but once shared, the collaborator can do anything the owner
> could do operationally on that namespace.
>
> **Known limitation**: shared access covers live operations only. DLQ Intelligence history, Bulk
> Operation job history, and audit trail entries remain visible only to whichever owner performed
> each action — a shared collaborator does not retroactively see another owner's past
> investigation history for that namespace.

### Audit log retention

- [ ] If your organization has a defined audit-log retention policy (a common SOC2/ISO 27001/GDPR
      data-minimization requirement), **`Audit__Retention__Enabled` is `true`** and
      `Audit__Retention__RetentionDays` matches your policy.
  - **Why**: Off by default — audit logs are kept forever unless you opt in, so upgrading never
    silently deletes existing compliance records. If your policy requires bounded retention (e.g.
    "delete audit records after 2 years"), this must be explicitly enabled; nothing purges on its
    own otherwise.
  - A background sweep (`Audit__Retention__SweepIntervalHours`, default every 24h) enforces this
    automatically. `POST /api/v1/audit/purge` (requires `admin` scope and
    `X-ServiceHub-Intent: audit:purge`) enforces a tightened policy immediately instead of waiting
    for the next scheduled sweep.

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

## 4. What ServiceHub stores and what it never touches

Understand this before connecting any real namespace.

| Data | What happens |
|---|---|
| **Service Bus connection string** | Encrypted with AES-256-GCM using **your** `Security__EncryptionKey` immediately on receipt — before any other processing. The ciphertext is stored in `/home/data/servicehub-namespaces.json`. The plaintext connection string is discarded — never written to disk, never returned to the browser in any API response, never logged. |
| **Your encryption key** | Lives **only** in your Azure App Service Application Settings. ServiceHub's authors cannot see it. If you delete or rotate the key, all stored connection strings become permanently unreadable. |
| **Message content** | Read transiently from Azure Service Bus to display in your browser. Never written to disk, never logged, never stored in the DLQ database. The DLQ intelligence database stores only aggregated metadata (queue names, error counts, timestamps) — not message bodies. |
| **API requests and response times** | Sent to **your** Application Insights resource only, if you configure one. Zero telemetry if you leave the connection string empty. |
| **API keys and SPA tokens** | Stripped from all log lines by `LogRedactor.cs` before any write operation. They appear as `[REDACTED]` in any log output. |
| **OIDC Bearer tokens** | Never logged, in full or in part — only the HTTP method, path, and (on failure) the validation exception type are logged. Signing keys are fetched from your IdP's own discovery document and cached in memory; the raw token itself is never persisted anywhere. |

### Redaction coverage and its limits

`LogRedactor` (`services/api/src/ServiceHub.Infrastructure/Security/LogRedactor.cs`) is
**pattern-matching against known secret shapes, not a content-aware secret scanner** — it is
best-effort, not a guarantee that no sensitive value can ever reach a log line. It currently
recognizes and masks:
- Azure Service Bus connection-string fields (`SharedAccessKey`, `SharedAccessSignature`,
  `AccountKey`, the `Endpoint=` host)
- Generic `password`/`pwd`/`passwd` fields, generic API-key patterns, and `Authorization`/
  `X-API-Key` header values
- Bearer JWTs (by structural shape — three dot-separated segments)
- ServiceHub's own encrypted-value markers (`ENC[v1]:`, legacy `ENC:V2:`/`PROTECTED:`) — so an
  already-encrypted connection string can't be logged as ciphertext either
- AWS access key IDs (`AKIA`/`ASIA` prefix) and `aws_secret_access_key`/`aws_session_token` fields
- GCP service-account JSON `private_key`/`private_key_id` fields and raw PEM private-key blocks
- Slack/Teams incoming-webhook URLs (the URL itself is a bearer secret for those)

Because this is regex-based, a secret in a shape the patterns above don't recognize — a
custom-format token, an unusual field name, a secret embedded mid-sentence in free text — can
still reach a log line. Treat redaction as a defense-in-depth safety net, not a substitute for
not logging sensitive values in the first place. This pattern-matching runs on every log line
carrying user-controlled content, which is a deliberate, accepted CPU cost — see
[docs/KNOWN-LIMITATIONS.md](../../docs/KNOWN-LIMITATIONS.md).

---

## 5. Rotate secrets after go-live

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

## 6. Recommended Service Bus policy

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
