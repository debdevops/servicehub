# ServiceHub

**Azure Service Bus Message Browser & Forensic Debugger**

Browse, search, and debug Azure Service Bus messages during incidents — when Azure Portal can't show you what's inside.

![ServiceHub Message Browser](docs/screenshots/25-main-message-display.png)
*Browse messages from queues and topic subscriptions with real-time AI pattern detection*

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![React](https://img.shields.io/badge/React-18-blue.svg)](https://react.dev/)
[![TypeScript](https://img.shields.io/badge/TypeScript-5.0-blue.svg)](https://www.typescriptlang.org/)

---

## Why ServiceHub?

**The Problem:** It's 2 AM. Your queue has 5,000 messages in the Dead-Letter Queue. Azure Portal shows you the count — but not the message content, headers, or why they failed.

**The Solution:** ServiceHub lets you browse actual message content, search by correlation ID, and investigate DLQ failures — all read-only, all during the incident.

---

## Quick Start

### Prerequisites
- .NET 8.0 SDK
- Node.js 18+
- Azure Service Bus connection string (Listen permission)

### Run ServiceHub

```bash
git clone https://github.com/debdevops/servicehub.git
cd servicehub
./run.sh
```

Open **http://localhost:3000**

---

## Connect to Your Namespace

Enter your connection string and connect:

![Connection Form](docs/screenshots/02-quickstart-connection-form.png)
*Enter a display name and your Service Bus connection string*

Once connected, your queues and topics appear in the sidebar:

![Connected Namespace](docs/screenshots/03-quickstart-connected-namespace.png)
*Namespace shows 2 queues and 2 topics with subscription counts*

---

## Core Features

### 1. Message Browser

Click any queue or subscription to browse messages:

![Empty Queue](docs/screenshots/04-feature-message-browser-empty.png)
![Full Messages](docs/screenshots/25-main-message-display.png)
*Empty queue showing Active and Dead-Letter tabs*

Generate test messages or view existing ones:

![Messages Loaded](docs/screenshots/08-hero-message-browser-loaded.png)
*50 messages loaded with event types, timestamps, and status badges*

### 2. Message Details

Click any message to view full details across three tabs:

**Properties Tab** — Message ID, timestamps, delivery count:

![Message Properties](docs/screenshots/25-main-message-display.png)
*Complete message metadata including enqueue time, TTL, and sequence number*

**Custom Properties** — Application headers and metadata:

![Custom Properties](docs/screenshots/12-feature-message-details-custom-props.png)
*Custom headers like ServiceHub-Generated, scenario, environment, region*

**Body Tab** — Full JSON message content:

![Message Body](docs/screenshots/14-feature-ai-findings-badge.png)
*Syntax-highlighted JSON body with copy button*

### 3. Search Messages

Search across message content, properties, and headers:

![Search](docs/screenshots/24-feature-search-functionality.png)
*Search "bank" finds 2 matching payment messages*

### 4. Dead-Letter Queue Investigation

View DLQ messages with failure details:

![DLQ Tab](docs/screenshots/18-feature-dlq-tab-with-ai.png)
*Dead-Letter tab showing 3 failed messages with AI indicators*

Inspect the dead-letter reason and Azure metadata:

![DLQ Properties](docs/screenshots/19-workflow-dlq-investigation-step1.png)
*Dead-letter reason, error description, and delivery count*

Review suggested actions:

![DLQ Guidance](docs/screenshots/20-workflow-dlq-investigation-step2.png)
*ServiceHub provides suggested actions based on failure patterns*

### 5. AI Pattern Detection

ServiceHub automatically detects error patterns across messages:

![AI Patterns](docs/screenshots/17-feature-ai-patterns-popup.png)
*AI detected 2 error patterns: email cluster (31%) and sms cluster (31%)*

Click any message to see its AI insights:

![AI Insights](docs/screenshots/15-feature-ai-insights-error-cluster.png)
*Error Cluster: push — 3 messages share this error pattern*

### 6. Replay DLQ Messages

After fixing the root cause, replay messages from DLQ:

![Replay Dialog](docs/screenshots/22-workflow-dlq-replay-step4.png)
*Confirm replay to re-send message to original queue*

---

## Testing Tools

### Message Generator

Generate realistic test messages with configurable scenarios:

![Generator Scenarios](docs/screenshots/07-feature-message-generator-scenarios.png)
*6 scenarios: Order Processing, Payment Gateway, Notifications, Inventory, User Activity, Error Events*

Features:
- Volume presets: 30, 50, 100, 150, 200 messages
- Anomaly rate slider: 0% to 50%
- Tagged with `ServiceHub-Generated` for easy cleanup

### Send Single Message

Send custom messages to any queue or topic:

![Send Message](docs/screenshots/09-feature-send-message-topic.png)
*Send JSON message to topic with custom properties*

---

## Quick Actions Menu

Click the floating action button (bottom-right) for quick actions:

![FAB Menu](docs/screenshots/05-feature-fab-menu.png)
*Refresh All, Test DLQ, Generate Messages, Send Message*

---

## Architecture

```
servicehub/
├── apps/web/          # React + TypeScript frontend
├── services/api/      # ASP.NET Core backend
│   └── src/
│       ├── ServiceHub.Api/           # REST API
│       ├── ServiceHub.Core/          # Domain entities
│       └── ServiceHub.Infrastructure/ # Azure SDK integration
└── run.sh             # Start both services
```

### Tech Stack
- **Frontend:** React 18, TypeScript, Tailwind CSS, TanStack Query
- **Backend:** ASP.NET Core 8, Azure.Messaging.ServiceBus SDK
- **AI Analysis:** Client-side heuristic pattern detection

---

## Security

- **Read-only by design** — Uses `PeekMessagesAsync` (messages stay in queue)
- **Listen permission only** — Create a dedicated SAS policy with Listen-only access
- **No data storage** — Messages are never persisted, only displayed in-memory
- **Local AI analysis** — Pattern detection runs in your browser, no external API calls

---

## Use Cases

✅ **Incident Investigation** — View message content during 2 AM outages  
✅ **DLQ Forensics** — Understand why messages failed  
✅ **Correlation Tracking** — Search by order ID or correlation ID  
✅ **Integration Testing** — Generate realistic test messages  
✅ **Post-Mortem Analysis** — Review message patterns after incidents  

---

## Roadmap

- [ ] Bulk DLQ replay
- [ ] Message export (JSON/CSV)
- [ ] Advanced regex search
- [ ] Multi-namespace support
- [ ] Session-aware browsing

---

## Contributing

1. Fork the repository
2. Create a feature branch
3. Submit a pull request

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

---

## License

MIT License — see [LICENSE](LICENSE) for details.

---

## Links

- **Repository:** https://github.com/debdevops/servicehub
- **Issues:** https://github.com/debdevops/servicehub/issues
- **Documentation:** [docs/COMPREHENSIVE-GUIDE.md](docs/COMPREHENSIVE-GUIDE.md)

---

**ServiceHub** — Because your Service Bus messages shouldn't be invisible during incidents.
