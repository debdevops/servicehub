# Self-Hosting ServiceHub

> 🛡️ **You are in full control.**
> When you self-host ServiceHub, your connection strings, Service Bus data, and messages
> never leave your own Azure subscription or machine. There are no callbacks to ServiceHub's
> servers. The encryption key is yours — we never see it.

---

## Prerequisites

Before you start, confirm the following are installed on your machine.

| Prerequisite | Minimum version | How to check |
|---|---|---|
| Azure subscription | Any paid or free tier | `az account show` |
| Azure CLI | Latest | `az --version` |
| .NET SDK | 10.0 | `dotnet --version` |
| Node.js | 20.x | `node --version` |
| Git | Any | `git --version` |

---

## Choose your deployment path

### 🌐 Deploy to Azure App Service

**Recommended for production.** Your data stays in your Azure subscription.

→ [Deploy to Azure App Service](./azure-app-service/README.md)

What this gives you:
- HTTPS out of the box via `*.azurewebsites.net`
- Persistent storage at `/home/data` — data survives restarts
- ~$13/month on the B1 plan (Basic, 1 core, 1.75 GB RAM)
- Zero configuration for SSL certificates

---

### 💻 Run locally on your machine

**For testing and development.** Zero cloud dependencies.

→ [Run locally](./local-development/README.md)

What this gives you:
- Instant feedback loop — changes hot-reload immediately
- No Azure costs
- Full debug tooling
- Works on macOS, Linux, and Windows (via WSL)

---

## After deployment

Once your instance is running, continue with:

| Guide | When to use |
|---|---|
| [Application Insights](./application-insights/README.md) | Optional. Monitor performance in your own Azure subscription. Zero data goes to ServiceHub's servers. |
| [Security Hardening](./security-hardening/README.md) | **Required before production.** Generate proper secrets, complete the pre-launch checklist. |
| [Troubleshooting](./troubleshooting/README.md) | When something isn't working. Covers the 8 most common errors with exact fixes. |

---

## Architecture in one sentence

ServiceHub is a **single deployable unit**: the React UI is compiled into the .NET API's `wwwroot/` folder, and both are served from one Azure App Service. You do not run a separate frontend server in production.

```
Your browser
    │  HTTPS
    ▼
Azure App Service (one app, one URL)
    ├── .NET 10 API  →  Azure Service Bus (yours)
    └── React UI     →  served as static files from wwwroot/
```

Your Azure Service Bus connection strings are **AES-256-GCM encrypted** using a key you generate. The plaintext key never leaves your App Service configuration.

---

## Quick start: generate your secret keys

Before deploying to Azure, generate the four secrets you will need. Run this from the repository root:

```bash
./scripts/generate-keys.sh
```

Save the output securely in a password manager before proceeding. You will need these values in the Application Settings step.
