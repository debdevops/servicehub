# ServiceHub on Google Cloud

Run from the folder above: `./deploy.sh gcp` (Windows: `.\deploy.ps1 -Cloud gcp`). See the [main page](../README.md).

## What is created

| Resource | Why | Billed |
|---|---|---|
| VPC network, one subnet | A small network of its own | — |
| Cloud Router + Cloud NAT | Lets the VM reach out; it has **no external address** | Small |
| One firewall rule | SSH from Google's Identity-Aware Proxy range only — it carries the tunnel | — |
| VM `e2-medium`, Debian 12, Shielded VM | Runs ServiceHub | Yes |
| Persistent disk, 32 GB | The database and backups | Yes |
| Service account | The identity ServiceHub uses — see below | — |
| Secret Manager secret | The encryption key | Small |
| Cloud Storage bucket, private, versioned | Copies of each backup, kept 30 days | Small |
| Snapshot schedule | A daily snapshot of the data disk, kept 7 days | Small |

It also switches on the Compute, Secret Manager, IAP and Pub/Sub APIs in the project if they are off.
Prices: [Google Cloud pricing calculator](https://cloud.google.com/products/calculator).

## What ServiceHub may do — and nothing more

| On | Role | When |
|---|---|---|
| The project | `roles/pubsub.viewer` — names and settings of topics and subscriptions, never messages | Always |
| The subscriptions in `messaging_resources` | `roles/pubsub.subscriber` | Always |
| The topics in `replay_topics` | `roles/pubsub.publisher` | Only when `allow_replay = true` |
| Its own bucket | Add backups — it cannot read, list or delete them | Always |

**List each subscription and its dead-letter subscription**, and the topics a replay publishes back to.

In ServiceHub: *Add a cloud* → Google Cloud → **use this server's identity** → your project. No key file is made or typed.

## Open it

```bash
gcloud compute ssh <name> --project <project> --zone <zone> --tunnel-through-iap -- -N -L 8080:127.0.0.1:8080
```

The exact command is printed at the end. Then open <http://localhost:8080>. You need the *IAP-secured Tunnel User* role
(`roles/iap.tunnelResourceAccessor`) on the project, which project owners have.

## Inputs

| Variable | Default | |
|---|---|---|
| `project_id` | — | Required |
| `region`, `zone` | — | Required |
| `messaging_resources` | `[]` | Subscription IDs ServiceHub may read |
| `replay_topics` | `[]` | Topic IDs a replay publishes to |
| `allow_replay` | `true` | `false` = read-only |
| `name` | `servicehub` | Prefix for names |
| `servicehub_version` | `4.1.0` | |
| `vm_size` | `e2-medium` | |
| `data_disk_gb` | `32` | |
| `snapshot_retention_days` | `7` | |
| `backup_retention_days` | `30` | |
| `tags` | `{}` | Extra labels, added to `app = servicehub` |

## What a keeping destroy leaves

The data disk, its snapshot schedule and snapshots, the backup bucket, and the encryption key.
`--destroy --purge` removes those as well.
