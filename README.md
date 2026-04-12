<div align="center">

# ServiceHub

### The Forensic Debugger for Azure Service Bus

**Debug Azure Service Bus in seconds. See what's REALLY inside your queues — not just message counts.**

![ServiceHub Demo](docs/screenshots/ServiceHub-Demo.gif)

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-purple.svg)](https://dotnet.microsoft.com/)
[![React 19](https://img.shields.io/badge/React-19-61dafb.svg)](https://react.dev/)
[![TypeScript](https://img.shields.io/badge/TypeScript-5-3178c6.svg)](https://www.typescriptlang.org/)
[![Version](https://img.shields.io/badge/version-3.1.0-brightgreen.svg)](.version)
[![Live Demo](https://img.shields.io/badge/Live%20Demo-Azure-0078D4.svg)](https://app-servicehub-prod.azurewebsites.net/)

[Live Demo](https://app-servicehub-prod.azurewebsites.net/) · [Get Started](#-quick-start) · [Features](#-features) · [Screenshots](#-screenshots) · [API Docs](#-api-documentation) · [Contributing](#-contributing)

</div>

---

## Why ServiceHub?

Production breaks at 2 AM. Azure Portal shows **5,000 messages in Dead-Letter Queue** but you can't read them — only counts and metadata. You manually sample messages one by one, spending hours on what should take minutes.

**ServiceHub is a self-hosted web application that lets you browse, search, and analyze Azure Service Bus messages in real time** — like email for your message queues.

| Capability | Azure Portal | ServiceHub |
|---|---|---|
| View message content | Count only | Full body + properties |
| Search messages | Not available | Full-text search |
| DLQ investigation | One at a time | Batch analysis + AI |
| Pattern detection | Not available | AI-powered clustering |
| Replay from DLQ | Not available | One-click replay |
| Access | Cloud portal | Any browser |

---

## � Core Features in Action

### 🔌 Connect to Azure Service Bus — 30 Seconds from Zero to Insights

Enter your connection string and you're in. Supports Listen-only (read-only) or Manage (full access) policies. No installation, no setup complexity — just instant access to your message data.

![Connect Page](docs/screenshots/archive/01-connect-page.png)
![Connected Namespace](docs/screenshots/archive/02-connect-page-namespace-connected.png)

### 📨 Message Browser — View Thousands of Messages Instantly

Browse **active** and **dead-letter** messages side by side. See full message previews, status badges, and metadata at a glance. Real-time search across message content and properties — find exactly what you need in seconds, not hours.

![Messages Queue Overview](docs/screenshots/archive/02-messages-queue-active.png)
![Message Queue Details](docs/screenshots/archive/04-messages-queue-browser.png)

#### Message Properties & Body — Full Forensic Inspection

Click any message for complete forensic analysis:
- **Properties:** Message ID, enqueue time, TTL, sequence number, delivery count
- **Body:** Full JSON/XML syntax highlighting with one-click copy
- **Headers:** All custom properties and correlation IDs

![Message Properties](docs/screenshots/archive/03-message-detail-properties.png)
![Message Body](docs/screenshots/archive/04-message-detail-body.png)
![Message Headers](docs/screenshots/archive/08-message-detail-headers.png)

#### 🤖 AI-Powered Insights — Detect Patterns at Scale

AI automatically analyzes your messages and detects:
- Error clusters and patterns
- Anomalies and outliers
- Remediation suggestions

All processing happens in your browser — **no data leaves your environment**. Analyze thousands of messages in seconds.

![AI Insights](docs/screenshots/archive/05-message-detail-ai-insights.png)
![Forensic Analysis](docs/screenshots/archive/06-message-detail-forensic.png)
![AI Findings](docs/screenshots/archive/19-feature-ai-findings-1.png)

### 💀 Dead-Letter Queue (DLQ) — From Pain to Insight

Investigate failed messages with full forensic analysis:
- **DLQ Reason & Error Description:** See exactly why messages failed
- **AI-Powered Remediation:** Automatic categorization and suggested fixes
- **One-Click Replay:** Replay messages after fixing root cause

#### DLQ Intelligence Dashboard — Track & Analyze Your Failures

Persistent tracking with:
- 30-day trend visualization
- Auto-categorization (Transient, MaxDelivery, Expired, DataQuality, Authorization)
- Live statistics
- Export to JSON/CSV

![Dead Letter Queue](docs/screenshots/archive/08-messages-deadletter-queue.png)
![DLQ Message Detail](docs/screenshots/archive/09-dlq-message-detail.png)
![DLQ History Tracking](docs/screenshots/archive/16-dlq-history-detail.png)
![DLQ AI Insights](docs/screenshots/archive/10-dlq-message-ai-insights.png)

### 🎯 Quick Actions (FAB) — One-Click Access to Everything

Floating action button provides instant access to:
- **Send Message** — Ad-hoc testing with custom properties
- **Generate Messages** — 6 realistic scenarios with configurable volume (30-200) and anomaly rate (0-50%)
- **Test DLQ** — Move messages to dead-letter queue for testing
- **Refresh All** — Instant sync

![FAB Quick Actions Menu](docs/screenshots/archive/12-fab-quick-actions-open.png)
![Send Message Dialog](docs/screenshots/archive/13-send-message-dialog.png)
![Generate Messages Scenarios](docs/screenshots/archive/14-generate-messages-dialog.png)
![Test DLQ Dialog](docs/screenshots/archive/15-test-dlq-dialog.png)

### 📌 Topic Subscriptions — Same Power for Topic Messages

Browse messages from topic subscriptions with identical forensic inspection tools.

![Topic Subscription Messages](docs/screenshots/archive/11-messages-topic-subscription.png)
![Topic Messages Overview](docs/screenshots/archive/11-topic-subscription-messages.png)

### ⚡ Auto-Replay Rules — Automate Your Recovery

Define conditional replay rules that automatically fix common failure patterns:

#### Intelligent Rule Engine
- **Flexible Matching:** Match by reason, error description, entity, delivery count, body text
- **Rich Operators:** Contains, Equals, StartsWith, EndsWith, Regex, GreaterThan, LessThan
- **Rate Limiting:** Safe replay with configurable max replays/hour
- **Circuit Breaker:** Auto-disable rules if success rate drops
- **Live Monitor:** Real-time Pending/Replayed/Success counts

#### Pre-Built Templates
Browse rule templates for common scenarios:
- Transient errors (timeout, throttle)
- Max delivery exceeded
- Expired messages
- Authorization failures
- Custom patterns

![Auto-Replay Rules](docs/screenshots/archive/18-rules-page.png)
![Rules Template Gallery](docs/screenshots/archive/20-rules-template-gallery.png)
![Create Replay Rule](docs/screenshots/archive/21-rules-create-dialog.png)

### 💚 System Health — Monitor Your Setup

Real-time monitoring dashboard:
- API health and uptime
- Memory, thread, and GC metrics
- Server information
- Performance trends

![System Health Dashboard](docs/screenshots/archive/19-health-page.png)
![Health Monitoring Metrics](docs/screenshots/archive/22-health-page.png)

### 🔍 Advanced Search & Navigation

**Message Search & Filter**
Find messages instantly by any property:
- Message ID, Correlation ID
- Body content (full-text)
- Custom headers
- System properties

![Message Filter Search](docs/screenshots/archive/28-message-filter.png)
![Correlation ID Explorer](docs/screenshots/ServiceHub-CorelationId-Explorer.png)

**Smart Sidebar Navigation**
Namespace browser with:
- Live message counts
- Queue/topic tree
- Quick Access panel

![Sidebar Navigation](docs/screenshots/archive/25-sidebar-navigation.png)

### 📚 Comprehensive Documentation

**Built-In Help**
Searchable help guide with:
- Feature walkthroughs
- Azure Service Bus concepts
- Keyboard shortcuts
- Guided tour

![Help Page](docs/screenshots/archive/20-help-page.png)
![Help Guide Full](docs/screenshots/archive/21-help-page-full.png)

**Interactive API Docs**
Scalar-powered OpenAPI documentation — test endpoints directly from the browser.

![API Documentation](docs/screenshots/archive/25-scalar-api-docs.png)

---

## ✨ Complete Feature Set

### 📨 Message Browser
- Browse **active** and **dead-letter** queue messages side by side
- View full message body with **JSON/XML syntax highlighting**
- Inspect system properties, custom headers, and application properties
- **Real-time full-text search** across message content and properties
- **Auto-refresh** with configurable polling intervals
- Filter by entity, status, message age

### 🤖 AI-Powered Analysis (Client-Side)
- **Pattern detection** — Identify error clusters instantly across thousands of messages
- **Anomaly identification** — Flag unusual messages automatically
- **Remediation suggestions** — Actionable remediation guidance
- **Zero-Trust Privacy** — All analysis runs in your browser; **no data leaves your environment**
- **Processing speed** — Analyzes 1000s of messages in seconds

### 💀 DLQ Intelligence System
- **Persistent tracking** — SQLite database stores DLQ history locally
- **Auto-categorization** — 5 failure types: Transient, MaxDelivery, Expired, DataQuality, Authorization
- **30-Day Trends** — Visualize DLQ patterns over time
- **Instant scanning** — "Scan Now" button for on-demand polling
- **Export capabilities** — Download as JSON or CSV for analysis
- **Status lifecycle** — Track: Active → Replayed → ReplayFailed → Resolved

### ⚡ Auto-Replay Rules Engine
- **Smart Matching** — Match by reason, error, entity, delivery count, body content
- **8 Operators** — Contains, Equals, StartsWith, EndsWith, Regex, GreaterThan, LessThan, In
- **Real-Time Stats** — Live Pending/Replayed/Success counting
- **Safe Replay** — Rate limiting prevents downstream service overload
- **Batch Operations** — Replay all matching messages with one click
- **9+ Templates** — Pre-built rules for common failure patterns
- **Smart Protection** — Circuit breaker auto-disables unsafe rules

### 🧪 Testing & Development Tools
- **Send Message** — Single message testing with custom properties and headers
- **Scenario Generator** — 6 built-in realistic scenarios (Orders, Payments, Notifications, etc.)
- **Configurable Testing** — Generate 30-200 messages with 0-50% anomaly rate
- **Test DLQ Workflows** — Move messages to DLQ for end-to-end testing
- **Auto-Cleanup** — All generated messages tagged `ServiceHub-Generated` for easy filtering

### 🔐 Enterprise Security & Safety
- **Read-Only by Default** — Uses PeekMessagesAsync; **messages never removed**
- **Minimal Permissions** — Works with Listen-only access (no Manage required)
- **Encrypted at Rest** — Connection strings encrypted with **AES-GCM**
- **Zero External Calls** — AI analysis runs entirely in-browser
- **No Data Persistence** — Messages displayed in-memory only
- **Production-Safe** — Won't interfere with your message consumers
- **Private Deployment** — Deploy in your own network

---

## ⚡ Quick Start

### Automated Setup (Recommended)

```bash
git clone https://github.com/debdevops/servicehub.git
cd servicehub
./run.sh
```

The script automatically:
- Installs .NET 10 SDK (if not present)
- Installs Node.js 18+ (if not present)
- Builds and starts both API and UI servers

Open **http://localhost:3000** and connect with your Azure Service Bus connection string.

### Prerequisites

Auto-installed by `run.sh`:
- .NET 10.0 SDK
- Node.js 18+

You provide:
- Azure Service Bus connection string (Listen permission minimum)

### Create a Service Bus Policy

For read-only browsing:
```bash
az servicebus namespace authorization-rule create \
  --namespace-name <your-namespace> \
  --resource-group <your-rg> \
  --name servicehub \
  --rights Listen
```

For full access (send, generate, replay):
```bash
az servicebus namespace authorization-rule create \
  --namespace-name <your-namespace> \
  --resource-group <your-rg> \
  --name servicehub \
  --rights Listen Send Manage
```

### URLs

| Service | URL |
|---|---|
| **Live Demo** | **https://app-servicehub-prod.azurewebsites.net/** |
| UI (local) | http://localhost:3000 |
| API (local) | http://localhost:5153 |
| API Docs (local) | http://localhost:5153/scalar/v1 |

---

## 🏗️ Architecture

```
servicehub/
├── apps/web/                    # React 19 + TypeScript + Vite frontend
│   └── src/
│       ├── components/          # UI components (messages, DLQ, rules, FAB)
│       ├── hooks/               # React Query hooks for API communication
│       ├── lib/                 # API client, utilities, help content
│       └── pages/               # Page components (Messages, Connect, Rules, Health, Help)
│
├── services/api/                # ASP.NET Core 10 backend
│   └── src/
│       ├── ServiceHub.Api/      # REST controllers, middleware, auth
│       ├── ServiceHub.Core/     # Domain entities, DTOs, interfaces
│       ├── ServiceHub.Infrastructure/  # Azure SDK integration, SQLite
│       └── ServiceHub.Shared/   # Common types and utilities
│
├── scripts/                     # Setup and utility scripts
└── run.sh                       # One-command startup
```

**Tech Stack:**
| Layer | Technology |
|---|---|
| Frontend | React 19, TypeScript, Tailwind CSS, TanStack Query |
| Backend | ASP.NET Core 10, Azure.Messaging.ServiceBus SDK |
| AI Analysis | Client-side pattern detection (no external API) |
| Database | SQLite (DLQ Intelligence), in-memory cache |
| API Docs | Scalar (OpenAPI) |

For detailed backend architecture, see [services/api/ARCHITECTURE.md](services/api/ARCHITECTURE.md).

---

## 💡 Real-World Scenarios

### Scenario 1: Dead-Letter Queue Incident
**Problem:** 5,000 orders stuck in DLQ at 2 AM.

**With ServiceHub:**
1. Browse all 5,000 DLQ messages instantly
2. AI detects 3 error patterns: Payment Timeout (40%), Invalid Address (35%), Duplicate Order (25%)
3. Search for specific customer orders by correlation ID
4. Create auto-replay rule for Payment Timeout — replay all 2,000 messages

**Time saved:** 6 hours → 45 minutes

### Scenario 2: Message Correlation
**Problem:** Customer reports order never processed.

**With ServiceHub:**
1. Search across message body and properties
2. Find order in 3 seconds across 10,000 messages
3. Review full message properties and delivery history

**Time saved:** 30 minutes → 30 seconds

### Scenario 3: Integration Testing
**Problem:** Need to test error handling with 100 realistic failure scenarios.

**With ServiceHub:**
1. Select Payment Gateway scenario in Message Generator
2. Generate 100 messages with 30% anomaly rate
3. Verify error handling and DLQ behavior
4. Clean up test messages filtered by ServiceHub-Generated tag

**Time saved:** Manual testing → Automated in 2 minutes

---

## 🔐 Permissions Guide

| Permission Level | Capabilities |
|---|---|
| **Listen only** | Browse messages, inspect DLQ, search, AI insights, view health |
| **Listen + Send** | All above + replay from DLQ + send test messages |
| **Manage** | All above + generate messages, test DLQ, full management |

> **Tip:** Create a dedicated `servicehub` policy instead of using `RootManageSharedAccessKey`.

---

## 📖 API Documentation

ServiceHub exposes a REST API documented with Scalar (OpenAPI). Access interactive docs at:

**http://localhost:5153/scalar/v1**

![API Documentation](docs/screenshots/archive/25-scalar-api-docs.png)

Key endpoints:
- `GET /api/v1/namespaces` — List connected namespaces
- `GET /api/v1/namespaces/{id}/queues` — List queues with message counts
- `GET /api/v1/namespaces/{id}/topics` — List topics with subscription counts
- `GET /api/v1/namespaces/{id}/queues/{name}/messages` — Browse messages
- `POST /api/v1/namespaces/{id}/queues/{name}/messages` — Send a message
- `GET /api/v1/dlq-history` — DLQ Intelligence records
- `GET /api/v1/replay-rules` — Auto-replay rules

---

## ❓ FAQ

**Q: Does ServiceHub remove messages from queues?**
No. ServiceHub uses PeekMessagesAsync which reads messages without removing them. Your consumers continue processing normally.

**Q: Is it safe for production?**
Yes. ServiceHub only requires Listen permission and operates in read-only mode. Messages remain in queues for your actual consumers.

**Q: How does AI analysis work?**
ServiceHub analyzes message content using heuristic algorithms entirely in your browser. No data leaves your environment.

**Q: What about sensitive data?**
Messages are displayed only in your browser session — not persisted. Deploy ServiceHub in your private network and restrict access.

**Q: Can I deploy to Azure App Service / Kubernetes?**
Yes. ServiceHub is a standard ASP.NET Core + React SPA. Containerize with Docker and deploy anywhere supporting .NET 10.

**Q: Does it support topics with subscriptions?**
Yes. Browse messages from both queues and topic subscriptions independently.

---

## 🤝 Contributing

We welcome contributions! Bug fixes, features, and documentation improvements.

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Make your changes with tests
4. Commit and push
5. Open a Pull Request

---

## License

MIT License — see [LICENSE](LICENSE) for details.

---

<div align="center">

**ServiceHub** — Because your Service Bus messages should not be invisible during incidents.

Built for DevOps, Platform, and SRE Engineers.

[Live Demo](https://app-servicehub-prod.azurewebsites.net/) · [Get Started](#-quick-start) · [View Features](#-features) · [Report Issue](https://github.com/debdevops/servicehub/issues)

</div>
