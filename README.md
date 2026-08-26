<div align="center">

# ServiceHub

### The Forensic Debugger for Cloud Messaging — Azure Service Bus (Supported) · AWS SQS/SNS & GCP Pub/Sub (Preview)

![ServiceHub Banner](docs/screenshots/servicehub-cover-v3.7.0.png)

[![CI](https://github.com/debdevops/servicehub/actions/workflows/servicehub.yml/badge.svg)](https://github.com/debdevops/servicehub/actions/workflows/servicehub.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-purple.svg)](https://dotnet.microsoft.com/)
[![React 19](https://img.shields.io/badge/React-19-61dafb.svg)](https://react.dev/)
[![TypeScript](https://img.shields.io/badge/TypeScript-5-3178c6.svg)](https://www.typescriptlang.org/)
[![Version](https://img.shields.io/badge/version-3.7.0-brightgreen.svg)](.version)
[![Self-Hosted](https://img.shields.io/badge/Deployment-Self--Hosted-0078D4.svg)](#quick-start)

[⚡ Quick Start](#quick-start) · [🖥️ Run It Locally (Plain-Language Guide)](LOCAL-DEPLOYMENT.md) · [📚 User Guides](#user-guides) · [✨ Core Capabilities](#core-capabilities) · [🌐 Multi-Cloud](#multi-cloud-bridge) · [🏗️ Architecture](#architecture) · [🛡️ Security](#security) · [🚀 Self-Hosting](self-hosting/README.md) · [📋 Changelog](CHANGELOG.md)

</div>

---

## What is ServiceHub?

**ServiceHub is a self-hosted, open-source forensic debugger for cloud message queues.** Point
it at Azure Service Bus, AWS SQS/SNS, or GCP Pub/Sub and it gives you what the cloud console
won't: full message bodies, real-time search, AI-assisted dead-letter pattern detection, one-click
replay, and a permanent, tamper-evident record of every recovery decision it makes — all running
in a single process you control, with no message data ever leaving your network. Azure Service Bus
is fully supported (GA); AWS and GCP are in preview.

---

## Why ServiceHub?

Production breaks at 2 AM. Your cloud portal shows **5,000 messages in the Dead-Letter Queue** — but you can't read their bodies or search them without writing throwaway scripts. You manually sample messages one by one, spending hours on what should take minutes.

> **Your cloud console shows you counts. ServiceHub shows you answers.**

> [!IMPORTANT]
> **Built for strict environments, single-operator by default.** Read-only by default (`Peek`, never consume) · connection strings AES-GCM-256 encrypted at rest · analysis runs entirely in your browser — no message data ever leaves your network ([telemetry](#telemetry-opt-in-vendor-neutral) is opt-in, disabled unless you enable it) · destructive actions (replay, send) blocked on production namespaces. **Every browser session shares one admin identity unless you turn on per-user identity** — OIDC (any standards-compliant IdP) or Azure Easy Auth, both off by default. Details in [Security](#security).

> [!TIP]
> **No credentials?** The Welcome page's **"Try a live demo"** buttons open a fully client-side demo walkthrough per cloud — no backend, no cloud account needed.

<p align="center">
  <a href="docs/screenshots/showcase/01-dlq-populated.jpg"><img src="docs/screenshots/showcase/01-dlq-populated.jpg" width="85%"/></a>
  <br/><sub>A real Dead-Letter Queue in ServiceHub — 168 AWS SQS failures, honest about what SQS does and doesn't tell you about them.</sub>
</p>

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

## 🛡️ Investigate → Recover → Prove It Happened

That's the whole product, in three words. **Investigate** a failure with full message bodies and
AI-assisted pattern clustering — not just a count. **Recover** it with one-click or automated
replay, safety-gated on production namespaces. **Prove it happened** with the Recovery Evidence
Ledger, a permanent, append-only, hash-chained record of exactly what ServiceHub asked the
provider to do and what it subsequently observed — so replay isn't a black box you have to trust
blindly.

Every screenshot below is a real capture — live Azure Service Bus, AWS SQS/SNS, and GCP Pub/Sub
namespaces connected to ServiceHub simultaneously, not mocked data. Click any image to open it
full-size.

<table>
<tr>
<td width="33%"><a href="docs/screenshots/showcase/01-dlq-populated.jpg"><img src="docs/screenshots/showcase/01-dlq-populated.jpg" width="100%"/></a><br/><sub><b>1. Investigate</b> — Dead-Letter Queue, 168 real AWS failures, AI-tagged</sub></td>
<td width="33%"><a href="docs/screenshots/showcase/02-ai-findings.jpg"><img src="docs/screenshots/showcase/02-ai-findings.jpg" width="100%"/></a><br/><sub><b>2. Investigate</b> — AI Findings clusters the pattern, confidence scored, never hidden</sub></td>
<td width="33%"><a href="docs/screenshots/showcase/03-multi-cloud-connected.jpg"><img src="docs/screenshots/showcase/03-multi-cloud-connected.jpg" width="100%"/></a><br/><sub><b>3. Investigate</b> — Azure, AWS, and GCP connected side by side, one UI</sub></td>
</tr>
<tr>
<td width="33%"><a href="docs/screenshots/showcase/04-auto-replay-circuit-breaker.jpg"><img src="docs/screenshots/showcase/04-auto-replay-circuit-breaker.jpg" width="100%"/></a><br/><sub><b>4. Recover</b> — Auto-Replay Rules, with a real circuit breaker that self-disables on low success</sub></td>
<td width="33%"><a href="docs/screenshots/showcase/05-recovery-evidence-ledger.jpg"><img src="docs/screenshots/showcase/05-recovery-evidence-ledger.jpg" width="100%"/></a><br/><sub><b>5. Prove it happened</b> — the Recovery Evidence Ledger, one row per recovery decision</sub></td>
<td width="33%"><a href="docs/screenshots/showcase/06-recovery-evidence-detail.jpg"><img src="docs/screenshots/showcase/06-recovery-evidence-detail.jpg" width="100%"/></a><br/><sub><b>6. Prove it happened</b> — one operation's hash chain, verifiable and exportable as evidence</sub></td>
</tr>
</table>

See the [Quick Access Guide](docs/guides/quick-access-guide.md) for what every one of these
screens does, and the cloud provider guides linked below
([Azure](docs/guides/azure-guide.md) / [AWS](docs/guides/aws-guide.md) /
[GCP](docs/guides/gcp-guide.md)) for a full walkthrough per provider.

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

**Preview** means: implemented and unit-tested, not validated against live AWS/GCP services in this project's own CI, capability-gated, no parity guarantee with Azure.

### 🌐 Cross-Cloud Trace
Connect namespaces from two or more cloud providers and use **Multi-Cloud Trace** to trace a single Correlation ID or message GUID as it routes from Azure $\rightarrow$ AWS $\rightarrow$ GCP (or any combination). The result is a visual routing path diagram, a chronological hop timeline, and a namespace search-coverage panel.
*(Azure namespaces are always searched in parallel. AWS and GCP namespaces are searched the same way whenever those providers are registered on the server; if a provider isn't registered, its namespaces are skipped with a reason shown in the search-coverage panel instead of being silently omitted.)*

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
> **Zero-trust privacy:** the primary AI Findings surface runs entirely as client-side heuristics in your browser — no message content ever leaves your environment. A richer, optional backend path exists for Failure Signature clustering; it can call a **self-hosted, disabled-by-default companion container you run on your own network** — never a third-party or cloud AI API — and transparently falls back to a local deterministic strategy whenever that container is off. Full boundary details: [`docs/ARCHITECTURE.md` § The AI capability boundary](docs/ARCHITECTURE.md#6a-the-ai-capability-boundary).

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
- **Safety controls** with rate limiting to prevent overwhelming downstream services — including a **circuit breaker** that disables a rule automatically when its real-world success rate drops too low, so a bad rule can't quietly keep failing.

### 🎯 Failure Signature Intelligence — Name the Repeat, Not Just the Symptom
Recurring dead-letter patterns get clustered into a named, confidence-scored **Failure Signature** with its own lifecycle (active → resolved, suppressed, or archived) and guided replay — so the fifth time `PaymentGatewayError` shows up, you're managing a known case instead of re-diagnosing from scratch. Backed by a searchable knowledge base of what worked last time.

### 🔎 Real-Time Search & Correlation Explorer
Search across message body, properties, and headers instantly. Filter 1,000+ messages down to exactly what you need in under a second. Paste any Correlation ID to trace a message's full journey across all queues, topics, and namespaces.

### 🕐 Scheduled Messages
See every message queued for future delivery. Reschedule or cancel individual messages directly from the UI. Azure Service Bus only — AWS SQS (15-minute `DelaySeconds` cap, not inspectable) and GCP Pub/Sub (no scheduled delivery) show an explanatory panel instead of an empty table.

### 📈 Multi-Namespace Dashboard
One glance at every connected namespace — Azure, AWS, and GCP side by side, sorted by DLQ severity. Each card shows live active/DLQ/scheduled counts, a health badge, and one-click jumps into Browse Queues or DLQ History. Quick Actions surface the four things you reach for during an incident: Browse All DLQs, All Scheduled, Cross-Cloud Trace, Auto-Replay Rules.

### 📝 Audit Trail & Recovery Evidence Ledger
Every critical operation — send, replay, purge, dead-letter, rule changes — is written to a persistent, per-owner **Audit Trail**: timestamp, user, cloud/environment, action, resource, and outcome. Exportable, filterable, and isolated so one tenant can never see another's history. Replay and purge specifically get a second, deeper record: the **Recovery Evidence Ledger** (`/recovery`) — an append-only, hash-chained history of exactly what ServiceHub asked the provider to do and what it subsequently observed, so a recovery claim never has to just be taken on faith. See [`docs/RECOVERY-EVIDENCE.md`](docs/RECOVERY-EVIDENCE.md) for the full technical model.

### 🛡️ Security & Privacy Page
An in-app page that answers the trust question before anyone has to ask it: a diagram of exactly how data moves from browser → ServiceHub server → cloud SDK, what's encrypted (connection strings, AES-256-GCM), what's redacted from logs, and what's never stored (message bodies, plaintext secrets) — with links to verify each claim directly in the open-source code.

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

## User Guides

Already connected and want to know what to actually *do* with ServiceHub? This is the official
ServiceHub user handbook — plain language, screenshot-illustrated, no code or scripting required.
Each guide below walks the full message-debugging journey — browsing, DLQ investigation, AI
Insights, replay, and the Recovery Evidence Ledger — verified live against a real namespace, with
an honest, explicit list of what's supported and what isn't for that cloud:

- **[🧭 Quick Access Guide](docs/guides/quick-access-guide.md)** — every navigation shortcut explained, with a full navigation map
- **[☁️ Azure Service Bus Guide](docs/guides/azure-guide.md)** — the fully supported (GA) provider
- **[🟧 AWS SQS/SNS Guide](docs/guides/aws-guide.md)** — Preview, with SQS's own limitations explained
- **[🟩 GCP Pub/Sub Guide](docs/guides/gcp-guide.md)** — Preview, with Pub/Sub's own limitations explained

New to ServiceHub and haven't connected a cloud account yet? Start with
[LOCAL-DEPLOYMENT.md](LOCAL-DEPLOYMENT.md) instead — it covers installing ServiceHub and
connecting your first namespace, with a link back to the matching guide above once you're in.

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

### What do you want to do?

```
Just try ServiceHub?             → Demo                     (below)
Run on my laptop?                → With Docker              (#docker-fastest-with-docker)
                                  → Without Docker           (#one-command-setup-without-docker-from-source)
Test my real cloud?              → AWS / Azure / GCP         (self-hosting/README.md#cloud-credentials-least-privilege-setup)
Run ServiceHub inside my org?    → Azure App Service         (#azure-app-service-recommended)
                                  → Azure Container Apps      (#azure-container-apps-alternative)
Want a ready-made container?     → GHCR                      (#container-image)
```

No cloud account, credentials, or infrastructure are required for the first option. Every
option below runs the same single Docker image — nothing is a separate build.

Never used Docker or a terminal before? Skip the commands below and follow
**[LOCAL-DEPLOYMENT.md](LOCAL-DEPLOYMENT.md)** — the same "run on my laptop" steps, written
for a non-technical reader with screenshots at every step.

> [!TIP]
> **No credentials yet?** The Welcome page's **"Try a live demo"** buttons open a fully
> client-side demo walkthrough per cloud (`/demo/azure`, `/demo/aws`, `/demo/gcp`) — no backend
> calls, no credentials, safe to click around before connecting anything real. This is the
> supported, tested demo experience and the one worth trying first.

### Docker (fastest, with Docker)

ServiceHub encrypts stored connection strings at rest, so it needs two secrets generated on your
machine before first run. There are no defaults — a shipped default key would be identical across
every deployment that never overrode it.

```bash
git clone https://github.com/debdevops/servicehub.git
cd servicehub

cp .env.example .env
printf 'SECURITY__ENCRYPTIONKEY=%s\n'    "$(openssl rand -hex 32)" >> .env
printf 'SECURITY__SPATOKEN__SECRET=%s\n' "$(openssl rand -hex 32)" >> .env

docker compose up --build
```

Open **[http://localhost:8080](http://localhost:8080)**, then connect a namespace with your own
cloud credentials. The port is bound to `127.0.0.1` (loopback) only by default, so it isn't
reachable from your network until you deliberately change that. One image serves both the SPA and
the API.

Prefer not to build locally? Pull the official image instead of `--build`:
`docker pull ghcr.io/debdevops/servicehub:latest` (see [Container Image](#container-image)).

If you skip the `.env` step, `docker compose` stops immediately and names the variable that is
missing rather than starting a container that fails its configuration check.

To point at real cloud messaging with persisted data and production hardening, run the image in Production mode. Every variable below is **required** — the app validates its Production configuration at startup and refuses to start if any is missing or still holds a `SET_VIA_ENV_VAR` placeholder:

```bash
docker build -t servicehub .
docker run --rm -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e SECURITY__ENCRYPTIONKEY="$(openssl rand -hex 32)" \
  -e SECURITY__SPATOKEN__SECRET="$(openssl rand -hex 32)" \
  -e SITEURL="http://localhost:8080" \
  -e AllowedHosts="localhost" \
  -v servicehub-data:/var/servicehub/data \
  servicehub
```

The namespace store and SQLite DLQ/audit database persist to the `servicehub-data` volume. This
example starts the process correctly — it is **not** the complete production
checklist. `Cors:AllowedOrigins` and at least one API key (or OIDC) also need setting before real
users reach it, and `SITEURL`/`AllowedHosts` must name the hostname users actually visit rather
than `localhost`.

### Container Image

Official images are published to GitHub Container Registry on every tagged release:

```bash
docker pull ghcr.io/debdevops/servicehub:latest
# or pin a version: ghcr.io/debdevops/servicehub:3.7.0
```

Run it the same way as the locally built image — same required secrets, same volume, same
port. See [Self-Hosting](self-hosting/README.md) before pointing a pulled image at real
credentials or a non-loopback address.

### One-Command Setup (without Docker, from source)

```bash
git clone https://github.com/debdevops/servicehub.git
cd servicehub
./run.sh
```

Open **[http://localhost:3000](http://localhost:3000)** — then connect with your connection string. The script automatically installs .NET 10 SDK and Node.js 22+ if not already present.

Step-by-step with screenshots (no command-line experience assumed):
[LOCAL-DEPLOYMENT.md](LOCAL-DEPLOYMENT.md).

### Create a Dedicated Read-Only Credential

Azure:
```bash
az servicebus namespace authorization-rule create \
  --namespace-name <your-namespace> \
  --resource-group <your-rg> \
  --name servicehub-readonly \
  --rights Listen
```

AWS and GCP least-privilege IAM policies (exact SDK actions ServiceHub calls, JSON policy and
`gcloud` commands included) are in [Self-Hosting → Cloud credentials](self-hosting/README.md#cloud-credentials-least-privilege-setup).
Quick "create a resource, connect it, verify it, tear it down" walkthroughs for all three
clouds are in [Self-Hosting → Quick end-to-end test](self-hosting/README.md#quick-end-to-end-test).

<details>
<summary>Two additional, experimental standalone apps exist in this repo (<code>apps/demo</code>, <code>apps/sandbox</code>) — click to expand</summary>

`./run.sh demo` and `./run.sh sandbox` (or `./run.sh all`) start two separate exploratory apps on
ports 5174 and 5175. They're real and launchable, but **experimental and unsupported** — no test
suite, not covered by the e2e suite, CI only checks that they build and typecheck. If you just want
to try ServiceHub, use the in-app demo mentioned above instead. See
[`apps/demo/README.md`](apps/demo/README.md) and [`apps/sandbox/README.md`](apps/sandbox/README.md)
for what each one is for.

</details>

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
provide.

---

## Self-Host on Azure

Both options run the same GHCR image (`ghcr.io/debdevops/servicehub:latest`) as a single,
non-scaled container — see [Deployment Model](#deployment-model). Pick one; you don't need
both.

### Azure App Service (Recommended)

The most mature managed path today — Web App for Containers, one instance, no code changes.

```bash
az login
az group create --name rg-servicehub --location eastus

az appservice plan create --name plan-servicehub --resource-group rg-servicehub \
  --is-linux --sku B1

az webapp create --name <globally-unique-app-name> --resource-group rg-servicehub \
  --plan plan-servicehub --deployment-container-image-name ghcr.io/debdevops/servicehub:latest

# Required secrets + config — same variables as the Docker section above
az webapp config appsettings set --name <app-name> --resource-group rg-servicehub --settings \
  ASPNETCORE_ENVIRONMENT=Production \
  SECURITY__ENCRYPTIONKEY="$(openssl rand -hex 32)" \
  SECURITY__SPATOKEN__SECRET="$(openssl rand -hex 32)" \
  SITEURL="https://<app-name>.azurewebsites.net" \
  AllowedHosts="<app-name>.azurewebsites.net" \
  WEBSITES_PORT=8080

az webapp restart --name <app-name> --resource-group rg-servicehub
```

Then mount **persistent** storage — App Service's local container disk is not guaranteed to
survive a restart or scale event. Attach an Azure Files share via `az webapp config storage-account add`
and point both `DlqDatabase__DataDirectory` and `NamespaceRepository__DataDirectory` at the
same mounted path (see [Self-Hosting → Persistent storage](self-hosting/README.md#persistent-storage-two-stores-two-config-keys) —
this is the single most common misconfiguration).

Verify: `curl https://<app-name>.azurewebsites.net/health/live`, then open the URL in a
browser. **Pin the App Service Plan to a single instance** — do not enable auto-scale-out;
duplicate replicas would run duplicate background workers against the same data.

This repo's own `deploy/` folder contains the maintainer's personal production pipeline
(specific budget, resource names, and an Azure DevOps release flow) — useful as a reference,
not something you need to read or reuse for the steps above.

### Azure Container Apps (Alternative)

Workable, but Container Apps' headline feature — scale-to-zero and elastic replica count —
actively fights this architecture: a cold start after scale-to-zero drops in-flight SSE
connections and resets the in-process event bus, and any replica count above 1 risks two
copies of the same background worker acting on the same SQLite database. Use this only if
your organization is already standardized on Container Apps.

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

`--min-replicas 1 --max-replicas 1` is not optional — it's what makes this safe to run at
all. Attach Azure Files storage the same way as App Service, mounted at both
`DataDirectory` paths, then verify against `/health/live` as above.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `docker compose up` exits immediately, names a missing variable | `SECURITY__ENCRYPTIONKEY` or `SECURITY__SPATOKEN__SECRET` unset | `cp .env.example .env` and fill both in — see [Quick Start](#quick-start) |
| App starts but every request is rejected / wrong host | `AllowedHosts` or `SITEURL` doesn't match the hostname you're actually visiting | Set both to the real external hostname, not `localhost`, once you're off loopback |
| Creating an AWS or GCP namespace returns `503` | `CloudProviders:Aws:Enabled` / `CloudProviders:Gcp:Enabled` is `false` (Azure-only by default) | Set `CLOUDPROVIDERS__AWS__ENABLED=true` / `CLOUDPROVIDERS__GCP__ENABLED=true` before connecting that provider |
| `/health/live` fails after deploy | Container isn't listening on the platform's expected port, or hasn't finished startup config validation | Confirm `WEBSITES_PORT`/`--target-port` is `8080`; check container logs for the startup config validator's specific missing-variable error |
| `docker pull ghcr.io/debdevops/servicehub` fails with "denied" | GHCR package visibility is private, or the tag doesn't exist yet | Confirm the tag (`:latest` or a released `:X.Y.Z`) exists under the repo's Packages tab |
| Namespace credentials are gone after a restart, but DLQ history is intact | Only `DlqDatabase__DataDirectory` was persisted, not `NamespaceRepository__DataDirectory` | Mount **both** `DataDirectory` paths to the same persistent volume — see [Self-Hosting → Persistent storage](self-hosting/README.md#persistent-storage-two-stores-two-config-keys) |

---

## Security

ServiceHub is built for strict enterprise environments.

### What ServiceHub guarantees
- **Read-only by default** — Uses `PeekMessagesAsync`; messages are **never removed or consumed**.
- **AES-GCM encryption** — Connection strings encrypted at rest; key stored in local config, never returned to the browser.
- **No third-party or cloud AI calls, in either direction** — the primary AI analysis path runs entirely in-browser; an optional backend path for deeper clustering only ever reaches a self-hosted companion container on your own network, never an external service. No message data leaves your environment either way.
- **No message persistence for live browsing** — Messages viewed on the Queues/Topics/Messages pages are in-memory only during your session, never written to a database. The deliberate exception is DLQ Intelligence, which stores a 500-character body preview and classification metadata per dead-lettered message in local SQLite to power History and 30-Day Trends.
- **Log redaction** — Backend logging pipeline strips connection strings, API keys, and access tokens (best-effort pattern matching, not a formal guarantee).

### What ServiceHub does not do by default
- **No per-user authentication out of the box** — every browser session shares one built-in admin identity. Enable **OIDC** (any standards-compliant identity provider) or **Azure Easy Auth**, both off by default, to isolate individual users. The browser's SPA token is a CSRF/casual-automation mitigation, not an identity boundary.

### Telemetry (opt-in, vendor-neutral)
ServiceHub can emit operational telemetry two ways, **both disabled by default**:

- **OpenTelemetry** — vendor-neutral traces + metrics over OTLP, for Prometheus/Grafana/Datadog/Jaeger or any OTLP collector. Enable by setting `OpenTelemetry:Enabled=true` or the standard `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable.
- **Azure Application Insights** — enabled when `ApplicationInsights:ConnectionString` is set.

When enabled, telemetry is strictly limited to request durations, error codes, and system metrics. Connection strings, message payloads, business IDs, and user inputs are **explicitly excluded**.

---

## Architecture

ServiceHub is a modern Single Page Application communicating with a .NET Core backend.

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

Full picture — provider abstraction, `ProviderCapabilities`, the Recovery Evidence Ledger,
autonomy/safety model, persistence, SSE, security boundaries: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).
Why the foundational decisions were made the way they were: [`docs/adr/`](docs/adr/). Adding a new
messaging provider (Kafka, RabbitMQ, a fourth cloud, …): [`docs/extending/adding-a-provider.md`](docs/extending/adding-a-provider.md).

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
Yes. Listen-only mode is fully read-only. Deploy ServiceHub inside your private network for extra safety.

**How does AI analysis work without an API key?**
The primary AI Findings surface is client-side heuristic pattern detection — pure JavaScript in your browser, no API key needed. A deeper, optional backend path (Failure Signature clustering) can call a self-hosted companion container you run yourself, disabled by default — never GPT or any other third-party/cloud service, and no data exfiltration either way.

**Can I delete a single message?**
On AWS (delete by receipt handle) and GCP (acknowledge), yes — the Purge action, guarded by explicit-intent headers and blocked on production namespaces. Azure Service Bus has no reliable single-message delete in the SDK, so ServiceHub disables the action there instead of faking it.

**How is this different from Service Bus Explorer?**
Service Bus Explorer is a well-established, Azure-only desktop tool for browsing and managing Service Bus entities. ServiceHub also covers Azure Service Bus, but adds full-text message search, batch DLQ analysis with client-side AI pattern detection, auto-replay rules, a persistent multi-namespace fleet dashboard, cross-cloud correlation tracing, and the hash-chained Recovery Evidence Ledger — plus preview support for AWS SQS/SNS and GCP Pub/Sub in the same tool. Both are free and self-hosted; the difference is investigation/recovery depth and multi-cloud scope.

---

## Contributing

Bug fixes, features, and documentation improvements are welcome! See [CONTRIBUTING.md](CONTRIBUTING.md)
for the full guide, including [adding a new messaging provider](docs/extending/adding-a-provider.md).

```bash
# Frontend unit tests (Vitest, ≥60% coverage required)
npm run -w apps/web test:coverage
npm run -w packages/servicehub-ui-shared test   # hooks + API client live here, not in apps/web

# Backend tests (xUnit — unit + integration)
dotnet test services/api/tests/ServiceHub.UnitTests
dotnet test services/api/tests/ServiceHub.IntegrationTests

# E2E tests (Playwright, against client-side Demo Mode)
npm run -w apps/web test:e2e
```

---

## Roadmap

ServiceHub is built depth-first: make one workflow excellent before adding the next surface. Here's
where it stands and where it's headed.

| | Stage | Focus | Status |
|---|---|---|---|
| 🟢 | **Now** | Investigate → Recover → Prove | Shipped |
| 🔵 | **Next** | Team & Governance | Planned |
| 🟣 | **Later** | AI-Guided → Bounded Autonomous Operations | Strategic direction |

**🟢 Now — Investigate → Recover → Prove.** The forensic core, live today across Azure Service Bus
(GA) and AWS SQS/SNS + GCP Pub/Sub (preview): full message inspection, real-time search, client-side
AI pattern detection, one-click and rule-based replay, purge, bulk operations with dry-run preview,
a fleet dashboard, DLQ triage, Live Tail (Azure/GCP), Failure Signature Intelligence, and the
Recovery Evidence Ledger — a hash-chained, tamper-evident record of every recovery. Also shipped:
Slack/Teams alerts, OIDC SSO, role-based scopes (Viewer/Operator/Auditor), an exportable audit
trail, and namespace sharing for live operations (Preview).

**🔵 Next — Team & Governance.** Approval workflows for destructive operations, and extending
namespace sharing so a collaborator also sees shared DLQ history and audit visibility — not just
live namespace access.

**🟣 Later — AI-Guided → Bounded Autonomous Operations** *(strategic direction, not a committed
feature or date)*. Today's building blocks — named Failure Signatures, rule-based Auto-Replay under
a circuit breaker, and the Recovery Evidence Ledger's proof of what was done — are the foundation
for closing the loop: AI-guided recovery recommendations with reasoning attached, and, only where an
operator opts in, bounded automation for known, high-confidence cases. Every safeguard in place
today — operator control, production write-protection, rate limits, circuit breakers, permanent
provable evidence — carries forward unchanged; no autonomous or agentic behavior ships without it.

Have a use-case that should shape this? [Open a feature request](https://github.com/debdevops/servicehub/issues/new) — describe the problem, not just the solution.

---

<div align="center">

**ServiceHub** — Investigate. Recover. Prove it happened. Because your Service Bus messages should not be invisible during incidents.

Built for DevOps, Platform, and SRE Engineers.

[⚡ Self-Host ServiceHub](#quick-start) · [Report Issue](https://github.com/debdevops/servicehub/issues)

</div>
