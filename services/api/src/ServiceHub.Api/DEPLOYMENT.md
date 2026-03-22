# ServiceHub API — Azure App Service Deployment

## Required Azure App Service Environment Variables

Set these in **Azure Portal → App Service → Configuration → Application Settings**:

| Key | Description | Example |
|-----|-------------|---------|
| `Security__EncryptionKey` | AES-256 encryption key for stored connection strings. **MUST be set.** Min 32 chars. | *(generate with: `openssl rand -base64 32`)* |
| `ASPNETCORE_ENVIRONMENT` | Set to `Production` | `Production` |
| `DlqDatabase__DataDirectory` | SQLite storage path | `/home/data` |
| `NamespaceRepository__DataDirectory` | Namespace JSON storage path | `/home/data` |

## Important Notes

- **Never commit the actual encryption key** to source control or `appsettings.*.json`.
- The `appsettings.Production.json` file contains an empty `EncryptionKey` placeholder. The real value **must** be provided via the environment variable `Security__EncryptionKey`.
- ASP.NET Core's configuration system automatically maps `Security__EncryptionKey` (env var) to `Security:EncryptionKey` (JSON path).
- API key authentication is disabled by default (`Authentication.Enabled = false`). To enable it, set `Security__Authentication__Enabled=true` and add keys via `Security__Authentication__ApiKeys__0=your-api-key`.

## Generating an Encryption Key

```bash
# On macOS/Linux:
openssl rand -base64 32

# On Windows (PowerShell):
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]])
```

## Deployment Architecture

The React SPA is built into `wwwroot/` inside the API project. A single `dotnet publish` produces both the API and the frontend. See `.github/workflows/deploy.yml` for the full CI/CD pipeline.
