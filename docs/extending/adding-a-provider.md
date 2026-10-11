# Adding a messaging provider

For an engineer adding the next cloud or broker (a fourth cloud, Kafka, RabbitMQ, …) to ServiceHub 4.2.0. It is code and configuration, not
screenshots; to *use* a provider that already exists, see the [Azure](../clouds/azure.md), [AWS](../clouds/aws.md) or
[Google Cloud](../clouds/gcp.md) guide.

**The shape of the work:** a provider is one project, `services/api/src/ServiceHub.Providers.<Name>/`, that depends only on `ServiceHub.Core`.
The three that exist (`ServiceHub.Providers.Azure`, `.Aws`, `.Gcp`) are peers — copy the smallest of them, not a design from scratch. Nothing
but the API's DI root references a provider project, and `DependencyDirectionTests` fails the build if that changes.

## What you do not have to touch

- **The router.** `CloudProviderRouter` takes every registered `ICloudMessagingProvider`, keys them by `CloudProviderType`, and refuses duplicates.
- **The gate, the ledger and the agents.** They ask `ProviderCapabilities`, never a provider's name. That is rule R4, and
  `CapabilityHonestyTests` fails the build if code outside an adapter branches on a provider name.
- **`Message.ApplicationProperties`.** It is the generic attribute bag. Put a broker's headers there; do not add typed fields to `Message`.

## Step by step

1. **Add the enum value.** `CloudProviderType` (`services/api/src/ServiceHub.Core/Enums/CloudProviderType.cs`) has `Azure = 0`, `Aws = 1`, `Gcp = 2`.
   Add yours as `3`. Never renumber: the value is stored.
2. **Declare `ProviderCapabilities` — honestly.** Add a static preset in `services/api/src/ServiceHub.Core/Models/ProviderCapabilities.cs`
   beside `Azure`, `Aws` and `Gcp`, and add an arm to `ProviderCapabilities.For(...)`.
   **Do not skip the `For` arm:** its default branch returns the AWS preset, so a provider without an arm silently inherits AWS's claims.
3. **Create the project** `ServiceHub.Providers.<Name>` and add it to `services/api/ServiceHub.slnx`. Reference `ServiceHub.Core` only.
4. **Implement the three contracts** (all in `ServiceHub.Core.Interfaces`):
   - `ICloudMessagingProvider`: `ProviderType`, `Capabilities`, `ValidateConnectionAsync`, `ListEntitiesAsync`, `GetMessageReceiver`,
     `GetMessageSender`. Override `CanHaveDeadLetters` if some entities only exist to receive another's dead letters (Google's `-dlq`
     subscription), and `ListEntitiesForReconciliationAsync` only if discovery can fail per entity (AWS).
   - `IMessageReceiver`: peek, peek dead letters, count, dead-letter, replay, purge, scheduled lookup. `ReplayMessageAsync` returns a
     `ReplayExecutionResult` — whether the recovery marker was applied and the replay's new provider message id. The verification agent needs both.
   - `IMessageSender`: `SendAsync`, `SendBatchAsync`.
5. **Add one DI extension** `Add<Name>Provider(this IServiceCollection)`. The smallest working example is `services/api/src/ServiceHub.Providers.Gcp/GcpDependencyInjection.cs`:
   register the client factory, register the receiver and sender as concrete types, add the provider with `TryAddEnumerable`, and add a health
   check tagged `dependencies` (never `ready` — a broker outage must not mark ServiceHub itself unready).
6. **Add one line to `services/api/src/ServiceHub.Api/Program.cs`**, next to `AddAzureProvider()`, `AddAwsProvider()` and `AddGcpProvider()`.
   That is the only place the API knows a provider exists.
7. **Credentials.** A connection is one encrypted string on the namespace (`ServiceHub.Core.Entities.Namespace`), plus the odd provider-specific
   field (`AwsRegion`, `GcpProjectId`). Add your auth types to `ConnectionAuthType`, a case in the credential validation
   (`services/api/src/ServiceHub.Core/Validation/NamespaceCredentials.cs`) and the provider-conditional rules in `CreateNamespaceRequest`. A
   broker that needs several independent secrets (SASL plus a TLS truststore) does not fit one string — raise it as a design question first.
8. **The web app.** Add the provider to the `CloudProvider` type and to `providerLabel`, `providerOrder` and `providerService` in
   `apps/servicehub/src/lib/providers.ts` (names for display only), and a section in the Add-a-cloud form. Anything that *behaves* differently
   must read capabilities, not `provider === '…'`.
9. **Tests**, mirroring `tests/backend/ServiceHub.UnitTests/Providers/`: `ProviderType`, every boolean of `Capabilities`, connection validation
   success and failure, entity listing, and peek, replay, purge and dead-letter behaviour. Then `./runtest.sh --backend`.

## Declaring capabilities

Every field is a fact about the platform, not about one namespace. A `false` visibly disables the affected button or path with your `Notes`
text as the reason; a wrong `true` is a bug that can lose messages.

| Field | Declare `true` only if… | What it gates |
|---|---|---|
| `SupportsMessageCounts` | the platform reports a real active-message count | counts on Home; otherwise "can't count here" |
| `SupportsManualDeadLetter` | an operator can move one message to the dead-letter queue | moving a message to the DLQ by hand |
| `SupportsPurge` | one message can be deleted by identity | permanently deleting one message |
| `SupportsScheduledMessages` | scheduled delivery can be queried and cancelled | looking up scheduled messages |
| `SupportsRepeatablePeek` | peeking every few seconds, forever, has **no side effect that accumulates** | Live Tail, auto-refresh, the watching agent |
| `SupportsRecoveryMarker` | the envelope can carry `x-servicehub-recovery-id` | marking a replay so it can be recognised later |
| `CanProveDlqAbsence` | a scan can read the *whole* dead-letter queue, uncapped | whether a result may read **Verified**, and unattended (L4/L5) replay |
| `SupportsTopics`, `SupportsSubscriptions` | the platform has those concepts | the topic and subscription entries in `GET /api/v1/namespaces/{id}/entities` |

Two of these protect messages:

- **`SupportsRepeatablePeek`.** If your "peek" is really a receive (as on AWS SQS and Google Pub/Sub), every poll counts toward the redelivery
  limit and can dead-letter a message just by watching it. Declare `false`; the product then only looks when a person presses **Look now**.
- **`CanProveDlqAbsence`.** A capped sample cannot prove a message is gone. Declare `false` and the result reads *"verification required"* —
  never *"verified"* (see [Recovery Evidence](../RECOVERY-EVIDENCE.md)).

If you cannot answer a capability truthfully, declare `false` and say why in `Notes`.

## Where the first three providers hit friction

- **Message identity is a single `long`.** `ReplayMessageAsync` and `PurgeMessageAsync` take a `sequenceNumber`. Only Azure has one. AWS
  receipt handles and Pub/Sub ack ids change on every delivery, so those adapters hash the stable message id into a `long` and re-scan the
  entity to find the message at replay or purge time (`FindAndLockMessageAsync` in the AWS and GCP receivers). A broker identified by
  `(partition, offset)` fits no better; follow that pattern rather than inventing a third.
- **No non-destructive peek** except on Azure. The others receive with a short visibility timeout and release at once.
- **`ServiceBusEntityType` is a closed two-value enum** (`Queue`, `Subscription`). A broker with a different shape (consumer groups,
  exchange plus binding) means widening it — search every `switch` on it first.
- **Schema changes are frozen.** ServiceHub freezes database migrations 0001–0015 (the maintainers' ADR-0017). A provider that
  needs a new column needs a dated maintainer sign-off first; ask in the issue or PR rather than adding a migration.

## Security

- Never log a credential or a provider error payload that might embed one. Sanitise anything provider-supplied before it reaches a log call.
- Credentials go through the same AES-GCM-encrypted field as the other clouds, or an identity-based option (managed identity, IAM role,
  workload identity). Never add a second, plaintext path.
- Prefer a provider SDK's identity-based auth, and document the narrowest permissions the provider needs — the existing guides list theirs.
