<div align="center">

# ServiceHub

### The Forensic Debugger for Cloud Messaging — Azure Service Bus (Supported) · AWS SQS/SNS & GCP Pub/Sub (Preview)

![ServiceHub Banner](docs/screenshots/servicehub-banner.png)

[![CI](https://github.com/debdevops/servicehub/actions/workflows/servicehub.yml/badge.svg)](https://github.com/debdevops/servicehub/actions/workflows/servicehub.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-purple.svg)](https://dotnet.microsoft.com/)
[![React 19](https://img.shields.io/badge/React-19-61dafb.svg)](https://react.dev/)
[![TypeScript](https://img.shields.io/badge/TypeScript-5-3178c6.svg)](https://www.typescriptlang.org/)
[![Version](https://img.shields.io/badge/version-3.3.0-brightgreen.svg)](.version)
[![Self-Hosted](https://img.shields.io/badge/Deployment-Self--Hosted-0078D4.svg)](#quick-start)

[⚡ Quick Start](#quick-start) · [✨ Core Capabilities](#core-capabilities) · [🌐 Multi-Cloud](#multi-cloud-bridge) · [🏗️ Architecture](#architecture) · [🛡️ Security](#security)

</div>

---

## Why ServiceHub?

Production breaks at 2 AM. Your cloud portal shows **5,000 messages in the Dead-Letter Queue** — but you can't read their bodies or search them without writing throwaway scripts. You manually sample messages one by one, spending hours on what should take minutes.

**ServiceHub is an ultra-fast, self-hosted web application that gives engineers full forensic visibility into their cloud message queues** — like a debugger, but for Azure Service Bus, AWS SQS/SNS, and GCP Pub/Sub.

> **Your cloud console shows you counts. ServiceHub shows you answers.**

> [!IMPORTANT]
> **Built for strict environments, single-operator by default.** Read-only by default (`Peek`, never consume) · connection strings AES-GCM-256 encrypted at rest · analysis runs entirely in your browser — no message data ever leaves your network ([telemetry](#telemetry-opt-in-vendor-neutral) is opt-in, disabled unless you enable it) · destructive actions (replay, send) blocked on production namespaces. **Every browser session shares one admin identity unless you turn on per-user identity** — OIDC (any standards-compliant IdP) or Azure Easy Auth, both off by default. Details in [Security](#security).

> [!TIP]
> **No credentials?** The Welcome page's **"Try a live demo"** buttons open a fully client-side demo walkthrough per cloud — no backend, no cloud account needed.

```mermaid
%%{init: {'theme':'dark', 'themeVariables': { 'fontSize':'22px', 'primaryTextColor':'#ffffff', 'fontFamily':'arial', 'lineColor':'#ffffff'}}}%%
graph TB
    subgraph UI["🌐 UI — React 19 SPA"]
        SPA["TanStack Query hooks<br/>Axios API client"]
    end

    subgraph Core["🧭 PROVIDER-NEUTRAL CORE"]
        ROUTER["CloudProviderRouter<br/>the extension point — one interface, N providers"]
        CAPS["ProviderCapabilities<br/>honest per-provider asymmetry"]
        SAFETY["Safety rails<br/>Peek-only by default · replay/send blocked on production namespaces"]
    end

    subgraph Providers["☁️ CLOUD PROVIDERS — same ICloudMessagingProvider contract"]
        AZ["Azure Service Bus<br/>GA"]
        AWS["AWS SQS / SNS<br/>Preview"]
        GCP["GCP Pub/Sub<br/>Preview"]
    end

    subgraph Storage["💾 PERSISTENCE — two stores, by design"]
        JSON["Namespaces<br/>JSON file"]
        SQLITE["DLQ history · audit · bulk ops<br/>SQLite"]
    end

    SPA --> ROUTER
    ROUTER --> CAPS
    ROUTER --> SAFETY
    ROUTER --> AZ
    ROUTER --> AWS
    ROUTER --> GCP
    ROUTER --> JSON
    ROUTER --> SQLITE

    style UI fill:#1565c0,stroke:#0d47a1,stroke-width:3px,color:#fff
    style Core fill:#388e3c,stroke:#1b5e20,stroke-width:3px,color:#fff
    style Providers fill:#d84315,stroke:#bf360c,stroke-width:3px,color:#fff
    style Storage fill:#004d40,stroke:#00695c,stroke-width:3px,color:#fff
```

---

## Deployment Model

ServiceHub is **self-hosted, single-instance software for one team** — not a multi-tenant SaaS
platform. Every deployment is one process: one SQLite database (DLQ history, auto-replay rules,
audit trail) and one in-process event bus, both scoped to that process's own lifetime. There is no
shared state between instances and no supported way to run two instances against the same data
directory.

This is a **deliberate choice for this release, not an omission**. It keeps the architecture
simple, the data local, and the operational surface small — the trade-off is no horizontal
scaling and no built-in multi-tenant isolation beyond the per-owner scoping OIDC/API keys already
provide. See [docs/KNOWN-LIMITATIONS.md](docs/KNOWN-LIMITATIONS.md) for the complete list of
what this does and doesn't support today.

---

## Try It

```bash
docker compose up --build
```

Open **[http://localhost:8080](http://localhost:8080)**, then connect a namespace with your own
cloud credentials. The port is bound to `127.0.0.1` (loopback) only by default, so it isn't
reachable from your network until you deliberately change that — see
[self-hosting/README.md](self-hosting/README.md) for a real deployment.

No credentials yet? The Welcome page's **"Try a live demo"** buttons open a fully client-side
demo walkthrough per cloud (`/demo/azure`, `/demo/aws`, `/demo/gcp`) — no backend calls, no
credentials, safe to click around before connecting anything real.

For connecting real cloud credentials, persistent storage, and production hardening, see
[Quick Start](#quick-start) below.

---

| Capability | Standard Cloud Portals | ServiceHub |
|---|---|---|
| View message body & content | ❌ Count only | ✅ Full body + syntax highlighting |
| Search across message content | ❌ Not available | ✅ Real-time full-text search |
| Dead-letter queue investigation | ❌ One at a time | ✅ Batch analysis + AI patterns |
| AI pattern detection | ❌ Not available | ✅ Client-side clustering, zero data sent |
| Replay from DLQ | ❌ Not available | ✅ One-click or auto-replay rules |
| Delete a single message | ❌ Not available | ✅ Purge (AWS & GCP; Azure SDK has no single-message delete) |
| Multi-namespace support | ❌ Portal only | ✅ Manage multiple connections |
| Correlation ID tracing | ❌ Not available | ✅ Trace journeys across all queues |
| Scheduled message management | ❌ Not available | ✅ View, reschedule, and cancel (Azure only — see the provider table below) |
| Cross-cloud message trace | ❌ Not available | ✅ Trace across Azure + AWS + GCP (AWS/GCP require an operator to enable them on the server) |

---

## Core Capabilities

Everything below serves three jobs: **Investigate** the failure, **Recover** the messages, **Prevent** the repeat. ServiceHub's deepest and most mature features are built natively for Azure Service Bus.

### 🔌 Connect in 30 Seconds — Zero Configuration
Enter your connection string once and you're browsing messages instantly. Supports Listen-only (read-only), Send, and Manage policies. Connection strings are **AES-GCM encrypted at rest** — no plain-text secrets stored anywhere.

### 📨 Message Browser — 1,000s of Messages at Your Fingertips
Browse **Active** and **Dead-Letter** queue messages side by side. See full message previews, status badges, enqueue times, and metadata in a virtualized grid that handles thousands of records without breaking a sweat. Auto-refresh every 7 seconds keeps your view live during incidents.

### 🔍 Forensic Message Inspection — Every Byte Visible
Click any message for complete forensic analysis:
- **Body** — Full JSON/XML with syntax highlighting and one-click copy.
- **Properties** — Message ID, sequence number, TTL, delivery count, enqueue time.
- **Headers** — All custom application properties and correlation IDs.
- **AI Insights** — Pattern context and remediation hints, computed entirely in-browser.

### 🤖 AI Findings — Detect Patterns Across Thousands of Messages
Click **AI Findings** to see error pattern clusters detected across your current queue view. The engine groups messages by error type, calculates confidence scores, and surfaces the most impactful clusters — so you know exactly where to look first.
> [!NOTE]
> **Zero-trust privacy:** All analysis runs entirely in your browser. No message content ever leaves your environment.

### 💀 Dead-Letter Queue Investigation & Recovery
Select the **Dead-Letter** tab to inspect failed messages in full. Each DLQ message shows exactly why the broker moved it, the full error text, the assessment in plain English, and one-click actions: **Replay** it back to the main queue after fixing the root cause, or **Purge** it permanently (AWS & GCP — Azure's SDK has no reliable single-message delete, so the action is disabled there rather than pretending).

### 📊 DLQ Intelligence — Persistent History & 30-Day Trends
DLQ Intelligence automatically scans your dead-letter queues and stores every finding in a local SQLite database — so you can track failures over time, not just during the current session. Features include a 30-day trend chart, auto-categorization (Transient, MaxDelivery, Expired, DataQuality, Authorization), and CSV/JSON exports.

### 🛰️ Fleet Operations — "What died overnight, across everything?"
One cross-namespace operations dashboard that aggregates dead-letter health across **all** your namespaces at once — the daily glance you open with your coffee, not just during an incident. See total active backlog, what's new in the last 24h–7d, a 7-day fleet trend, top failure categories, and a worst-first namespace table (severity, active count, top offending entity, oldest un-actioned message). Click any namespace to jump straight into its DLQ history.

### 🗂️ DLQ Triage Inbox
Turn the dead-letter history into a triage workflow. From any message, **Resolve**, **Archive**, or **Ignore** it — or **Reopen** something you triaged earlier — with the lifecycle status, timestamps, and notes tracked for you. Inbox-zero for dead letters.

### 🔁 Bulk Operations — Replay or Purge Thousands, With a Dry Run First
"Replay everything matching this filter" as a real workflow, not a one-message-at-a-time chore. Preview the exact match count and a sample before anything mutates, then run it as a cancellable background job with a live progress panel — no request timeout on large batches, no guessing what happened. Blocked in production namespaces and gated by provider capability (purge isn't offered where the provider can't reliably support it) exactly like single-message actions.

### ⚡ Auto-Replay Rules — Automate Your Recovery
Define rules that watch DLQ messages and automatically replay them when conditions match. Recover from common failures without manual intervention.
- **AI-generated rules** or pre-built templates for timeouts and throttles.
- **Flexible matching** by DLQ reason, error description, entity, delivery count, or regex.
- **Safety controls** with rate limiting to prevent overwhelming downstream services.

### 🔎 Real-Time Search & Correlation Explorer
Search across message body, properties, and headers instantly. Filter 1,000+ messages down to exactly what you need in under a second. Paste any Correlation ID to trace a message's full journey across all queues, topics, and namespaces.

### 🕐 Scheduled Messages
See every message queued for future delivery. Reschedule or cancel individual messages directly from the UI. Azure Service Bus only — AWS SQS (15-minute `DelaySeconds` cap, not inspectable) and GCP Pub/Sub (no scheduled delivery) show an explanatory panel instead of an empty table.

### 📈 Multi-Namespace Dashboard
One glance at every connected namespace — Azure, AWS, and GCP side by side, sorted by DLQ severity. Each card shows live active/DLQ/scheduled counts, a health badge, and one-click jumps into Browse Queues or DLQ History. Quick Actions surface the four things you reach for during an incident: Browse All DLQs, All Scheduled, Cross-Cloud Trace, Auto-Replay Rules.

### 📝 Audit Trail
Every critical operation — send, replay, purge, dead-letter, rule changes — is written to a persistent, per-owner audit log: timestamp, user, cloud/environment, action, resource, and outcome. Exportable, filterable, and isolated so one tenant can never see another's history.

### 🛡️ Security & Privacy Page
An in-app page that answers the trust question before anyone has to ask it: a diagram of exactly how data moves from browser → ServiceHub server → cloud SDK, what's encrypted (connection strings, AES-256-GCM), what's redacted from logs, and what's never stored (message bodies, plaintext secrets) — with links to verify each claim directly in the open-source code.

---

## Multi-Cloud Bridge

ServiceHub extends beyond Azure Service Bus to support **AWS SQS/SNS** and **GCP Pub/Sub** via the Cloud Bridge — a dedicated page that lists every queue, topic, and subscription for a selected non-Azure namespace in one provider-agnostic view, independent of the Correlation ID tracing described below.

| Provider | Status | Browse & Search | Dead-Letter | Replay | Purge | Send & Test Tools³ | Cross-Cloud Trace |
|----------|--------|-----------------|-------------|--------|-------|--------------------|-------------------|
| **Azure Service Bus** | ✅ GA | ✅ | ✅ | ✅ | — (SDK limitation) | ✅ | ✅ |
| **AWS SQS / SNS** | 🔶 Preview | ✅ | ✅ (redrive DLQ) | ✅ | ✅ | ✅ | ✅¹ |
| **GCP Pub/Sub** | 🔶 Preview | ✅ | ✅ peek (nack/ack deadline)² | ✅ | ✅ | ✅ | ✅¹ |

¹ Cross-Cloud Trace searches any namespace whose provider is registered in the API's dependency-injection container. Azure is always registered; AWS/GCP registration is disabled by default in this build — register the provider to exercise AWS/GCP trace search.
² GCP Pub/Sub dead-lettering is policy-driven via `MaxDeliveryAttempts`; ServiceHub reads the DLQ through the subscription's configured dead-letter topic, and its test tooling moves messages there by republishing through the subscription's dead-letter policy. Message counts are unavailable via the Pub/Sub API and are reported as `0`.
³ Test tools (send a message, generate realistic test data, push messages to the DLQ) are available only on **DEV** namespaces with a Manage-level connection — never in UAT or production.

**Preview** means: implemented and unit-tested, not validated against live AWS/GCP services in this project's own CI, capability-gated, no parity guarantee with Azure. See [docs/PROVIDER-SUPPORT.md](docs/PROVIDER-SUPPORT.md) for the full capability matrix, required IAM permissions, and the enabling flags — and [docs/KNOWN-LIMITATIONS.md](docs/KNOWN-LIMITATIONS.md) for every deliberate architectural trade-off in one place.

### 🌐 Cross-Cloud Trace
Connect namespaces from two or more cloud providers and use **Multi-Cloud Trace** to trace a single Correlation ID or message GUID as it routes from Azure $\rightarrow$ AWS $\rightarrow$ GCP (or any combination). The result is a visual routing path diagram, a chronological hop timeline, and a namespace search-coverage panel.
*(Azure namespaces are always searched in parallel. AWS and GCP namespaces are searched the same way whenever those providers are registered on the server; if a provider isn't registered, its namespaces are skipped with a reason shown in the search-coverage panel instead of being silently omitted.)*

---

## Visual Showcase

Every screenshot below is a real capture — live Azure Service Bus, AWS SQS/SNS, and GCP Pub/Sub namespaces connected to ServiceHub simultaneously, not mocked data. Click any image to open it full-size.

### 💀 The Core Story — 5,000 Dead Letters to a Fixed Root Cause

<table>
<tr>
<td width="33%"><a href="docs/screenshots/23-ServiceHub-DLQ-Populated-MultiCloud.png"><img src="docs/screenshots/23-ServiceHub-DLQ-Populated-MultiCloud.png" width="100%"/></a><br/><sub><b>1. Dead-Letter Queue</b> — populated with real failures, AI-tagged</sub></td>
<td width="33%"><a href="docs/screenshots/08-ServiceHub-Search-Messages.png"><img src="docs/screenshots/08-ServiceHub-Search-Messages.png" width="100%"/></a><br/><sub><b>2. Search</b> — filter thousands of messages down to exactly what you need</sub></td>
<td width="33%"><a href="docs/screenshots/18-ServiceHub-Message-Detail-Safety.png"><img src="docs/screenshots/18-ServiceHub-Message-Detail-Safety.png" width="100%"/></a><br/><sub><b>3. Open one</b> — full forensic properties; Replay correctly disabled on active messages</sub></td>
</tr>
<tr>
<td width="33%"><a href="docs/screenshots/03-ServiceHub-Message-Detail-Expanded.png"><img src="docs/screenshots/03-ServiceHub-Message-Detail-Expanded.png" width="100%"/></a><br/><sub><b>4. Read the body</b> — full JSON/XML with syntax highlighting</sub></td>
<td width="33%"><a href="docs/screenshots/07-ServiceHub-Auto-Replay-1.png"><img src="docs/screenshots/07-ServiceHub-Auto-Replay-1.png" width="100%"/></a><br/><sub><b>5. Replay it</b> — one-click, or an auto-replay rule for the whole cluster</sub></td>
<td width="33%"></td>
</tr>
</table>

### 🌐 Multi-Cloud

<table>
<tr>
<td width="33%"><a href="docs/screenshots/13-ServiceHub-MultiCloud-Dashboard.png"><img src="docs/screenshots/13-ServiceHub-MultiCloud-Dashboard.png" width="100%"/></a><br/><sub><b>Multi-Namespace Dashboard</b> — all 3 clouds, sorted by DLQ severity</sub></td>
<td width="33%"><a href="docs/screenshots/16-ServiceHub-AWS-SNS-FanOut.png"><img src="docs/screenshots/16-ServiceHub-AWS-SNS-FanOut.png" width="100%"/></a><br/><sub><b>AWS SNS Fan-Out</b> — subscription status &amp; live queue depth (preview)</sub></td>
<td width="33%"><a href="docs/screenshots/17-ServiceHub-GCP-Message-Detail.png"><img src="docs/screenshots/17-ServiceHub-GCP-Message-Detail.png" width="100%"/></a><br/><sub><b>GCP Pub/Sub Message Detail</b> — same forensic UI, different cloud (preview)</sub></td>
</tr>
<tr>
<td width="33%"><a href="docs/screenshots/22-ServiceHub-Cloud-Bridge.png"><img src="docs/screenshots/22-ServiceHub-Cloud-Bridge.png" width="100%"/></a><br/><sub><b>Cloud Bridge</b> — cross-provider entity browser</sub></td>
<td width="33%"></td>
<td width="33%"></td>
</tr>
</table>

---

## Real-World Scenarios

### Scenario 1: DLQ Incident at 2 AM
**Problem:** 5,000 orders stuck in Dead-Letter Queue. Azure Portal shows counts only.
**With ServiceHub:**
1. Browse all 5,000 DLQ messages in seconds.
2. AI detects 3 error clusters: Payment Timeout (40%), Invalid Address (35%), Duplicate (25%).
3. Create an auto-replay rule for Payment Timeout $\rightarrow$ replay 2,000 messages automatically.
**Time saved:** 6 hours $\rightarrow$ 45 minutes.

### Scenario 2: Missing Order Investigation
**Problem:** Customer reports order never processed. Which queue did it land in?
**With ServiceHub:**
1. Open Correlation Explorer.
2. Paste the order's Correlation ID.
3. Trace the message journey across all queues and namespaces in one search.
**Time saved:** 30 minutes $\rightarrow$ 30 seconds.

### Scenario 3: Integration Testing
**Problem:** Need 100 realistic failure scenarios to test error handling.
**With ServiceHub:**
1. Open Message Generator $\rightarrow$ select Payment Gateway scenario.
2. Generate 100 messages with 30% anomaly rate.
3. Verify DLQ behavior and error handling.
**Time saved:** Hours of manual test data $\rightarrow$ 2 minutes.

---

## Recommended Usage Flow

Follow this path before connecting to a production namespace. This protects your live environment and gives you confidence in every operation before it matters.

1. **DEV**: Connect your development namespace. Explore message browsing, DLQ inspection, and auto-replay rules in a safe environment.
2. **UAT**: Validate replay targets, confirm rule logic, and review AI findings with realistic data.
3. **PROD**: Connect only after DEV and UAT validation. Production namespaces enforce read-only browsing by default — Quick Actions (replay, send, generate) are disabled to prevent accidental data modification.

> [!WARNING]
> While ServiceHub is read-only by default, replay and send operations are destructive. Validate your replay rules and message targets in lower environments first.

---

## Quick Start

### Docker (fastest)

```bash
docker compose up --build
```

Open **[http://localhost:8080](http://localhost:8080)**, then connect a namespace with your own cloud credentials. One image serves both the SPA and the API.

To point at real cloud messaging with persisted data and production hardening, run the image in Production mode with your own encryption key:

```bash
docker build -t servicehub .
docker run --rm -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e SECURITY__ENCRYPTIONKEY="$(openssl rand -hex 32)" \
  -v servicehub-data:/var/servicehub/data \
  servicehub
```

The namespace store and SQLite DLQ/audit database persist to the `servicehub-data` volume. This
minimal example is enough to start the process — it is **not** the complete production
checklist. `AllowedHosts`, `Cors:AllowedOrigins`, the SPA token secret, and at least one API key
(or OIDC) also need setting before real users reach it; a leftover `SET_VIA_ENV_VAR` placeholder
on `AllowedHosts` in particular causes the app to reject every request. See
[self-hosting/README.md](self-hosting/README.md) for the full checklist and
[docs/CONFIGURATION.md](docs/CONFIGURATION.md) for every option.

### One-Command Setup (from source)

```bash
git clone https://github.com/debdevops/servicehub.git
cd servicehub
./run.sh
```

Open **[http://localhost:3000](http://localhost:3000)** — then connect with your connection string. The script automatically installs .NET 10 SDK and Node.js 22+ if not already present.

### Create a Dedicated Policy (Azure)

For read-only browsing (recommended for production):
```bash
az servicebus namespace authorization-rule create \
  --namespace-name <your-namespace> \
  --resource-group <your-rg> \
  --name servicehub-readonly \
  --rights Listen
```

---

## Security

ServiceHub is built for strict enterprise environments.

### What ServiceHub guarantees
- **Read-only by default** — Uses `PeekMessagesAsync`; messages are **never removed or consumed**.
- **AES-GCM encryption** — Connection strings encrypted at rest; key stored in local config, never returned to the browser.
- **Zero external calls** — AI analysis runs entirely in-browser; no message data leaves your environment.
- **No message persistence** — Messages are displayed in-memory only during your session; never written to a database.
- **Log redaction** — Backend logging pipeline strips connection strings, API keys, and access tokens (best-effort pattern matching, not a formal guarantee).

### What ServiceHub does not do by default
- **No per-user authentication out of the box** — every browser session shares one built-in admin identity. Enable **OIDC** (any standards-compliant identity provider) or **Azure Easy Auth**, both off by default, to isolate individual users. The browser's SPA token is a CSRF/casual-automation mitigation, not an identity boundary — see [Security Hardening](self-hosting/security-hardening/README.md) for the full threat model and setup steps.

### Telemetry (opt-in, vendor-neutral)
ServiceHub can emit operational telemetry two ways, **both disabled by default**:

- **OpenTelemetry** — vendor-neutral traces + metrics over OTLP, for Prometheus/Grafana/Datadog/Jaeger or any OTLP collector. Enable by setting `OpenTelemetry:Enabled=true` or the standard `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable.
- **Azure Application Insights** — enabled when `ApplicationInsights:ConnectionString` is set.

When enabled, telemetry is strictly limited to request durations, error codes, and system metrics. Connection strings, message payloads, business IDs, and user inputs are **explicitly excluded**.

---

## Architecture

ServiceHub is a modern Single Page Application communicating with a .NET Core backend.

```
Browser (React 19 SPA)
  └── TanStack Query hooks (useMessages, useQueues, useRules, …)
        └── Axios API client → Vite dev proxy
              └── ASP.NET Core 10 API
                    ├── NamespacesController      → AES-GCM encrypted connections
                    ├── DlqHistoryController      → SQLite DLQ intelligence (no cloud SDK call)
                    ├── RulesController           → auto-replay rule engine
                    ├── MessagesController        ┐
                    ├── QueuesController          ├── IMessageOperationsService → CloudProviderRouter
                    ├── TopicsController          ┘
                    └── CrossCloudTraceController → same ICloudMessagingProvider abstraction
                                                     (Azure dispatched via IAzureTraceSearcher,
                                                      AWS/GCP dispatched directly)
                                                            │
                                                            ▼
                                                  ICloudMessagingProvider implementations
                                                            ├── Azure.Messaging.ServiceBus SDK
                                                            ├── AWSSDK.SQS / AWSSDK.SNS
                                                            └── Google.Cloud.PubSub.V1
```

For deep-dive architecture details, see [ARCHITECTURE.md](services/api/ARCHITECTURE.md) and the [Comprehensive Guide](docs/COMPREHENSIVE-GUIDE.md). For exactly which controllers share a routing path today (and which don't yet), see [docs/FLOW.md](docs/FLOW.md). Building a new messaging provider (Kafka, RabbitMQ, IBM MQ, ...)? See [docs/EXTENDING-PROVIDERS.md](docs/EXTENDING-PROVIDERS.md). What each provider genuinely supports? See [docs/PROVIDER-SUPPORT.md](docs/PROVIDER-SUPPORT.md). Deliberate trade-offs and constraints? See [docs/KNOWN-LIMITATIONS.md](docs/KNOWN-LIMITATIONS.md).

---

## API Documentation

ServiceHub exposes a full REST API with interactive documentation interfaces accessible when running locally:

- **Scalar (Modern)**: `http://localhost:5153/scalar/v1`
- **Swagger UI**: `http://localhost:5153/swagger/index.html`

---

## FAQ

**Does ServiceHub remove messages from queues?**
No. ServiceHub only uses `PeekMessagesAsync`. Your consumers continue processing normally, unaffected.

**Is it safe to point at production?**
Yes. Listen-only mode is fully read-only. Deploy ServiceHub inside your private network for extra safety. Check out the [Self-Hosting Guide](self-hosting/README.md).

**How does AI analysis work without an API key?**
ServiceHub uses client-side heuristic pattern detection — pure JavaScript in your browser. No GPT, no external service, no data exfiltration.

**Can I delete a single message?**
On AWS (delete by receipt handle) and GCP (acknowledge), yes — the Purge action, guarded by explicit-intent headers and blocked on production namespaces. Azure Service Bus has no reliable single-message delete in the SDK, so ServiceHub disables the action there instead of faking it.

---

## Contributing

Bug fixes, features, and documentation improvements are welcome!

```bash
# Unit tests (Vitest — 1,100+ tests, ≥60% coverage required)
npm run -w apps/web test:coverage

# Backend tests (xUnit — 1,500+ unit + integration tests)
dotnet test services/api/tests/ServiceHub.UnitTests
dotnet test services/api/tests/ServiceHub.IntegrationTests

# E2E tests (Playwright)
npm run -w apps/web test:e2e
```
For deep backend developer guidelines, refer to the [API README](services/api/README.md).

---

## Roadmap

ServiceHub is built depth-first: make one workflow excellent before adding the next surface.

- **Now (MVP)** — the forensic core across Azure (GA) and AWS/GCP (preview): explore, search, DLQ investigation, replay, purge, send, auto-replay rules, live updates. Also shipped: bulk replay/purge with dry-run preview, a fleet dashboard across namespaces, Slack/Teams-native alerts (DLQ spikes, bulk operation completion), Live Tail (real-time "tail -f" for a queue/subscription, Azure and GCP), a DLQ triage inbox, OIDC SSO (bring your own standards-compliant identity provider), role-based access via API key/OIDC scopes (Viewer/Operator/Auditor), and an exportable per-owner audit trail.
- **Next** — Failure Signature Intelligence: clustering dead-letter messages into named, recurring failure patterns instead of one-off incidents.
- **Later** — team & governance: approval workflows for destructive operations, extending namespace sharing to cover shared DLQ history and audit visibility (not just live namespace access).

Have a use-case that should shape this? [Open a feature request](https://github.com/debdevops/servicehub/issues/new) — describe the problem, not just the solution.

---

<div align="center">

**ServiceHub** — Because your Service Bus messages should not be invisible during incidents.

Built for DevOps, Platform, and SRE Engineers.

[⚡ Self-Host ServiceHub](#quick-start) · [Report Issue](https://github.com/debdevops/servicehub/issues)

</div>
