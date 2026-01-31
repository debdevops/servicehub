# ServiceHub

**A Forensic Investigation Workbench for Azure Service Bus**

> A Class-A enterprise-grade tool for forensic investigation of Azure Service Bus. Used during incident response for safe, point-in-time message browsing, dead-letter queue analysis, and controlled message replay.

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![React](https://img.shields.io/badge/React-18.3-61DAFB?logo=react)](https://react.dev/)
[![TypeScript](https://img.shields.io/badge/TypeScript-5.7-3178C6?logo=typescript)](https://www.typescriptlang.org/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

---

## 🎯 What is ServiceHub?

ServiceHub solves a critical problem for teams using Azure Service Bus: **visibility into message queues**. When messages fail to process or queues back up, debugging becomes like fixing a car engine with the hood closed.

### The Problem Without ServiceHub

❌ Messages disappear into a "black box"  
❌ No visibility into queue contents  
❌ Can't see why messages failed  
❌ Manual investigation takes hours  
❌ No pattern detection for recurring issues  
❌ Hard to debug production problems safely  

### The Solution With ServiceHub

✅ **Point-in-time visibility** into all messages for stable investigation  
✅ **Dead-letter queue inspection** — see exactly what failed and why  
✅ **Optional AI-powered analysis** — identify recurring issue patterns  
✅ **Read-mostly by design** — safe for production forensics  
✅ **Outlook-style browsing** — designed for 4-8 hour debugging sessions  
✅ **Safe message replay** — reprocess failed messages with no risk of message loss  
✅ **Class-A quality** — enterprise-grade trust and clarity  

---

## 📖 Documentation

### Quick Links

| Document | Purpose | Audience |
|----------|---------|----------|
| **[Comprehensive Guide](docs/COMPREHENSIVE-GUIDE.md)** | Complete guide with diagrams | Everyone (novices to experts) |
| [API Documentation](services/api/README.md) | Backend API reference | Backend developers |
| [API Architecture](services/api/ARCHITECTURE.md) | System design & patterns | Architects, senior devs |
| [Frontend Guide](apps/web/README.md) | React app documentation | Frontend developers |
| [Deployment Guide](services/api/DEPLOYMENT_OPERATIONS.md) | Production deployment | DevOps, SREs |

### 📊 Visual Documentation

The [Comprehensive Guide](docs/COMPREHENSIVE-GUIDE.md) includes **high-resolution Mermaid diagrams**:

- 🏗️ **System Architecture** — How all components work together
- 🔄 **Complete Application Flows** — Step-by-step sequences
- 💾 **Data Flow Diagrams** — Request/response cycles
- 🔐 **Security Model** — Authentication & encryption
- 🚀 **Deployment Architecture** — Production setup
- 🧩 **Component Details** — Frontend & backend breakdown

---

## 🚀 Quick Start

### Prerequisites

- **.NET 8 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Node.js 20+** — [Download](https://nodejs.org/)
- **Azure Service Bus namespace** — [Create one](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-quickstart-portal)

### 1. Clone the Repository

```bash
git clone https://github.com/YOUR-USERNAME/servicehub.git
cd servicehub
```

### 2. Start the Backend API

```bash
cd services/api
dotnet restore
dotnet run --project src/ServiceHub.Api/ServiceHub.Api.csproj
```

API will be available at: **http://localhost:5153**  
Swagger UI: **http://localhost:5153/swagger**

### 3. Start the Frontend

```bash
cd apps/web
npm install
npm run dev
```

UI will be available at: **http://localhost:3000**

### 4. Connect to Azure Service Bus

1. Open **http://localhost:3000**
2. Click **"Connect to Service Bus"**
3. Create a Shared Access Policy with **Manage**, **Send**, and **Listen** permissions (do not use RootManageSharedAccessKey)
4. Enter your Azure Service Bus connection string:
   ```
   Endpoint=sb://YOUR-NAMESPACE.servicebus.windows.net/;
   SharedAccessKeyName=ServiceHub-Policy;
   SharedAccessKey=YOUR-KEY
   ```
5. Click **"Connect"**

You're ready to inspect your queues! 🎉

---

## 🏗️ Architecture Overview

ServiceHub uses a **Clean Architecture** approach with clear separation of concerns:

```
┌─────────────────────────────────────────────────────────────┐
│                    🌐 React Frontend                        │
│  (TypeScript + React Query + Tailwind CSS + Vite)          │
│  • Message browsing  • Queue inspection  • AI insights     │
└────────────────┬────────────────────────────────────────────┘
                 │ HTTP/REST API
┌────────────────▼────────────────────────────────────────────┐
│                    🔧 .NET 8 Backend API                    │
│        (Clean Architecture + Azure Service Bus SDK)        │
│  • REST endpoints  • Business logic  • Data transformation │
└────────────────┬────────────────────────────────────────────┘
                 │ AMQP Protocol
┌────────────────▼────────────────────────────────────────────┐
│                 ☁️ Azure Service Bus                        │
│    • Queues  • Topics  • Dead-letter queues  • Messages    │
└─────────────────────────────────────────────────────────────┘
```

**Key Features:**

- **Read-mostly by design** — All browsing uses non-destructive peeks. Write actions (like Replay) are explicit and require user confirmation.
- **Dead-letter queue support** — Inspect failed messages
- **Safe message replay** — Send DLQ messages back to the main queue with an at-least-once guarantee.
- **Optional AI analysis** — ML-powered insights that never block core workflows.
- **Connection pooling** — Efficient client management
- **Encryption** — Connection strings encrypted at rest

See the [Comprehensive Guide](docs/COMPREHENSIVE-GUIDE.md) for detailed architecture diagrams.

---

## 🎨 User Interface

ServiceHub provides an **Outlook-style interface** designed for long debugging sessions:

### Main Features

1. **Sidebar Navigation**
   - Namespace selector
   - Queue tree view
   - Topic/subscription hierarchy
   - Message counts (active + DLQ)

2. **Message Browser**
   - Virtualized list (handles 10,000+ messages)
   - Active/Dead-letter tabs
   - Status badges (Normal/Retried/Dead-Letter)
   - AI pattern indicators

3. **Detail Panel**
   - **Properties Tab** — Message metadata with ServiceHub analysis
   - **Body Tab** — JSON syntax highlighting
   - **AI Insights Tab** — Pattern membership and recommendations
   - **Headers Tab** — System and custom headers

4. **Actions**
   - Send message (testing)
   - Generate test messages
   - Replay DLQ messages
   - Copy message ID

### Design Philosophy

- **Sky Blue + White** theme (no dark mode)
- **Class-A quality** — Clear fact vs. inference separation
- **Trust-focused** — All assessments clearly labeled
- **Accessible** — ARIA labels, keyboard navigation

---

## 🔐 Security

ServiceHub takes security seriously:

### Data Protection

- ✅ **AES-256 encryption** for connection strings at rest
- ✅ **Azure Key Vault** integration for secrets
- ✅ **No credential logging** — connection strings never appear in logs
- ✅ **Dedicated policies** — use custom Shared Access Policies, not root keys

### API Security

- ✅ **CORS protection** — Whitelist of allowed origins
- ✅ **Rate limiting** — 100 requests/minute per IP
- ✅ **API Key Authentication** — Enabled by default in production environments.
- ✅ **Input validation** — All requests validated

### Azure Permissions Required

ServiceHub requires a Shared Access Policy with **Manage**, **Send**, and **Listen** permissions for full functionality.

**To Create:**
1. Azure Portal → Service Bus → Shared Access Policies → + Add
2. Name: `ServiceHub-Policy`
3. Check: ✅ Manage, ✅ Send, ✅ Listen
4. Use the connection string from this policy (not RootManageSharedAccessKey)

**What Each Permission Enables:**
- **Listen**: Browse messages, view queue/topic metrics
- **Send**: Replay messages from DLQ, create test DLQ messages
- **Manage**: Full control (future features)

See the **[Permissions Guide](docs/PERMISSIONS.md)** for detailed information about permission requirements.

---

## 📦 Technology Stack

### Frontend

- **React 18.3** — UI library
- **TypeScript 5.7** — Type safety
- **Vite 6** — Build tool & dev server
- **React Query** — Server state management
- **Tailwind CSS 3** — Styling
- **React Router 7** — Navigation
- **Lucide React** — Icons
- **React Hot Toast** — Notifications

### Backend

- **.NET 8** — Runtime & framework
- **ASP.NET Core** — Web API framework
- **Azure Service Bus SDK** — Queue integration
- **SQLite** — Local persistence
- **Serilog** — Structured logging
- **Swashbuckle** — OpenAPI/Swagger

### Infrastructure

- **Azure Service Bus** — Message queuing
- **Azure Key Vault** — (Optional) Secret management
- **Docker** — Containerization
- **Kubernetes** — (Optional) Orchestration

---

## 🧪 Testing

### Run Backend Tests

```bash
cd services/api
dotnet test
```

### Run Frontend Tests

```bash
cd apps/web
npm run test
```

### Integration Tests

```bash
cd services/api/tests/ServiceHub.IntegrationTests
dotnet test
```

---

## 🚀 Deployment

### Docker

```bash
# Build images
docker-compose build

# Run containers
docker-compose up -d
```

### Kubernetes

```bash
# Apply manifests
kubectl apply -f infrastructure/k8s/

# Check status
kubectl get pods -n servicehub
```

### Azure

See [Deployment Guide](services/api/DEPLOYMENT_OPERATIONS.md) for:
- Azure App Service deployment
- Azure Container Apps
- Azure Kubernetes Service (AKS)
- Environment configuration
- Monitoring & logging

---

## 🤝 Contributing

We welcome contributions! Please see our contributing guidelines.

### Development Setup

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests
5. Submit a pull request

### Code Quality Standards

- ✅ **Class-A quality** — Trust and clarity first
- ✅ **Type safety** — TypeScript strict mode
- ✅ **Clean Architecture** — Clear layer separation
- ✅ **Unit tests** — High coverage
- ✅ **Documentation** — Code comments + markdown

---

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## 🙏 Acknowledgments

- **Azure Service Bus team** — For the excellent SDK and documentation
- **React Query** — For making server state management simple
- **Clean Architecture** — For the architectural principles

---

## 📧 Support

- **Documentation**: [docs/COMPREHENSIVE-GUIDE.md](docs/COMPREHENSIVE-GUIDE.md)
- **Issues**: [GitHub Issues](https://github.com/YOUR-USERNAME/servicehub/issues)
- **Discussions**: [GitHub Discussions](https://github.com/YOUR-USERNAME/servicehub/discussions)

---

**Built with ❤️ for engineers debugging Azure Service Bus**

*Last Updated: January 26, 2026*
