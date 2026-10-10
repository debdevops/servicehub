# Host ServiceHub on Azure — an operator's runbook

> **Who this is for:** a DevOps engineer or SRE. You do not need to know .NET, Node.js or the ServiceHub code, and you do not build anything.
> You run a published container image on one Azure VM, copy-paste the commands, and check each step with the line that follows it.
>
> **Time:** about 30 minutes. **Result:** an always-on ServiceHub that survives restarts and reboots, whose port is never exposed to the internet, with
> backups, a patching story, an upgrade path and a way to remove it.

If you only want to look at ServiceHub, use [Run with Docker](DOCKER.md) or [Local setup](LOCAL-SETUP.md): no Azure account needed.

> **Prefer Terraform?** [`infra/`](../infra/README.md) builds the same thing for you (VM, data disk, Key Vault, daily snapshots, backup copies) with
> `./deploy.sh azure`, and removes it again. This page is the manual, step-by-step version, and the one to read if you want to understand each piece or fit it into your own landing zone.

---

## How to follow this guide: CLI or portal

Every Azure step is shown **two ways**. Pick one and stay with it:

| | **Option A — Azure CLI** | **Option B — Azure portal** (clicks) |
|---|---|---|
| Best for | Engineers who live in a terminal; repeatable | Anyone new to Azure; nothing to install |
| You need | Azure CLI (or Cloud Shell), `ssh` | A web browser, plus `ssh` on your laptop for the final step |
| Setting up the VM | Commands in your terminal, then `ssh` into the VM | Portal pages, then paste one script into **Run command** (no SSH needed for setup) |

Look for the headings **Option A — CLI** and **Option B — Portal** under each step. The steps that run *on the VM* (Docker, the key, the compose file) are the same
either way; Option B pastes them into the portal's **Run command** box instead of an SSH session.

> **No tools installed?** The portal has a ready-made terminal: click the **`>_` (Cloud Shell)** icon in the top bar and choose **Bash**. It already has `az` and `openssl`.
> Windows 10/11, macOS and Linux all include an `ssh` command (check with `ssh -V` in a terminal or PowerShell).

---

## 0. What you are building

```
 your laptop ──ssh -L 8080──►  Azure VM (Ubuntu 24.04, no web port open)
 (browser: localhost:8080)       └─ Docker ─ ServiceHub container ─ /data (Docker volume, SQLite)
                                         │
                                         └──► outbound only: Azure Service Bus (:5671), ghcr.io (image pull)
```

- **One container, one SQLite file, one VM.** ServiceHub is deliberately single-instance: a second copy on the same data exits on purpose. Do not put it behind a load balancer, scale set or AKS replica set.
- **There is no login.** Whoever can reach ServiceHub's port is its Administrator. That is why this design never exposes the port: people reach it through an SSH tunnel (§7).
- **Nothing ServiceHub-specific lives in Azure** besides the VM. It only *connects out* to the Service Bus namespaces you give it.

### Decisions to make before you start

| Decision | Recommended default | Notes |
|---|---|---|
| Region | The same region as the Service Bus namespaces you will watch | Lower latency, simpler firewalling |
| VM size | `Standard_B2s` (2 vCPU, 4 GB) | Comfortable for one team |
| Image tag | `latest` while you evaluate, then a pinned release such as `4.2.0` | `latest` moves; production should pin ([§9](#9-upgrade-and-roll-back)) |
| Who may tunnel in | A named list of engineers | Everyone who can SSH in is an Administrator of ServiceHub |

---

## 1. Prerequisites

| You need | Check |
|---|---|
| An Azure subscription where you can create a resource group and a VM (**Contributor** on the subscription or a resource group is enough) | `az account show` |
| **Option A:** Azure CLI 2.50+, signed in (or Cloud Shell, which already has it). **Option B:** just a browser signed in to <https://portal.azure.com> | `az --version` · `az login` |
| **bash**, `ssh`, `curl`, `openssl` on your workstation (Cloud Shell has all of them) | `openssl version` |
| The Service Bus namespace(s) to watch, and someone who can create a **Shared access policy** on each | Not created here — see [§6](#6-connect-service-bus) |
| Outbound internet from the VM to `ghcr.io` (port 443) | If your egress is locked down, allow `ghcr.io` and its content hosts, or mirror the image into your own registry ([§11](#11-air-gapped-or-locked-down-networks)) |

> **Cost.** You pay for the VM, its disk and its public IP while they exist (about the price of one small VM). ServiceHub adds no charge. Prices vary by region: use the
> [Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/).

---

## 2. Create the VM

### Option A — CLI

Set your own names once; every later command reuses them:

```bash
RG=rg-servicehub
LOCATION=westeurope            # pick yours: az account list-locations -o table
VM=vm-servicehub

az group create --name "$RG" --location "$LOCATION" --tags app=servicehub

az vm create \
  --resource-group "$RG" --name "$VM" \
  --image Canonical:ubuntu-24_04-lts:server:latest \
  --size Standard_B2s \
  --os-disk-size-gb 64 \
  --admin-username azureuser --generate-ssh-keys \
  --public-ip-sku Standard \
  --nsg-rule SSH \
  --tags app=servicehub
```

Copy the **`publicIpAddress`** from the output into a variable (a Standard-SKU address is static, so it survives restarts):

```bash
IP=<publicIpAddress from the output>
```

> **Check:** `az vm show -g "$RG" -n "$VM" -d --query powerState -o tsv` prints `VM running`.

#### Lock the door

By default SSH is open to the whole internet. Allow only your own address, and **do not open any other port**:

```bash
MYIP="$(curl -s https://api.ipify.org)"
az network nsg rule update \
  --resource-group "$RG" --nsg-name "${VM}NSG" --name default-allow-ssh \
  --source-address-prefixes "$MYIP"
```

> **Check:** `az network nsg rule show -g "$RG" --nsg-name "${VM}NSG" -n default-allow-ssh --query sourceAddressPrefix -o tsv` prints your address.
> If your address changes later, run the first command again.

Your organisation may require something stricter. Both are fine, and ServiceHub does not care which you use:

- **Several engineers:** put your office or VPN egress range in `--source-address-prefixes` (space-separated list).
- **No public IP at all:** use Azure Bastion or a VPN into the VNet, delete the public IP, and tunnel through that. The rest of this page is unchanged.

### Option B — Portal

1. Sign in at <https://portal.azure.com>. Search for **Virtual machines** → **Create** → **Azure virtual machine**.
2. **Basics** tab:
   - **Subscription:** yours. **Resource group:** **Create new** → `rg-servicehub`.
   - **Virtual machine name:** `vm-servicehub`. **Region:** the one nearest your Service Bus namespaces.
   - **Availability options:** *No infrastructure redundancy required*. **Security type:** *Standard*.
   - **Image:** *Ubuntu Server 24.04 LTS - x64 Gen2*. **Size:** `Standard_B2s` (use **See all sizes** to find it).
   - **Authentication type:** *SSH public key*. **Username:** `azureuser`. **SSH public key source:** *Generate new key pair*, key pair name `vm-servicehub_key`.
   - **Public inbound ports:** *Allow selected ports* → **SSH (22)**. (You will narrow this to your own address in a minute.)
3. **Disks** tab: **OS disk size** → *64 GiB*. Leave the rest.
4. **Networking** tab: leave the defaults. The **Public IP** is created for you; keep its SKU *Standard* (static).
5. **Tags** tab: name `app`, value `servicehub`.
6. **Review + create** → **Create**. In the pop-up choose **Download private key and create resource**. A file named `vm-servicehub_key.pem` is saved to your Downloads folder.
   **Keep that file safe and private**: it is the only way in over SSH, and Azure cannot give it to you again.
7. When it says *Your deployment is complete*, choose **Go to resource**. On **Overview** copy the **Public IP address**. This is your `<IP>` in the rest of the page.

> **Check:** **Overview** shows *Status: Running*.

#### Lock the door (portal)

1. In the VM, open **Networking** (under *Settings*; newer portals call it **Network settings**).
2. In the **Inbound port rules** list, click the **SSH** rule (port 22).
3. Set **Source** to **IP Addresses**. In **Source IP addresses/CIDR ranges** enter your own public address (search the web for "what is my IP") and **Save**.
4. Make sure **no other inbound rule** allows anything from *Any* source. You never open port 8080.

> **Check:** the SSH rule's *Source* column shows your address instead of *Any*. If your address changes later, edit the rule again. In an office, use your company's egress range instead.

---

## 3. Install Docker

> **Option B — Portal users:** you do not SSH in. Do the key step in §4 (Option B), then run **one script** from §5 (Option B), which installs Docker, writes the files and starts ServiceHub. Skip the rest of §3 and §4's CLI box.

### Option A — CLI (SSH)

```bash
ssh azureuser@"$IP"
```

On the VM:

```bash
sudo apt-get update
sudo apt-get install -y docker.io docker-compose-v2
sudo systemctl enable --now docker          # Docker starts on every boot
sudo docker run --rm hello-world            # proves Docker can run and reach the internet
```

> **Check:** `hello-world` prints "Hello from Docker!". You do not need git, .NET or Node.js on this VM.

---

## 4. Make the encryption key — once

ServiceHub encrypts every cloud credential it stores, and **refuses to start in production without a key**. Make it, keep a copy off the VM, and put it in `.env` on the VM.

### Option A — CLI (on the VM)

```bash
sudo install -d -m 700 /opt/servicehub && cd /opt/servicehub
KEY="$(openssl rand -hex 32)"
printf 'SECURITY__ENCRYPTIONKEY=%s\n' "$KEY" | sudo tee .env >/dev/null
sudo chmod 600 .env
echo "$KEY"          # copy this into your secret manager NOW (next box)
unset KEY
```

> [!IMPORTANT]
> **Store the key in your secret manager before you continue** (Azure Key Vault, 1Password, …). If the VM is lost and the key with it, every saved cloud
> connection is unreadable and has to be re-entered. Backups do **not** contain the key. Never change it on its own: to rotate it, follow
> [Encryption key rotation](ENCRYPTION-KEY-ROTATION.md).

With Azure Key Vault, from your workstation (not the VM) — or use Option B below:

```bash
KV=<globally-unique-vault-name>
az keyvault create --resource-group "$RG" --name "$KV" --enable-rbac-authorization true
az role assignment create --role "Key Vault Secrets Officer" \
  --assignee "$(az ad signed-in-user show --query id -o tsv)" \
  --scope "$(az keyvault show --name "$KV" --query id -o tsv)"
# role assignments can take a minute or two to apply, then:
az keyvault secret set --vault-name "$KV" --name servicehub-encryption-key --value '<paste the key>'
```

> **Check:** `sudo ls -l /opt/servicehub/.env` shows `-rw-------` owned by root. The key is in your secret manager.

### Option B — Portal

1. **Make the key.** Click the **`>_` Cloud Shell** icon in the top bar → **Bash** (first time: accept the storage prompt). Run:
   ```bash
   openssl rand -hex 32
   ```
   It prints 64 letters and digits. Copy them. This is your key.
2. **Store it safely first.**
   1. Search **Key vaults** → **Create**. Resource group `rg-servicehub`, a globally unique **Key vault name**, same region, **Permission model: Azure role-based access control** → **Review + create** → **Create**.
   2. Open the vault → **Access control (IAM)** → **Add** → **Add role assignment** → role **Key Vault Secrets Officer** → **Members** → **Select members** → choose yourself → **Review + assign**. (It can take a minute or two to apply.)
   3. **Objects → Secrets** → **Generate/Import**. Name `servicehub-encryption-key`, **Secret value**: the key → **Create**.
3. Keep the key handy: you paste it into the script in §5.

> **Check:** the secret `servicehub-encryption-key` is listed in the vault. Never put the key in a ticket, chat or repository.

---

## 5. Start ServiceHub

### Option A — CLI (on the VM)

Still on the VM, in `/opt/servicehub`, write the compose file:

```bash
sudo tee compose.yaml >/dev/null <<'EOF'
name: servicehub
services:
  servicehub:
    image: ghcr.io/debdevops/servicehub:latest        # pin a release for production, e.g. :4.2.0
    container_name: servicehub
    restart: unless-stopped
    ports:
      - "127.0.0.1:8080:8080"                          # loopback only: never reachable from outside the VM
    volumes:
      - servicehub-data:/data                          # the SQLite database and its backups
    env_file: .env                                     # holds SECURITY__ENCRYPTIONKEY
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      Backup__ScheduledBackupIntervalHours: "24"       # a ServiceHub backup every day, 14 kept
    logging:
      driver: json-file
      options: { max-size: "10m", max-file: "5" }      # Docker's default would grow without limit
volumes:
  servicehub-data:
    name: servicehub-data
EOF

sudo docker compose up -d
```

The image is public, so there is no registry sign-in. The first start downloads it (about 230 MB) and creates the database.

> **Check (allow up to a minute):**
> ```bash
> sudo docker compose ps                         # STATUS shows "healthy"
> curl -s http://localhost:8080/health/ready     # prints: Healthy
> ```
> If it does not become healthy: `sudo docker compose logs --tail 50`, and see [Troubleshooting](#troubleshooting).

You can already look around: **Try it with sample data** uses made-up data and connects to nothing.

### Option B — Portal (one script, no SSH)

1. Open your VM in the portal → **Operations** → **Run command** → **RunShellScript**.
2. Paste the whole script below. **Replace `PASTE_KEY_HERE`** with your key from §4 (keep the quotes), then **Run**. It takes two to five minutes; the result appears in the output pane.

```bash
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y docker.io docker-compose-v2
systemctl enable --now docker

install -d -m 700 /opt/servicehub
cd /opt/servicehub
printf 'SECURITY__ENCRYPTIONKEY=%s\n' 'PASTE_KEY_HERE' > .env
chmod 600 .env

cat > compose.yaml <<'EOF'
name: servicehub
services:
  servicehub:
    image: ghcr.io/debdevops/servicehub:latest        # pin a release for production, e.g. :4.2.0
    container_name: servicehub
    restart: unless-stopped
    ports:
      - "127.0.0.1:8080:8080"                          # loopback only: never reachable from outside the VM
    volumes:
      - servicehub-data:/data
    env_file: .env
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      Backup__ScheduledBackupIntervalHours: "24"
    logging:
      driver: json-file
      options: { max-size: "10m", max-file: "5" }
volumes:
  servicehub-data:
    name: servicehub-data
EOF

docker compose up -d
sleep 60
docker compose ps
curl -s http://localhost:8080/health/ready; echo
```

> **Check:** the output ends with a `servicehub` row whose status says `healthy` (or `starting` right after the start), and the last line says `Healthy`.
> If it says `starting`, run **Run command** again with just `cd /opt/servicehub && docker compose ps && curl -s localhost:8080/health/ready`.
> Anyone who may use **Run command** on this VM can read the script and so the key; that is the same trust as root on the VM, so give that role only to the people who run it.
> To run any later command from this page (logs, restart, backup) in the portal, paste it into **Run command** the same way, without `sudo`.

---

## 6. Connect Service Bus

ServiceHub connects to Azure Service Bus with a **connection string** from a Shared access policy. Ask whoever owns each namespace (or do it yourself):

1. In the Azure portal open the namespace → **Settings → Shared access policies → Add**.
2. Name it `servicehub-app` and tick **Manage** (Azure ticks Send and Listen for you). Manage is needed: Azure only lets a Manage policy *list* queues and *count* their messages. Send is used only when someone presses Replay. **Do not** hand over `RootManageSharedAccessKey`.
3. Copy the **Primary connection string**. Treat it as a secret.

The full walkthrough with screenshots is the [Azure guide](clouds/azure.md) (Part 1 and Part 2).

**Network path.** The VM connects *outward* to `<namespace>.servicebus.windows.net` on **port 5671 (AMQP over TLS)**. Azure allows outbound traffic by default, so normally there is nothing to do. If
the namespace has a firewall (**Networking → Selected networks**), add the VM's public IP, or connect them with a private endpoint. Otherwise ServiceHub reports the namespace as unreachable.

Paste the connection string in the app (next section: **Add a cloud**). It is encrypted on the VM with the key from §4 and never shown again.

---

## 7. Open it from your workstation

On **your own computer**, open a tunnel and leave it running:

```bash
ssh -N -L 8080:127.0.0.1:8080 azureuser@"$IP"
```

Browse to <http://localhost:8080>. Everything between you and ServiceHub is inside the encrypted SSH connection. Choose **Add a cloud → Azure** and paste the connection string.

**Option B — Portal users** connect with the `.pem` file you downloaded in §2, using the VM's public IP from **Overview**:

```bash
# macOS / Linux: the key file must be private to you
chmod 600 ~/Downloads/vm-servicehub_key.pem
ssh -i ~/Downloads/vm-servicehub_key.pem -N -L 8080:127.0.0.1:8080 azureuser@<IP>
```

```powershell
# Windows PowerShell: make the key file private to you, then connect
icacls "$HOME\Downloads\vm-servicehub_key.pem" /inheritance:r /grant:r "$($env:USERNAME):(R)"
ssh -i "$HOME\Downloads\vm-servicehub_key.pem" -N -L 8080:127.0.0.1:8080 azureuser@<IP>
```

The window appears to hang: that is the tunnel working. Leave it open while you use ServiceHub, and close it (Ctrl-C) when you finish. Answer `yes` the first time it asks to trust the host.
(Anywhere this page shows `ssh azureuser@"$IP"`, portal users add `-i <path to the .pem>`.)

> **Check:** the namespace shows **Connected** and its queues are listed.

### Giving more than one person access

ServiceHub does not log the browser in, so an SSH tunnel makes **everyone who can SSH in an Administrator**. Decide this on purpose:

- **A few trusted engineers** — give each their own SSH key on the VM (`/home/azureuser/.ssh/authorized_keys`, or their own Linux user) and restrict the NSG source range to your network. Easiest, and fine for a small on-call team.
- **Different powers for different people** (Viewer, Operator, Admin) — put an **authenticating reverse proxy** (for example one that signs people in with Microsoft Entra ID) in front, and give ServiceHub the proxy's name. That needs HTTPS and extra settings; read the *Who is asking* section of the [README](../README.md#deploying-it-for-real) and [SECURITY.md](../SECURITY.md) first, and
  never enable `Security__EasyAuth__TrustClientPrincipalHeader` unless the proxy strips and sets that header itself. This is a security design decision for your organisation.
- **Reaching it by a name other than `localhost`** — ServiceHub refuses any `Host` it was not told about (so a web page on another site cannot drive it through your browser). Add `AllowedHosts: "localhost;your.host.name"` under `environment:` in `compose.yaml`, keeping `localhost` in the list, and run `sudo docker compose up -d`.

---

## 8. Operate it

### Daily commands (on the VM, in `/opt/servicehub`)

| Task | Command |
|---|---|
| Is it healthy? | `sudo docker compose ps` · `curl -s localhost:8080/health/ready` |
| Logs | `sudo docker compose logs -f --tail 100` |
| Restart ServiceHub only | `sudo docker compose restart` |
| Stop / start | `sudo docker compose stop` · `sudo docker compose start` |
| Which version is running? | `sudo docker inspect servicehub --format '{{.Config.Image}} {{.Image}}'` |

**Monitoring.** Probe `GET http://localhost:8080/health/ready` (200 and `Healthy`) from a local agent, a cron job, or Azure Monitor's VM insights. `/health` and `/health/ready` are
the only paths that answer on any host name. There is nothing to scrape on a public port, by design.

### Backups — do both

ServiceHub keeps everything in one SQLite file. Backups need two layers:

1. **ServiceHub's own backup** (consistent, verified): the compose file above takes one every 24 hours and keeps the newest 14, in the volume. Take one on demand:
   ```bash
   curl -X POST http://localhost:8080/api/v1/admin/backup -H "X-ServiceHub-Intent: create-backup"
   ```
   The answer is a manifest with `"integrityCheck": "ok"`. Details and restore steps: [Backup & restore](BACKUP-RESTORE.md).
2. **Copy them off the VM** — ServiceHub does not ship backups elsewhere. From your workstation:
   ```bash
   ssh azureuser@"$IP" 'sudo tar -C /var/lib/docker/volumes/servicehub-data/_data -cz backups' > "servicehub-backups-$(date +%F).tgz"
   ```
   Put this in a scheduled job that writes to private storage you control. (The `infra/` Terraform does the copy automatically if you prefer.)

Also snapshot the VM's OS disk as a second safety net (the Docker volume lives on it). Use Azure Backup for the VM, or by hand:

```bash
OSDISK="$(az vm show -g "$RG" -n "$VM" --query storageProfile.osDisk.managedDisk.id -o tsv)"
az snapshot create -g "$RG" -n "snap-servicehub-$(date +%F)" --source "$OSDISK"
```

**Portal:** VM → **Settings → Disks** → click the **OS disk** name → **Create snapshot** → name it `snap-servicehub-<date>`, resource group `rg-servicehub`, **Snapshot type: Full** → **Review + create**. For a schedule, VM → **Backup** → enable Azure Backup with the default policy.

> A disk snapshot taken while the database is busy is only crash-consistent. **ServiceHub's own backup is the one to restore from**; the snapshot is for losing the VM.

**Practice a restore once** on a scratch VM or container before you need it. **Keep the encryption key with the backups**: a backup restored under a different key opens, but its saved connections do not.

### Patching the VM

Ubuntu installs security updates by itself (`unattended-upgrades`). Reboot when a kernel update asks for it (`/var/run/reboot-required` exists):

```bash
sudo reboot        # ServiceHub and Docker start again on their own, with the data
```

Allow a minute, then re-check `/health/ready`. Reboot in a quiet period: ServiceHub watches nothing and replays nothing while it is down.

### Saving money when idle

`az vm deallocate -g "$RG" -n "$VM"` stops the compute charge (the disk is still billed). `az vm start -g "$RG" -n "$VM"` brings it back, and ServiceHub restarts with its data.

### Never

- Run a second ServiceHub against the same volume, or behind a load balancer or scale set.
- Copy the database file to another machine while ServiceHub is running (use a backup).
- Delete the `servicehub-data` volume to "reset" something you want to keep.
- Publish port 8080 on `0.0.0.0` or put a public IP rule in front of it.

---

## 9. Upgrade and roll back

ServiceHub's database is upgraded **forward only**: a newer version changes it on first start, and an older version cannot open the result. So **always back up first**, and keep the key.

```bash
# 1. Back up (on the VM), and copy it off the VM as in §8
curl -X POST http://localhost:8080/api/v1/admin/backup -H "X-ServiceHub-Intent: create-backup"

# 2. Read the release notes: https://github.com/debdevops/servicehub/blob/main/CHANGELOG.md

# 3. Pull and restart on the new image
cd /opt/servicehub
sudo sed -i 's#servicehub:[^ ]*#servicehub:4.2.0#' compose.yaml        # or keep :latest and just pull
sudo docker compose pull
sudo docker compose up -d

# 4. Check
sudo docker compose ps ; curl -s localhost:8080/health/ready
```

The `.env`, the volume and your connections are untouched. Open the app and confirm your namespaces still show **Connected**.

**Rolling back** a start that fails: put the previous tag back in `compose.yaml` and `sudo docker compose up -d`. If the new version already changed the database and the old one refuses
it, restore the backup you took in step 1 ([Backup & restore](BACKUP-RESTORE.md#3-restore)), then start the old tag.

---

## 10. Remove everything

**Option A — CLI**

```bash
az group delete --name "$RG" --yes --no-wait
```

**Option B — Portal:** **Resource groups** → `rg-servicehub` → **Delete resource group** → type the group name → **Delete**.

This deletes the VM, its disk (and with it ServiceHub's data and backups), the IP and the network. Download any backup you want to keep first. Key Vault secrets sit in the same resource group in
this guide, so they go too (a deleted vault is recoverable for its soft-delete period).

To remove only ServiceHub but keep the VM: `cd /opt/servicehub && sudo docker compose down && sudo docker volume rm servicehub-data` (irreversible: it deletes the database and ledger).

---

## 11. Air-gapped or locked-down networks

If the VM cannot reach `ghcr.io`, copy the image through a machine that can, into **your own** Azure Container Registry:

```bash
ACR=<your-registry-name>
az acr import --name "$ACR" --source ghcr.io/debdevops/servicehub:4.2.0 --image servicehub:4.2.0
```

Then give the VM pull access (`az vm identity assign` plus the **AcrPull** role on the registry, and `az acr login` on the VM, or a registry token) and change the `image:` line in `compose.yaml` to
`<your-registry-name>.azurecr.io/servicehub:4.2.0`. Everything else is the same. For private networking to Service Bus, use a private endpoint ([§6](#6-connect-service-bus)).

---

## 12. Hand-over checklist

Tick these before you call it done:

- [ ] `/health/ready` answers `Healthy`, and `docker compose ps` shows `healthy`
- [ ] The encryption key is in the secret manager, and a second person knows where
- [ ] NSG allows SSH only from approved addresses; **no** rule exposes 8080
- [ ] Each Service Bus connection uses its own `servicehub-app` policy, not the root key
- [ ] The image tag is pinned (not `latest`) for production
- [ ] A backup was taken, copied off the VM, and a restore was practised
- [ ] A reboot test passed: `sudo reboot`, wait, `/health/ready` is `Healthy`, connections still **Connected**
- [ ] Someone owns upgrades and watches the [changelog](https://github.com/debdevops/servicehub/blob/main/CHANGELOG.md)
- [ ] The team knows that **anyone who can SSH in is an Administrator** of ServiceHub

---

## Troubleshooting

| You see | Why | Do this |
|---|---|---|
| `docker compose up` fails: `env file … not found` | You are not in `/opt/servicehub`, or `.env` is missing | `cd /opt/servicehub; sudo ls -la` |
| Container restarts in a loop; logs say `Neither Security:EncryptionKeyRegistry nor Security:EncryptionKey is configured` | The key line in `.env` is empty or misspelled | `.env` must contain `SECURITY__ENCRYPTIONKEY=<64 hex characters>`; then `sudo docker compose up -d` |
| Connections stopped decrypting after a restart or upgrade | A **different** key is in `.env` than the one that made the data | Put the original key back from the secret manager; if it is lost, remove the namespaces and add them again |
| `pull access denied` / `unauthorized` / timeout pulling the image | The VM cannot reach `ghcr.io`, or the image name or tag is mistyped | `curl -sI https://ghcr.io/v2/` from the VM; check the NSG/firewall egress rules, or use [§11](#11-air-gapped-or-locked-down-networks) |
| `Another ServiceHub instance already holds the data directory` | Two containers share the volume | `sudo docker ps -a`; stop the extra one |
| `localhost:8080` refuses the connection on your laptop | The tunnel is not running, or something local uses 8080 | Start the `ssh -N -L …` command; or use `-L 8081:127.0.0.1:8080` and browse to `:8081` |
| `ssh` times out | Your public address changed | Re-run the `az network nsg rule update` command in §2 |
| A namespace says "unreachable" | The namespace firewall blocks the VM, or the string is wrong | Allow the VM's public IP in the namespace's Networking page; re-paste the connection string |
| Page says `400 This address is not one ServiceHub answers to` | You browsed by a name other than `localhost`, `127.0.0.1` or `[::1]` | Use the tunnel and `http://localhost:8080`, or set `AllowedHosts` as in §7 |
| `The database … is not a ServiceHub 4.1.0 database` | The volume holds a 4.0.0 database (there is no upgrade path from 4.0.0) | Use a fresh volume name in `compose.yaml` |
| Disk filling up | Logs, backups, or both | Logs are capped by the compose file; lower `Backup__RetentionCount` or move old backups off the VM |

Still stuck? [Open an issue](https://github.com/debdevops/servicehub/issues/new/choose) with the last 50 lines of `sudo docker compose logs` (remove anything sensitive first).
