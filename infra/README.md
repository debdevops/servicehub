# Deploy ServiceHub in your own cloud

> **In this folder:** everything needed to put a private ServiceHub in **your own** Azure, AWS or Google Cloud account, and to
> remove it again. About ten minutes. Nothing here is a hosted service: your credentials and your messages stay in your account.

## What you get

One small virtual machine running ServiceHub, with:

- **No way in from the internet.** ServiceHub has no sign-in yet, so its port is never exposed. You open it through the
  cloud's own tunnel, from your own computer.
- **Its data on a separate disk**, snapshotted every day, with each backup also copied to private storage.
- **Its encryption key made inside the cloud** and kept in the cloud's secret store. It is never shown on screen.
- **Only the access you name.** On AWS and Google Cloud ServiceHub uses the machine's own identity, limited to the queues
  or subscriptions you list — no access key is typed anywhere.

## Before you start

You need three things on your computer, and nothing else:

| | |
|---|---|
| **Terraform** 1.6 or newer | <https://developer.hashicorp.com/terraform/install> |
| **Your cloud's CLI**, signed in | `az login` · `aws configure` · `gcloud auth login` and `gcloud auth application-default login` |
| A shell you already have | bash (macOS, Linux) or PowerShell (Windows) |

AWS only: to open ServiceHub afterwards you also need the
[Session Manager plugin](https://docs.aws.amazon.com/systems-manager/latest/userguide/session-manager-working-with-install-plugin.html).

## Deploy

```bash
git clone https://github.com/debdevops/servicehub.git && cd servicehub/infra
./deploy.sh aws            # or: azure, gcp
```

```powershell
# Windows
.\deploy.ps1 -Cloud aws
```

It checks your computer, asks a few questions, shows exactly what will be created, and asks before creating anything.
At the end it prints the one command that opens the tunnel, and the address to open.

| | macOS / Linux | Windows |
|---|---|---|
| Only check this computer | `./deploy.sh aws --doctor` | `.\deploy.ps1 -Cloud aws -Doctor` |
| Show the plan, change nothing | `./deploy.sh aws --plan` | `.\deploy.ps1 -Cloud aws -Plan` |
| No questions (for a pipeline) | add `--yes` and pass the answers as options | add `-Yes` |
| Read-only (see, never replay) | add `--read-only` | add `-ReadOnly` |
| List what exists | `./deploy.sh aws --list` | `.\deploy.ps1 -Cloud aws -List` |

Each cloud's own page says what is created, what it costs, and every option:
[Azure](azure/README.md) · [AWS](aws/README.md) · [Google Cloud](gcp/README.md).

## Remove it

```bash
./deploy.sh aws --destroy            # removes ServiceHub; KEEPS the data disk, the backups and the encryption key
./deploy.sh aws --destroy --purge    # removes everything. Asks you to type PURGE. Cannot be undone.
```

A plain destroy keeps your data on purpose: the disk and the key together are your recovery history, and losing the key
makes every stored credential unreadable. After either one, the script lists whatever still carries the ServiceHub tag,
so you can see for yourself what is left.

## Prefer plain Terraform?

Each folder is an ordinary module. Copy `terraform.tfvars.example` to `terraform.tfvars`, fill it in, and run
`terraform init`, `plan`, `apply`. Or call it from your own code:

```hcl
module "servicehub" {
  source              = "github.com/debdevops/servicehub//infra/aws?ref=v4.2.0"
  region              = "eu-west-1"
  messaging_resources = ["arn:aws:sqs:eu-west-1:111122223333:orders", "arn:aws:sqs:eu-west-1:111122223333:orders-dlq"]
}
```

State is kept locally by default. **It contains the encryption key — keep it private**, and use your own remote backend
with encryption if more than one person deploys. A plain `terraform destroy` removes everything, data included.

## Update ServiceHub

Open a shell on the machine (Azure: `ssh servicehub@<address>`; AWS: `aws ssm start-session --target <instance id>`;
Google Cloud: `gcloud compute ssh <name> --tunnel-through-iap`) and run:

```bash
echo 4.2.1 | sudo tee /etc/servicehub/version      # the release you want
sudo servicehub-setup
```

It fetches that version, restarts ServiceHub on it, and leaves the data disk and the key untouched. Then set
`servicehub_version` to the same value in `terraform.tfvars`, so a rebuilt machine matches. (Changing that variable alone
deliberately does not replace a running machine.)

## If something goes wrong

| You see | Do this |
|---|---|
| `Stopped: This computer is not ready yet` | Fix the lines marked ✗ — each says the command to run |
| The tunnel opens but the page does not load | The first start takes about five minutes. On the machine, read `/var/log/servicehub-setup.log` |
| The log says it is *building from source* | The published image could not be pulled, so the machine builds that version itself. It works; it takes longer |
| The apply stopped half way | Run the same command again. Or `--destroy` to remove what was made |
| ServiceHub says a queue is unreachable | It is not in the list you gave. Add it to `messaging_resources` and deploy again |
