# Self-Hosting ServiceHub

Everything here applies once you move past `docker compose up --build` on `localhost` and
start pointing ServiceHub at real cloud credentials, real users, or a network address other
than loopback. See the root [README](../README.md#quick-start) for the basic Docker Quick
Start — this doc covers the parts that only matter for a real deployment: persistent
storage, secrets, authentication, and per-cloud IAM setup.

ServiceHub is single-instance, self-hosted software for one team, not a multi-tenant SaaS
platform — see [Deployment Model](../README.md#deployment-model) in the root README before
planning capacity or scaling.

---

## Before you expose ServiceHub beyond localhost

Three defaults are safe for local trial and dangerous to carry into a real deployment
unchanged:

> [!WARNING]
> **Authentication is off by default.** `Security:Authentication:Enabled` defaults to
> `false` — every request is trusted as one shared admin identity. The Docker Compose Quick
> Start binds to `127.0.0.1` for exactly this reason. Before binding to `0.0.0.0`, a LAN
> address, or a public hostname, enable one of: scoped API keys
> (`Security:Authentication:Enabled=true` + `ApiKeys`/`ScopedApiKeys`), OIDC
> (`Security:Oidc:*`, any standards-compliant identity provider), or Azure Easy Auth on App
> Service. Details in the root [README → Security](../README.md#security).

> [!WARNING]
> **Encryption key loss is unrecoverable.** `SECURITY__ENCRYPTIONKEY` is never generated or
> stored by ServiceHub — you supply it, and losing it makes every stored connection string
> permanently undecryptable, with no rotation path in the current release. Generate it once
> per deployment (`openssl rand -hex 32`) and back it up in a real secret manager (Azure Key
> Vault, AWS Secrets Manager, GCP Secret Manager, HashiCorp Vault) outside the deployment
> host. See [SECURITY.md → Key rotation and credential backup](../SECURITY.md#key-rotation-and-credential-backup).

> [!IMPORTANT]
> **Both persistent-storage paths must be mounted — not just one.** See below.

---

## Persistent storage: two stores, two config keys

ServiceHub writes to two separate locations, controlled by two independent config keys:

| Store | Config key | Contents | Loss impact |
|---|---|---|---|
| SQLite (`DlqDbContext`) | `DlqDatabase:DataDirectory` / `DlqDatabase__DataDirectory` | DLQ history, replay history, auto-replay rules, audit log, bulk-operation jobs, failure signatures, Recovery Evidence Ledger | All investigation history and audit trail lost |
| Namespace credential store (JSON) | `NamespaceRepository:DataDirectory` / `NamespaceRepository__DataDirectory` | Encrypted connection strings / auth config for every namespace you've connected | Every namespace must be re-added by hand |

The root `Dockerfile` points **both** keys at the same path (`/var/servicehub/data`) by
default, so the documented Docker Quick Start already gets this right with a single mounted
volume — nothing extra to do there.

The trap is when you deviate from that default: if you deploy behind a platform where the
two directories end up on *different* mounts (for example, two separate Azure Files shares,
or one EFS mount and one ephemeral local path), you must persist **both** independently. A
setup that only persists the SQLite path will silently lose all namespace credentials on the
next restart while looking otherwise healthy — DLQ history survives, but every namespace has
to be re-entered.

---

## Cloud credentials: least-privilege setup

Static credentials are the simplest path for local/dev use. For production, prefer the
identity-based `authType` for your provider (Azure Managed Identity / Service Principal /
`DefaultAzureCredential`, AWS IAM role / OIDC, GCP Workload Identity) so no long-lived secret
is stored at all — ServiceHub supports all of these as first-class `authType` values on
namespace creation.

### Azure Service Bus

Read-only policy (recommended for production):

```bash
az servicebus namespace authorization-rule create \
  --namespace-name <your-namespace> \
  --resource-group <your-rg> \
  --name servicehub-readonly \
  --rights Listen
```

Grant `Manage` instead of `Listen` only on a DEV namespace where you also want replay/send/
test-data tooling.

### AWS SQS / SNS

ServiceHub's AWS provider calls: `ReceiveMessage`, `DeleteMessage`,
`ChangeMessageVisibilityBatch`, `GetQueueAttributes`, `GetQueueUrl`, `ListQueues`,
`SendMessage`/`SendMessageBatch` on SQS, and `ListTopics`, `ListSubscriptionsByTopic`,
`Publish` on SNS. A least-privilege IAM policy:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "ServiceHubSqs",
      "Effect": "Allow",
      "Action": [
        "sqs:ReceiveMessage",
        "sqs:DeleteMessage",
        "sqs:ChangeMessageVisibility",
        "sqs:ChangeMessageVisibilityBatch",
        "sqs:GetQueueAttributes",
        "sqs:GetQueueUrl",
        "sqs:ListQueues",
        "sqs:SendMessage",
        "sqs:SendMessageBatch"
      ],
      "Resource": "arn:aws:sqs:<region>:<account-id>:*"
    },
    {
      "Sid": "ServiceHubSns",
      "Effect": "Allow",
      "Action": [
        "sns:ListTopics",
        "sns:ListSubscriptionsByTopic",
        "sns:Publish"
      ],
      "Resource": "arn:aws:sns:<region>:<account-id>:*"
    }
  ]
}
```

Scope `Resource` down to specific queue/topic ARNs once you know which ones ServiceHub needs
— the wildcard above is a starting point, not the recommended end state. `DeleteMessage` and
`SendMessage`/`SendMessageBatch` are only exercised on DEV namespaces or via the explicit
Purge/Replay actions, which ServiceHub itself blocks on production namespaces — you can omit
them from a read-only-only policy if you never intend to replay/purge from that credential.
If using the `AwsIamRole`/`AwsOidc` `authType`, attach this policy to the role instead of an
IAM user, and set the role's trust policy to allow ServiceHub's runtime identity to assume it.

### GCP Pub/Sub

ServiceHub's GCP provider calls `Pull`, `Acknowledge`, `ModifyAckDeadline`, `Publish`,
`ListTopics`, `ListSubscriptions` (via `ListTopicSubscriptions`), and `GetSubscription`. The
combination of three predefined roles covers this with no custom role needed:

```bash
gcloud projects add-iam-policy-binding <project-id> \
  --member="serviceAccount:<sa-email>" --role="roles/pubsub.viewer"
gcloud projects add-iam-policy-binding <project-id> \
  --member="serviceAccount:<sa-email>" --role="roles/pubsub.subscriber"
gcloud projects add-iam-policy-binding <project-id> \
  --member="serviceAccount:<sa-email>" --role="roles/pubsub.publisher"
```

Omit `roles/pubsub.publisher` for a read-only credential — it's only needed for Replay/Purge/
test tooling, which ServiceHub blocks on production namespaces regardless. Prefer
`GcpWorkloadIdentity` over a downloaded service-account key file where your hosting platform
supports it (GKE, Cloud Run, Compute Engine with attached service accounts).

> [!NOTE]
> GCP Pub/Sub reports no message counts and has no manual dead-letter operation — this is a
> Pub/Sub API limitation, not a missing ServiceHub feature. See the provider comparison table
> in the root [README](../README.md#multi-cloud-bridge).

---

## Enabling AWS/GCP as live providers

Azure is the only provider registered by default. AWS and GCP are feature-flagged off:

```bash
CLOUDPROVIDERS__AWS__ENABLED=true
CLOUDPROVIDERS__GCP__ENABLED=true
```

Set these before connecting an AWS or GCP namespace — the namespace-creation endpoint
returns a `503` with an actionable message if you try to connect a namespace for a provider
whose flag is off.

---

## Team features: alerts, roles, and namespace sharing

These three roadmap capabilities are shipped and config/API-level by design — there's no
dedicated settings page for them yet, so they're set the same way as everything else above.

**Slack/Teams webhook alerts** — `Webhooks:Enabled=true` plus `Webhooks:Url` (a Slack or Teams
Incoming Webhook URL) and `Webhooks:Format` (`Slack` | `Teams` | `Generic`, default `Generic`)
send a DLQ-spike or completed-bulk-operation notification straight into a channel, no relay in
between. Optional `Webhooks:PublicUrl` adds a deep "Investigate" link back into this instance;
`Webhooks:DlqSpikeThreshold` / `Webhooks:CooldownSeconds` tune sensitivity.

**RBAC roles for API keys and OIDC** — a scoped API key's `Scopes` array, or an OIDC token's
`scope` claim, accepts named roles instead of enumerating individual scope strings: `Viewer`
(read-only), `Operator` (Viewer + send/replay/purge), `Auditor` (Viewer + audit trail access).
Example: `"Scopes": ["Viewer"]` in a `ScopedApiKeys` entry.

**Namespace sharing (Preview)** — a namespace owner can grant another owner identity (an OIDC
user, a scoped API key) live operational access to one namespace, without transferring
ownership:

```bash
# Discover your own owner ID to share
curl "$SITEURL/api/v1/me" -H "Authorization: Bearer <token>"

# Grant access (owner-only)
curl -X POST "$SITEURL/api/v1/namespaces/<id>/share" \
  -H "X-ServiceHub-Intent: namespaces:share" -H "Content-Type: application/json" \
  -d '{"ownerId": "<their-owner-id>"}'

# Revoke access (owner-only)
curl -X DELETE "$SITEURL/api/v1/namespaces/<id>/share/<their-owner-id>" \
  -H "X-ServiceHub-Intent: namespaces:share"
```

A shared collaborator gets live browse/peek/replay/purge/Live Tail access to that namespace
only — not the DLQ history, bulk-operation history, or audit trail entries recorded before the
share (each is stamped with whichever owner acted at write time; retroactive shared visibility
into that history is tracked as separate future work).

---

## Quick end-to-end test

A minimal "create a throwaway resource, connect it, verify a message round-trips, tear it
down" loop for each cloud. These use the read-only-plus-send-on-DEV policies above; run
ServiceHub locally (`docker compose up --build`, see root
[README → Quick Start](../README.md#quick-start)) before starting.

### Azure

```bash
az servicebus queue create --namespace-name <your-namespace> --resource-group <your-rg> \
  --name servicehub-e2e-test
```

The `az servicebus` CLI has no message send/peek commands — connect the namespace in
ServiceHub instead (Add Namespace → paste a `Manage`-rights connection string for this test),
open `servicehub-e2e-test`, and use ServiceHub's own floating **Generate Messages** action to
send one (send/generate is only available on non-production namespaces — see
[Deployment Model](../README.md#deployment-model)). Confirm the message appears with its body
visible. Clean up:

```bash
az servicebus queue delete --namespace-name <your-namespace> --resource-group <your-rg> \
  --name servicehub-e2e-test
```

### AWS

```bash
QUEUE_URL=$(aws sqs create-queue --queue-name servicehub-e2e-test --query QueueUrl --output text)
aws sqs send-message --queue-url "$QUEUE_URL" --message-body '{"hello":"servicehub"}'
```

Connect the AWS namespace in ServiceHub (enable the AWS flag first — see above) → open
`servicehub-e2e-test` → confirm the message appears. Clean up:

```bash
aws sqs delete-queue --queue-url "$QUEUE_URL"
```

### GCP

```bash
gcloud pubsub topics create servicehub-e2e-test
gcloud pubsub subscriptions create servicehub-e2e-test-sub --topic servicehub-e2e-test
gcloud pubsub topics publish servicehub-e2e-test --message '{"hello":"servicehub"}'
```

Connect the GCP namespace in ServiceHub (enable the GCP flag first — see above) → open the
`servicehub-e2e-test-sub` subscription → confirm the message appears. Clean up:

```bash
gcloud pubsub subscriptions delete servicehub-e2e-test-sub
gcloud pubsub topics delete servicehub-e2e-test
```

---

## Where deployment recipes live today

- **Docker / Docker Compose** — the primary, fully documented path. Root
  [README → Quick Start](../README.md#quick-start).
- **Azure App Service for Containers** — **RECOMMENDED**, the most mature managed-hosting
  path today. Generic GHCR-image steps are in the root
  [README → Self-Host on Azure](../README.md#azure-app-service-recommended); this repo's
  `deploy/` folder additionally contains the maintainer's own production pipeline (specific
  budget/resource names, Azure DevOps release flow) as a reference, not a required read.
- **Azure Container Apps** — **ALTERNATIVE**, see below.
- **AWS, GCP, and other managed platforms** — no dedicated deployment scripts exist yet.
  The container is portable to any platform that can run a single long-lived Docker image
  with a persistent volume (see the storage section above) — ECS/Fargate and a single
  Compute Engine VM are reasonable starting points, respectively. Contributions welcome.
- **Generic Docker / VM / on-prem** — the same image, run via Docker Engine or Podman on
  hardware you control. Put a reverse proxy (nginx, Caddy, Traefik) in front for TLS
  termination; ServiceHub itself terminates plain HTTP only.

### Azure Container Apps (Alternative)

Workable, but Container Apps' headline feature — scale-to-zero and elastic replica count —
fights this architecture directly: a cold start after scale-to-zero drops in-flight SSE
connections and resets the in-process event bus, and any replica count above 1 risks two
copies of a background worker acting on the same SQLite database. Reasonable only if your
organization already standardizes on Container Apps; otherwise prefer App Service above.

```bash
az login
az group create --name rg-servicehub --location eastus
az containerapp env create --name env-servicehub --resource-group rg-servicehub --location eastus

az containerapp create --name servicehub --resource-group rg-servicehub \
  --environment env-servicehub --image ghcr.io/debdevops/servicehub:latest \
  --target-port 8080 --ingress external \
  --min-replicas 1 --max-replicas 1 \
  --secrets encryption-key="$(openssl rand -hex 32)" spa-secret="$(openssl rand -hex 32)" \
  --env-vars ASPNETCORE_ENVIRONMENT=Production \
    SECURITY__ENCRYPTIONKEY=secretref:encryption-key \
    SECURITY__SPATOKEN__SECRET=secretref:spa-secret \
    SITEURL=https://<app-fqdn>
```

`--min-replicas 1 --max-replicas 1` is what makes this safe to run at all — do not omit it.
Attach Azure Files storage mounted at both `DataDirectory` paths (see
[Persistent storage](#persistent-storage-two-stores-two-config-keys) above), then verify with
`curl https://<app-fqdn>/health/live`.

---

## Single-instance by design

ServiceHub runs 8 in-process background workers (DLQ monitoring, auto-replay, bulk
operations, signature replay, audit retention, recovery verification/ageing, autonomy
evaluation) and an in-process SSE event bus, backed by SQLite. Running more than one replica
against the same data directory is unsupported and unsafe: two replicas would each run their
own copy of every background worker against the same database, risking duplicate
replay/purge actions. Pin any hosting platform's instance/replica count to exactly 1.
