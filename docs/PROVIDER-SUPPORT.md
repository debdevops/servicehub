# Provider Support Matrix

The canonical reference for what each cloud provider genuinely supports. Every claim below is
sourced directly from `services/api/src/ServiceHub.Core/Models/ProviderCapabilities.cs` — the
single place these facts are declared in code (`GET /api/v1/cloud-bridge/capabilities` exposes the
same data to the frontend, so the UI and this page can never drift). Field names match the C#
record exactly.

## Maturity

| Provider | Maturity | Meaning |
|---|---|---|
| **Azure Service Bus** | Supported (GA) | Fully implemented, unit-tested, and validated against live Azure Service Bus namespaces. |
| **AWS SQS/SNS** | Preview | Implemented and unit-tested. Not validated against live AWS services in CI. Capability-gated (see below) — no parity guarantee with Azure. |
| **GCP Pub/Sub** | Preview | Implemented and unit-tested. Not validated against live GCP services in CI. Capability-gated (see below) — no parity guarantee with Azure. |

"Preview" here means concretely: the provider's `ICloudMessagingProvider` implementation exists,
is unit-tested, and is exercised end-to-end by Simulator mode — but has not been validated against
a real, live AWS/GCP account as part of this project's own test process, is gated behind a
disabled-by-default flag (below), and does not claim feature parity with Azure where the
underlying cloud platform itself doesn't offer an equivalent capability (see the table below).

## Enabling AWS/GCP

Both are registered only when explicitly enabled — inert by default, and **absent entirely from
`appsettings.Production.json`** (the flags exist only in `appsettings.json`'s shipped defaults,
both `false`):

| Flag | Default | Effect |
|---|---|---|
| `CloudProviders:Aws:Enabled` | `false` | Registers the AWS `ICloudMessagingProvider`, its client factory, and the `aws-connectivity` health check. Inert until an AWS namespace is created. |
| `CloudProviders:Gcp:Enabled` | `false` | Registers the GCP `ICloudMessagingProvider`, its client factory, and the `gcp-connectivity` health check. Inert until a GCP namespace is created. |

Azure is always registered outside Simulator mode. Simulator mode registers all three
unconditionally, regardless of these flags, so Simulator is the only way to exercise AWS/GCP code
paths with zero configuration.

## Capability matrix

| Operation | Azure | AWS | GCP | Source field |
|---|:---:|:---:|:---:|---|
| Real active-message counts | ✅ | ✅ | ❌ (reports `0`) | `SupportsMessageCounts` |
| Manual dead-letter (move a specific message to DLQ on demand) | ✅ | ✅ | ❌ (policy-driven only) | `SupportsManualDeadLetter` |
| Purge (permanent single-message delete by identity) | ❌ | ✅ | ✅ | `SupportsPurge` |
| Scheduled / delayed-delivery messages (queryable, cancellable) | ✅ | ❌ | ❌ | `SupportsScheduledMessages` |
| Repeatable, non-destructive peek (safe for auto-refresh, Live Tail, background DLQ scanning) | ✅ | ❌ | ✅ | `SupportsRepeatablePeek` |

### `Notes` (verbatim from source)

> **Azure**: "Purge is not supported — the SDK has no reliable single-message delete by sequence number."
>
> **AWS**: "Scheduled messages are not supported — SQS only offers DelaySeconds (max 15 minutes) at send time. Repeated/live polling is also not supported — SQS has no non-destructive peek, so every call is a receive that counts toward the queue's maxReceiveCount."
>
> **GCP**: "Message counts and manual dead-lettering are not supported — Pub/Sub has no count API and dead-lettering is policy-driven via MaxDeliveryAttempts. Scheduled messages are not supported either."

## AWS DLQ background monitoring is opt-in, off by default

`SupportsRepeatablePeek: false` for AWS exists because SQS has no non-destructive peek — every
`ReceiveMessage` call increments the message's `ReceiveCount`, which can push a message past its
queue's `maxReceiveCount` and dead-letter it by accident. Because of this, `DlqMonitorService`
skips background DLQ scanning entirely for AWS namespaces unless an operator explicitly opts in:

| Flag | Default | Effect |
|---|---|---|
| `DlqMonitor:AllowDestructivePeek:Aws` | `false` | When `false` (default), background DLQ scans and manual "Scan Now" triggers both skip AWS namespaces and return a distinct "not monitored" result — DLQ History shows this state explicitly rather than an empty (and misleading) result. When `true`, scanning proceeds and accepts the `ReceiveCount` consequence above. |

This same `SupportsRepeatablePeek` flag also gates Live Tail (`MessagesController`) — AWS
namespaces get `409 Conflict` rather than a live-polling session that would silently mutate
delivery state.

## Credential shapes and required permissions

Every provider's backend supports more authentication mechanisms than the current Connect page UI
exposes. This is true for all three providers, not just AWS/GCP — documented here rather than left
implicit:

| Provider | UI-exposed auth | Also implemented in `ConnectionAuthType`, no UI path today |
|---|---|---|
| Azure | Connection string (SAS) | `ManagedIdentity`, `ServicePrincipal`, `DefaultAzureCredential` |
| AWS | Access Key ID + Secret Access Key | `AwsIamRole` (assume-role via the host's ambient credentials), `AwsOidc` (web identity federation) |
| GCP | Service Account JSON key + Project ID | `GcpWorkloadIdentity` (keyless federation) |

### AWS — credential shape and IAM permissions

- **UI-exposed shape**: Access Key ID + Secret Access Key + region, stored as `AKID:SecretKey`
  (AES-GCM encrypted at rest; a legacy `aws://AKID:SecretKey@region` URL format is still accepted
  on read for namespaces saved by older frontend versions).
- **Required IAM permissions** — verified against every AWS SDK call actually made in
  `ServiceHub.Infrastructure.Aws` (not copied from a general-purpose provisioning guide, which
  needs broader permissions than ServiceHub's own runtime does):
  - `sqs:GetQueueUrl`, `sqs:GetQueueAttributes`, `sqs:ListQueues`, `sqs:ReceiveMessage`,
    `sqs:SendMessage`, `sqs:DeleteMessage`
  - `sns:ListTopics`, `sns:ListSubscriptionsByTopic`, `sns:Publish`
  - `sqs:SendMessage`/`sns:Publish` and `sqs:DeleteMessage` are only exercised by Send/Test tooling
    and Purge respectively — a read-only deployment can scope a policy down to the `Get`/`List`/
    `Receive` actions only.

### GCP — credential shape and IAM permissions

- **UI-exposed shape**: full service-account JSON key pasted into the Connect form, plus the GCP
  project ID (falls back to a `projectId=...` token in the connection string if unset).
- **Required IAM roles** — verified against every Pub/Sub SDK call in `ServiceHub.Infrastructure.Gcp`:
  - `roles/pubsub.viewer` — list/get topics and subscriptions
  - `roles/pubsub.subscriber` — pull, acknowledge, and modify ack deadline (peek and the DLQ scan path)
  - `roles/pubsub.publisher` — publish (Send/Test tooling only; omit for a read-only deployment)

### Azure — credential shape

- **UI-exposed shape**: Service Bus connection string (Shared Access Signature), any of
  Listen-only, Send, or Manage policy — AES-GCM encrypted at rest, same as AWS/GCP.

---

## Verification

Every claim above was cross-checked field-by-field against
`services/api/src/ServiceHub.Core/Models/ProviderCapabilities.cs` and the actual SDK call sites in
`ServiceHub.Infrastructure.Aws`/`ServiceHub.Infrastructure.Gcp`. **One mismatch found, not silently
reconciled**: the Connect page UI (`ConnectionAuthType` in `apps/web/src/lib/api/types.ts`) exposes
exactly one auth mechanism per provider, while the backend enum and client factories implement
five total across the three providers (see the credential-shapes table above). This is a real gap
between what the backend can do and what an operator can configure through the UI today, not a
documentation error — flagged here rather than corrected, since closing it is a frontend feature
change outside this documentation pass.
