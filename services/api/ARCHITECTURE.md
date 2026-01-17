# ServiceHub API Architecture & Design

## Overview

ServiceHub API is built on **Clean Architecture** principles with four distinct layers:
- **Shared**: Common types, constants, and utilities
- **Core**: Business logic and domain interfaces
- **Infrastructure**: Implementations, external service integrations
- **API**: HTTP handlers, middleware, and ASP.NET Core configuration

This document provides deep architectural insights through diagrams and detailed explanations.

---

## 1. Architecture Overview - Layered Design Diagram

```mermaid
graph TB
    subgraph Presentation["🎨 Presentation Layer"]
        REST["REST API Endpoints"]
        SWAGGER["Swagger/OpenAPI UI"]
        HEALTH["Health Checks"]
    end

    subgraph API["🔌 API Layer (ServiceHub.Api)"]
        CTRL["Controllers"]
        FILTER["Filters & Validators"]
        MIDDLEWARE["Middleware Pipeline"]
        EXT["Extensions & Configuration"]
    end

    subgraph Core["💼 Core Layer (ServiceHub.Core)"]
        ENTITY["Domain Entities"]
        INTERFACE["Service Interfaces"]
        RESULT["Result Types"]
    end

    subgraph Infrastructure["⚙️ Infrastructure Layer (ServiceHub.Infrastructure)"]
        IMPL["Service Implementations"]
        SB["Azure Service Bus"]
        AI["AI Service"]
        REPO["Repositories"]
        CRYPTO["Encryption"]
    end

    subgraph Shared["🧩 Shared Layer (ServiceHub.Shared)"]
        CONST["Constants"]
        HELPER["Helpers & Utilities"]
        MODEL["Data Models"]
    end

    subgraph External["🌐 External Systems"]
        ASB["Azure Service Bus"]
        AIAPI["AI API"]
        KV["Azure Key Vault"]
    end

    REST --> CTRL
    SWAGGER --> CTRL
    HEALTH --> CTRL
    CTRL --> FILTER
    CTRL --> INTERFACE
    FILTER --> MIDDLEWARE
    MIDDLEWARE --> EXT
    INTERFACE --> IMPL
    IMPL --> REPO
    IMPL --> CRYPTO
    IMPL --> SB
    IMPL --> AI
    ENTITY --> INTERFACE
    RESULT --> CTRL
    HELPER --> IMPL
    CONST --> IMPL
    REPO --> ENTITY
    SB --> ASB
    AI --> AIAPI
    CRYPTO --> KV

    style Presentation fill:#e1f5ff
    style API fill:#f3e5f5
    style Core fill:#e8f5e9
    style Infrastructure fill:#fff3e0
    style Shared fill:#fce4ec
    style External fill:#f5f5f5
```

---

## 2. Request/Response Sequential Flow

```mermaid
sequenceDiagram
    participant Client as HTTP Client
    participant Middleware as Middleware Pipeline
    participant Controller as Controller
    participant Service as Service Layer
    participant Repository as Repository
    participant Cache as In-Memory Cache
    participant Logger as Logger

    Client->>Middleware: HTTP Request
    Note over Middleware: 1. Security Headers
    Note over Middleware: 2. Error Handling
    Note over Middleware: 3. Correlation ID
    Note over Middleware: 4. Request Logging
    Note over Middleware: 5. API Key Auth
    Note over Middleware: 6. Rate Limiting

    Middleware->>Controller: Processed Request
    Controller->>Controller: Validate Input
    Controller->>Service: Call Business Logic
    
    Service->>Cache: Check Cache
    alt Cache Hit
        Cache-->>Service: Cached Result
    else Cache Miss
        Service->>Repository: Fetch Data
        Repository-->>Service: Domain Entity
        Service->>Cache: Store Result
    end

    Service-->>Controller: Result<T>
    Controller->>Logger: Log Operation
    Controller-->>Middleware: Response Object
    
    Middleware->>Middleware: Add Security Headers
    Middleware->>Logger: Log Response
    Middleware-->>Client: HTTP Response (JSON)

    Note over Client,Logger: Total Flow: ~5-50ms
```

---

## 3. Detailed Class & Dependency Injection Diagram

```mermaid
graph LR
    subgraph DI["Dependency Injection Container"]
        direction TB
        HTTP["IHttpContextAccessor"]
        CONFIG["IConfiguration"]
        LOGGER["ILogger"]
        OPTIONS["IOptions"]
    end

    subgraph Services["Core Services"]
        direction TB
        NSMGR["INamespaceService<br/>Manages connections"]
        MSGMGR["IMessageService<br/>CRUD operations"]
        QMGR["IQueueService<br/>Queue operations"]
        TMGR["ITopicService<br/>Topic operations"]
    end

    subgraph Infrastructure_Impl["Infrastructure Implementations"]
        direction TB
        NSMGR_IMPL["NamespaceService"]
        MSGMGR_IMPL["MessageService"]
        QMGR_IMPL["QueueService"]
        TMGR_IMPL["TopicService"]
        SB_FACT["ServiceBusClientFactory"]
        REPO["INamespaceRepository"]
        REPO_IMPL["InMemoryNamespaceRepository"]
    end

    subgraph Security["Security Services"]
        direction TB
        AUTH["ConnectionStringProtector<br/>AES-GCM Encryption"]
        APIKEY["ApiKeyAuthenticationMiddleware"]
        SECEADER["SecurityHeadersMiddleware"]
        LOGGER_PROV["RedactingLoggerProvider<br/>Log Redaction"]
    end

    subgraph External_SDK["External SDKs"]
        direction TB
        SB_SDK["Azure.Messaging.ServiceBus"]
        AZURE_ID["Azure.Identity"]
    end

    CONFIG --> AUTH
    LOGGER --> LOGGER_PROV
    OPTIONS --> SECEADER
    HTTP --> APIKEY

    Services -->|implemented by| Infrastructure_Impl
    NSMGR_IMPL --> SB_FACT
    NSMGR_IMPL --> REPO
    REPO --> REPO_IMPL
    MSGMGR_IMPL --> SB_FACT
    QMGR_IMPL --> SB_FACT
    TMGR_IMPL --> SB_FACT
    SB_FACT --> SB_SDK
    SB_FACT --> AZURE_ID
    NSMGR_IMPL --> AUTH

    DI -.->|provides| Services
    Infrastructure_Impl -.->|uses| DI

    style DI fill:#fff9c4
    style Services fill:#c8e6c9
    style Infrastructure_Impl fill:#ffe0b2
    style Security fill:#ffccbc
    style External_SDK fill:#eceff1
```

---

## 4. API Request Processing Pipeline - Flow Diagram

```mermaid
graph TD
    A["📨 HTTP Request<br/>Received"] --> B["🔐 SecurityHeadersMiddleware<br/>Add Security Headers"]
    B --> C["⚠️ ErrorHandlingMiddleware<br/>Exception Handling"]
    C --> D["🔗 CorrelationIdMiddleware<br/>Track Request Trace"]
    D --> E["📝 RequestLoggingMiddleware<br/>Log Request Details"]
    E --> F["🔑 ApiKeyAuthenticationMiddleware<br/>Validate API Key"]
    
    F --> G{API Key<br/>Valid?}
    G -->|No| H["❌ 401/403 Response<br/>Return Error"]
    H --> I["🔄 SecurityHeadersMiddleware<br/>Add Headers to Error Response"]
    I --> J["📤 Return Response"]
    
    G -->|Yes| K["⏱️ RateLimitingMiddleware<br/>Check Rate Limit"]
    K --> L{Rate Limit<br/>Exceeded?}
    L -->|Yes| M["❌ 429 Response<br/>Too Many Requests"]
    M --> I
    
    L -->|No| N["📦 ResponseCompressionMiddleware<br/>Prepare Compression"]
    N --> O["🌐 CorsMiddleware<br/>Add CORS Headers"]
    O --> P["📚 Swagger Middleware<br/>API Documentation"]
    P --> Q["🛣️ RoutingMiddleware<br/>Route to Controller"]
    
    Q --> R["🎯 Controller Action<br/>Process Request"]
    R --> S["✅ Service Layer<br/>Business Logic"]
    S --> T["💾 Repository/Cache<br/>Data Access"]
    
    T --> U{Operation<br/>Success?}
    U -->|Error| V["⚠️ Error Result<br/>Build Error Response"]
    U -->|Success| W["✅ Success Result<br/>Build Success Response"]
    
    V --> X["🔄 Serialize Response<br/>JSON/XML"]
    W --> X
    
    X --> Y["💾 Response Caching<br/>Cache Headers"]
    Y --> Z["📤 Add Security Headers<br/>Send Response"]
    Z --> J

    style A fill:#e3f2fd
    style B fill:#ffccbc
    style C fill:#ffccbc
    style D fill:#c8e6c9
    style E fill:#b2dfdb
    style F fill:#ffccbc
    style G fill:#fff9c4
    style H fill:#ffcdd2
    style I fill:#ffccbc
    style J fill:#e3f2fd
    style K fill:#ffccbc
    style L fill:#fff9c4
    style M fill:#ffcdd2
    style R fill:#bbdefb
    style S fill:#c8e6c9
    style T fill:#b2dfdb
```

---

## 5. Data Flow: Create Namespace to Access Messages

```mermaid
graph LR
    subgraph Client_Side["Client Application"]
        REQ["POST /api/v1/namespaces<br/>{connectionString, name}"]
    end

    subgraph Validation["1️⃣ Validation Layer"]
        VAL["ValidateModelAttribute<br/>Schema Validation"]
        DECRYPT["Decrypt Connection String<br/>AES-GCM"]
    end

    subgraph Business_Logic["2️⃣ Business Logic"]
        NSMGR["NamespaceService<br/>Create Namespace"]
        FACT["ServiceBusClientFactory<br/>Create Client"]
    end

    subgraph Storage["3️⃣ Data Storage"]
        REPO["InMemoryNamespaceRepository<br/>Store Metadata"]
        CACHE["IServiceBusClientCache<br/>Cache Client Connection"]
    end

    subgraph Usage["4️⃣ Use Namespace"]
        GETMSG["GET /api/v1/namespaces/:id/messages<br/>Fetch Messages"]
        RETRIEVE["ServiceBusReceiver<br/>Receive from Queue"]
    end

    subgraph Response["5️⃣ Response Handling"]
        SERIALIZE["Serialize Messages<br/>JSON Format"]
        HEADERS["Add Response Headers<br/>X-Total-Count, X-Page-Size"]
        CACHE_HEADERS["Add Cache Headers<br/>ETag, Last-Modified"]
    end

    REQ --> VAL
    VAL --> DECRYPT
    DECRYPT --> NSMGR
    NSMGR --> FACT
    FACT --> REPO
    REPO --> CACHE
    CACHE -.->|"Later Request"| GETMSG
    GETMSG --> RETRIEVE
    RETRIEVE --> SERIALIZE
    SERIALIZE --> HEADERS
    HEADERS --> CACHE_HEADERS
    CACHE_HEADERS --> Response["✅ HTTP 200 Response"]

    style Client_Side fill:#e1f5fe
    style Validation fill:#fff9c4
    style Business_Logic fill:#c8e6c9
    style Storage fill:#b2dfdb
    style Usage fill:#c8e6c9
    style Response fill:#b2dfdb
    style Response fill:#c8e6c9
```

---

## 6. Security Architecture - Defense in Depth

```mermaid
graph TB
    subgraph Encryption["🔒 Encryption Layer"]
        CONNSTR["Connection String Protection<br/>AES-GCM-256"]
        PRIVKEY["Derive Key from Master<br/>SHA-256"]
        NONCE["Random Nonce (96-bit)<br/>Unique per value"]
        TAG["Auth Tag (128-bit)<br/>Tamper detection"]
    end

    subgraph Authentication["🔑 Authentication Layer"]
        APIKEY["API Key Validation<br/>X-API-KEY Header"]
        BYPASS["Bypass for Health/Swagger<br/>Public endpoints"]
        LOG_MASKING["Log Masking<br/>Hide sensitive data"]
    end

    subgraph Headers["🛡️ Security Headers Layer"]
        XCT["X-Content-Type-Options<br/>nosniff"]
        XFO["X-Frame-Options<br/>DENY"]
        CSP["Content-Security-Policy<br/>Prod vs Dev"]
        HSTS["Strict-Transport-Security<br/>1 year, HTTPS only"]
        REF["Referrer-Policy<br/>strict-origin"]
        PERM["Permissions-Policy<br/>Disable browser features"]
    end

    subgraph Input_Validation["✅ Input Validation"]
        SCHEMA["Schema Validation<br/>FluentValidation"]
        SANITIZE["Input Sanitization<br/>XSS Prevention"]
        SIZE_LIMIT["Size Limits<br/>Prevent DoS"]
    end

    subgraph Monitoring["📊 Monitoring & Logging"]
        AUDIT["Audit Logging<br/>Track operations"]
        REDACT["Redacted Logs<br/>No secrets in logs"]
        CORRELATION["Correlation ID<br/>Request tracing"]
    end

    CONNSTR --> PRIVKEY
    PRIVKEY --> NONCE
    NONCE --> TAG
    APIKEY --> LOG_MASKING
    BYPASS --> APIKEY
    XCT --> CSP
    XFO --> HSTS
    REF --> PERM
    SCHEMA --> SANITIZE
    SANITIZE --> SIZE_LIMIT
    AUDIT --> REDACT
    REDACT --> CORRELATION

    Encryption -->|"Protects"| Authentication
    Authentication -->|"Enables"| Headers
    Headers -->|"Enforced by"| Input_Validation
    Input_Validation -->|"Tracked by"| Monitoring

    style Encryption fill:#ffccbc
    style Authentication fill:#ffccbc
    style Headers fill:#ffccbc
    style Input_Validation fill:#fff9c4
    style Monitoring fill:#c8e6c9
```

---

## 7. Middleware Pipeline Execution Order

```mermaid
graph TD
    A["Request Entry"] --> B["1. SecurityHeadersMiddleware<br/>Add Security Headers to Response<br/>Order: FIRST<br/>Scope: ALL requests"]
    
    B --> C["2. ErrorHandlingMiddleware<br/>Wrap request in try-catch<br/>Order: SECOND<br/>Scope: ALL requests"]
    
    C --> D["3. CorrelationIdMiddleware<br/>Generate/Extract CorrelationId<br/>Order: THIRD<br/>Scope: ALL requests"]
    
    D --> E["4. RequestLoggingMiddleware<br/>Log incoming request<br/>Order: FOURTH<br/>Scope: ALL requests"]
    
    E --> F["5. ApiKeyAuthenticationMiddleware<br/>Validate X-API-KEY header<br/>Order: FIFTH<br/>Scope: ALL (except /health, /swagger)"]
    
    F --> G["6. RateLimitingMiddleware<br/>Check rate limit per IP<br/>Order: SIXTH<br/>Scope: PRODUCTION only"]
    
    G --> H["7. ResponseCompressionMiddleware<br/>Enable gzip/brotli<br/>Order: SEVENTH<br/>Scope: ALL responses"]
    
    H --> I["8. CorsMiddleware<br/>Apply CORS policy<br/>Order: EIGHTH<br/>Scope: ALL cross-origin"]
    
    I --> J["9. SwaggerMiddleware<br/>Serve API docs<br/>Order: NINTH<br/>Scope: /swagger only"]
    
    J --> K["10. RoutingMiddleware<br/>Match route & select controller<br/>Order: TENTH<br/>Scope: ALL requests"]
    
    K --> L["11. ResponseCachingMiddleware<br/>Apply caching headers<br/>Order: ELEVENTH<br/>Scope: GET requests"]
    
    L --> M["🎯 CONTROLLER & ACTION<br/>Process business logic"]
    
    M -.->|"Response flows back through<br/>middleware in REVERSE order"| N["Response Header Assembly<br/>All middleware add response headers"]
    
    N --> O["🎁 HTTP Response to Client"]

    style A fill:#e3f2fd
    style B fill:#ffccbc
    style C fill:#ffccbc
    style D fill:#c8e6c9
    style E fill:#b2dfdb
    style F fill:#ffccbc
    style G fill:#ffccbc
    style H fill:#b2dfdb
    style I fill:#b2dfdb
    style J fill:#b2dfdb
    style K fill:#b2dfdb
    style L fill:#b2dfdb
    style M fill:#bbdefb
    style N fill:#c8e6c9
    style O fill:#e3f2fd
```

---

## 8. Entity Relationship & Domain Model

```mermaid
erDiagram
    NAMESPACE ||--o{ QUEUE : contains
    NAMESPACE ||--o{ TOPIC : contains
    TOPIC ||--o{ SUBSCRIPTION : contains
    QUEUE ||--o{ MESSAGE : contains
    SUBSCRIPTION ||--o{ MESSAGE : contains
    NAMESPACE ||--|| CONNECTION_STRING : "encrypted"

    NAMESPACE {
        string id PK
        string name UK
        string displayName
        string description
        datetime createdAt
        datetime updatedAt
        string connectionString FK
    }

    QUEUE {
        string id PK
        string namespaceId FK
        string name UK
        int deadLetterCount
        int activeMessageCount
        datetime createdAt
    }

    TOPIC {
        string id PK
        string namespaceId FK
        string name UK
        int subscriptionCount
        datetime createdAt
    }

    SUBSCRIPTION {
        string id PK
        string topicId FK
        string name UK
        int deadLetterCount
        int activeMessageCount
        datetime createdAt
    }

    MESSAGE {
        string id PK
        string queueId FK
        string subscriptionId FK
        string body
        string contentType
        string correlationId
        datetime enqueuedAt
        int deliveryCount
        datetime expiresAt
    }

    CONNECTION_STRING {
        string id PK
        string value_encrypted "ENC:V2:{nonce}{ciphertext}{tag}"
        string keyVersion
        datetime lastRotated
    }
```

---

## 9. Exception Handling Flow

```mermaid
graph TD
    A["Exception Occurs<br/>in Business Logic"] --> B{Exception<br/>Type?}
    
    B -->|ValidationException| C["❌ 400 Bad Request<br/>Client Error<br/>Input validation failed"]
    B -->|NotFoundException| D["❌ 404 Not Found<br/>Resource doesn't exist"]
    B -->|UnauthorizedException| E["❌ 401 Unauthorized<br/>Missing/Invalid credentials"]
    B -->|ForbiddenException| F["❌ 403 Forbidden<br/>Insufficient permissions"]
    B -->|TimeoutException| G["❌ 504 Gateway Timeout<br/>Operation timed out"]
    B -->|ServiceBusException| H["❌ 503 Service Unavailable<br/>External service error"]
    B -->|Unexpected Exception| I["❌ 500 Internal Server Error<br/>Unhandled exception"]
    
    C --> J["Build ProblemDetails<br/>RFC 7231 Format"]
    D --> J
    E --> J
    F --> J
    G --> J
    H --> J
    I --> J
    
    J --> K["Log Exception<br/>Full stack trace<br/>Correlation ID"]
    K --> L["Add Error Headers<br/>X-Error-Code<br/>X-Correlation-Id"]
    L --> M["Serialize JSON Response<br/>Include error details"]
    M --> N["Send Response<br/>Appropriate HTTP Status"]
    N --> O["Client Receives<br/>Structured Error"]

    style A fill:#ffcdd2
    style C fill:#ffcdd2
    style D fill:#ffcdd2
    style E fill:#ffcdd2
    style F fill:#ffcdd2
    style G fill:#ffcdd2
    style H fill:#ffcdd2
    style I fill:#ffcdd2
    style J fill:#fff9c4
    style K fill:#c8e6c9
    style L fill:#b2dfdb
    style M fill:#b2dfdb
    style N fill:#e3f2fd
```

---

## 10. Caching Strategy - In-Memory Cache Lifecycle

```mermaid
graph LR
    subgraph Request_Flow["Cache Lookup Flow"]
        A["🔍 Service Layer<br/>Needs Data"] --> B{Cache<br/>Hit?}
        B -->|Yes| C["⚡ Return from Cache<br/>~1-2ms"]
        B -->|No| D["💾 Fetch from<br/>Repository"]
        D --> E["📦 Store in Cache<br/>with TTL"]
        E --> F["✅ Return to Client"]
        C --> F
    end

    subgraph Cache_Types["Cache Strategy per Entity"]
        G["ServiceBus Client<br/>Cache Duration: 60 min<br/>Strategy: LRU"]
        H["Namespace List<br/>Cache Duration: 5 min<br/>Strategy: TTL"]
        I["Queue Messages<br/>Cache Duration: 1 min<br/>Strategy: Event-driven"]
    end

    subgraph Invalidation["Cache Invalidation"]
        J["Manual Invalidation<br/>On Create/Update/Delete"]
        K["Time-based Invalidation<br/>TTL Expiration"]
        L["Event-driven Invalidation<br/>Service Bus Events"]
    end

    A --> G
    B -.->|"Cache Hit"| H
    B -.->|"Cache Miss"| I
    J --> K
    K --> L
    L --> A

    style Request_Flow fill:#b2dfdb
    style Cache_Types fill:#c8e6c9
    style Invalidation fill:#fff9c4
```

---

## 11. Configuration Hierarchy

```mermaid
graph TD
    A["Environment Variables<br/>Highest Priority"] -->|Override| B["appsettings.Production.json"]
    B -->|Override| C["appsettings.Staging.json"]
    C -->|Override| D["appsettings.Development.json"]
    D -->|Override| E["appsettings.json<br/>Base Configuration"]

    E --> F["Logging Settings"]
    E --> G["CORS Configuration"]
    E --> H["Security Headers"]
    E --> I["HTTP Headers Names"]
    E --> J["Rate Limiting"]
    E --> K["Service Bus Options"]

    F --> L["Log Level: Information"]
    F --> M["Log Providers: Console, Debug"]

    G --> N["AllowedOrigins Array"]
    G --> O["DevelopmentDefaults Array"]

    H --> P["CSP Production Policy"]
    H --> Q["CSP Development Policy"]
    H --> R["HSTS max-age"]
    H --> S["Permissions Policy"]

    I --> T["Header Names<br/>X-Correlation-Id<br/>X-RateLimit-*<br/>X-Total-Count<br/>X-Page-*"]

    J --> U["Max Requests: 100"]
    J --> V["Window Duration: 1 minute"]

    K --> W["Connection Cache: 60 min"]
    K --> X["Max Concurrent: 10"]
    K --> Y["Prefetch Count: 100"]

    style A fill:#ffccbc
    style B fill:#fff9c4
    style C fill:#fff9c4
    style D fill:#c8e6c9
    style E fill:#b2dfdb
    style F fill:#e8f5e9
    style G fill:#e8f5e9
    style H fill:#e8f5e9
    style I fill:#e8f5e9
    style J fill:#e8f5e9
    style K fill:#e8f5e9
```

---

## 12. Deployment Architecture

```mermaid
graph TB
    subgraph Development["🖥️ DEVELOPMENT ENVIRONMENT"]
        DEV_API["ServiceHub API<br/>localhost:5000<br/>Debug Mode"]
        DEV_SB["Local Service Bus<br/>Emulator or Dev Namespace"]
        DEV_CONFIG["appsettings.Development.json<br/>- Permissive CORS<br/>- Full CSP for Swagger<br/>- API Key: Optional"]
    end

    subgraph Staging["🧪 STAGING ENVIRONMENT"]
        STAG_API["ServiceHub API<br/>staging.api.servicehub.com<br/>Release Mode"]
        STAG_SB["Staging Namespace<br/>sb://servicehub-staging.servicebus.windows.net"]
        STAG_CONFIG["appsettings.Staging.json<br/>- Limited CORS<br/>- Production CSP<br/>- API Key: Required"]
        STAG_CACHE["Redis Cache<br/>Optional for performance"]
    end

    subgraph Production["🚀 PRODUCTION ENVIRONMENT"]
        PROD_API["ServiceHub API<br/>api.servicehub.com<br/>Release Mode<br/>HTTPS + HSTS"]
        PROD_SB["Production Namespace<br/>sb://servicehub-prod.servicebus.windows.net"]
        PROD_CONFIG["appsettings.Production.json<br/>- Restricted CORS<br/>- Strict CSP<br/>- API Key: Required + Rotation"]
        PROD_CACHE["Redis Cache Cluster<br/>High Availability"]
        PROD_KV["Azure Key Vault<br/>Encryption Keys<br/>Connection Strings"]
        MONITOR["Application Insights<br/>Monitoring & Logging"]
        WAF["Web Application Firewall<br/>DDoS Protection"]
    end

    DEV_API --> DEV_SB
    DEV_CONFIG --> DEV_API
    
    STAG_API --> STAG_SB
    STAG_CONFIG --> STAG_API
    STAG_CACHE --> STAG_API
    
    PROD_API --> PROD_SB
    PROD_CONFIG --> PROD_API
    PROD_CACHE --> PROD_API
    PROD_KV --> PROD_API
    PROD_API --> MONITOR
    WAF --> PROD_API

    style Development fill:#c8e6c9
    style Staging fill:#fff9c4
    style Production fill:#ffccbc
```

---

## Key Architectural Principles

### 1. **Clean Architecture**
- Clear separation of concerns across four layers
- Each layer has specific responsibilities
- Dependencies flow inward (Core doesn't depend on Infrastructure/API)

### 2. **Dependency Injection**
- All services registered in DI container
- Constructor injection for compile-time safety
- Configuration-driven behavior

### 3. **Result-Based Error Handling**
- No exception throwing in business logic
- All operations return `Result<T>` with success/failure
- Consistent error handling across all endpoints

### 4. **Security by Default**
- AES-GCM encryption for connection strings (authenticated encryption)
- API key authentication on all endpoints
- Security headers on all responses
- Log redaction for sensitive data

### 5. **Observability**
- Correlation IDs for request tracing
- Structured logging with redaction
- Health checks (live & ready)
- Exception details in responses (development only)

### 6. **Caching Strategy**
- In-memory caching with TTL
- Efficient Service Bus client connection caching
- Cache invalidation on data changes

### 7. **Configuration Management**
- All hardcoded values externalized to appsettings
- Environment-specific configurations
- Support for environment variables override
- Azure Key Vault integration

---

## Component Interaction Example: Get Messages

```
Client
  ↓ GET /api/v1/namespaces/:id/messages?page=1&limit=10
  ↓ X-API-KEY: dev-api-key-12345
  ↓
Middleware Pipeline (11 steps)
  ↓
Controller: GetMessages()
  ↓ Validate API Key ✓
  ↓ Validate Input ✓
  ↓
MessageService.GetMessages()
  ↓ Get Namespace from Repository
  ↓
ServiceBusClientCache.GetOrCreate(namespaceId)
  ↓ Cache HIT → Return cached client
  ↓
ServiceBusReceiver.ReceiveMessagesAsync()
  ↓ Fetch from Azure Service Bus
  ↓
InMemoryRepository.Cache(messages)
  ↓
Serialize → Result<PagedList<Message>>
  ↓
Controller adds Response Headers:
  - X-Total-Count: 42
  - X-Page-Number: 1
  - X-Page-Size: 10
  - X-Correlation-Id: sh-abc123...
  ↓
Middleware adds Security Headers
  ↓
Client receives HTTP 200 + JSON
```

---

## Performance Considerations

| Component | Performance Impact | Optimization |
|-----------|-------------------|--------------|
| Connection Caching | **High** | 60-min TTL on ServiceBus clients |
| Message Caching | **High** | 1-min TTL on frequently accessed messages |
| Encryption/Decryption | **Medium** | AES-GCM optimized, cached after first use |
| Rate Limiting | **Low** | In-memory counter per IP |
| Security Headers | **Negligible** | Calculated once per response |
| Logging | **Medium** | Async logging with redaction |

---

## Recommended Reading Order

1. Start with **Architecture Overview** (Section 1)
2. Understand the **Request Flow** (Section 2)
3. Explore **Class Diagram** (Section 3)
4. Follow **Request Processing Pipeline** (Section 4)
5. Study **Security Architecture** (Section 6)
6. Review **Middleware Execution Order** (Section 7)

---

## Next Steps

- Review source code in `src/ServiceHub.Api/` directory
- Run the API: `dotnet run --project src/ServiceHub.Api/ServiceHub.Api.csproj`
- Test endpoints via Swagger UI: http://localhost:5000/swagger
- Check health status: http://localhost:5000/health
