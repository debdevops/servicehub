# Host ServiceHub on Azure — Portal (clicks, the short version)

> **Time:** about 20 minutes. **Result:** ServiceHub on one small Azure VM, always on, with **no web port open to the internet**. You reach it through an SSH tunnel.
> Prefer a terminal? Use [HOSTING-AZURE-CLI.md](HOSTING-AZURE-CLI.md). Want a managed service instead of a VM? Read [HOSTING-AZURE-APP-SERVICE.md](HOSTING-AZURE-APP-SERVICE.md) first.
> The long, complete runbook is [HOSTING-AZURE.md](HOSTING-AZURE.md).

**Why a VM:** ServiceHub has **no login** (whoever can reach it is its Administrator) and keeps its data in **one SQLite file** (one instance only). A VM with the port closed and an SSH tunnel is the simplest design that is both safe and correct.

You need: a browser signed in to <https://portal.azure.com>, and `ssh` on your laptop for the last step.

## 1. Create the VM

1. Search **Virtual machines** → **Create** → **Azure virtual machine**.
2. **Basics**
   - Resource group: **Create new** → `rg-servicehub`
   - Name: `vm-servicehub`. Region: nearest your Service Bus namespaces.
   - Image: **Ubuntu Server 24.04 LTS - x64 Gen2**. Size: **Standard_B2s**.
   - Authentication: **SSH public key**, username `azureuser`, **Generate new key pair**.
   - Public inbound ports: **Allow selected ports → SSH (22)**.
3. Leave Disks and Networking as they are. **Review + create → Create**.
4. Choose **Download private key and create resource**. Keep the `.pem` file safe; Azure cannot give it again.
5. **Go to resource** and copy the **Public IP address**.

## 2. Lock the door

1. VM → **Networking** (or **Network settings**) → click the **SSH (22)** rule.
2. **Source** → **IP Addresses**; enter your own public IP (search "what is my IP"). **Save**.
3. Make sure no other rule allows traffic from *Any*. **You never open port 8080.**

## 3. Install and start ServiceHub (no SSH needed)

1. VM → **Operations → Run command → RunShellScript**.
2. Paste this, then **Run**. It prints your encryption key at the end: **copy it into your password manager now.**

```bash
set -e
curl -fsSL https://get.docker.com | sh
mkdir -p /opt/servicehub
[ -f /opt/servicehub/key ] || openssl rand -hex 32 > /opt/servicehub/key
chmod 600 /opt/servicehub/key
docker rm -f servicehub 2>/dev/null || true
docker run -d --name servicehub --restart unless-stopped \
  -p 127.0.0.1:8080:8080 -v servicehub-data:/data \
  -e SECURITY__ENCRYPTIONKEY="$(cat /opt/servicehub/key)" \
  -e Backup__ScheduledBackupIntervalHours=24 \
  ghcr.io/debdevops/servicehub:latest
sleep 30
curl -s http://localhost:8080/health/ready; echo
echo "KEY: $(cat /opt/servicehub/key)"
```

> **Check:** the output contains `Healthy`. If not, wait a minute and run `docker ps` the same way.
> **Lose the key and every saved cloud connection becomes unreadable.** The key file on the VM is not a backup; keep your own copy.

## 4. Open it from your laptop

```bash
chmod 600 ~/Downloads/vm-servicehub_key.pem
ssh -i ~/Downloads/vm-servicehub_key.pem -N -L 8080:127.0.0.1:8080 azureuser@<PUBLIC-IP>
```

Leave that running and browse to <http://localhost:8080>. Choose **Add a cloud → Azure** and paste a Service Bus connection string (a Shared access policy with **Manage**, not `RootManageSharedAccessKey`; see [clouds/azure.md](clouds/azure.md)).

If the namespace has a firewall (**Networking → Selected networks**), add the VM's public IP.

## 5. Day two

- **Upgrade:** repeat step 3 with a newer tag (the script keeps the same key and data volume).
- **Save money:** VM → **Stop** (deallocates). **Start** when needed.
- **Remove everything:** **Resource groups → rg-servicehub → Delete resource group**.

## Rules that matter

- **Never** open port 8080 to the internet.
- **Never** run a second copy on the same data.
- Everyone who can SSH in is an Administrator of ServiceHub. Keep the list short.
