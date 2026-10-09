# ServiceHub on Azure

Run from the folder above: `./deploy.sh azure` (Windows: `.\deploy.ps1 -Cloud azure`). See the [main page](../README.md).

## What is created

| Resource | Why | Billed |
|---|---|---|
| Resource group `rg-<name>` | Holds everything | — |
| Virtual network, subnet, network security group | SSH from **your address only**; no rule for ServiceHub's port | — |
| Public IP (static) | So the tunnel has somewhere to connect | Yes |
| Linux VM `Standard_B2s`, Ubuntu 24.04 | Runs ServiceHub | Yes |
| Managed data disk, 32 GB | The database and backups | Yes |
| User-assigned managed identity | Reads the VM's own key, writes its own backups | — |
| Key Vault + one secret | The encryption key | Small |
| Storage account + private container | Copies of each backup, kept 30 days | Small |
| Backup vault + daily disk snapshot policy | Snapshots kept 7 days | Small |

Prices change by region — check the [Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/).

## Connecting to Service Bus — read this

**ServiceHub 4.1.0 connects to Azure Service Bus with a connection string**, pasted into *Add a cloud*. Its Azure adapter
does not use a managed identity yet, so this module gives the VM's identity **no** access to your namespaces.
Create a Shared Access Policy for ServiceHub in each namespace as [the Azure guide](../../docs/clouds/azure.md) describes.
The connection string is encrypted with the key in your Key Vault and never leaves your subscription.

If a namespace has a firewall, allow the VM's public IP there.

## Open it

```bash
ssh -N -L 8080:127.0.0.1:8080 servicehub@<the address printed at the end>
```

Then open <http://localhost:8080>. If your own IP address changes, set `allowed_ssh_cidr` again and re-run the deploy.

## Inputs

| Variable | Default | |
|---|---|---|
| `subscription_id` | — | Required |
| `region` | — | Required, e.g. `westeurope` |
| `ssh_public_key` | — | Required: the text of your public key |
| `allowed_ssh_cidr` | — | Required: your address, e.g. `203.0.113.7/32`. `0.0.0.0/0` is refused |
| `name` | `servicehub` | Prefix for names |
| `servicehub_version` | `4.2.0` | |
| `vm_size` | `Standard_B2s` | |
| `data_disk_gb` | `32` | |
| `snapshot_retention_days` | `7` | |
| `backup_retention_days` | `30` | |
| `messaging_resources` | `[]` | Recorded as a tag only (see above) |
| `tags` | `{}` | Added to `app = servicehub` |

## What a keeping destroy leaves

The resource group, with the data disk, the Key Vault and its key, the storage account with the backups, and the backup
vault with its snapshots. `--destroy --purge` removes those as well.
