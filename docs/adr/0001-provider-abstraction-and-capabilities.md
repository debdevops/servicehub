# ADR-0001: Provider abstraction — one interface, per-provider declared capabilities

**Status:** Accepted

## Context

ServiceHub started as an Azure Service Bus tool and grew to cover AWS SQS/SNS and GCP Pub/Sub.
These are not interchangeable systems: Azure Service Bus has a true non-destructive peek and no
reliable single-message delete; AWS SQS has no non-destructive peek at all (every peek is a real
receive that counts toward the queue's redelivery limit) and does support single-message delete;
GCP Pub/Sub has no message-count API and policy-driven, not manual, dead-lettering.

Without a deliberate design, this asymmetry gets expressed as scattered conditionals —
`if (provider == "aws")` in a controller here, a duplicated `Record<string, {title, detail}>` map
in a frontend component there — each an independent, driftable fact about the same underlying
constraint.

## Decision

Every cloud integration implements a single interface, `ICloudMessagingProvider`
(`ServiceHub.Core.Interfaces`), covering connection validation, entity listing, and message
receive/send. Providers are registered generically through `CloudProviderRouter`, which resolves
`IEnumerable<ICloudMessagingProvider>` by `CloudProviderType` and requires no code change to add a
new registrant.

What each provider *actually supports* is declared once, as data, on
`ICloudMessagingProvider.Capabilities` — a `sealed record ProviderCapabilities` with named boolean
fields (`SupportsMessageCounts`, `SupportsPurge`, `SupportsRepeatablePeek`,
`CanProveDlqAbsence`, …) and a human-readable `Notes` explanation. `GET
/api/v1/cloud-bridge/capabilities` exposes the same record to the frontend, so backend gating and
UI copy read from one source instead of two independently maintained ones.

A capability that a provider genuinely cannot support is declared `false`, with a reason in
`Notes` — never approximated as `true` and left to fail at call time, and never silently defaulted
to the safest guess. A provider must not claim a capability it cannot prove.

## Alternatives considered

- **Per-provider feature flags checked ad hoc at each call site.** Rejected: this is what existed
  before `ProviderCapabilities` and is exactly the drift problem described above — three
  independent facts about the same four questions, none of which a compiler or test could keep in
  sync.
- **A generic plugin/capability-negotiation protocol** (providers advertise capabilities via a
  discovery call at runtime). Rejected as premature: three providers, all known at compile time,
  don't need runtime negotiation — this would add indirection with no current benefit, contradicting
  the "simplicity first" principle of building a component only when a named problem requires it.
- **Capability parity forced across providers** (e.g., synthesize a `Purge` action on Azure by
  best-effort receive-and-drop). Rejected outright: this would mean claiming a guarantee the SDK
  cannot deliver, undermining the honesty this exact model exists to preserve.

## Consequences

- Adding a provider means declaring one `ProviderCapabilities` preset and wiring it through the
  handful of call sites documented in `docs/extending/adding-a-provider.md` — not re-deriving
  asymmetry logic from scratch.
- A small number of call sites still branch on `CloudProviderType` directly instead of through a
  capability (e.g., topic/subscription support in three controllers) — this is tracked as
  technical debt against the model's own stated intent, not a rejection of the model.
- The model requires provider authors to be honest under pressure to ship a feature — a capability
  marked `false` visibly blocks UI/API behavior rather than letting an operation fail unpredictably
  later. This is the trade-off deliberately being made: a smaller feature surface that is provably
  correct, over a larger one that quietly lies in the AWS/GCP case.
