# ServiceHub API Documentation Index

Complete architectural documentation with deep-dive diagrams, implementation patterns, and operational guides.

---

## 📚 Documentation Overview

| Document | Purpose | Audience | Time to Read |
|----------|---------|----------|--------------|
| [README.md](./README.md) | Quick start & API overview | Everyone | 15 min |
| **[ARCHITECTURE.md](./ARCHITECTURE.md)** | System design & diagrams | Architects, Senior devs | 45 min |
| **[IMPLEMENTATION_PATTERNS.md](./IMPLEMENTATION_PATTERNS.md)** | Code patterns & best practices | Developers | 40 min |
| **[DEPLOYMENT_OPERATIONS.md](./DEPLOYMENT_OPERATIONS.md)** | Deployment & troubleshooting | DevOps, Site reliability | 35 min |

---

## 🏗️ Architecture Documentation

### [ARCHITECTURE.md](./ARCHITECTURE.md) - 12 Detailed Diagrams

The primary architectural document with comprehensive Mermaid diagrams:

#### 1. **Architecture Overview - Layered Design**
   - 4-layer architecture visualization
   - Clean Architecture principles
   - Component relationships
   - [→ View Diagram](./ARCHITECTURE.md#1-architecture-overview---layered-design-diagram)

#### 2. **Sequential Flow Diagram**
   - HTTP request lifecycle
   - Middleware processing
   - Business logic execution
   - Response generation
   - [→ View Diagram](./ARCHITECTURE.md#2-requestresponse-sequential-flow)

#### 3. **Class & Dependency Injection Diagram**
   - Service interfaces & implementations
   - DI container configuration
   - Service relationships
   - Factory patterns
   - [→ View Diagram](./ARCHITECTURE.md#3-detailed-class--dependency-injection-diagram)

#### 4. **Request Processing Pipeline - Flow**
   - 11-step middleware pipeline
   - Decision points & error handling
   - Request/response flow
   - Color-coded steps
   - [→ View Diagram](./ARCHITECTURE.md#4-api-request-processing-pipeline---flow-diagram)

#### 5. **Data Flow Example**
   - Create namespace to access messages
   - Step-by-step operation flow
   - Cache interaction
   - Response building
   - [→ View Diagram](./ARCHITECTURE.md#5-data-flow-create-namespace-to-access-messages)

#### 6. **Security Architecture - Defense in Depth**
   - Encryption layer (AES-GCM)
   - Authentication layer (API keys)
   - Security headers
   - Input validation
   - Monitoring & logging
   - [→ View Diagram](./ARCHITECTURE.md#6-security-architecture---defense-in-depth)

#### 7. **Middleware Pipeline Execution Order**
   - 11 middleware steps in order
   - Request/response flow through each middleware
   - Key responsibilities
   - [→ View Diagram](./ARCHITECTURE.md#7-middleware-pipeline-execution-order)

#### 8. **Entity Relationship & Domain Model**
   - Namespace, Queue, Topic, Subscription entities
   - Message relationships
   - Connection string encryption
   - Database schema
   - [→ View Diagram](./ARCHITECTURE.md#8-entity-relationship--domain-model)

#### 9. **Exception Handling Flow**
   - Exception types mapping
   - HTTP status codes
   - Error response format
   - Logging & response building
   - [→ View Diagram](./ARCHITECTURE.md#9-exception-handling-flow)

#### 10. **Caching Strategy - In-Memory Cache Lifecycle**
   - Cache lookup flow
   - Cache types per entity
   - Cache invalidation strategies
   - TTL management
   - [→ View Diagram](./ARCHITECTURE.md#10-caching-strategy---in-memory-cache-lifecycle)

#### 11. **Configuration Hierarchy**
   - Environment variable precedence
   - Configuration sections
   - Settings per environment
   - [→ View Diagram](./ARCHITECTURE.md#11-configuration-hierarchy)

#### 12. **Deployment Architecture**
   - Development environment
   - Staging environment
   - Production environment
   - Infrastructure components
   - [→ View Diagram](./ARCHITECTURE.md#12-deployment-architecture)

---

## 💻 Implementation Patterns

### [IMPLEMENTATION_PATTERNS.md](./IMPLEMENTATION_PATTERNS.md) - Code Patterns & Best Practices

Deep-dive into how the codebase is structured and patterns used:

#### 1. **Result Pattern for Error Handling**
   - Why Result Pattern over exceptions
   - Implementation details
   - Usage in controllers
   - Benefits & trade-offs
   - [→ Read More](./IMPLEMENTATION_PATTERNS.md#1-result-pattern-for-error-handling)

#### 2. **Dependency Injection Container Setup**
   - Service registration patterns
   - Service lifetime decisions
   - Factory pattern usage
   - Configuration binding
   - [→ Read More](./IMPLEMENTATION_PATTERNS.md#2-dependency-injection-container-setup)

#### 3. **Middleware Pipeline Design**
   - Custom middleware architecture
   - Execution order (CRITICAL)
   - Why the order matters
   - Performance considerations
   - [→ Read More](./IMPLEMENTATION_PATTERNS.md#3-middleware-pipeline-design)

#### 4. **Repository Pattern & Caching**
   - Repository interface & implementation
   - Current: In-memory repository
   - Future: SQL Server repository
   - Cache invalidation strategies
   - [→ Read More](./IMPLEMENTATION_PATTERNS.md#4-repository-pattern--caching)

#### 5. **Security Implementation**
   - Encryption: AES-GCM pattern
   - API Key authentication flow
   - Security headers strategy
   - Prod vs Dev configurations
   - [→ Read More](./IMPLEMENTATION_PATTERNS.md#5-security-implementation)

#### 6. **Configuration Management**
   - Configuration precedence
   - appsettings structure
   - Options Pattern usage
   - Environment-specific configs
   - [→ Read More](./IMPLEMENTATION_PATTERNS.md#6-configuration-management)

#### 7. **Logging & Observability**
   - Structured logging pattern
   - Log redaction for security
   - Correlation ID flow
   - Request tracing
   - [→ Read More](./IMPLEMENTATION_PATTERNS.md#7-logging--observability)

#### 8. **Validation Strategy**
   - Validation layers
   - FluentValidation examples
   - Error response format
   - [→ Read More](./IMPLEMENTATION_PATTERNS.md#8-validation-strategy)

#### 9. **API Response Format**
   - Success response structure
   - Paginated responses
   - Error responses (RFC 7231)
   - Response headers
   - [→ Read More](./IMPLEMENTATION_PATTERNS.md#9-api-response-format)

#### 10. **Testing Approach**
   - Unit testing patterns
   - Integration testing patterns
   - Mock frameworks
   - Test structure
   - [→ Read More](./IMPLEMENTATION_PATTERNS.md#10-testing-approach)

#### Plus:
   - Performance optimization tips
   - Common mistakes to avoid
   - Best practices summary

---

## 🚀 Deployment & Operations

### [DEPLOYMENT_OPERATIONS.md](./DEPLOYMENT_OPERATIONS.md) - Operational Runbooks

Comprehensive guide for deploying and operating the API:

#### 1. **Environment Configurations**
   - Development environment settings
   - Staging environment settings
   - Production environment settings
   - Key characteristics per environment
   - [→ View Configs](./DEPLOYMENT_OPERATIONS.md#1-environment-configurations)

#### 2. **Deployment Strategies**
   - Local development deployment
   - Docker container deployment
   - Kubernetes deployment with YAML
   - Health checks & probes
   - Resource limits & requests
   - [→ View Strategies](./DEPLOYMENT_OPERATIONS.md#2-deployment-strategies)

#### 3. **Operational Checklists**
   - Pre-deployment checklist
   - Health check verification
   - Post-deployment verification
   - Configuration review
   - [→ View Checklist](./DEPLOYMENT_OPERATIONS.md#3-operational-checklists)

#### 4. **Monitoring & Alerting**
   - Key metrics to monitor
   - Alert threshold matrix
   - Availability metrics
   - Performance metrics
   - Security metrics
   - Business metrics
   - Log analytics queries
   - [→ View Monitoring](./DEPLOYMENT_OPERATIONS.md#4-monitoring--alerting)

#### 5. **Troubleshooting Guide**
   - Common issues & solutions
   - 401 Unauthorized debugging
   - Connection string decryption failures
   - Rate limiting issues
   - High memory usage
   - Slow response times
   - Diagnostic procedures
   - [→ View Troubleshooting](./DEPLOYMENT_OPERATIONS.md#5-troubleshooting-guide)

#### 6. **Performance Tuning**
   - Response time optimization
   - Cache hit ratio improvement
   - Connection pooling
   - Message batching
   - [→ View Tuning](./DEPLOYMENT_OPERATIONS.md#6-performance-tuning)

#### 7. **Disaster Recovery**
   - Backup strategy
   - Recovery procedures
   - RTO/RPO targets
   - Failover procedures
   - [→ View DR Plan](./DEPLOYMENT_OPERATIONS.md#7-disaster-recovery)

#### 8. **API Versioning Strategy**
   - Current v1 implementation
   - Future v2 plans
   - Versioning headers
   - Migration strategy
   - [→ View Versioning](./DEPLOYMENT_OPERATIONS.md#8-api-versioning-strategy)

#### Plus:
   - Production deployment runbook
   - Step-by-step procedures
   - Rollback procedures
   - Incident response

---

## 🎯 Quick Navigation by Role

### For **Architects & Tech Leads**
1. Start: [ARCHITECTURE.md](./ARCHITECTURE.md) - Diagrams 1-4
2. Deep-dive: All 12 diagrams in ARCHITECTURE.md
3. Review: Section "Architectural Principles"
4. Plan: [DEPLOYMENT_OPERATIONS.md](./DEPLOYMENT_OPERATIONS.md) - Deployment strategies
5. Evaluate: [IMPLEMENTATION_PATTERNS.md](./IMPLEMENTATION_PATTERNS.md) - All sections

### For **Backend Developers**
1. Start: [README.md](./README.md) - Quick start
2. Learn: [IMPLEMENTATION_PATTERNS.md](./IMPLEMENTATION_PATTERNS.md) - All sections
3. Study: [ARCHITECTURE.md](./ARCHITECTURE.md) - Diagrams 3, 4, 8, 9
4. Build: Reference code in `src/` directory
5. Debug: [DEPLOYMENT_OPERATIONS.md](./DEPLOYMENT_OPERATIONS.md) - Troubleshooting

### For **DevOps / Site Reliability**
1. Start: [DEPLOYMENT_OPERATIONS.md](./DEPLOYMENT_OPERATIONS.md) - Section 1-3
2. Deploy: Docker/Kubernetes sections
3. Monitor: Monitoring & alerting section
4. Troubleshoot: Troubleshooting guide
5. Prepare: Disaster recovery & runbooks

### For **QA / Test Engineers**
1. Start: [ARCHITECTURE.md](./ARCHITECTURE.md) - Diagram 2 (Sequential Flow)
2. Understand: Data flow (Diagram 5)
3. Test Plan: [IMPLEMENTATION_PATTERNS.md](./IMPLEMENTATION_PATTERNS.md) - Section 10 (Testing)
4. Edge Cases: Diagram 9 (Exception Handling)
5. Scenarios: [DEPLOYMENT_OPERATIONS.md](./DEPLOYMENT_OPERATIONS.md) - Disaster recovery

---

## 📊 Key Architectural Decisions

### 1. Clean Architecture with 4 Layers
- **Why**: Clear separation of concerns, testability, maintainability
- **Details**: [ARCHITECTURE.md](./ARCHITECTURE.md#architectural-principles)
- **Implementation**: [IMPLEMENTATION_PATTERNS.md](./IMPLEMENTATION_PATTERNS.md)

### 2. Result Pattern Instead of Exceptions
- **Why**: Explicit error handling, better performance, clearer contracts
- **Details**: [IMPLEMENTATION_PATTERNS.md](./IMPLEMENTATION_PATTERNS.md#1-result-pattern-for-error-handling)
- **Usage**: Throughout service layer

### 3. In-Memory Repository (MVP)
- **Why**: Fast development, no database setup needed initially
- **Future**: SQL Server for persistence
- **Plan**: [IMPLEMENTATION_PATTERNS.md](./IMPLEMENTATION_PATTERNS.md#4-repository-pattern--caching)

### 4. AES-GCM Encryption for Secrets
- **Why**: Authenticated encryption, industry standard, tamper detection
- **Details**: [IMPLEMENTATION_PATTERNS.md](./IMPLEMENTATION_PATTERNS.md#5-security-implementation)
- **Diagram**: [ARCHITECTURE.md](./ARCHITECTURE.md#6-security-architecture---defense-in-depth)

### 5. Configuration-Driven Everything
- **Why**: Environment-specific behavior, secrets management, flexibility
- **Details**: [IMPLEMENTATION_PATTERNS.md](./IMPLEMENTATION_PATTERNS.md#6-configuration-management)
- **Hierarchy**: [ARCHITECTURE.md](./ARCHITECTURE.md#11-configuration-hierarchy)

### 6. Custom Middleware Pipeline
- **Why**: Control over request processing, security layers, observability
- **Order**: [ARCHITECTURE.md](./ARCHITECTURE.md#7-middleware-pipeline-execution-order)
- **CRITICAL**: Order matters! [IMPLEMENTATION_PATTERNS.md](./IMPLEMENTATION_PATTERNS.md#3-middleware-pipeline-design)

---

## 📖 Reading Order by Learning Goal

### Goal: Understand the Complete System
1. [README.md](./README.md) - Overview (15 min)
2. [ARCHITECTURE.md](./ARCHITECTURE.md) - Section 1: Overview (10 min)
3. [ARCHITECTURE.md](./ARCHITECTURE.md) - Diagrams 2, 4, 5 (15 min)
4. [IMPLEMENTATION_PATTERNS.md](./IMPLEMENTATION_PATTERNS.md) - Section 1-3 (15 min)
5. [ARCHITECTURE.md](./ARCHITECTURE.md) - Remaining diagrams (20 min)

### Goal: Implement New Features
1. [IMPLEMENTATION_PATTERNS.md](./IMPLEMENTATION_PATTERNS.md) - Section 1, 4, 8 (25 min)
2. [ARCHITECTURE.md](./ARCHITECTURE.md) - Diagram 3, 9 (15 min)
3. Browse relevant code in `src/ServiceHub.Api/`
4. Study existing service implementations
5. Follow patterns in new code

### Goal: Deploy to Production
1. [DEPLOYMENT_OPERATIONS.md](./DEPLOYMENT_OPERATIONS.md) - Section 1-3 (20 min)
2. Review environment-specific configs
3. Read deployment strategy (Docker/K8s)
4. Follow pre-deployment checklist
5. Study runbook and rollback procedures

### Goal: Debug Issues
1. [DEPLOYMENT_OPERATIONS.md](./DEPLOYMENT_OPERATIONS.md) - Troubleshooting (20 min)
2. [ARCHITECTURE.md](./ARCHITECTURE.md) - Diagrams 2, 4, 7 (15 min)
3. Check logs and correlation IDs
4. Use monitoring queries
5. Follow diagnostic procedures

---

## 🔗 External References

### .NET & Architecture
- [Microsoft Clean Architecture Guide](https://github.com/ardalis/CleanArchitecture)
- [ASP.NET Core Dependency Injection](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection)
- [Middleware in ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/middleware)

### Azure Services
- [Azure Service Bus](https://docs.microsoft.com/en-us/azure/service-bus-messaging/)
- [Azure Key Vault](https://docs.microsoft.com/en-us/azure/key-vault/)
- [Azure Application Insights](https://docs.microsoft.com/en-us/azure/azure-monitor/app/app-insights-overview)

### Security
- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [NIST Cybersecurity Framework](https://www.nist.gov/cyberframework/)
- [RFC 7231 - HTTP Semantics](https://tools.ietf.org/html/rfc7231)

### Operational Excellence
- [12-Factor App Methodology](https://12factor.net/)
- [SRE Golden Signals](https://sre.google/books/)
- [Kubernetes Best Practices](https://kubernetes.io/docs/concepts/configuration/overview/)

---

## 📝 Document Versions

| Document | Version | Last Updated | Status |
|----------|---------|--------------|--------|
| ARCHITECTURE.md | 1.0 | 2026-01-17 | ✅ Current |
| IMPLEMENTATION_PATTERNS.md | 1.0 | 2026-01-17 | ✅ Current |
| DEPLOYMENT_OPERATIONS.md | 1.0 | 2026-01-17 | ✅ Current |
| README.md | 1.0 | 2025-12-10 | ✅ Current |

---

## 💡 Tips for Using This Documentation

1. **Use Mermaid Diagrams**: Click on diagram links to view interactive diagrams (many tools support zooming, filtering)

2. **Search Effectively**: Use Ctrl+F to search across documents for specific patterns (e.g., "cache", "encryption", "middleware")

3. **Reference Often**: Keep architecture diagrams visible when coding to stay aligned with design

4. **Update Documentation**: If you change architecture/patterns, update relevant diagrams and docs

5. **Share with Team**: These docs are great for onboarding new team members

6. **Generate HTML**: Use tools like `mdbook` or `pandoc` to generate prettier HTML versions

---

## ❓ FAQ

**Q: Where do I start if I'm new to this codebase?**
A: Start with [README.md](./README.md), then read [ARCHITECTURE.md](./ARCHITECTURE.md) diagrams 1-4, then explore the code.

**Q: How do I add a new feature?**
A: Read [IMPLEMENTATION_PATTERNS.md](./IMPLEMENTATION_PATTERNS.md) sections 1-5, study existing services, follow same patterns.

**Q: How do I deploy to production?**
A: Follow [DEPLOYMENT_OPERATIONS.md](./DEPLOYMENT_OPERATIONS.md) deployment strategy and pre-deployment checklist.

**Q: What if the API is slow?**
A: See [DEPLOYMENT_OPERATIONS.md](./DEPLOYMENT_OPERATIONS.md) Performance Tuning section and [ARCHITECTURE.md](./ARCHITECTURE.md) diagram 10 (Caching).

**Q: How is security handled?**
A: See [ARCHITECTURE.md](./ARCHITECTURE.md) diagram 6 (Security Architecture) and [IMPLEMENTATION_PATTERNS.md](./IMPLEMENTATION_PATTERNS.md) section 5 (Security Implementation).

**Q: Are there example requests?**
A: Yes! See [README.md](./README.md) section "First API Calls" with curl examples, or use Swagger UI at `/swagger`

---

## 📞 Support & Questions

For questions about:
- **Architecture**: Review relevant diagrams in [ARCHITECTURE.md](./ARCHITECTURE.md)
- **Implementation**: Check [IMPLEMENTATION_PATTERNS.md](./IMPLEMENTATION_PATTERNS.md)
- **Operations**: See [DEPLOYMENT_OPERATIONS.md](./DEPLOYMENT_OPERATIONS.md)
- **Getting Started**: Read [README.md](./README.md)

---

**Last Updated**: 2026-01-17  
**Version**: 1.0  
**Status**: Complete & Ready for Use 🎉
