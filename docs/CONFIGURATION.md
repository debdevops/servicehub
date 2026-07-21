# Configuration Reference

ServiceHub is configured through the standard ASP.NET Core configuration stack. Every setting
can be supplied by (in increasing order of precedence):

1. `appsettings.json` (shipped defaults)
2. `appsettings.{Environment}.json` — `Development`, `Production`, `Simulator`
3. `appsettings.Local.json` (git-ignored; for local secrets)
4. **Environment variables** (recommended for secrets and containers)

Environment variables use `__` (double underscore) to represent nested keys. For example the
key `Security:EncryptionKey` becomes the environment variable `SECURITY__ENCRYPTIONKEY`.

> **Startup validation.** Several sections are validated when the process starts
> (`ValidateOnStart`). If a value is structurally invalid — for example a negative
> `RateLimit:MaxRequests`, or `Webhooks:Enabled=true` with no valid `Webhooks:Url` — ServiceHub
> **fails fast with a clear message** instead of surfacing an opaque error later. A one-line,
> secret-free summary of the effective configuration is also logged at startup.

---

## Security

| Key | Env var | Default | Notes |
|---|---|---|---|
| `Security:EncryptionKey` | `SECURITY__ENCRYPTIONKEY` | placeholder | **Required in non-Development.** 32-byte random hex recommended (`openssl rand -hex 32`). The app **refuses to start** in non-Development if this is a placeholder/dev value. |
| `Security:EnableConnectionStringEncryption` | `SECURITY__ENABLECONNECTIONSTRINGENCRYPTION` | `true` | AES-GCM-256 encryption of stored connection strings. |
| `Security:Authentication:Enabled` | `SECURITY__AUTHENTICATION__ENABLED` | `true` | Enables `X-API-KEY` authentication. |
| `Security:Authentication:ApiKeys` | — | `[]` | API key definitions (key + scopes). |
| `Security:Authentication:ScopedApiKeys[].Scopes` | — | `[]` | Literal scopes (`"dlq:read"`) and/or role names (`"Viewer"`, `"Operator"`, `"Auditor"`) — freely mixable in the same list. See [Roles](#api-key--oidc-roles). |
| `Security:EasyAuth:Enabled` | `SECURITY__EASYAUTH__ENABLED` | `true` | Trust Azure App Service EasyAuth headers. Only unforgeable behind Azure's proxy — leave off elsewhere. |
| `Security:Oidc:*` | `SECURITY__OIDC__*` | `Enabled=false` | Provider-neutral per-user SSO for any OIDC identity provider, on any host. See [Oidc](#oidc-byo-idp-sso---validated-at-startup) below. |

## Cloud providers

| Key | Env var | Default | Notes |
|---|---|---|---|
| `CloudProviders:Aws:Enabled` | `CLOUDPROVIDERS__AWS__ENABLED` | `false` | Registers the AWS (preview) provider. Inert until an AWS namespace exists. |
| `CloudProviders:Gcp:Enabled` | `CLOUDPROVIDERS__GCP__ENABLED` | `false` | Registers the GCP (preview) provider. Inert until a GCP namespace exists. |

Azure is always registered as the live provider outside Simulator mode. Simulator mode registers all three unconditionally.

## Persistence (data directory)

| Key | Env var | Default | Notes |
|---|---|---|---|
| `DlqDatabase:DataDirectory` | `DLQDATABASE__DATADIRECTORY` | `/var/servicehub/data` (Prod) | Directory holding `servicehub-dlq.db` (SQLite: DLQ history, auto-replay rules, audit logs). |
| `NamespaceRepository:DataDirectory` | `NAMESPACEREPOSITORY__DATADIRECTORY` | `/var/servicehub/data` (Prod) | Directory holding `servicehub-namespaces.json`. Must resolve under the app base or `/home`, `/var`, `/opt`, or the temp dir. |

In the Docker image both default to `/var/servicehub/data`, exposed as a volume.

## ServiceBus (client tuning) — *validated at startup*

| Key | Default | Constraint |
|---|---|---|
| `ServiceBus:ConnectionCacheExpirationMinutes` | `60` | 1–1440 |
| `ServiceBus:MaxConcurrentCalls` | `10` | 1–1000 |
| `ServiceBus:PrefetchCount` | `100` | 0–10000 |
| `ServiceBus:RetryCount` | `3` | 0–20 |
| `ServiceBus:RetryDelayMs` | `1000` | 0–600000 |
| `ServiceBus:MaxRetryDelayMs` | `30000` | ≥ `RetryDelayMs` |

## Rate limiting (production only) — *validated at startup*

| Key | Default | Constraint |
|---|---|---|
| `RateLimit:MaxRequests` | `300` (Prod: `60`) | ≥ 1 |
| `RateLimit:WindowDuration` | `00:01:00` | > 0 |

## Webhooks (DLQ-spike + bulk operation alerts) — *validated at startup*

Fires on two triggers: a DLQ spike (≥ `DlqSpikeThreshold` new messages in one scan cycle, subject
to `CooldownSeconds`) and every completed bulk replay/purge job (no threshold or cooldown — each
job result is worth reporting). Delivery always goes through the same outbound SSRF guard as
other ServiceHub egress: HTTPS-only, and the target host must not resolve to a loopback or
RFC-1918/link-local address.

| Key | Default | Constraint |
|---|---|---|
| `Webhooks:Enabled` | `false` | — |
| `Webhooks:Url` | `""` | Valid absolute http/https URL **when enabled**; must also pass the SSRF guard (HTTPS, non-internal host) to actually deliver |
| `Webhooks:DlqSpikeThreshold` | `10` | ≥ 1 |
| `Webhooks:CooldownSeconds` | `300` | ≥ 0 |
| `Webhooks:Format` | `Generic` | One of `Generic`, `Slack`, `Teams` |
| `Webhooks:PublicUrl` | `null` (unset) | Valid absolute http/https URL **when set** |

`Webhooks:Format` selects the payload shape sent to `Webhooks:Url`:
- **`Generic`** (default) — the original flat JSON body. Existing deployments are unaffected by
  upgrading; nothing changes unless you opt into `Slack` or `Teams`.
- **`Slack`** — Block Kit payload compatible with a Slack [Incoming
  Webhook](https://api.slack.com/messaging/webhooks).
- **`Teams`** — legacy `MessageCard` payload compatible with a Microsoft Teams Incoming Webhook
  connector.

`Webhooks:PublicUrl` is optional and only affects `Slack`/`Teams` payloads. ServiceHub is
self-hosted and has no way to know its own externally-reachable address, so if you set it (e.g.
`https://servicehub.mycompany.com`), Slack/Teams notifications add a deep "Investigate" link/button
back into your ServiceHub instance; without it, notifications are still sent, just without the
link.

## Oidc (BYO-IdP SSO) — *validated at startup*

Provider-neutral per-user authentication: validates `Authorization: Bearer <JWT>` tokens issued
by any standards-compliant OIDC identity provider (Entra ID, Okta, Auth0, Ping, Google Workspace,
...). Unlike `Security:EasyAuth`, which only works behind Azure App Service's built-in auth proxy,
this works identically on Azure, AWS, GCP, Docker Compose, or bare Kestrel — signing keys are
fetched and auto-rotated from the IdP's own discovery document
(`{Authority}/.well-known/openid-configuration`), so there's nothing platform-specific to trust.

| Key | Default | Constraint |
|---|---|---|
| `Security:Oidc:Enabled` | `false` | — |
| `Security:Oidc:Authority` | `""` | Valid absolute **HTTPS** URL **when enabled** — the discovery document and signing keys fetched from it must not be interceptable |
| `Security:Oidc:Audience` | `""` | Required (non-empty) **when enabled** — the client/application ID this instance was registered as with the IdP |
| `Security:Oidc:ClockSkewSeconds` | `120` | ≥ 0 |

A validated token sets `OwnerId` to `oidc:{sub}` — each authenticated human/service gets their own
isolated namespace/DLQ/audit scope, unlike the shared `__spa__` identity every browser session
gets today. By default OIDC-authenticated requests are trusted the same way SPA/EasyAuth sessions
are (full access) — but if the token carries a standard OAuth2 `scope` claim, it's enforced
exactly like a scoped API key's `Scopes`; see [Roles](#api-key--oidc-roles) below and
`self-hosting/security-hardening/README.md` for the full trust-model writeup and setup steps.

This middleware never hard-rejects a request itself: a missing, expired, or invalid Bearer token
simply falls through to the SPA token / API key path, exactly like an untrusted EasyAuth header
does. `ApiKeyAuthenticationMiddleware` owns the final "nothing authenticated this request"
401.

## API key / OIDC roles

A role is a named bundle of scopes (`ServiceHub.Api.Authorization.ApiKeyRoles`) — shorthand so an
operator (or an enterprise IdP's app-registration scope mapping) doesn't have to enumerate
individual scopes by hand. A role name is valid anywhere a scope is accepted: a scoped API key's
`Scopes` array, or an OIDC token's `scope` claim.

| Role | Scopes granted |
|---|---|
| `Viewer` | `namespaces:read`, `queues:read`, `topics:read`, `subscriptions:read`, `messages:peek`, `dlq:read`, `anomalies:read` |
| `Operator` | Viewer scopes + `messages:send`, `dlq:write` |
| `Auditor` | Viewer scopes + `audit:read` |

There is no separate `Admin` role — the existing `admin` scope (or an API key/OIDC token with no
scopes at all) already grants everything; a role bundle for that would just be another name for
the same thing. Role names and literal scopes can be freely mixed in the same list, e.g.
`["Viewer", "audit:read"]`. Expansion happens once, at config-load time for API keys and at token
validation time for OIDC — `ScopeAuthorizationFilter` only ever sees the expanded scope list.

## Telemetry (opt-in, both disabled by default)

| Key | Env var | Default | Notes |
|---|---|---|---|
| `OpenTelemetry:Enabled` | `OPENTELEMETRY__ENABLED` | `false` | Enables OpenTelemetry traces + metrics. |
| `OpenTelemetry:Otlp:Endpoint` | `OPENTELEMETRY__OTLP__ENDPOINT` | — | OTLP collector endpoint (e.g. `http://otel-collector:4317`). Setting this (or the standard `OTEL_EXPORTER_OTLP_ENDPOINT`) also enables OpenTelemetry. |
| `ApplicationInsights:ConnectionString` | `APPLICATIONINSIGHTS__CONNECTIONSTRING` | `""` | Enables Azure Application Insights when set. |

`OpenTelemetry` and `ApplicationInsights` can be used together or independently. Neither ever emits connection strings, message payloads, or user input.

When OpenTelemetry is enabled, ServiceHub also emits domain SLIs under the **`ServiceHub.Operations`** meter (e.g. `servicehub.fleet.overview.requests` and `servicehub.fleet.active_backlog`), alongside the standard ASP.NET Core / HTTP / runtime instrumentation.

## AI / pattern detection

| Key | Default | Notes |
|---|---|---|
| `AI:Enabled` | `false` | ServiceHub performs **no external AI calls**. Message/DLQ pattern detection is heuristic and runs locally (in the browser and in local backend heuristics). This flag gates that local feature only. |

## CORS, security headers, health checks

See `appsettings.json` for `Cors`, `SecurityHeaders`, and `HealthChecks` sections. Defaults are
production-safe (deny-by-default CSP, HSTS, `nosniff`, `frame-ancestors 'none'`). In development
a relaxed CSP is used automatically.

---

## Minimal production environment variables

```bash
ASPNETCORE_ENVIRONMENT=Production
SECURITY__ENCRYPTIONKEY=<32-byte random hex>       # required — app won't start without it
DLQDATABASE__DATADIRECTORY=/var/servicehub/data    # a persistent volume
NAMESPACEREPOSITORY__DATADIRECTORY=/var/servicehub/data
# Optional:
# OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
# CLOUDPROVIDERS__AWS__ENABLED=true
# CLOUDPROVIDERS__GCP__ENABLED=true
```
