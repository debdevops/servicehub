# Adding a Messaging Provider

A guide for whoever adds the next `ICloudMessagingProvider` — Kafka, RabbitMQ, IBM MQ, ActiveMQ,
Pulsar, NATS, or a fourth cloud provider. It starts from the real extension seam rather than asking
you to re-derive it from the codebase, and names the friction the Azure/AWS/GCP providers actually
hit, so a fourth provider doesn't rediscover it the hard way.

Read [`docs/ARCHITECTURE.md`](../ARCHITECTURE.md#4-provider-abstraction) first if you haven't — this
document assumes you know what `CloudProviderRouter` and `ProviderCapabilities` are for.

---

## What's already extensible — no changes needed

**Registration is a solved problem.** Each provider is a self-contained project exposing one DI
extension method:

```csharp
// ServiceHub.Infrastructure.Kafka/KafkaDependencyInjection.cs
public static IServiceCollection AddKafkaProvider(this IServiceCollection services)
{
    services.TryAddEnumerable(
        ServiceDescriptor.Singleton<ICloudMessagingProvider, KafkaMessagingProvider>());
    // + client factory, resilience pipeline, connectivity health check, etc.
    return services;
}
```

`Program.cs` wires it behind a config flag, following the exact pattern AWS/GCP already use:

```csharp
if (builder.Configuration.GetValue("CloudProviders:Kafka:Enabled", false))
{
    builder.Services.AddKafkaProvider();
}
```

`CloudProviderRouter` resolves `IEnumerable<ICloudMessagingProvider>` into a
`Dictionary<CloudProviderType, ICloudMessagingProvider>`, throws on duplicate registrations, and
gives callers `IsRegistered(type)` for feature-flag-style checks. None of this needs touching — it
was designed to take an arbitrary number of providers.

If a new API controller needs to check `IsRegistered`/`Resolve` a provider directly (most won't —
that's what `IMessageOperationsService` is for), depend on
`ServiceHub.Core.Interfaces.ICloudProviderRouter`, not the concrete
`ServiceHub.Infrastructure.Routing.CloudProviderRouter`. Both resolve to the same DI registration,
so this costs nothing at runtime — it's what keeps the Api layer decoupled from Infrastructure
details, and it's enforced by `tests/ServiceHub.UnitTests/Architecture/ApiLayerBoundaryTests.cs`.

**`Message.ApplicationProperties`** (`IReadOnlyDictionary<string, object>?`) is already a generic
attribute bag. Kafka headers or RabbitMQ headers map onto it directly — don't add new typed fields
to `Message` for a new provider's header/attribute concept; put them here.

**`ConnectionAuthType`** reserves a numeric band per provider (`0–9` Azure, `10–19` AWS, `20–29`
GCP) specifically so a new provider can claim the next band (`30–39`) without renumbering anything.

---

## What every new provider must implement

1. **`ICloudMessagingProvider`** (`ServiceHub.Core.Interfaces`) — `ProviderType`, `Capabilities`,
   `ValidateConnectionAsync`, `ListEntitiesAsync`, `GetMessageReceiver()`, `GetMessageSender()`.
2. **`IMessageReceiver`** / **`IMessageSender`** — peek, count, dead-letter, replay, purge,
   scheduled-message lookup, send/send-batch.
3. **A `CloudProviderType` enum value**, plus updates to the handful of call sites listed below
   that make real provider-specific decisions (not the registration/status-check call sites, which
   are already generic).
4. **A `ProviderCapabilities` declaration** — the one piece that didn't exist for the first three
   providers, and is now required for a new one. Do not skip this.

### Declare your `ProviderCapabilities` — honestly

`ServiceHub.Core.Models.ProviderCapabilities` is the single source of truth for "what does this
provider actually support." Declare yours as a static preset, the same way Azure/AWS/GCP do:

```csharp
// ServiceHub.Core/Models/ProviderCapabilities.cs
public static readonly ProviderCapabilities Kafka = new(
    SupportsMessageCounts: false,      // no live count API without an admin client call
    SupportsManualDeadLetter: false,   // no broker-native DLQ concept — consumers build their own
    SupportsPurge: false,              // no single-message delete by offset
    SupportsScheduledMessages: false,  // no native delayed delivery
    SupportsRepeatablePeek: false,     // consumer group offsets move on every fetch — no true non-destructive peek
    Notes: "Kafka has no dead-letter, purge, scheduling, or non-destructive peek primitives — these are consumer-side patterns, not broker features.",
    SupportsRecoveryMarker: false,     // no application-property-equivalent envelope slot yet — flag if your broker has one
    CanProveDlqAbsence: false);        // no ability to page/scan the topic uncapped
```

Then your provider's `Capabilities` getter is one line:
`Capabilities => ProviderCapabilities.Kafka;`

**`SupportsRepeatablePeek` gates Live Tail and the Messages page's auto-refresh.** `true` means
peeking this provider on a short, repeating interval (every few seconds, indefinitely) is safe with
no accumulating side effects. Get this wrong in the unsafe direction and repeated polling can
silently push messages toward the entity's own redelivery limit and dead-letter them by accident —
this is exactly the AWS SQS situation: its "peek" is actually a receive that increments
`ReceiveCount`, so it declares `SupportsRepeatablePeek: false` while Azure and GCP (whose peek
implementations genuinely re-queue with no consumer blocked) declare `true`.

**`CanProveDlqAbsence` gates unattended (L4/L5) auto-replay and recovery verification** — see
[`docs/ARCHITECTURE.md`](../ARCHITECTURE.md#5-recovery-evidence-ledger-and-verification). Declare
`true` only if your provider lets a background scan page the entire entity uncapped, so a "no
recurrence found" result is actually "recurrence proven absent," not just "not found in the sample
we could afford to take."

If your provider genuinely can't answer a capability question truthfully (no admin API available at
runtime, for instance), declare `false` and explain why in `Notes`, rather than defaulting to `true`.
The entire point of this model is that a `false` here visibly disables the affected UI/API path with
an explanation, instead of the operation failing unpredictably — or worse, silently returning a
fabricated answer — later. See [ADR-0001](../adr/0001-provider-abstraction-and-capabilities.md) for
the reasoning behind this requirement.

The frontend consumes your capabilities via `useProviderCapabilities()`
(`packages/servicehub-ui-shared/src/hooks/useCloudBridge.ts`) and
`getProviderCapabilities(map, provider)`
(`packages/servicehub-ui-shared/src/lib/api/cloudBridge.ts`). Wire your provider into any UI that
already gates on a capability (purge buttons, scheduling panels, message-count displays) by adding
your preset — **do not** add another `cloudProvider === 'kafka'` branch next to the ones this
pattern replaced.

---

## Constraints the first three providers hit — plan around these

These aren't blockers, but they are real friction every non-Azure provider has hit so far:

- **Message identity is a single `long` sequence number.** `IMessageReceiver.ReplayMessageAsync`
  and `PurgeMessageAsync` take `sequenceNumber: long`. Azure has a genuine provider-issued sequence
  number; AWS and GCP don't (SQS receipt handles and Pub/Sub ack IDs rotate on every delivery), so
  both **SHA-256-hash the stable `MessageId` into a `long`** purely to satisfy this signature, then
  **linearly re-scan the whole entity** at replay/purge time to find the message matching that hash
  (see `AwsMessageReceiver.FindAndLockMessageAsync` / `GcpMessageReceiver.FindAndLockMessageAsync`).
  Kafka's natural identity is `(partition, offset)` — a compound key that doesn't fit a single `long`
  any more cleanly. If your provider has the same problem, follow the existing hash-and-rescan
  pattern for consistency rather than inventing a third approach; a proper fix (an opaque cursor
  type instead of `long`) is real but breaking, and out of scope for a single provider addition.
- **No non-destructive peek.** Only Azure has a true peek-without-locking primitive. AWS/GCP fake it
  by receiving with a short visibility timeout / ack deadline, then immediately releasing it. If your
  broker has no peek equivalent either, this receive-then-release pattern is the established one to
  copy.
- **Credentials are a single opaque encrypted string.** `Namespace.ConnectionString` is one
  `string?`, parsed differently per provider (`CloudCredentialValidator` in
  `ServiceHub.Core.Validation`, dispatched via a manual `switch` in `NamespacesController.Create`).
  This works for a connection string, an access-key pair, or a service-account JSON blob, but not
  cleanly for anything needing *multiple* independent secrets (Kafka SASL_SSL commonly needs
  bootstrap servers + SASL credentials + a separate TLS truststore; mTLS needs a client cert + key +
  CA bundle as three distinct PEM blobs). One extra nullable field directly on `Namespace` (see
  `AwsRegion`, `GcpProjectId`) is the established pattern for one more piece of config — don't force
  a genuine multi-secret credential model through that pattern; flag it as a real extension rather
  than working around it silently.
- **`ServiceBusEntityType` is a closed 2-value enum** (`Queue`, `Subscription`) used on the persisted
  DLQ history row. AWS/GCP were already force-fit into it (SNS/Pub/Sub "subscriptions" both map to
  `Subscription`). A broker with a genuinely different topology (Kafka consumer groups, RabbitMQ
  exchange+binding+queue) will need this enum widened — search for every `switch`/`if` on it before
  changing it.
- **`CloudEntity.Name` encodes hierarchy as a path string** for Azure subscriptions
  (`"topic/subscriptions/sub"`) rather than exposing parent/child as separate fields. This is why
  Cross-Cloud Trace's Azure and non-Azure search paths remain separate — unifying them needs this
  resolved first.

## Places that branch on `CloudProviderType` — expect to touch these

A registered provider needs zero changes to `CloudProviderRouter`, `Program.cs`'s DI wiring, or
`CloudBridgeController`'s status/capabilities endpoints (both already iterate all known types
generically — extend them to a 4th key the same way). What genuinely needs a new arm for real
business logic:

- `DlqMonitorService` — provider-specific DLQ-scan conventions (Azure's `DeadLetterCount`
  short-circuit, GCP's `-dlq` naming convention, sequence-key-vs-`MessageId` dedup).
- `TopicsController` / `SubscriptionsController` / `QueuesController` — their `GetAll` actions
  already route non-Azure namespaces through `ICloudProviderRouter` to the registered provider, so
  entity listing works through these controllers directly. Single-entity lookups (`GetByName`) are
  still Azure-only — add that routing explicitly if a caller needs it.
- `Namespace.Create`'s auth-type inference switch and `CreateNamespaceRequest.Validate()`'s
  provider-conditional validation blocks.
- Frontend: `ConnectPage.tsx` is by far the most invasive file (one onboarding form section per
  provider, independent local state) — budget real UI work here; it will not be a drop-in `Record`
  extension the way `providerStyles.tsx`/`providerTheme.ts` are.

---

## Testing checklist

Mirror the existing provider test suites (`tests/ServiceHub.UnitTests/Infrastructure/{Azure,Aws,Gcp}`):

- `ProviderType_ReturnsX` and `Capabilities_ReflectsXConstraints` — assert every
  `ProviderCapabilities` boolean, not just the ones that happen to differ from Azure.
- Connection validation success/failure paths.
- `ListEntitiesAsync` mapping to `CloudEntity`.
- Peek/replay/purge/dead-letter behavior, including whatever workaround your provider needs for the
  message-identity and non-destructive-peek constraints above.

There is deliberately no credential-free way to exercise a new provider end-to-end against a real
backend in this project's CI — AWS and GCP are verified through unit tests and manual testing
against a real, flag-enabled provider, and a fourth provider should follow the same pattern rather
than trying to build a simulator into CI.

---

## Security considerations

- Never log a raw credential, connection string, or provider-returned error payload that might
  embed one — route anything provider-supplied through `LogRedactor.SanitiseForLog()` before it
  reaches a log call. This is CodeQL-enforced (`cs/log-forging`) in CI.
- Don't add a new plaintext credential storage path. Provider credentials flow through the same
  AES-GCM-256-encrypted `Namespace.ConnectionString` field (or the identity-first alternatives —
  managed identity, IAM role, workload identity — where the provider's SDK supports them) as
  Azure/AWS/GCP.
- A capability you cannot safely support must be declared `false`, never approximated as `true` —
  see [ADR-0001](../adr/0001-provider-abstraction-and-capabilities.md). This is a security property
  as much as a UX one: a UI that thinks an unsupported destructive action is available is a bug with
  real consequences.
