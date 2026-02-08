# ServiceHub API

**AI-Powered Azure Service Bus Inspector API** built with .NET 8 and Clean Architecture.

> This README provides quick start instructions and API reference. For complete documentation with architecture diagrams, design patterns, and detailed flows, see:
> - **[Comprehensive Guide](../../docs/COMPREHENSIVE-GUIDE.md)** — Complete guide with Mermaid diagrams
> - **[Architecture Details](ARCHITECTURE.md)** — 805 lines of architectural documentation
> - **[Documentation Index](DOCUMENTATION_INDEX.md)** — Index of all API documentation

---

## 📖 Documentation Overview

| Document | Purpose | Audience |
|----------|---------|----------|
| **[Comprehensive Guide](../../docs/COMPREHENSIVE-GUIDE.md)** | Complete guide with diagrams | Everyone (novices to experts) |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Deep architectural details (805 lines) | Architects, senior developers |
| [DOCUMENTATION_INDEX.md](DOCUMENTATION_INDEX.md) | Index of 12 architectural diagrams | All developers |
| [IMPLEMENTATION_PATTERNS.md](IMPLEMENTATION_PATTERNS.md) | Code patterns & conventions | Backend developers |
| [DEPLOYMENT_OPERATIONS.md](DEPLOYMENT_OPERATIONS.md) | Production deployment guide | DevOps, SREs |
| [FIXES_APPLIED.md](FIXES_APPLIED.md) | Applied fixes history | Maintainers |

---

## 🚀 Quick Start

### Prerequisites
- **.NET 8 SDK** ([Download](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Azure Service Bus namespace** (for testing)
- **VS Code** or **Visual Studio 2022**

### Run Locally

```bash
# Navigate to API directory
cd services/api

# Restore packages
dotnet restore

# Run the API
dotnet run --project src/ServiceHub.Api/ServiceHub.Api.csproj

# Or use watch mode (hot reload for development)
dotnet watch --project src/ServiceHub.Api/ServiceHub.Api.csproj
```

### Access Points

- **API**: http://localhost:5000
- **Swagger UI**: http://localhost:5000/swagger
- **Health Check**: http://localhost:5000/health
- **Ready Check**: http://localhost:5000/health/ready
- **Live Check**: http://localhost:5000/health/live

---

## 📋 First API Calls

### 1. Create a Namespace Connection

```bash
curl -X POST http://localhost:5000/api/v1/namespaces \
  -H "Content-Type: application/json" \
  -d '{
    "name": "my-servicebus",
    "connectionString": "Endpoint=sb://YOUR-NAMESPACE.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=YOUR-KEY",
    "authType": 0,
    "displayName": "My Service Bus",
    "description": "Production Service Bus"
  }'
```

**Response:**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "my-servicebus",
  "displayName": "My Service Bus",
  "description": "Production Service Bus",
  "authType": "ConnectionString",
  "isActive": true,
  "createdAt": "2026-01-17T10:00:00Z",
  "modifiedAt": null,
  "lastConnectionTestAt": null,
  "lastConnectionTestSucceeded": null
}
```

### 2. Test Namespace Connection

```bash
curl -X GET http://localhost:5000/api/v1/namespaces/{namespaceId}/test
```

**Response:**
```json
{
  "isConnected": true,
  "message": "Connection successful",
  "testedAt": "2026-01-17T10:05:00Z"
}
```

### 3. List All Queues

```bash
curl -X GET "http://localhost:5000/api/v1/queues?namespaceId={namespaceId}"
```

**Response:**
```json
[
  {
    "name": "my-queue",
    "activeMessageCount": 42,
    "deadLetterMessageCount": 0,
    "sizeInBytes": 8192,
    "status": "Active",
    "createdAt": "2026-01-10T08:00:00Z",
    "maxSizeInMegabytes": 1024,
    "requiresDuplicateDetection": false,
    "requiresSession": false
  }
]
```

### 4. Send a Message to Queue

```bash
curl -X POST http://localhost:5000/api/v1/messages/queue/my-queue \
  -H "Content-Type: application/json" \
  -d '{
    "namespaceId": "{namespaceId}",
    "entityName": "my-queue",
    "body": "Hello from ServiceHub!",
    "contentType": "text/plain",
    "applicationProperties": {
      "source": "api-test",
      "priority": "high",
      "timestamp": "2026-01-17T10:00:00Z"
    }
  }'
```

**Response:** `202 Accepted`

### 5. Peek Messages from Queue

```bash
curl -X GET "http://localhost:5000/api/v1/messages/queue/my-queue?namespaceId={namespaceId}&maxMessages=10"
```

**Response:**
```json
[
  {
    "messageId": "abc123",
    "sequenceNumber": 1,
    "body": "Hello from ServiceHub!",
    "contentType": "text/plain",
    "enqueuedTime": "2026-01-17T10:00:00Z",
    "deliveryCount": 0,
    "state": "Active",
    "applicationProperties": {
      "source": "api-test",
      "priority": "high"
    }
  }
]
```

### 6. List Topics

```bash
curl -X GET "http://localhost:5000/api/v1/topics?namespaceId={namespaceId}"
```

### 7. List Subscriptions for a Topic

```bash
curl -X GET "http://localhost:5000/api/v1/subscriptions?namespaceId={namespaceId}&topicName=my-topic"
```

### 8. Peek Dead Letter Messages

```bash
curl -X GET "http://localhost:5000/api/v1/messages/queue/my-queue/deadletter?namespaceId={namespaceId}&maxMessages=10"
```

### 9. Get All Namespaces

```bash
curl -X GET http://localhost:5000/api/v1/namespaces
```

### 10. Delete a Namespace

```bash
curl -X DELETE http://localhost:5000/api/v1/namespaces/{namespaceId}
```

---

## 🏗️ Architecture

```
ServiceHub.Api/              # HTTP Layer
├── Controllers/             # API endpoints
│   └── V1/                 # Version 1 controllers
│       ├── NamespacesController.cs
│       ├── QueuesController.cs
│       ├── TopicsController.cs
│       ├── SubscriptionsController.cs
│       ├── MessagesController.cs
│       ├── AnomaliesController.cs
│       └── HealthController.cs
├── Middleware/             # Request pipeline
│   ├── ErrorHandlingMiddleware.cs
│   ├── CorrelationIdMiddleware.cs
│   ├── RequestLoggingMiddleware.cs
│   └── RateLimitingMiddleware.cs
├── Configuration/          # Service configuration
│   ├── CorsConfiguration.cs
│   ├── SwaggerConfiguration.cs
│   └── HealthCheckConfiguration.cs
└── Extensions/             # Service registration
    └── ServiceCollectionExtensions.cs

ServiceHub.Core/             # Domain Logic
├── Entities/               # Domain entities
│   ├── Namespace.cs
│   ├── Message.cs
│   └── Anomaly.cs
├── DTOs/                   # Data transfer objects
│   ├── Requests/
│   └── Responses/
├── Interfaces/             # Abstractions
└── Enums/                  # Domain enums

ServiceHub.Infrastructure/   # External Integrations
├── ServiceBus/             # Azure Service Bus
│   ├── ServiceBusClientFactory.cs
│   ├── ServiceBusClientCache.cs
│   ├── ServiceBusClientWrapper.cs
│   ├── MessageSender.cs
│   └── MessageReceiver.cs
├── Persistence/            # Data storage
├── Security/               # Encryption, auth
└── AI/                     # AI service client

ServiceHub.Shared/          # Cross-cutting Concerns
├── Results/                # Result pattern
├── Constants/              # Error codes, routes
└── Helpers/                # Utilities
```

---

## ⚙️ Configuration

### Environment Variables

Copy `.env.example` to `.env` and configure:

```bash
cp .env.example .env
```

Key settings:
- `ASPNETCORE_ENVIRONMENT`: Development, Staging, Production
- `ENCRYPTION_KEY`: For connection string encryption (32+ characters)
- `AI_SERVICE_URL`: AI service endpoint (optional)
- `CORS_ORIGINS`: Allowed frontend origins

### appsettings.json

Main configuration file with:
- **Logging**: Log levels by namespace
- **ServiceBus**: Connection settings, retry policies
- **Security**: Encryption settings
- **AI**: AI service configuration
- **RateLimit**: API rate limiting
- **Swagger**: API documentation settings

---

## 🔒 Security Features

1. **Connection String Encryption**
   - AES-256 encryption for stored connection strings
   - Configurable via `Security:EnableConnectionStringEncryption`

2. **CORS Policy**
   - Configurable allowed origins
   - Prevents unauthorized cross-origin requests

3. **Rate Limiting**
   - Protects API from abuse
   - Configurable limits per endpoint

4. **Request Correlation**
   - Tracks requests across distributed systems
   - Automatic correlation ID generation

---

## 🧪 Testing

### Unit Tests
```bash
dotnet test tests/ServiceHub.UnitTests/ServiceHub.UnitTests.csproj
```

### Integration Tests
```bash
dotnet test tests/ServiceHub.IntegrationTests/ServiceHub.IntegrationTests.csproj
```

### All Tests
```bash
dotnet test
```

---

## 📊 Health Checks

The API provides three health check endpoints:

1. **General Health**: `/health`
   - Overall application health
   - Returns 200 OK if healthy

2. **Readiness**: `/health/ready`
   - Checks if app is ready to accept requests
   - Verifies dependencies (database, external services)

3. **Liveness**: `/health/live`
   - Checks if app is running
   - Used for restart decisions

---

## 🐳 Docker Support (Coming Soon)

```bash
# Build image
docker build -t servicehub-api .

# Run container
docker run -p 5000:5000 servicehub-api
```

---

## 📈 API Endpoints

### Namespaces
- `POST /api/v1/namespaces` - Create namespace connection
- `GET /api/v1/namespaces` - List all namespaces
- `GET /api/v1/namespaces/{id}` - Get namespace by ID
- `GET /api/v1/namespaces/{id}/test` - Test connection
- `DELETE /api/v1/namespaces/{id}` - Delete namespace

### Queues
- `GET /api/v1/queues?namespaceId={id}` - List queues
- `GET /api/v1/queues/{queueName}?namespaceId={id}` - Get queue details

### Topics
- `GET /api/v1/topics?namespaceId={id}` - List topics
- `GET /api/v1/topics/{topicName}?namespaceId={id}` - Get topic details

### Subscriptions
- `GET /api/v1/subscriptions?namespaceId={id}&topicName={topic}` - List subscriptions
- `GET /api/v1/subscriptions/{subscriptionName}?namespaceId={id}&topicName={topic}` - Get subscription details

### Messages
- `POST /api/v1/messages/queue/{queueName}` - Send to queue
- `POST /api/v1/messages/topic/{topicName}` - Send to topic
- `GET /api/v1/messages/queue/{queueName}?namespaceId={id}` - Peek queue messages
- `GET /api/v1/messages/queue/{queueName}/deadletter?namespaceId={id}` - Peek DLQ
- `GET /api/v1/messages/topic/{topicName}/subscription/{subscriptionName}?namespaceId={id}` - Peek subscription
- `GET /api/v1/messages/topic/{topicName}/subscription/{subscriptionName}/deadletter?namespaceId={id}` - Peek subscription DLQ

### Anomalies (AI-Powered)
- `POST /api/v1/anomalies/detect?namespaceId={id}` - Detect anomalies
- `GET /api/v1/anomalies/{id}` - Get anomaly by ID

### Health
- `GET /health` - General health
- `GET /health/ready` - Readiness check
- `GET /health/live` - Liveness check

---

## 🎯 Clean Architecture Principles

1. **Dependency Flow**: API → Infrastructure → Core ← Shared
2. **Domain Isolation**: Core has no external dependencies
3. **Result Pattern**: Consistent error handling throughout
4. **DTOs**: Clear separation between domain and API models
5. **Interfaces**: Abstractions for all external dependencies

---

## 🔧 Development

### Prerequisites
```bash
dotnet --version  # Should be 8.0 or higher
```

### Build
```bash
dotnet build
```

### Clean
```bash
dotnet clean
```

### Restore
```bash
dotnet restore
```

### Run with Hot Reload
```bash
dotnet watch run --project src/ServiceHub.Api/ServiceHub.Api.csproj
```

---

## 📚 Additional Resources

- [Azure Service Bus Documentation](https://docs.microsoft.com/azure/service-bus-messaging/)
- [.NET 8 Documentation](https://docs.microsoft.com/dotnet/core/whats-new/dotnet-8)
- [Clean Architecture](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)
- [Result Pattern](https://enterprisecraftsmanship.com/posts/error-handling-exception-or-result/)

---

## 🏗️ Clean Architecture Overview

ServiceHub API follows **Clean Architecture** principles with clear layer separation:

### Layer Structure

```
┌─────────────────────────────────────────────────────────┐
│                   Presentation Layer                    │
│              (ServiceHub.Api - ASP.NET Core)            │
│  • REST Controllers  • Middleware  • Filters           │
│  • No business logic - only HTTP concerns              │
└────────────────┬────────────────────────────────────────┘
                 │ Depends on ↓
┌────────────────▼────────────────────────────────────────┐
│                     Domain Layer                        │
│                (ServiceHub.Core)                        │
│  • Entities  • DTOs  • Interfaces  • Enums             │
│  • No dependencies - pure domain models                │
└────────────────┬────────────────────────────────────────┘
                 │ Implemented by ↓
┌────────────────▼────────────────────────────────────────┐
│                Infrastructure Layer                     │
│            (ServiceHub.Infrastructure)                  │
│  • Azure Service Bus client  • SQLite  • AI services   │
│  • Implements Core interfaces                          │
└─────────────────────────────────────────────────────────┘
```

### Key Principles Applied

1. **Dependency Inversion** — Core defines interfaces, Infrastructure implements them
2. **Single Responsibility** — Each layer has one clear purpose
3. **Separation of Concerns** — Business logic separate from infrastructure
4. **Testability** — Easy to mock dependencies

See [ARCHITECTURE.md](ARCHITECTURE.md) for detailed architectural diagrams and patterns.

---

## 📝 License

MIT License - see LICENSE file for details

---

## 👥 Contributing

Contributions are welcome! Please follow:
1. Fork the repository
2. Create a feature branch
3. Commit your changes
4. Push to the branch
5. Create a Pull Request

---

## 🆘 Support

For issues, questions, or feature requests:
- Open an issue on GitHub
- Check existing documentation
- Review Swagger UI for API details

---

**Built with ❤️ using .NET 8, Clean Architecture, and Azure Service Bus**
