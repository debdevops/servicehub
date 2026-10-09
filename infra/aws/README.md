# ServiceHub on AWS

Run from the folder above: `./deploy.sh aws` (Windows: `.\deploy.ps1 -Cloud aws`). See the [main page](../README.md).

## What is created

| Resource | Why | Billed |
|---|---|---|
| VPC, one subnet, internet gateway | A small network of its own (or use yours: `existing_network`) | — |
| Security group with **no inbound rule** | Nothing can connect in. Out: HTTPS only | — |
| EC2 instance `t3.medium`, Amazon Linux 2023 | Runs ServiceHub. No SSH key | Yes |
| EBS volume, 32 GB, encrypted | The database and backups | Yes |
| IAM role + instance profile | The identity ServiceHub uses — see below | — |
| Secrets Manager secret | The encryption key | Small |
| S3 bucket, private, versioned, encrypted | Copies of each backup, kept 30 days | Small |
| Data Lifecycle Manager policy | A daily snapshot of the data volume, kept 7 days | Small |

The instance has a public address **only so it can reach out** (a NAT gateway would cost more than the instance itself).
With no inbound rule, nothing can reach in. Prices: [AWS pricing calculator](https://calculator.aws/).

## What ServiceHub may do — and nothing more

The instance's role is given exactly the calls ServiceHub makes, and only on the queues you list:

| On | Allowed | When |
|---|---|---|
| The whole account | `sqs:ListQueues`, `sns:ListTopics` — names only, never messages | Always (AWS cannot limit listing to named queues) |
| The queues in `messaging_resources` | `GetQueueUrl`, `GetQueueAttributes`, `ReceiveMessage`, `ChangeMessageVisibility` | Always |
| The same queues | `SendMessage`, `DeleteMessage` | Only when `allow_replay = true` |
| Topics in `messaging_resources` | `ListSubscriptionsByTopic`; `Publish` only with `allow_replay` | |

**List each queue and its dead-letter queue.** A queue that is not listed shows in ServiceHub by name but cannot be read.

In ServiceHub: *Add a cloud* → AWS → **use this server's identity** → your region. No access key is typed.

## Open it

```bash
aws ssm start-session --region <region> --target <instance id> \
  --document-name AWS-StartPortForwardingSession --parameters portNumber=8080,localPortNumber=8080
```

The exact command is printed at the end. Then open <http://localhost:8080>.

## Inputs

| Variable | Default | |
|---|---|---|
| `region` | — | Required |
| `messaging_resources` | `[]` | Queue (and topic) ARNs ServiceHub may use |
| `allow_replay` | `true` | `false` = read-only |
| `name` | `servicehub` | Prefix for names |
| `servicehub_version` | `4.2.0` | |
| `vm_size` | `t3.medium` | |
| `data_disk_gb` | `32` | |
| `snapshot_retention_days` | `7` | |
| `backup_retention_days` | `30` | |
| `existing_network` | `null` | `{ vpc_id, subnet_id }` of a subnet that can reach the internet |
| `tags` | `{}` | Added to `app = servicehub` |

## What a keeping destroy leaves

The data volume and its snapshots, the backup bucket, and the encryption key. `--destroy --purge` removes those as well
(the secret goes through Secrets Manager's seven-day recovery window).
