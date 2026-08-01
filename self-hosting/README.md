# Self-Hosting ServiceHub

> 🛡️ **You are in full control.**
> When you self-host ServiceHub, your connection strings, Service Bus data, and messages
> never leave your own infrastructure. There are no callbacks to ServiceHub's
> servers. The encryption key is yours — we never see it.

---

## Prerequisites

Before you start, confirm the following are installed on your machine.

| Prerequisite | Minimum version | How to check |
|---|---|---|
| .NET SDK | 10.0 | `dotnet --version` |
| Node.js | 20.x | `node --version` |
| Git | Any | `git --version` |

---

## Choose your deployment path

One canonical path, in order of how most operators should approach this:

1. **Docker — the primary, recommended path.** One image serves the SPA and the API together;
   works identically on any host that runs containers (a laptop, a Linux VM, AWS/GCP/Azure
   compute, on-prem). Covered in full below.
2. **Azure App Service — a documented alternative**, not a second first-class path. Use it if your
   organization is already standardized on Azure App Service specifically. → [Azure App
   Service](./azure-app-service/README.md)
3. **The rest of `self-hosting/`** — detailed reference material for after you've picked a path:
   [local development](./local-development/README.md), [security
   hardening](./security-hardening/README.md), [Application Insights](./application-insights/README.md),
   [troubleshooting](./troubleshooting/README.md).

### 🐳 Run with Docker (primary path)

**Fastest path to a deployable artifact — one image serves the SPA and the API.**

```bash
docker compose up --build            # → http://localhost:8080
```

This binds to `127.0.0.1:8080` (loopback) only — **not reachable from your network**, even on
the same machine's other interfaces, until you deliberately change that. For deliberate LAN/
network exposure, change the port mapping in your own compose file to `"0.0.0.0:8080:8080"` —
do this only once you've completed the checklist below, not as a shortcut around it.

For a real deployment, save the following as `docker-compose.prod.yml` next to your checkout —
this is a complete, working configuration, not a template with pieces to fill in later:

```yaml
services:
  servicehub:
    build:
      context: .
      dockerfile: Dockerfile
    image: servicehub:local
    ports:
      - "127.0.0.1:8080:8080"   # change to "0.0.0.0:8080:8080" only for deliberate LAN exposure
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      SECURITY__ENCRYPTIONKEY: ${SECURITY__ENCRYPTIONKEY:?set a 32-byte random hex key}
      SECURITY__SPATOKEN__SECRET: ${SECURITY__SPATOKEN__SECRET:?set a 32-byte random hex secret}
      ALLOWEDHOSTS: ${ALLOWEDHOSTS:?set your real hostname, e.g. servicehub.mycompany.com}
      CORS__ALLOWEDORIGINS__0: ${CORS__ALLOWEDORIGINS__0:?set your exact origin, e.g. https://servicehub.mycompany.com}
      SECURITY__AUTHENTICATION__SCOPEDAPIKEYS__0__KEY: ${SERVICEHUB_ADMIN_KEY:?set an admin API key}
      SECURITY__AUTHENTICATION__SCOPEDAPIKEYS__0__SCOPES__0: admin
      # Enable preview providers only if you need them (both default false):
      # CLOUDPROVIDERS__AWS__ENABLED: "true"
      # CLOUDPROVIDERS__GCP__ENABLED: "true"
    volumes:
      - servicehub-data:/var/servicehub/data
    healthcheck:
      test: ["CMD", "curl", "-fsS", "http://localhost:8080/health/live"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 25s
    restart: unless-stopped

volumes:
  servicehub-data:
```

Generate the required values and run it:

```bash
export SECURITY__ENCRYPTIONKEY=$(openssl rand -hex 32)
export SECURITY__SPATOKEN__SECRET=$(openssl rand -hex 32)
export SERVICEHUB_ADMIN_KEY="sh_admin_$(openssl rand -hex 32)"
export ALLOWEDHOSTS="servicehub.mycompany.com"                    # your real hostname — see below
export CORS__ALLOWEDORIGINS__0="https://servicehub.mycompany.com" # your real origin — see below

docker compose -f docker-compose.prod.yml up --build -d
```

The image runs as a non-root user, exposes port `8080`, includes a container `HEALTHCHECK`
against `/health/live`, and persists the namespace store + SQLite DLQ/audit DB to
`/var/servicehub/data` (see [Data directory](#data-directory-what-lives-there-and-why-you-must-back-it-up)
below). All settings are in [../docs/CONFIGURATION.md](../docs/CONFIGURATION.md).

#### Required configuration checklist

Every one of these has a **shipped placeholder value** (`SET_VIA_ENV_VAR`) in
`appsettings.Production.json` that **must** be overridden — ServiceHub does not silently fall back
to an insecure default for any of them. Leftover placeholders don't fail quietly: **`AllowedHosts`
left as `SET_VIA_ENV_VAR` rejects every single request** (ASP.NET Core's host-header filtering has
no match, so nothing gets through — this is usually the first "why won't anything load" support
question). The others fail in their own confusing way: a missing encryption key refuses to start
entirely; a missing SPA token secret breaks every browser session; empty CORS origins block the
SPA from calling its own API from a browser.

- [ ] `Security:EncryptionKey` (`SECURITY__ENCRYPTIONKEY`) — 32-byte random hex (`openssl rand -hex 32`). App refuses to start without a real value outside Development.
- [ ] `AllowedHosts` (`ALLOWEDHOSTS`) — your exact deployment hostname(s), semicolon-separated. **Never `*`.**
- [ ] `Cors:AllowedOrigins` (`CORS__ALLOWEDORIGINS__0`, `__1`, ...) — your exact browser origin(s), `https://` included, no trailing slash. **Never `*`.**
- [ ] `Security:SpaToken:Secret` (`SECURITY__SPATOKEN__SECRET`) — 32-byte random hex, different from the encryption key.
- [ ] **At least one** of: a scoped API key (`Security:Authentication:ScopedApiKeys`) or OIDC (`Security:Oidc:Enabled=true` + `Authority` + `Audience`) — otherwise nobody has a way to authenticate at all once you also lock down the SPA token's implicit trust. See [Security Hardening](./security-hardening/README.md) for what the SPA token does and doesn't protect against before relying on it alone.

> **Note:** In Production mode the app **will not start** without a real `SECURITY__ENCRYPTIONKEY`
> (this is intentional — it prevents shipping with a known default key).

#### 🌐 Reverse proxy and X-Forwarded-For header configuration

If ServiceHub runs behind a reverse proxy (Nginx, HAProxy, AWS ALB, Azure Application Gateway, etc.),
the proxy must be explicitly configured to ensure audit logging and auth throttling work correctly.

**Why this matters:** ServiceHub reads the client IP from `X-Forwarded-For` headers to log audit
events and throttle failed authentication attempts. If your reverse proxy is not explicitly trusted,
ServiceHub will accept `X-Forwarded-For` from any client, allowing spoofed IPs in audit logs and
bypassing auth throttling.

**If you run ServiceHub directly on the internet (not behind a proxy):** No action needed.

**If you run behind a reverse proxy:** You must explicitly configure which proxy IP(s) to trust.
Edit `appsettings.Production.json` or set environment variables:

**For Nginx reverse proxy** (forward traffic from `10.0.0.5`):
```json
{
  "ForwardedHeaders": {
    "KnownProxies": ["10.0.0.5"],
    "ForwardedHeaders": ["XForwardedFor", "XForwardedProto"]
  }
}
```

**For AWS ALB or Azure Application Gateway** (trust all traffic from load balancer):
```json
{
  "ForwardedHeaders": {
    "KnownIPNetworks": ["10.0.0.0/8"],
    "ForwardedHeaders": ["XForwardedFor", "XForwardedProto"]
  }
}
```

**For multi-proxy setups** (Nginx → Application Gateway → ServiceHub):
```json
{
  "ForwardedHeaders": {
    "KnownProxies": ["10.0.0.5"],
    "KnownIPNetworks": ["10.0.0.0/8"],
    "ForwardedHeaders": ["XForwardedFor", "XForwardedProto"]
  }
}
```

See [docs/CONFIGURATION.md](../docs/CONFIGURATION.md) for the complete `ForwardedHeaders` schema and
all allowed values. Once configured, ServiceHub logs the actual client IP in audit trails and
correctly throttles authentication failures per source.

---

### 💻 Run locally on your machine

**Fastest option — zero cloud dependencies.**

→ [Run locally](./local-development/README.md)

What this gives you:
- Instant feedback loop — changes hot-reload immediately
- No cloud costs
- Full debug tooling
- Works on macOS, Linux, and Windows (via WSL)

---

### 🌐 Azure App Service (documented alternative)

Deploy to Azure App Service specifically if your organization is already standardized there.
For any other host (a Linux VM, AWS EC2, GCP Compute Engine, bare metal), Docker (above) is the
primary path — Azure App Service isn't a second general-purpose option, it's a platform-specific
one with its own setup guide because Azure's deployment model (Application Settings, Easy Auth,
Always On) differs enough to warrant separate steps.

What this gives you on Azure specifically:
- Always-on access for your team
- HTTPS via Azure's own TLS termination
- Persistent storage via App Service's `/home` mount
- Optional **Easy Auth** for per-user identity without configuring OIDC yourself

→ [Azure App Service](./azure-app-service/README.md)

---

## Data directory: what lives there, and why you must back it up

Both `DlqDatabase:DataDirectory` and `NamespaceRepository:DataDirectory` point at the same
directory by default (`/var/servicehub/data` in the Docker image). It contains:

| File | Contents |
|---|---|
| `servicehub-namespaces.json` | Every saved namespace connection — **AES-GCM-256 encrypted** with your `Security:EncryptionKey`, but this is the *only* copy. Lose this file (without a backup) and every saved connection is gone; lose or rotate the encryption key and the file becomes unreadable even if it still exists. |
| `servicehub-dlq.db` (SQLite) | DLQ history, auto-replay rules, and the audit trail. Message bodies are never stored here — only metadata (queue names, error categories, timestamps). |

**This directory must be backed up.** It holds the only copy of every stored credential
(encrypted, but still the only copy — there is no server-side escrow or recovery path if it's
lost) and your entire DLQ/audit history. Mount it on a persistent volume (the Docker examples
above already do this via the `servicehub-data` named volume) and back that volume up the same
way you'd back up any production database.

---

## After deployment

Once your instance is running, continue with:

| Guide | When to use |
|---|---|
| [Application Insights](./application-insights/README.md) | Optional. Monitor performance in your own Azure subscription. |
| [Security Hardening](./security-hardening/README.md) | **Required before production.** Generate proper secrets, complete the pre-launch checklist. |
| [Troubleshooting](./troubleshooting/README.md) | When something isn't working. Covers the 8 most common errors with exact fixes. |
| [Provider Support Matrix](../docs/PROVIDER-SUPPORT.md) | Before enabling AWS or GCP. What "preview" means concretely, the capability matrix, and required IAM permissions. |
| [Known Limitations](../docs/KNOWN-LIMITATIONS.md) | Read once before your first production deployment. Every deliberate architectural trade-off in one page. |

---

## Architecture in one sentence

ServiceHub is a **single deployable unit**: the React UI is compiled into the .NET API's `wwwroot/` folder, and both are served by one process. You do not run a separate frontend server in production.

```
Your browser
    │  HTTP(S)
    ▼
ServiceHub (.NET 10 API — one process, one port)
    ├── .NET 10 API  →  Azure Service Bus / AWS SQS / GCP Pub/Sub
    └── React UI     →  served as static files from wwwroot/
```

Your cloud connection strings are **AES-256-GCM encrypted** using a key you generate. The plaintext key never leaves your server configuration.

Azure Service Bus is supported (GA). AWS SQS/SNS and GCP Pub/Sub are **preview** — disabled by
default (`CloudProviders:Aws:Enabled` / `CloudProviders:Gcp:Enabled`, both `false`, both absent
from the production config profile entirely) and enabled explicitly by an operator. See
[docs/PROVIDER-SUPPORT.md](../docs/PROVIDER-SUPPORT.md) for exactly what "preview" means and the
required IAM permissions before enabling either one.


---

## Quick start: generate your secret keys

Before any production deployment — Docker or Azure App Service — generate the secrets you will
need. Run this from the repository root:

```bash
./scripts/generate-keys.sh
```

Save the output securely in a password manager before proceeding. You will need these values for
the [required configuration checklist](#required-configuration-checklist) above, or Azure App
Service's Application Settings if you're deploying there instead.
