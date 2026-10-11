# Host ServiceHub on Azure — CLI (the short version)

> **Time:** about 15 minutes. **Result:** ServiceHub on one small Azure VM, always on, with **no web port open to the internet**. You reach it through an SSH tunnel.
> Prefer clicking? Use [HOSTING-AZURE-PORTAL.md](HOSTING-AZURE-PORTAL.md). Want a managed service instead of a VM? Read [HOSTING-AZURE-APP-SERVICE.md](HOSTING-AZURE-APP-SERVICE.md) first.
> The long, complete runbook (backups, patching, upgrade, removal) is [HOSTING-AZURE.md](HOSTING-AZURE.md).

**Why a VM:** ServiceHub has **no login** (whoever can reach it is its Administrator) and keeps its data in **one SQLite file** (one instance only). A VM with the port closed and an SSH tunnel is the simplest design that is both safe and correct.

You need: Azure CLI signed in (`az login`; Cloud Shell works), plus `ssh` and `openssl`.

## 1. Create the VM

```bash
RG=rg-servicehub
LOC=westeurope            # pick the region nearest your Service Bus namespaces

az group create -n $RG -l $LOC

az vm create -g $RG -n vm-servicehub \
  --image Ubuntu2404 --size Standard_B2s \
  --admin-username azureuser --generate-ssh-keys \
  --public-ip-sku Standard \
  --nsg-rule NONE

IP=$(az vm show -d -g $RG -n vm-servicehub --query publicIps -o tsv)
echo $IP
```

`--nsg-rule NONE` opens nothing. Now allow SSH from **your address only**:

```bash
MYIP=$(curl -s https://api.ipify.org)
az vm open-port -g $RG -n vm-servicehub --port 22 --priority 100 \
  --nsg-rule-name ssh-me 2>/dev/null
az network nsg rule update -g $RG --nsg-name vm-servicehubNSG -n ssh-me \
  --source-address-prefixes "$MYIP/32"
```

> **Check:** `az network nsg rule list -g $RG --nsg-name vm-servicehubNSG -o table` shows port 22 from your IP, and nothing from `*`.

## 2. Install Docker, then start ServiceHub

```bash
ssh azureuser@$IP
```

On the VM:

```bash
curl -fsSL https://get.docker.com | sudo sh

# The encryption key. Make it ONCE and keep a copy in your password manager.
KEY=$(openssl rand -hex 32); echo "$KEY"

sudo docker run -d --name servicehub --restart unless-stopped \
  -p 127.0.0.1:8080:8080 -v servicehub-data:/data \
  -e SECURITY__ENCRYPTIONKEY="$KEY" \
  -e Backup__ScheduledBackupIntervalHours=24 \
  ghcr.io/debdevops/servicehub:latest        # pin a release for production, e.g. :4.2.0

curl -s http://localhost:8080/health/ready     # → Healthy (allow up to a minute)
```

> **Lose the key and every saved cloud connection becomes unreadable.** Keep it outside the VM.

## 3. Open it from your laptop

On your own computer, leave this running:

```bash
ssh -N -L 8080:127.0.0.1:8080 azureuser@$IP
```

Browse to <http://localhost:8080>. Choose **Add a cloud → Azure** and paste a Service Bus connection string (a Shared access policy with **Manage**, not `RootManageSharedAccessKey`; see [clouds/azure.md](clouds/azure.md)).

If the namespace has a firewall, allow the VM's public IP (`$IP`) on it.

## 4. Day two

```bash
# on the VM
sudo docker logs --tail 50 servicehub
sudo docker pull ghcr.io/debdevops/servicehub:latest && sudo docker rm -f servicehub   # then re-run the docker run command above
```

Re-running `docker run` with the same `-v servicehub-data:/data` and the **same key** keeps your data.

Stop paying while idle: `az vm deallocate -g $RG -n vm-servicehub` (start again with `az vm start`).

## 5. Remove everything

```bash
az group delete -n rg-servicehub --yes --no-wait
```

## Rules that matter

- **Never** open port 8080 to the internet.
- **Never** run a second copy on the same data.
- Everyone who can SSH in is an Administrator of ServiceHub. Keep the list short.
