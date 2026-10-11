# Host ServiceHub on Azure App Service — and whether you should

> **Short answer:** it works, but it is the **weakest** of the Azure options for ServiceHub. Use the [VM guide](HOSTING-AZURE-CLI.md) unless you have a reason not to.
> If you do go ahead, follow the **hardened** setup below, not the quick one.

## Why it is riskier than a VM

| Issue | Why it matters for ServiceHub |
|---|---|
| **Public by default** | App Service gets a public `*.azurewebsites.net` address. ServiceHub has **no login**: whoever reaches it is an **Administrator** who can read your stored cloud connections' targets and replay or purge messages. A VM keeps the port closed; App Service does not, unless you add access control. |
| **SQLite on network storage** | App Service persistent storage (`/home`, Azure Files) is a network share. ServiceHub's database runs in **WAL mode**, which SQLite documents as unsafe on network filesystems (corruption or "database is locked"). A VM uses a local disk. |
| **Single instance only** | ServiceHub exits if a second copy touches the same data. App Service **scale-out**, **deployment-slot swaps** and some platform restarts briefly run two instances. |
| **Host-name check** | ServiceHub refuses any `Host` it was not told about. You must set `AllowedHosts` to your app's name. |

So: App Service is acceptable for a **short evaluation** or a **demo on made-up data**, and with extra steps for real use. It is not the right home for the production, always-on ServiceHub that holds real connection strings.

## Recommendation

1. **Production / real connection strings → use the VM** ([CLI](HOSTING-AZURE-CLI.md) or [Portal](HOSTING-AZURE-PORTAL.md)).
2. **You must use a managed service → App Service with the hardened setup below** (Entra sign-in in front, one instance, accept the storage caveat, back up daily).
3. **Demo only → the quick setup is fine**, with sample data and no cloud connected.

## Setup (CLI)

```bash
RG=rg-servicehub-app
LOC=westeurope
PLAN=plan-servicehub
APP=servicehub-<something-unique>          # becomes https://$APP.azurewebsites.net
KEY=$(openssl rand -hex 32); echo "$KEY"   # keep a copy in your password manager

az group create -n $RG -l $LOC
az appservice plan create -g $RG -n $PLAN --is-linux --sku B1

az webapp create -g $RG -p $PLAN -n $APP \
  --container-image-name ghcr.io/debdevops/servicehub:4.2.0

az webapp config appsettings set -g $RG -n $APP --settings \
  WEBSITES_PORT=8080 \
  WEBSITES_ENABLE_APP_SERVICE_STORAGE=true \
  ServiceHub__DataDirectory=/home/servicehub-data \
  ASPNETCORE_ENVIRONMENT=Production \
  SECURITY__ENCRYPTIONKEY="$KEY" \
  AllowedHosts="$APP.azurewebsites.net" \
  Backup__ScheduledBackupIntervalHours=24

# One instance, never more
az appservice plan update -g $RG -n $PLAN --number-of-workers 1
az webapp config set -g $RG -n $APP --always-on true --generic-configurations '{"healthCheckPath":"/health"}'
az webapp update -g $RG -n $APP --https-only true
```

Do **not** add deployment slots and do **not** enable autoscale.

> **Portal equivalent:** *App Services → Create → Web App → Publish: Container, OS: Linux* → Container tab: image source **Other container registries**, image `ghcr.io/debdevops/servicehub:4.2.0`, port `8080`. Then **Settings → Environment variables** for the settings above.

## Hardened: put a login in front (do this before real use)

Pick one. The first is the easiest.

**A. Entra ID sign-in (built-in "Easy Auth")**

Portal: App → **Settings → Authentication → Add identity provider → Microsoft**, *Require authentication*, *HTTP 302 redirect*. Restrict to your tenant, and in the Entra app registration set **Assignment required = Yes** and assign only the people who should be Administrators.

```bash
az webapp auth microsoft update -g $RG -n $APP --client-id <app-reg-id> --issuer https://login.microsoftonline.com/<tenant-id>/v2.0 --yes
az webapp auth update -g $RG -n $APP --enabled true --action RedirectToLoginPage
```

Keep the `/health` path reachable for the platform probe by excluding it in **Authentication → Edit → Excluded paths** (`/health`).

**B. Network lock-down (no sign-in, address-based)**

Allow only your office/VPN egress range:

```bash
az webapp config access-restriction add -g $RG -n $APP --rule-name office \
  --action Allow --ip-address <your-cidr> --priority 100
```

Everything else is denied once an Allow rule exists. Better still: a **Private Endpoint** with no public access, reached over VPN.

## Open it and connect

Browse to `https://<app>.azurewebsites.net` (sign in if you set up A). **Add a cloud → Azure** and paste a Service Bus connection string (Shared access policy with **Manage**; see [clouds/azure.md](clouds/azure.md)). If the namespace has a firewall, allow the app's **outbound IP addresses** (`az webapp show -g $RG -n $APP --query possibleOutboundIpAddresses`).

## Back up (not optional here)

Because of the storage caveat, treat the data as recoverable, not safe: keep the daily ServiceHub backup on (set above) **and** copy it off the share regularly (see [BACKUP-RESTORE.md](BACKUP-RESTORE.md)). Check that `/home/servicehub-data` shows a fresh backup every day.

## Upgrade, remove

- **Upgrade:** `az webapp config container set -g $RG -n $APP --container-image-name ghcr.io/debdevops/servicehub:<new-tag>`, then check `/health`.
- **Remove:** `az group delete -n $RG --yes --no-wait`

## Troubleshooting

| You see | Cause | Fix |
|---|---|---|
| `400 This address is not one ServiceHub answers to` | `AllowedHosts` does not match the URL | Set it to the exact host name you browse to; with a custom domain use `host1;host2` |
| App restarts, then exits | Two instances on one database | Plan workers = 1, no slots, no autoscale |
| `database is locked` or odd errors | SQLite on a network share | Restore the latest backup; move to the VM guide |
| Container does not start | Wrong port | `WEBSITES_PORT=8080` |
| Starts then says it needs a key | `SECURITY__ENCRYPTIONKEY` missing | Add the app setting (64 hex characters) |
