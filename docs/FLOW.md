# ServiceHub — Current Request & Provider Flow

This document describes, as verified directly against source in this repository, how a request
gets from an API controller to a cloud provider's SDK today. It exists because the controllers
that touch cloud messaging do **not** all use the same path — this is the single place that spells
out exactly which ones do and which don't, so that doesn't have to be re-derived from the
codebase (or guessed from a diagram) every time.

Not every controller or background service is covered — only the ones that route to a cloud
provider, or that this doc's callers (CHANGELOG.md, docs/COMPREHENSIVE-GUIDE.md) link here for.

---

## The short version

| Component | Path to the cloud provider |
|---|---|
| `MessagesController`, `QueuesController`, `TopicsController` | → `IMessageOperationsService` → `CloudProviderRouter` → `ICloudMessagingProvider` |
| `CrossCloudTraceController` | → **its own dispatch**: Azure via `IAzureTraceSearcher`, non-Azure via a directly-injected `IEnumerable<ICloudMessagingProvider>` — **not** through `CloudProviderRouter` |
| `DlqHistoryController` | → `IDlqHistoryService` → SQLite (`DlqDbContext`) — **no cloud SDK call at all**, it only reads/writes locally persisted DLQ Intelligence history |
| `DlqMonitorWorker` (background) | → `DlqMonitorService.ScanNamespaceAsync` → `CloudProviderRouter.Resolve(namespace.Provider)` — provider-aware; a namespace is skipped (with a log line) only if its provider has no `ICloudMessagingProvider` registered on this server at all |
| `SimulatorController` | → in-memory simulator store, reachable only when `ASPNETCORE_ENVIRONMENT=Simulator` |

The rest of this doc walks through each row.

---

## 1. Messages / Queues / Topics — the unified path

```mermaid
%%{init: {'theme':'dark'}}%%
graph LR
    C1["MessagesController"] --> IMOS
    C2["QueuesController"] --> IMOS
    C3["TopicsController"] --> IMOS
    IMOS["IMessageOperationsService<br/>(MessageOperationsService)"] --> CPR
    CPR["CloudProviderRouter<br/>.Resolve(namespace.Provider)"] --> AZP["AzureMessagingProvider"]
    CPR --> AWSP["AwsMessagingProvider"]
    CPR --> GCPP["GcpMessagingProvider"]
    AZP --> AZR["Polly retry pipeline<br/>(inline in MessageReceiver/Sender)"] --> AZSDK["Azure.Messaging.ServiceBus"]
    AWSP --> AWSR["AwsResiliencePipeline"] --> AWSSDK["AWSSDK.SQS / AWSSDK.SNS"]
    GCPP --> GCPR["GcpResiliencePipeline"] --> GCPSDK["Google.Cloud.PubSub.V1"]
```

- All three controllers depend on `IMessageOperationsService` only — no controller holds a
  per-provider `if (namespace.Provider == ...)` branch anymore. `CloudProviderRouter.Resolve()`
  throws `InvalidOperationException` if a provider isn't registered; `IsRegistered()` lets callers
  check first without triggering the exception.
- **Every provider retries transient errors the same way**: 3 attempts, exponential backoff
  (1s base, 30s cap), jitter enabled.
  - Azure: retry logic lives inline in `MessageReceiver`/`MessageSender` (`ServiceHub.Infrastructure/ServiceBus`).
  - AWS: `AwsResiliencePipeline.Create()` — retries `AmazonServiceException` when the SDK marks it
    retryable, or on 5xx/429.
  - GCP: `GcpResiliencePipeline.Create()` — retries `RpcException` for `Unavailable`,
    `DeadlineExceeded`, `Internal`, `ResourceExhausted`, `Aborted`.
- **Provider registration today**: `AddAzureProvider()` is always called from `Program.cs`.
  `AddAwsProvider()` / `AddGcpProvider()` are called when `CloudProviders:Aws:Enabled` /
  `CloudProviders:Gcp:Enabled` is set (both default `false` in `appsettings.json`); enabling a
  flag registers that provider's `ICloudMessagingProvider`, client factory, and connectivity
  health check. Simulator mode (`ASPNETCORE_ENVIRONMENT=Simulator`) registers all three via
  `AddSimulatorProviders()` regardless of the flags — still the recommended way to exercise the
  AWS/GCP code paths end-to-end without live credentials.

### Provider-specific behavior worth knowing

| Operation | Azure | AWS SQS/SNS | GCP Pub/Sub |
|---|---|---|---|
| Peek dead-letter messages | ✅ | ✅ | ✅ (via convention subscription `{name}-dlq`) |
| Manual dead-letter (move a message to DLQ on demand) | ✅ | ✅ (`MaxReceive` redrive) | ❌ — Pub/Sub dead-lettering is policy-driven via `MaxDeliveryAttempts`; `DeadLetterMessagesAsync` returns a `Validation` failure explaining this |
| Message count | ✅ | ✅ | Normalized to `Success(0)` — Pub/Sub has no direct count API; mirrors the "unsupported read" convention used by `GetScheduledMessagesAsync` |

---

## 2. Cross-Cloud Trace — a deliberately separate path

```mermaid
%%{init: {'theme':'dark'}}%%
graph LR
    CCT["CrossCloudTraceController"] -->|"namespace.Provider == Azure"| ATS["IAzureTraceSearcher<br/>(AzureTraceSearcher)"]
    CCT -->|"namespace.Provider != Azure"| CMP["IEnumerable&lt;ICloudMessagingProvider&gt;<br/>injected directly, matched by ProviderType"]
    ATS --> AZSDK["Azure.Messaging.ServiceBus"]
    CMP -->|"provider registered"| PSDK["ListEntitiesAsync + PeekMessagesAsync<br/>per AWS/GCP entity"]
    CMP -->|"provider NOT registered"| SKIP["SkipReason recorded in the<br/>namespace summary — not an error"]
```

`CrossCloudTraceController` does **not** use `IMessageOperationsService` or `CloudProviderRouter`.
It resolves namespaces by provider itself and dispatches to two different code paths:

- **Azure namespaces** search in parallel (max 5 concurrent, 30s overall timeout) via
  `IAzureTraceSearcher` — extracted into its own service so the controller only orchestrates and
  aggregates results.
- **Non-Azure namespaces** are matched against the injected `IEnumerable<ICloudMessagingProvider>`
  by `ProviderType`. If no provider is registered for that type, the namespace is marked
  `WasSearched: false` with a human-readable `SkipReason` (e.g. `"AWS provider is not enabled on
  this server."`) rather than failing the whole trace or silently omitting the namespace.

This means AWS/GCP node search in Cross-Cloud Trace is **fully implemented**, not a "coming later"
feature — it's gated purely on whether `AddAwsProvider()`/`AddGcpProvider()` are called for the
running instance (see §1). Enable Simulator mode, or register a provider, to see it work
end-to-end.

**Why this hasn't been unified with §1's router**: no functional reason found in source — it
predates the `IMessageOperationsService`/`CloudProviderRouter` introduction and was carried forward
as-is. Flagging it here rather than describing it as unified elsewhere in the docs.

---

## 3. DLQ Intelligence — no cloud SDK involved

```mermaid
%%{init: {'theme':'dark'}}%%
graph LR
    DHC["DlqHistoryController<br/>(GetHistory/GetById/Timeline/Notes/Summary/Export)"] --> IDHS["IDlqHistoryService<br/>(DlqHistoryService)"]
    IDHS --> DB[("SQLite — DlqDbContext<br/>table: DlqMessages")]
```

- Every method on `IDlqHistoryService` takes `ownerId` and filters on it — `GetHistoryAsync`,
  `GetByIdAsync`, `GetTimelineAsync`, `UpdateNotesAsync`, `GetSummaryAsync`, `ExportAsync`. A caller
  can only see or modify DLQ Intelligence records for their own owner ID; a different owner's
  message ID returns `NotFound`, not the record.
- `DlqMessage` (the row persisted per dead-lettered message) has a `CloudProvider` column
  (`CloudProviderType`, defaults to `Azure` for rows written before the column existed). It's
  populated by whichever background process detected the dead-letter — `DlqMonitorWorker`, which
  scans every registered provider (see §4), so rows are attributed to the namespace's real
  provider (`Azure`/`Aws`/`Gcp`), not defaulted.
- This whole path never calls a cloud SDK — it's a read/write against the local SQLite database
  only. `DlqMonitorWorker` is what populates that database in the first place.

---

## 4. Background DLQ monitoring — provider-aware

`DlqMonitorWorker` polls active namespaces on an interval, scans each for newly dead-lettered
messages, and persists findings via `DlqHistoryService`/`DlqDbContext`. `DlqMonitorService.ScanNamespaceAsync`
resolves the namespace's provider through `CloudProviderRouter` — the same router `IMessageOperationsService`
uses in §1 — rather than hardcoding Azure:

- A namespace is skipped up front (not attempted-then-failed) only if `CloudProviderRouter.IsRegistered`
  returns false for its provider (e.g. the `CloudProviders:Aws:Enabled` / `:Gcp:Enabled` flags are off
  on this server) — with a log line noting why.
- Provider-specific conventions inside the scan: Azure short-circuits via the entity's own
  `DeadLetterCount`; AWS and GCP peek their DLQ side directly (GCP resolves the dead-letter
  subscription dynamically from the source subscription's `DeadLetterPolicy` — there's no fixed
  naming convention for it in real Pub/Sub).
- This means DLQ Intelligence (§3), 30-day trend, and Auto-Replay Rules all operate on real-time
  Azure **and** AWS/GCP dead-letter data whenever those providers are registered — not Azure only.

---

## 5. Simulator mode — environment-gated, not just hidden

```mermaid
%%{init: {'theme':'dark'}}%%
graph LR
    ENV{"ASPNETCORE_ENVIRONMENT<br/>== Simulator ?"}
    ENV -->|"yes"| DI["AddSimulatorProviders()<br/>registers in-memory Azure+AWS+GCP providers<br/>+ simulator store/clock/seeder"]
    ENV -->|"yes"| ROUTE["SimulatorOnlyAttribute<br/>(IActionConstraint) allows the route to match"]
    ENV -->|"no"| BLOCKED["SimulatorController actions don't match routing → 404<br/>(SimulatorOnlyAttribute fails closed)"]
```

Two independent layers both have to agree for Simulator endpoints to work, which is why this is
described as defense-in-depth rather than a single check:

1. **DI registration** (`Program.cs`): `AddSimulatorProviders()` is only called when
   `builder.Environment.IsEnvironment("Simulator")`. Outside that environment, the services
   `SimulatorController` depends on aren't registered at all.
2. **Routing** (`SimulatorOnlyAttribute`): an `IActionConstraint` on `SimulatorController` that
   only accepts the action when `IWebHostEnvironment.IsEnvironment("Simulator")` — if the check
   fails, or the environment service can't be resolved, the action doesn't match and ASP.NET Core
   returns a plain 404, not a 403 (so the existence of Simulator-only routes isn't even
   distinguishable from "route doesn't exist" in Production).

---

## 6. Rate limiting — owner-scoped, not IP-scoped

`RateLimitingMiddleware` keys its bucket on the authenticated `OwnerId` (from `HttpContext.Items`,
populated by the auth middleware earlier in the pipeline) when one is present — `"owner:{id}"` —
and falls back to the remote IP address — `"ip:{addr}"` — only for unauthenticated requests.
`X-Forwarded-For` is never trusted for this, since it's trivially spoofable by the client. This
matters behind a reverse proxy (e.g. Azure App Service): without owner-keying, every request would
present the same proxy IP and all tenants would share one bucket. Default limit: 300 requests/min
(`RateLimit:MaxRequests`, `RateLimit:WindowDuration` in `appsettings.json`).

---

## Sources checked for this document

`MessagesController.cs`, `QueuesController.cs`, `TopicsController.cs`, `CrossCloudTraceController.cs`,
`DlqHistoryController.cs`, `MessageOperationsService.cs`, `CloudProviderRouter.cs`,
`ICloudMessagingProvider.cs`, `AzureTraceSearcher.cs`/`IAzureTraceSearcher.cs`,
`GcpMessageReceiver.cs`, `AwsMessageReceiver.cs`, `AwsResiliencePipeline.cs`, `GcpResiliencePipeline.cs`,
`DlqMonitorWorker.cs`, `DlqHistoryService.cs`, `IDlqHistoryService.cs`, `DlqMessage.cs`,
`SimulatorOnlyAttribute.cs`, `SimulatorController.cs`, `Program.cs`, `RateLimitingMiddleware.cs`,
`appsettings.json` — all read directly in the session that produced this document (2026-07-07).
If behavior here looks wrong, re-check these files rather than assuming this doc is still current.

**§3/§4 updated 2026-07-20**: `DlqMonitorService.cs` re-read directly against a live 3-cloud
session (Azure + AWS + GCP simultaneously connected and dead-lettering) — confirmed
`ScanNamespaceAsync` resolves the namespace's actual provider via `CloudProviderRouter` rather
than hardcoding Azure, contradicting the original 2026-07-07 text this doc shipped with.
