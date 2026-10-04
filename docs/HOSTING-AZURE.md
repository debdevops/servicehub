# Host ServiceHub on Azure

> **In this article:** run ServiceHub on Azure so it is always on and keeps its data across restarts — on a virtual machine, or on App Service with or
> without a container. About 30 minutes.
>
> **Three ways, one page:** a virtual machine (recommended), App Service with a container, or App Service with plain .NET code — see §1 to choose.
>
> **In plain language (VM option):** you rent one small Linux computer in Azure, put ServiceHub on it in a Docker container, and reach it through a secure
> tunnel from your own laptop. Nobody else can open it.

If you only want to try ServiceHub, use the [local setup](LOCAL-SETUP.md) instead — it needs no Azure account.

> **Prefer one command?** [`infra/`](../infra/README.md) does the virtual-machine option below for you — on Azure, AWS or
> Google Cloud — and can remove it again. This page remains the step-by-step version, and covers App Service.

---

## 1. Choose how to host it

ServiceHub is **one process with one SQLite file**, and the README asks for that file on **local block storage** (not a network share). A second
instance on the same data folder exits on purpose. That shapes the choice:

| Azure option | Data lives on | Verdict |
|---|---|---|
| **A. Virtual machine + Docker** (§3–§8) | The VM's managed disk — local block storage | **Recommended.** Matches ServiceHub's design exactly |
| **B. App Service, container** (§9) | `/home`, which App Service backs with Azure Storage over the network | Works as a managed, no-server option, with the caveats in §9.1 |
| **C. App Service, code (no container)** (§10) | The same `/home` | Same caveats as B; you need no Docker or registry |
| Container Apps, AKS | Azure Files or similar | Not covered: they add scaling and rescheduling you would then have to switch off |

**Which one?** Pick **A** if the data matters and you are comfortable with a Linux VM. Pick **B or C** if your organisation wants a managed platform with
no server to patch — and accept §9.1. Between B and C: **C** is simpler (no registry); **B** gives you exactly the image that was tested.

Everything uses only standard Azure and Docker/.NET tooling; nothing ServiceHub-specific is installed in Azure.

---

## 2. Before you start

| You need | Notes |
|---|---|
| An Azure subscription where you may create a resource group and a VM | Contributor on the subscription or a resource group is enough |
| The **Azure CLI** (`az`), signed in | <https://learn.microsoft.com/cli/azure/install-azure-cli> · `az login` |
| An SSH key | `--generate-ssh-keys` below makes one if you have none |
| The Azure Service Bus namespace you want to watch | Not created here — ServiceHub only *connects* to it. See the [Azure guide](clouds/azure.md) |

> **Cost.** You pay for the VM, its disk and its public IP while they exist. Prices change by region — check the
> [Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/). ServiceHub itself adds no charge, and it keeps nothing in Azure except
> what is on this VM.

---

## 3. Option A — create the VM

Choose a name and a region close to your Service Bus namespace:

```bash
RG=rg-servicehub
LOCATION=<your-region>          # e.g. westeurope — see: az account list-locations -o table
VM=vm-servicehub

az group create --name "$RG" --location "$LOCATION"

az vm create \
  --resource-group "$RG" --name "$VM" \
  --image Canonical:ubuntu-24_04-lts:server:latest \
  --size Standard_B2s \
  --os-disk-size-gb 64 \
  --admin-username azureuser --generate-ssh-keys \
  --public-ip-sku Standard \
  --nsg-rule SSH
```

`Standard_B2s` (2 CPU, 4 GB) is comfortable for one team. Note the **`publicIpAddress`** in the output — a Standard-SKU address is static, so it
will not change when the VM restarts.

### Lock the door

By default SSH is open to the whole internet. Allow only your own address, and **do not open any other port** — ServiceHub is reached through an SSH
tunnel (§6), so no web port needs to be reachable at all.

```bash
MYIP="$(curl -s https://api.ipify.org)"
az network nsg rule update \
  --resource-group "$RG" --nsg-name "${VM}NSG" --name default-allow-ssh \
  --source-address-prefixes "$MYIP"
```

If your address changes, repeat this. (If your organisation uses Azure Bastion or a VPN, use that instead and delete the public IP.)

---

## 4. Install Docker and ServiceHub

Connect, then install the tools on the VM:

```bash
ssh azureuser@<publicIpAddress>

sudo apt-get update
sudo apt-get install -y docker.io docker-compose-v2 git
sudo systemctl enable --now docker          # Docker starts on every boot
```

Get ServiceHub. The simplest route is to build it on the VM (Docker does the .NET and Node work — you install neither). A version tag also publishes an image to `ghcr.io/debdevops/servicehub` (the package must be made public in GitHub → Packages after its first publish), but `docker-compose.yml` builds from source, and that is what this guide uses:

```bash
git clone https://github.com/debdevops/servicehub.git
cd servicehub
git checkout <the tag or branch you want to run>
```

### Make the encryption key — once

ServiceHub encrypts every cloud credential it stores, and **refuses to start in production without a key**. Make one and keep it:

```bash
umask 077
echo "SERVICEHUB_ENCRYPTION_KEY=$(openssl rand -hex 32)" > .env
```

> [!IMPORTANT]
> **Copy that key somewhere safe now** (a password manager, or a secret store such as Azure Key Vault): `cat .env`. If the VM is lost and the key
> with it, every stored credential is unreadable and you reconnect each cloud. Never put the key in the repository (`.env` is git-ignored) and never
> change it on its own — to rotate, follow [Encryption key rotation](ENCRYPTION-KEY-ROTATION.md).

### Start it

```bash
sudo docker compose up -d --build
```

The first build takes several minutes. `docker-compose.yml` already does the safe things for you: production mode, data in a named volume at `/data`,
the port bound to the VM's own loopback (`127.0.0.1:8080`) so it cannot be reached from outside, and `restart: unless-stopped` so it comes back after a
crash or a reboot.

Check it:

```bash
curl http://localhost:8080/health/ready        # → Healthy
sudo docker compose ps                          # → the container is "healthy" after about a minute
```

---

## 5. Let it reach your Service Bus

The VM connects **outward** to `<namespace>.servicebus.windows.net` on **port 5671 (AMQP over TLS)**. Azure allows outbound traffic by default, so
normally there is nothing to do.

If the namespace has a **firewall** ("Networking → Selected networks"), allow the VM's public IP address there, or connect them privately with a
private endpoint. Without that, ServiceHub will report the namespace as unreachable.

---

## 6. Open it from your laptop

On your own computer, open a tunnel and leave it running:

```bash
ssh -N -L 8080:127.0.0.1:8080 azureuser@<publicIpAddress>
```

Then browse to <http://localhost:8080>. Everything between you and ServiceHub is inside the encrypted SSH connection.

From here, **Add a cloud** and follow the [Azure guide](clouds/azure.md) (steps 2 onward — you already have ServiceHub running).

### More than one person?

ServiceHub does not log the browser in: **whoever can reach its port is the owner, an Administrator.** An SSH tunnel therefore makes every person with
SSH access an Administrator. To give people different powers you need something that identifies them in front of ServiceHub:

- **An authenticating reverse proxy** (for example one that signs people in with Microsoft Entra ID) on the VM or in front of it. If, and only if, that
  proxy strips any incoming `X-MS-CLIENT-PRINCIPAL-ID` header and sets it itself, enable `Security__EasyAuth__TrustClientPrincipalHeader=true`. Never
  enable it otherwise: anyone could then pretend to be anyone.
- **A name of your own.** If people reach ServiceHub through a proxy or a host name rather than `localhost`, set `AllowedHosts` to that name (`;`-separated, `*.example.com` wildcards) in `docker-compose.yml`'s `environment:`. It is refused otherwise, deliberately: see SECURITY.md.
- **OIDC bearer tokens** (`Security__Oidc__Enabled`, `Security__Oidc__Authority`, `Security__Oidc__Audience`) for automation and API users.
- **API keys** (`Security:Authentication:ApiKeys`, sent as `X-API-KEY`).

Roles (Viewer, Operator, Admin) are then granted in Settings. The README's "Who is asking" section and [SECURITY.md](../SECURITY.md) are the reference;
set these as environment variables in `docker-compose.yml`, then `docker compose up -d`. Put HTTPS in front of any of this before exposing a port.

---

## 7. Keep it healthy

| Task | How |
|---|---|
| **See logs** | `sudo docker compose logs -f` |
| **Update** | `cd servicehub && git pull && sudo docker compose up -d --build` — the data volume and `.env` are untouched |
| **Back up ServiceHub's data** | Settings → Backup → *Take a backup now* ([Backup & restore](BACKUP-RESTORE.md)). Backups land on the same disk, so also snapshot the disk or copy them off the VM |
| **Snapshot the disk** | `az snapshot create` on the VM's OS disk, or enable Azure Backup for the VM |
| **Save money when idle** | `az vm deallocate -g "$RG" -n "$VM"` stops the compute charge (the disk is still billed). `az vm start …` brings it back and ServiceHub restarts by itself with its data |
| **Restart ServiceHub only** | `sudo docker compose restart` |
| **Never** | Run a second ServiceHub against the same volume, copy the database to another machine while it runs, or delete the volume to "reset" something you want to keep |

---

## 8. Remove everything

```bash
az group delete --name "$RG" --yes --no-wait
```

This deletes the VM, its disk (and so ServiceHub's data), the IP and the network. Take a backup first if you want to keep anything.

---

## 9. App Service with a container (option B)

### 9.1 Read this first

App Service has a persistent folder, `/home`, that survives restarts and redeploys. It is backed by Azure Storage **over the network**. ServiceHub's
SQLite file and its single-instance lock would live there. That is the one thing ServiceHub's README warns against, so treat B and C as follows:

- **Run exactly one instance, always.** One worker, no scale-out, no autoscale, no deployment slots. A restart can briefly overlap the old and new
  process; if you see *"Another ServiceHub instance already holds the data directory"*, wait a minute and restart the app once.
- **Take backups and copy them off the app** (§11). The file is the only copy of your history and ledger.
- **Try it before relying on it.** After the first start, connect a cloud, restart the app, and confirm it is still there.
- **App Service is on the public internet by default and ServiceHub does not log browsers in.** Lock it down (§9.4) *before* connecting a cloud.

### 9.2 Build the image into a registry

Build the image in Azure Container Registry straight from the source (no Docker needed on your machine; a published `ghcr.io/debdevops/servicehub` image also exists for version tags, but this builds the exact tag or branch you check out):

```bash
RG=rg-servicehub
LOCATION=<your-region>
PLAN=plan-servicehub
APP=<globally-unique-app-name>          # becomes https://<APP>.azurewebsites.net
ACR=<globally-unique-registry-name>     # letters and digits only

az group create --name "$RG" --location "$LOCATION"
az acr create --resource-group "$RG" --name "$ACR" --sku Basic

git clone https://github.com/debdevops/servicehub.git && cd servicehub
git checkout <the tag or branch you want to run>
az acr build --registry "$ACR" --image servicehub:4.1.0 .
```

### 9.3 Create the app

```bash
# Linux plan, one worker. B1 or larger — Free/Shared cannot keep an app always on.
az appservice plan create --resource-group "$RG" --name "$PLAN" --is-linux --sku B1 --number-of-workers 1

az webapp create --resource-group "$RG" --plan "$PLAN" --name "$APP" \
  --container-image-name "$ACR.azurecr.io/servicehub:4.1.0"

# Let the app pull from the registry with its own identity (no passwords).
PRINCIPAL="$(az webapp identity assign --resource-group "$RG" --name "$APP" --query principalId -o tsv)"
az role assignment create --assignee "$PRINCIPAL" --role AcrPull --scope "$(az acr show --name "$ACR" --query id -o tsv)"
az webapp config set --resource-group "$RG" --name "$APP" --always-on true \
  --generic-configurations '{"acrUseManagedIdentityCreds": true}'
```

Now the settings. Make the encryption key **once** and keep it (see the key warning in §4 — it applies here too):

```bash
KEY="$(openssl rand -hex 32)"; echo "SAVE THIS KEY: $KEY"

az webapp config appsettings set --resource-group "$RG" --name "$APP" --settings \
  ASPNETCORE_ENVIRONMENT=Production \
  WEBSITES_PORT=8080 \
  WEBSITES_ENABLE_APP_SERVICE_STORAGE=true \
  ServiceHub__DataDirectory=/home/data \
  AllowedHosts="$APP.azurewebsites.net" \
  SECURITY__ENCRYPTIONKEY="$KEY"
```

| Setting | Why |
|---|---|
| `WEBSITES_PORT=8080` | The image listens on 8080; App Service must be told |
| `WEBSITES_ENABLE_APP_SERVICE_STORAGE=true` | Mounts the persistent `/home` |
| `ServiceHub__DataDirectory=/home/data` | Puts the database there instead of the container's throw-away `/data` |
| `AllowedHosts` | ServiceHub answers only to `localhost` unless told otherwise (so a web page on another site cannot drive it through a browser). App Service serves it as `<app>.azurewebsites.net`, so name that here — without it every page and API call is refused with *"This address is not one ServiceHub answers to"*. Add a custom domain with a `;`: `a.azurewebsites.net;servicehub.contoso.com`. The health endpoints answer on any host, so App Service's own probes still work |
| `SECURITY__ENCRYPTIONKEY` | Production refuses to start without it. App settings are encrypted at rest and visible only to people with access to the app |

Give it a minute, then check: `curl https://$APP.azurewebsites.net/health/ready` → `Healthy`. If it does not start, `az webapp log tail -g "$RG" -n "$APP"`.
The image runs as a non-root user; if the log says it cannot write `/home/data`, App Service's mount permissions are not compatible with it — use
option A or C instead.

### 9.4 Lock it down — required

Anyone who can reach the URL is the server's Administrator. Do **at least one** of these before you connect a cloud:

**Allow only your own address** (simple; also add the same rule for the deployment site):

```bash
MYIP="$(curl -s https://api.ipify.org)"
az webapp config access-restriction add --resource-group "$RG" --name "$APP" \
  --rule-name me --action Allow --ip-address "$MYIP/32" --priority 100
az webapp config access-restriction add --resource-group "$RG" --name "$APP" \
  --rule-name me-scm --action Allow --ip-address "$MYIP/32" --priority 100 --scm-site true
```

Adding an *Allow* rule makes everything else denied. Use a company VPN/egress range instead of one address if you have one.

**Or require Microsoft Entra sign-in** (Azure portal → your app → **Authentication** → *Add identity provider* → Microsoft → *Require authentication*,
unauthenticated requests: *HTTP 302 redirect*). Then in Entra → **Enterprise applications** → your app → **Properties** set *Assignment required* to
Yes and assign only the people who should have access. Understand what this does: it **gates** the site — only assigned people get in — and
ServiceHub then names them in the audit trail. It does not make them less than Administrators: **everyone you assign has full control.** For finer
roles see "More than one person?" in §6.

Always use the `https://` address; `az webapp update --https-only true` enforces it.

### 9.5 Update

```bash
az acr build --registry "$ACR" --image servicehub:4.1.1 .
az webapp config container set --resource-group "$RG" --name "$APP" \
  --container-image-name "$ACR.azurecr.io/servicehub:4.1.1"
```

Your settings and `/home/data` are untouched. Then open the app and confirm your clouds are still connected.

---

## 10. App Service without a container (option C)

Same platform, same §9.1 caveats, but App Service runs the .NET app directly — you publish a folder and upload a zip. You build on your own computer,
which needs the **.NET 10 SDK** and **Node 22** ([local setup](LOCAL-SETUP.md) §1).

### 10.1 Check that App Service offers .NET 10

```bash
az webapp list-runtimes --os linux | grep -i dotnet
```

You need an entry like `DOTNETCORE:10.0` (or `DOTNET|10.0`). If there is none in your region yet, use option B or A.

### 10.2 Build and package

```bash
git clone https://github.com/debdevops/servicehub.git && cd servicehub
git checkout <the tag or branch you want to run>

npm ci
npm run build                                   # builds the web app into services/api/src/ServiceHub.Api/wwwroot
dotnet publish services/api/src/ServiceHub.Api -c Release -o ./publish /p:UseAppHost=false
(cd publish && zip -r ../servicehub.zip .)
```

Build the web app **before** publishing: the web files are part of what gets published. Check `publish/wwwroot/index.html` exists. You can try the
package on your own machine first:

```bash
cd publish
ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://localhost:5390 \
  ServiceHub__DataDirectory=/tmp/sh-try SECURITY__ENCRYPTIONKEY="$(openssl rand -hex 32)" dotnet ServiceHub.Api.dll
# → http://localhost:5390/health/ready says Healthy
```

### 10.3 Create the app and deploy

```bash
RG=rg-servicehub
LOCATION=<your-region>
PLAN=plan-servicehub
APP=<globally-unique-app-name>

az group create --name "$RG" --location "$LOCATION"
az appservice plan create --resource-group "$RG" --name "$PLAN" --is-linux --sku B1 --number-of-workers 1
az webapp create --resource-group "$RG" --plan "$PLAN" --name "$APP" --runtime "<the entry from 10.1>"
az webapp config set --resource-group "$RG" --name "$APP" --always-on true

KEY="$(openssl rand -hex 32)"; echo "SAVE THIS KEY: $KEY"
az webapp config appsettings set --resource-group "$RG" --name "$APP" --settings \
  ASPNETCORE_ENVIRONMENT=Production \
  ServiceHub__DataDirectory=/home/data \
  AllowedHosts="$APP.azurewebsites.net" \
  SECURITY__ENCRYPTIONKEY="$KEY"

az webapp deploy --resource-group "$RG" --name "$APP" --src-path servicehub.zip --type zip
```

`AllowedHosts` is the one name ServiceHub will answer to (see the table in §9.3); without it the site loads nothing but its health check.

The data folder (`/home/data`) is created on first start. Check `curl https://$APP.azurewebsites.net/health/ready` → `Healthy`; logs:
`az webapp log tail -g "$RG" -n "$APP"`.

Now **lock it down — §9.4 is required here too.**

### 10.4 Update

Rebuild and re-run the last `az webapp deploy`. Settings and data are untouched; the app restarts onto the new build.

---

## 11. Keeping an App Service copy safe

| Task | How |
|---|---|
| **Backups** | Settings → Backup → *Take a backup now* ([Backup & restore](BACKUP-RESTORE.md)). They land under `/home/data/backups` — download them regularly. Consider turning on scheduled backups (`Backup__ScheduledBackupIntervalHours`) |
| **Logs** | `az webapp log tail`, or Portal → *Log stream* |
| **Restart** | `az webapp restart -g "$RG" -n "$APP"` |
| **Stop paying** | `az group delete --name "$RG" --yes --no-wait` removes everything, **including the data**. `az webapp stop` stops the app but the plan still bills |
| **Never** | Scale out, add a slot, or enable autoscale |

Reaching Service Bus: App Service connects outward on port 5671 like the VM (§5). If the namespace has a firewall, allow the app's
**outbound IP addresses** (`az webapp show -g "$RG" -n "$APP" --query possibleOutboundIpAddresses -o tsv`) or use VNet integration with a private
endpoint.

---

## Troubleshooting

| You see | Why | Do this |
|---|---|---|
| `docker compose up` says `set SERVICEHUB_ENCRYPTION_KEY first` | No `.env` in the folder you ran it from | `cd` into the `servicehub` folder; check `ls -a` shows `.env` |
| Container restarts in a loop; logs show `Neither Security:EncryptionKeyRegistry nor Security:EncryptionKey is configured` | The key is empty | Check `.env` has a non-empty `SERVICEHUB_ENCRYPTION_KEY=`, then `sudo docker compose up -d` |
| `Another ServiceHub instance already holds the data directory` | Two containers share the volume | `sudo docker ps -a`; stop the extra one |
| Browser says connection refused on `localhost:8080` | The tunnel is not running, or something else on your laptop uses 8080 | Start the `ssh -N -L …` command; use `-L 8081:127.0.0.1:8080` and browse to `:8081` |
| `ssh` times out | Your public address changed and the firewall rule no longer matches | Re-run the `az network nsg rule update` command in §3 |
| Added the namespace but it is "unreachable" | The namespace firewall blocks the VM | See §5 |
| Everything worked, then credentials stopped decrypting | The encryption key changed | Restore the original key into `.env` and `sudo docker compose up -d` |
| **App Service:** "Application Error" or the site never starts | Missing `SECURITY__ENCRYPTIONKEY`, or (container) `WEBSITES_PORT` not `8080` | `az webapp log tail`; check the app settings in §9.3 / §10.3 |
| **App Service:** the site answers *"This address is not one ServiceHub answers to"* (HTTP 400), but `/health/ready` works | `AllowedHosts` is missing or does not name the address you browse to | Set `AllowedHosts=<app>.azurewebsites.net` (plus any custom domain, `;`-separated) in the app settings and restart |
| **App Service:** image will not pull | The app's identity lacks `AcrPull`, or `acrUseManagedIdentityCreds` is not set | Redo the identity, role and `config set` steps in §9.3 |
| **App Service:** data gone after a redeploy | `WEBSITES_ENABLE_APP_SERVICE_STORAGE` is `false`, or `ServiceHub__DataDirectory` is not under `/home` | Fix the settings; anything stored elsewhere was in the container and is lost on replace |
