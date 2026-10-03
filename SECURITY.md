# Security Policy

**ServiceHub** is a self-hosted, open-source tool for recovering stuck messages in cloud queues (Azure
Service Bus, AWS SQS/SNS, GCP Pub/Sub) and proving what was done. This policy covers how to report a
vulnerability, which versions are supported, what automated scanning runs on this repository, and
ServiceHub's threat model.

## Reporting a Vulnerability

Please **do not** file a public GitHub issue for security vulnerabilities.

Report security issues privately via GitHub Security Advisories:
1. Go to the Security tab of this repository
2. Click "Report a vulnerability"
3. Fill in the details

We aim to respond within 48 hours.

## Supported versions

| Version | Status |
|---|---|
| **4.1.x** | Current. Security reports are handled against this line. |
| 4.0.x | Frozen in `archive/servicehub-4.0.0/` and still published from `v4.0.x` tags. It receives no new work, and **there is no upgrade path from 4.0.0 to 4.1.0** (see the [changelog](CHANGELOG.md)). |
| 3.x and older | Not supported. |

## Security Scanning

This repository uses the following automated security tools:

| Tool | What it checks | When it runs |
|------|---------------|--------------|
| **CodeQL** | C# and TypeScript source code (SAST) | Every push, weekly full scan |
| **Dependabot security alerts** | Known-vulnerable NuGet and npm dependencies, from the repository's manifests (a repository setting). Automated version-update pull requests are switched off in `.github/dependabot.yml` | Continuously |
| **Secret Scanning** | Accidentally committed credentials | Every push (real-time) |
| **npm audit** | npm production packages (fails on High/Critical) | Every 4.1.0 CI run |
| **NuGet audit** | NuGet packages, including transitive (fails on High/Critical) | Every 4.1.0 CI run |

## Enabling Secret Scanning (repository owners)

In GitHub → Settings → Security → Secret scanning:
- ✅ Enable Secret scanning
- ✅ Enable Push protection (blocks commits containing detected secrets)

## Known Non-Issues

The following value in the codebase is an intentional placeholder, not a real secret:

- `services/api/src/ServiceHub.Api/appsettings.Development.json`: `"EncryptionKey": "DEV_KEY_NOT_FOR_PRODUCTION_…"` — the throw-away
  key `./run.sh` uses in Development. A Production start with no key of your own **refuses to run** (it never falls back to this one), and no
  API keys are committed.

Real production secrets should be stored in your environment's secret manager
(e.g., environment variables, Azure Key Vault, AWS Secrets Manager, GCP Secret Manager,
or a `.env` file with restricted permissions), never in source code.

## Dependencies

This project uses:
- **Azure.Messaging.ServiceBus** — official Microsoft SDK
- **Azure.Identity** — official Microsoft authentication SDK
- **AWSSDK.SQS / AWSSDK.SimpleNotificationService / AWSSDK.SecurityToken** — official AWS SDKs
- **Google.Cloud.PubSub.V1 / Google.Apis.Auth** — official Google Cloud SDKs
- **Microsoft.EntityFrameworkCore.Sqlite** — SQLite for local persistence

Known-vulnerable dependencies raise Dependabot security alerts and fail the CI audit steps above.

## Security Fixes History

Fixes in releases before 4.1.0. 4.1.0 is a from-scratch rewrite: the files named below are in the frozen 4.0.0 tree or are gone, and the fixes
they describe were designed into the new code rather than ported.

| Version | Date | Description |
|---------|------|-------------|
| v2.1.2 | 2026-03-23 | Fixed CodeQL `cs/log-forging` in `ServiceBusClientWrapper.cs` — 65 taint paths sanitised with `LogRedactor.SanitiseForLog()` |
| v2.1.3 | 2026-03-23 | Removed duplicate `LogSanitizer` classes; all callers consolidated to single `LogRedactor.SanitiseForLog()` |
| v3.2.2 | 2026-06-13 | Fixed 6 CodeQL `cs/log-forging` alerts (Medium) in `AwsMessageSender.cs` (#143–#146) and `GcpClientFactory.cs` (#147–#148) — user-derived entity names, topic/subscription IDs, and project IDs now sanitised before logging |
| v3.4.0 | 2026-07-07 | Fixed cross-owner IDOR in DLQ Intelligence (`GetByIdAsync`/`GetTimelineAsync`/`UpdateNotesAsync`/`GetSummaryAsync` now require and filter on `ownerId`); fixed rate-limit bypass behind reverse proxies (keys on authenticated owner, not just remote IP); hardened `AllowedHosts` in production config (was `"*"`); removed backend Simulator (client-side Demo Mode remains). See CHANGELOG.md for full details. |

## Threat model and non-goals

ServiceHub defends against these scenarios:

- **Accidental log leakage:** connection strings and secrets are masked from console output and
  telemetry
- **Delegated access going too far:** API keys, OIDC and Azure Easy Auth identify *who* acted, and roles granted
  in Settings (Viewer, Operator, Admin) limit what a key holder may do — a Viewer key is refused a backup (`403`),
  and a wrong key is refused (`401`)

ServiceHub does NOT defend against:

- **An unauthenticated caller who can reach its port.** A request with no API key is the browser session — the server's
  *owner*, an Administrator — and granted roles limit key holders, not that session. (Verified 2026-09-29: with an API key
  configured and a Viewer grant made, a request with no key could still take a backup.) Keep ServiceHub on localhost, on a
  private network, or behind an authenticating reverse proxy; the Docker Compose file binds `127.0.0.1` for this reason

- **Malicious administrators:** a person with access to the self-hosted instance can read
  connection strings, modify routing rules, or export message bodies
- **Network eavesdropping:** deploy ServiceHub behind HTTPS and keep the encryption key secure;
  an attacker on the wire can see plaintext request/response bodies
- **Multi-tenant SaaS isolation:** ServiceHub is single-instance, single-team only; everyone
  who can reach one instance is assumed to be on the same team
- **Compromise of the host:** if the server is compromised, the encryption key is at risk; rotate
  the key immediately if the server is breached (see below)

## Key rotation and credential backup

**Encryption key rotation is supported** via a multi-key registry
(`Security:EncryptionKeyRegistry`) — see
[`docs/ENCRYPTION-KEY-ROTATION.md`](docs/ENCRYPTION-KEY-ROTATION.md) for the full configuration and
rotation procedure, including the compromise-response workflow. A single-key deployment (the
default, `Security:EncryptionKey` with no registry configured) does not need to do anything; only
rotating the plain `Security:EncryptionKey` value directly, without configuring the registry first,
renders existing stored connection strings unreadable — always migrate to the registry (which
carries your existing key forward as `legacy-v1`) before introducing a new key.

Treat every encryption key as a critical secret: back it up securely and store it in a secrets
manager (Azure Key Vault, Hashicorp Vault, etc.) outside the deployment host. There is no automated bulk re-encryption after a suspected key compromise — see
[`docs/ENCRYPTION-KEY-ROTATION.md` §5](docs/ENCRYPTION-KEY-ROTATION.md#5-if-the-key-may-have-leaked)
for the current, operator-driven remediation path.
