# Security Policy

## Reporting a Vulnerability

Please **do not** file a public GitHub issue for security vulnerabilities.

Report security issues privately via GitHub Security Advisories:
1. Go to the Security tab of this repository
2. Click "Report a vulnerability"
3. Fill in the details

We aim to respond within 48 hours.

## Security Scanning

This repository uses the following automated security tools:

| Tool | What it checks | When it runs |
|------|---------------|--------------|
| **CodeQL** | C# and TypeScript source code (SAST) | Every push, weekly full scan |
| **Dependabot** | NuGet and npm dependency vulnerabilities | Daily |
| **Secret Scanning** | Accidentally committed credentials | Every push (real-time) |
| **npm audit** | npm production packages | Every CI run |

## Enabling Secret Scanning (repository owners)

In GitHub → Settings → Security → Secret scanning:
- ✅ Enable Secret scanning
- ✅ Enable Push protection (blocks commits containing detected secrets)

## Known Non-Issues

The following values in the codebase are intentional placeholders, not real secrets:

- `appsettings.Development.json`: `ApiKeys` / `ScopedApiKeys` ship as empty arrays
  — no default development API keys are committed
- `appsettings.json`: `"EncryptionKey": "CHANGE_THIS_IN_PRODUCTION_USE_ENV_VAR"`
  — this is an explicit placeholder, not a real key

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

Dependency vulnerabilities are monitored daily via Dependabot.

## Security Fixes History

| Version | Date | Description |
|---------|------|-------------|
| v2.1.2 | 2026-03-23 | Fixed CodeQL `cs/log-forging` in `ServiceBusClientWrapper.cs` — 65 taint paths sanitised with `LogRedactor.SanitiseForLog()` |
| v2.1.3 | 2026-03-23 | Removed duplicate `LogSanitizer` classes; all callers consolidated to single `LogRedactor.SanitiseForLog()` |
| v3.2.2 | 2026-06-13 | Fixed 6 CodeQL `cs/log-forging` alerts (Medium) in `AwsMessageSender.cs` (#143–#146) and `GcpClientFactory.cs` (#147–#148) — user-derived entity names, topic/subscription IDs, and project IDs now sanitised before logging |
| Unreleased | 2026-07-07 | Fixed cross-owner IDOR in DLQ Intelligence (`GetByIdAsync`/`GetTimelineAsync`/`UpdateNotesAsync`/`GetSummaryAsync` now require and filter on `ownerId`); fixed rate-limit bypass behind reverse proxies (keys on authenticated owner, not just remote IP); hardened `AllowedHosts` in production config (was `"*"`); added `SimulatorOnlyAttribute` as defense-in-depth for Simulator-only endpoints. See CHANGELOG.md for full details. |
