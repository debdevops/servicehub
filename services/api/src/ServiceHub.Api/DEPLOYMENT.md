# ServiceHub API — Azure App Service Deployment

## Azure App Service Setup

**Single Linux App Service with .NET 10 runtime** — the React SPA is pre-built into static files and served by the .NET API. No separate Node.js hosting needed.

```bash
# Create resources
az group create --name rg-servicehub --location eastus
az appservice plan create --name plan-servicehub --resource-group rg-servicehub --sku B1 --is-linux
az webapp create --name servicehub --resource-group rg-servicehub --plan plan-servicehub --runtime "DOTNETCORE:10.0"

# Generate and set keys (recommended)
./scripts/setup-azure-env.sh servicehub rg-servicehub
```

## Required Azure App Service Environment Variables

Set these in **Azure Portal → App Service → Configuration → Application Settings**, or use `scripts/setup-azure-env.sh`:

| Key | Description | Example |
|-----|-------------|---------|
| `ASPNETCORE_ENVIRONMENT` | Set to `Production` | `Production` |
| `Security__EncryptionKey` | AES-256 encryption key for stored connection strings. **MUST be set.** Min 32 chars. | *(generate with: `openssl rand -hex 32`)* |
| `Security__SpaToken__Enabled` | Enable SPA token auth for the co-hosted UI | `true` |
| `Security__SpaToken__Secret` | HMAC-SHA256 secret for SPA tokens (hex-encoded) | *(generate with: `openssl rand -hex 32`)* |
| `DlqDatabase__DataDirectory` | SQLite storage path (persists across restarts) | `/home/data` |
| `NamespaceRepository__DataDirectory` | Namespace JSON storage path | `/home/data` |

### Optional: API Key Authentication

API key auth is available for external/programmatic API consumers. The co-hosted SPA uses SPA token authentication instead (enabled by default in production).

| Key | Description |
|-----|-------------|
| `Security__Authentication__Enabled` | Set to `true` to require API keys |
| `Security__Authentication__ScopedApiKeys__0__Key` | Admin API key (all scopes) |
| `Security__Authentication__ScopedApiKeys__1__Key` | Read-only API key |

## Important Notes

- **Never commit actual keys** to source control.
- All keys are managed via **environment variables** (Azure App Service Application Settings).
- ASP.NET Core maps double-underscore env vars to colon-separated config paths: `Security__EncryptionKey` → `Security:EncryptionKey`.
- The `scripts/generate-keys.sh` script generates keys for display or local use.
- The `scripts/setup-azure-env.sh` script generates and sets keys in Azure App Service.

## Local Development

Run `./run.sh` — it automatically generates an encryption key into `appsettings.Local.json` (git-ignored) on first run.

## Deployment Architecture

The React SPA is built into `wwwroot/` inside the API project. A single `dotnet publish` produces both the API and the frontend. See `.github/workflows/deploy.yml` for the CI/CD pipeline.
