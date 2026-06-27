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

### 💻 Run locally on your machine

**Fastest option — zero cloud dependencies.**

→ [Run locally](./local-development/README.md)

What this gives you:
- Instant feedback loop — changes hot-reload immediately
- No cloud costs
- Full debug tooling
- Works on macOS, Linux, and Windows (via WSL)

---

### 🌐 Deploy to a server or cloud VM

**Recommended for shared team use.** Deploy on any Linux VM, Azure App Service, AWS EC2, GCP Compute Engine, or any server with .NET 10 support.

What this gives you:
- Always-on access for your team
- HTTPS via your own reverse proxy (nginx/Caddy)
- Persistent storage — data survives restarts
- Full control of your infrastructure

→ [Azure App Service](./azure-app-service/README.md)

---

## After deployment

Once your instance is running, continue with:

| Guide | When to use |
|---|---|
| [Application Insights](./application-insights/README.md) | Optional. Monitor performance in your own Azure subscription. |
| [Security Hardening](./security-hardening/README.md) | **Required before production.** Generate proper secrets, complete the pre-launch checklist. |
| [Troubleshooting](./troubleshooting/README.md) | When something isn't working. Covers the 8 most common errors with exact fixes. |

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


---

## Quick start: generate your secret keys

Before deploying to Azure, generate the four secrets you will need. Run this from the repository root:

```bash
./scripts/generate-keys.sh
```

Save the output securely in a password manager before proceeding. You will need these values in the Application Settings step.
