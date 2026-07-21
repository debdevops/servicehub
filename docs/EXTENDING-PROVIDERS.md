# Extending ServiceHub with a Messaging Provider

This is a guide for whoever adds the next `ICloudMessagingProvider` — Kafka, RabbitMQ, IBM MQ,
ActiveMQ, Pulsar, NATS, or a fourth cloud provider. It exists so that work starts from the real
extension seam rather than being re-derived from the codebase each time, and so the constraints
found while building the Azure/AWS/GCP providers aren't rediscovered the hard way.

Written after implementing three real providers (Azure Service Bus, AWS SQS/SNS, GCP Pub/Sub) —
the patterns below reflect what those three actually needed, not a theoretical design.

---

## What's already extensible (no changes needed)

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
gives callers `IsRegistered(type)` for feature-flag-style checks. None of this needs touching —
it was designed to take an arbitrary number of providers.

If you're adding a new Api controller that needs to check `IsRegistered`/`Resolve` a provider
(most won't — that's what `IMessageOperationsService` is for), depend on
`ServiceHub.Core.Interfaces.ICloudProviderRouter`, not the concrete
`ServiceHub.Infrastructure.Routing.CloudProviderRouter`. Both are registered against the same DI
instance (see `AddServiceBus()` in `DependencyInjection.cs`), so this costs nothing at runtime —
it's what keeps the Api layer decoupled from Infrastructure implementation details, and it's
enforced by `tests/ServiceHub.UnitTests/Architecture/ApiLayerBoundaryTests.cs`.

**`Message.ApplicationProperties`** (`IReadOnlyDictionary<string, object>?`) is already a generic
attribute bag. Kafka headers or RabbitMQ headers map onto it directly — don't add new typed
fields to `Message` for a new provider's header/attribute concept; put them here.

**`ConnectionAuthType`** reserves a numeric band per provider (`0-9` Azure, `10-19` AWS, `20-29`
GCP) specifically so a new provider can claim the next band (`30-39`) without renumbering
anything.

---

## What every new provider must implement

1. **`ICloudMessagingProvider`** (`ServiceHub.Core.Interfaces`) — `ProviderType`, `Capabilities`,
   `ValidateConnectionAsync`, `ListEntitiesAsync`, `GetMessageReceiver()`, `GetMessageSender()`.
2. **`IMessageReceiver`** / **`IMessageSender`** — peek, count, dead-letter, replay, purge,
   scheduled-message lookup, send/send-batch.
3. **A `CloudProviderType` enum value** and, per §"Places that branch on `CloudProviderType`"
   below, updates to the ~15 call sites that make provider-specific decisions (not the ~15+
   registration/status checks, which are already generic).
4. **A `ProviderCapabilities` declaration** (see next section) — this is the one piece that
   didn't exist for the first three providers and is now required for a new one.

### Declare your `ProviderCapabilities` — don't skip this

`ServiceHub.Core.Models.ProviderCapabilities` (added in Phase 3) is the single source of truth
for "what does this provider actually support." Before this existed, Azure/AWS/GCP differences
were expressed as duplicated, drifting logic: a backend `Result.Failure` message in one place, a
hardcoded `Record<string, {title, detail}>` map in `ScheduledMessagesPage.tsx`, and a bare
`cloudProvider === 'aws' || 'gcp'` boolean in `MessageDetailPanel.tsx` — three independent facts
about the same four questions, none of which the compiler or a test could keep in sync.

Declare yours as a static preset, the same way Azure/AWS/GCP do:

```csharp
// ServiceHub.Core/Models/ProviderCapabilities.cs
public static readonly ProviderCapabilities Kafka = new(
    SupportsMessageCounts: false,      // no live count API without an admin client call
    SupportsManualDeadLetter: false,   // no broker-native DLQ concept — consumers build their own
    SupportsPurge: false,              // no single-message delete by offset
    SupportsScheduledMessages: false,  // no native delayed delivery
    SupportsRepeatablePeek: false,     // consumer group offsets move on every fetch — no true non-destructive peek
    Notes: "Kafka has no dead-letter, purge, scheduling, or non-destructive peek primitives — these are consumer-side patterns, not broker features.");
```

`SupportsRepeatablePeek` gates Live Tail and the Messages page's auto-refresh: `true` means peeking
this provider on a short repeating interval (every few seconds, indefinitely) is safe with no side
effects that accumulate. Get this wrong in the unsafe direction and repeated polling can silently
push messages toward the entity's own redelivery limit and dead-letter them — this is exactly what
happened conceptually with AWS SQS, whose peek is actually a receive that increments
`ReceiveCount`, and it is why AWS declares `SupportsRepeatablePeek: false` while Azure and GCP (whose
peek implementations are genuinely re-queuing/non-destructive) declare `true`.

Then your provider's `Capabilities` getter is one line: `Capabilities => ProviderCapabilities.Kafka;`

The frontend consumes this via `useProviderCapabilities()` (`apps/web/src/hooks/useCloudBridge.ts`)
and `getProviderCapabilities(map, provider)` (`apps/web/src/lib/api/cloudBridge.ts`) — wire your
new provider into any UI that currently gates on a capability (purge buttons, scheduling panels,
message-count displays) by adding your `ProviderCapabilities` preset; **do not** add another
`cloudProvider === 'kafka'` branch next to the ones this pattern replaced.

If your provider genuinely can't answer a capability question truthfully (e.g. no admin API
available at runtime), be honest about it rather than defaulting to `true` — the whole point is
that a `false` here disables the affected UI/API path with an explanation, instead of the
operation silently failing or returning a fabricated value later.

---

## Constraints the first three providers hit — plan around these

These aren't blockers, but they are real friction every non-Azure provider has hit so far, and
whoever builds the next one should expect the same:

- **Message identity is a single `long` sequence number.** `IMessageReceiver.ReplayMessageAsync`
  and `PurgeMessageAsync` take `sequenceNumber: long`. Azure has a genuine provider-issued
  sequence number; AWS and GCP don't (SQS receipt handles and Pub/Sub ack IDs rotate on every
  delivery), so both **SHA-256-hash the stable `MessageId` into a `long`** purely to satisfy this
  signature, then **linearly re-scan the whole entity** at replay/purge time to find the message
  matching that hash (see `AwsMessageReceiver.FindAndLockMessageAsync` /
  `GcpMessageReceiver.FindAndLockMessageAsync`). Kafka's natural identity is `(partition, offset)`
  — a compound key — which doesn't fit a single `long` any more cleanly. If your provider has the
  same problem, follow the existing hash-and-rescan pattern for consistency rather than inventing
  a third approach; a proper fix (an opaque cursor type instead of `long`) is a real but breaking
  change tracked as future work, not something to attempt inside a single provider addition.
- **No non-destructive peek.** Only Azure has a true peek-without-locking primitive. AWS/GCP fake
  it by receiving with a short visibility timeout / ack deadline, then immediately releasing it
  (`ChangeMessageVisibilityBatchAsync(...=0)` / `ModifyAckDeadlineAsync(...=0)`). If your broker
  has no peek equivalent either, this receive-then-release pattern is the established one to copy.
- **Credentials are a single opaque encrypted string.** `Namespace.ConnectionString` is one
  `string?`, parsed differently per provider (`CloudCredentialValidator` in
  `ServiceHub.Core.Validation`, dispatched via a manual `switch` in
  `NamespacesController.Create`). This works for a connection string, an access-key pair, or a
  service-account JSON blob, but not cleanly for anything needing *multiple* independent secrets
  (Kafka SASL_SSL commonly needs bootstrap servers + SASL credentials + a separate TLS
  truststore/keystore; mTLS needs a client cert + key + CA bundle as three distinct PEM blobs).
  The established workaround for "one more piece of config beyond the credential blob" is another
  nullable field directly on `Namespace` (see `AwsRegion`, `GcpProjectId`) — acceptable for one
  extra string, but don't force a multi-secret credential model through that pattern. If your
  provider needs real multi-secret auth, that's a legitimate case for extending the credential
  model properly rather than bolting on more nullable strings; flag it rather than working around
  it silently.
- **`ServiceBusEntityType` is a closed 2-value enum** (`Queue`, `Subscription`) used on the
  persisted `DlqMessage` row. AWS/GCP already had to be force-fit into it (SNS/Pub/Sub
  "subscriptions" both map to `Subscription`). A broker with a genuinely different topology
  (Kafka consumer groups, RabbitMQ exchange+binding+queue) will need this enum widened — search
  for every `switch`/`if` on it before changing it, since `DlqMonitorService` and the DLQ
  timeline both depend on its current two values.
- **`CloudEntity.Name` encodes hierarchy as a path string** for Azure subscriptions
  (`"topic/subscriptions/sub"`) rather than exposing parent/child as separate fields. This is why
  `CrossCloudTraceController`'s Azure and non-Azure search paths remain separate (see
  `docs/FLOW.md` §2) — unifying them needs this resolved first.

## Places that branch on `CloudProviderType` — expect to touch these

A registered provider needs zero changes to `CloudProviderRouter`, `Program.cs`'s DI wiring, or
`CloudBridgeController.GetProviderStatus`/`GetCapabilities` (both already iterate/declare all
three known types generically — extend them to a 4th key the same way). What genuinely needs a
new arm for real business logic (not just registration bookkeeping):

- `DlqMonitorService` — provider-specific DLQ-scan conventions (Azure's `DeadLetterCount`
  short-circuit, GCP's `-dlq` naming convention, sequence-key-vs-MessageId dedup).
- `TopicsController` / `SubscriptionsController` / `QueuesController` — currently Azure-only
  (`if (ns.Provider != CloudProviderType.Azure) return BadRequest(...)`); AWS/GCP entity access
  goes through `CloudBridgeController` instead. A new provider needs the same choice made
  explicitly, not silently inherited.
- `Namespace.Create`'s auth-type inference switch and `CreateNamespaceRequest.Validate()`'s
  provider-conditional validation blocks.
- Frontend: `ConnectPage.tsx` is by far the most invasive file (three parallel onboarding form
  sections, one per provider, with independent local state) — budget real UI work here, it will
  not be a drop-in `Record` extension the way `providerStyles.tsx`/`providerTheme.ts` are.

---

## Testing checklist for a new provider

Mirror the existing provider test suites (`tests/ServiceHub.UnitTests/Infrastructure/{Azure,Aws,Gcp}`):

- `ProviderType_ReturnsX` and `Capabilities_ReflectsXConstraints` (assert every
  `ProviderCapabilities` boolean, not just the ones that happen to differ from Azure).
- Connection validation success/failure paths.
- `ListEntitiesAsync` mapping to `CloudEntity`.
- Peek/replay/purge/dead-letter behavior, including whatever workaround your provider needs for
  the message-identity and non-destructive-peek constraints above.
- A `Simulated<X>MessagingProvider` implementing the same `ICloudMessagingProvider` contract
  in-memory, registered by `AddSimulatorProviders()` — this is how the whole product gets
  exercised end-to-end without live credentials (see `ServiceHub.Simulator/Providers/`), and it's
  the fastest way for reviewers/CI to verify a new provider actually works.
