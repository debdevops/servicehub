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

- `appsettings.Development.json`: `"dev-api-key-12345"` and `"test-api-key-67890"`
  — these are local development-only values, never used in production
- `appsettings.json`: `"CHANGE_THIS_IN_PRODUCTION_USE_AZURE_KEY_VAULT_OR_ENV_VAR"`
  — this is an explicit placeholder, not a real key

Real production secrets are stored in Azure Key Vault and Azure App Service
Application Settings, never in source code.

## Dependencies

This project uses:
- **Azure.Messaging.ServiceBus** — official Microsoft SDK
- **Azure.Identity** — official Microsoft authentication SDK
- **Microsoft.EntityFrameworkCore.Sqlite** — SQLite for local persistence

Dependency vulnerabilities are monitored daily via Dependabot.
