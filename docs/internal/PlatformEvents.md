# ServiceHub — Internal Platform Events

> **Status:** Phase 2 — Namespace publishers live. No subscribers yet.  
> **Location:** `docs/internal/PlatformEvents.md`

---

## Overview

Platform Events are **pure in-process notifications** that flow between ServiceHub modules
through a bounded `Channel<PlatformEvent>`. They are not Azure Event Grid events, not
Azure Service Bus messages, and not backed by any external broker. They are not visible
to end users.

The goal is to decouple producers of domain facts (namespace created, DLQ spike detected)
from consumers of those facts (webhook notifier, audit enricher, alert engine, SSE stream,
AI agents) without adding cross-cutting coupling to every service class.

---

## Architecture

```
Publisher (Controller / BackgroundService)
    │
    │  IPlatformEventBus.PublishAsync(event)   ← non-blocking, fire-and-forget
    ▼
Channel<PlatformEvent>                         ← bounded, 4096 capacity, DropOldest
    │
InProcessPlatformEventBus.ExecuteAsync()       ← BackgroundService drain loop
    │
    ├── Subscriber A  (Phase 3: WebhookDlqSpikeHandler)
    ├── Subscriber B  (Phase 4: AlertEngine)
    ├── Subscriber C  (Phase 5: SSE push pipeline)
    └── Subscriber D  (Phase 6: AI / analytics sink)
```

### Layer Ownership

| Concern | Layer | File |
|---|---|---|
| Envelope definition | `ServiceHub.Core` | `Events/PlatformEvent.cs` |
| Event type constants | `ServiceHub.Core` | `Events/EventTypes.cs` |
| Category constants | `ServiceHub.Core` | `Events/EventCategories.cs` |
| Severity enum | `ServiceHub.Core` | `Events/EventSeverity.cs` |
| Payload records | `ServiceHub.Core` | `Events/Payloads/*.cs` |
| Bus contract | `ServiceHub.Core` | `Interfaces/IPlatformEventBus.cs` |
| Bus implementation | `ServiceHub.Infrastructure` | `Events/InProcessPlatformEventBus.cs` |
| DI registration | `ServiceHub.Infrastructure` | `DependencyInjection.cs` |

The `Api` layer knows only `IPlatformEventBus`. It never references `InProcessPlatformEventBus`
or `System.Threading.Channels` directly. Clean Architecture boundaries are preserved.

---

## Hybrid Event Model

ServiceHub adopts a **hybrid model** inspired by [CloudEvents 1.0](https://cloudevents.io):

- **Publishers** work with **strongly-typed payload records** (e.g. `NamespaceCreatedPayload`).
- **The bus** transports a **single `PlatformEvent` envelope** that is provider- and
  payload-agnostic.
- **Subscribers** use `PlatformEvent.EventType` as a discriminator before casting `Payload`.

This gives compile-time safety at publish sites and a uniform wire format at the bus boundary —
exactly what is needed when adding future providers (Kafka, IBM MQ) or external consumers
(SSE, AI agents, analytics sinks) that must not be coupled to C# type hierarchies.

---

## Envelope Fields

| Field | Type | Description |
|---|---|---|
| `Id` | `Guid` | Unique event instance identifier. Auto-generated. |
| `Version` | `int` | Envelope schema version. Currently `1`. |
| `OccurredUtc` | `DateTimeOffset` | When the domain fact occurred. Set by publisher. |
| `Source` | `string` | Origin class: e.g. `ServiceHub.Infrastructure.DlqMonitorWorker`. |
| `Category` | `string` | Top-level grouping constant from `EventCategories`. |
| `EventType` | `string` | Dotted canonical name from `EventTypes`. Primary discriminator. |
| `Severity` | `EventSeverity` | Operational significance (Info / Warning / Error / Critical). |
| `CloudProvider` | `string?` | Provider string: `"azure"`, `"aws"`, `"gcp"`. |
| `NamespaceId` | `Guid?` | Namespace context, if applicable. |
| `NamespaceName` | `string?` | Snapshot of namespace display name. |
| `CorrelationId` | `string?` | Propagated HTTP or job correlation ID. |
| `Actor` | `string?` | User identity or `"System:<RuleName>"` for automation. |
| `TargetScope` | `string?` | Queue/topic/rule path targeted by the event. |
| `Metadata` | `IReadOnlyDictionary<string, string>?` | Additional key-value context. No secrets. |
| `Payload` | `object?` | Typed payload record. Cast using `EventType` discriminator. |

`CloudProvider` is `string?` (not the `CloudProviderType` enum) so that future providers
(Kafka, RabbitMQ, IBM MQ) do not require a Core assembly change.

---

## Event Type Naming Convention

```
servicehub.{category}.{verb}.{version}
```

- All lowercase.
- Dots as segment separators — no underscores, no hyphens.
- Past-tense verb — events record facts, not commands.
- Explicit version suffix — consumers filter on prefix; they can ignore `v2` until ready.

### Current Event Types

| Constant | Value |
|---|---|
| `EventTypes.NamespaceCreated` | `servicehub.namespace.created.v1` |
| `EventTypes.NamespaceDeleted` | `servicehub.namespace.deleted.v1` |
| `EventTypes.DlqMessageDetected` | `servicehub.dlq.message.detected.v1` |
| `EventTypes.DlqSpikeDetected` | `servicehub.dlq.spike.detected.v1` |
| `EventTypes.ReplayCompleted` | `servicehub.replay.completed.v1` |
| `EventTypes.RuleMatched` | `servicehub.rule.matched.v1` |

---

## Event Lifecycle

```
1. Domain fact occurs (namespace saved, DLQ scan completes, replay finishes)
2. Publisher checks IsSuccess / commit guard
3. Publisher calls IPlatformEventBus.PublishAsync(new PlatformEvent { ... })
4. TryWrite enqueues the envelope into Channel<PlatformEvent>          [non-blocking]
5. BackgroundService drain loop dequeues the envelope
6. Drain loop iterates registered subscribers in registration order
7. Each subscriber receives the envelope and filters by EventType
8. Subscriber executes its domain logic (webhook call, audit write, SSE push, ...)
9. Subscriber failures are caught, logged, and swallowed
10. Drain loop continues to next event
```

---

## Publish-After-Commit Rule

**Events must only be published after the state-change has been durably committed.**
Publishing before commit risks phantom events for rolled-back operations.

| Publisher Location | Commit Gate | Event to Publish |
|---|---|---|
| `NamespacesController.Create` | `IsSuccess` after `_namespaceRepository.AddAsync` | `NamespaceCreated` |
| `NamespacesController.Delete` | `IsSuccess` after `_namespaceRepository.DeleteAsync` | `NamespaceDeleted` |
| `DlqMonitorWorker` scan loop | `scanResult.Value > 0` (already used as the webhook gate) | `DlqMessageDetected`, `DlqSpikeDetected` |
| `AutoReplayExecutor.ExecuteAsync` | `replayResult.IsSuccess` guard | `ReplayCompleted` |
| `DlqMonitorWorker` rule-match loop | `replayResult.IsSuccess` guard | `RuleMatched` |

---

## Folder Structure

```
services/api/src/
├── ServiceHub.Core/
│   ├── Events/
│   │   ├── EventCategories.cs
│   │   ├── EventSeverity.cs
│   │   ├── EventTypes.cs
│   │   ├── PlatformEvent.cs
│   │   └── Payloads/
│   │       ├── DlqMessageDetectedPayload.cs
│   │       ├── DlqSpikeDetectedPayload.cs
│   │       ├── NamespaceCreatedPayload.cs
│   │       ├── NamespaceDeletedPayload.cs
│   │       ├── ReplayCompletedPayload.cs
│   │       └── RuleMatchedPayload.cs
│   └── Interfaces/
│       └── IPlatformEventBus.cs          ← alongside IAuditService, IWebhookNotifier
└── ServiceHub.Infrastructure/
    └── Events/
        └── InProcessPlatformEventBus.cs  ← alongside ServiceBus/, AI/, Security/
```

---

## Dependency Injection

`AddPlatformEvents()` registers three bindings against the same singleton instance:

```
InProcessPlatformEventBus  →  AddSingleton<InProcessPlatformEventBus>()
IPlatformEventBus          →  AddSingleton resolved from InProcessPlatformEventBus
IHostedService             →  AddHostedService resolved from InProcessPlatformEventBus
```

This is identical to the `AuditService` registration pattern (lines 172–174 of
`DependencyInjection.cs`). The `IHostedService` registration starts the drain loop
with the application.

---

## Future: SSE Integration

When SSE is introduced, an `SsePlatformEventHandler` will be registered as a subscriber.
It will maintain a thread-safe collection of active SSE response streams and push
serialised `PlatformEvent` envelopes to each connected client.

The envelope's uniform structure means the SSE endpoint requires no event-type-specific
serialisation logic. Every event type is pushed as the same JSON shape.

No changes to `InProcessPlatformEventBus`, `IPlatformEventBus`, or any payload record
are required to enable SSE.

---

## Future: Alert Engine

The Alert Engine will be a subscriber that evaluates `DlqSpikeDetectedPayload` and
`ReplayCompletedPayload` against configurable threshold rules and dispatches notifications
(email, PagerDuty, Slack) independently of the webhook notifier.

Registering it requires only calling `IPlatformEventBus.Subscribe(alertEngineHandler)` during
startup — no changes to publishers or existing subscribers.

---

## Future: Agentic AI

An AI event consumer can subscribe to the bus as a standard `Func<PlatformEvent, CancellationToken, Task>`.
The uniform `PlatformEvent` envelope is directly compatible with LLM function-calling
(single tool schema), RAG indexing (uniform document shape), and agent orchestration
frameworks (Semantic Kernel, AutoGen). The `EventType` string and structured payload
JSON allow a planner to reason about cross-provider patterns without provider-specific logic.

---

## Future: Analytics Sink

An analytics subscriber writes `PlatformEvent` envelopes to a time-series store or
event log. The homogeneous envelope schema (`EventType`, `Source`, `OccurredUtc`, `Payload`)
supports schema-on-read querying without per-event-type projections.

---

## What Phase 1 Does NOT Include

- No publishers wired into existing controllers or background workers.
- No subscribers registered.
- No SSE endpoints.
- No Alert Engine.
- No AI consumer.
- No external broker.
- No changes to `DlqMonitorWorker`, `AutoReplayExecutor`, `RulesController`,
  `NamespacesController`, `AuditService`, or `WebhookNotifier`.

---

## Phase 2 — Namespace Publishers

### Current Publishers

| Publisher | File | Event Published | Publish Gate |
|---|---|---|---|
| `NamespacesController.Create` | `Api/Controllers/V1/NamespacesController.cs` | `servicehub.namespace.created.v1` | `AddAsync` returns `IsSuccess` |
| `NamespacesController.Delete` | `Api/Controllers/V1/NamespacesController.cs` | `servicehub.namespace.deleted.v1` | `DeleteAsync` returns `IsSuccess` |

Both publishers respect the **publish-after-commit** rule. No event is ever published
if the underlying repository operation fails.

`IPlatformEventBus` is injected as an **optional constructor parameter** (`IPlatformEventBus? eventBus = null`),
consistent with the `IAuditLogger` injection pattern already in the codebase.
When `null` (e.g. in unit tests that do not inject the bus), the publish block is skipped entirely.

Debug-level log lines are emitted on every successful publish:
```
Published Platform Event {EventType} for NamespaceId {NamespaceId} CorrelationId {CorrelationId}
```
Payload contents and secrets are never logged.

### Current Subscribers

**None.** Events flow through the bus and are discarded by the drain loop.
This is intentional — Phase 2 proves the publisher wiring without any side effects.

### Future Publishers

The following publish sites are identified but **not yet wired**. They will be added
in Phase 3 and beyond:

| Future Publisher | Event | Phase |
|---|---|---|
| `DlqMonitorWorker` (scan loop) | `servicehub.dlq.message.detected.v1` | Phase 3 |
| `DlqMonitorWorker` (scan loop) | `servicehub.dlq.spike.detected.v1` | Phase 3 |
| `AutoReplayExecutor.ExecuteAsync` | `servicehub.replay.completed.v1` | Phase 3 |
| `DlqMonitorWorker` (rule-match loop) | `servicehub.rule.matched.v1` | Phase 3 |
| `NamespacesController.TestConnection` | `servicehub.namespace.connection.validated.v1` | Phase 4 |

> **Note on `TestConnection`:** The endpoint exists at `GET {id}/test` and
> `POST {id}/test-connection`. A `servicehub.namespace.connection.validated.v1`
> event type should be added to `EventTypes.cs` in Phase 4 before this publisher is wired.
> No payload class is needed in Phase 2.
