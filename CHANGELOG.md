# ServiceHub Changelog

## [3.0.0] - 2026-03-29

### Breaking Changes
- Bumped major version to 3.0.0 — aligns API and UI versioning under a single shared version

### Fixed
- Fix: use connection string for `ServiceBusAdministrationClient` (resolves authentication failures with SAS-based connection strings)
- Fix: resolve `RateLimitingMiddleware` DI failure on .NET 10

### Changed
- `ServiceCollectionExtensions`: switched `ServiceBusAdministrationClient` registration to use connection-string-based construction
- `ServiceBusClientWrapper`: tightened log sanitisation paths for `entityName` and `queueName`

---

## [2.1.3] - 2026-03-23

### Refactored
- Removed duplicate LogSanitizer classes (ServiceHub.Infrastructure and ServiceHub.Api.Extensions)
  — Both stripped only CR/LF characters, weaker than LogRedactor.SanitiseForLog()
  — All callers (AutoReplayExecutor, DlqMonitorService, ErrorHandlingMiddleware,
    RateLimitingMiddleware, ApiKeyAuthenticationMiddleware) now use the single
    LogRedactor.SanitiseForLog() which strips all control characters (\x00–\x1F, \x7F)
  — One consistent, stronger implementation across the entire codebase

---

## [2.1.2] - 2026-03-23

### Security
- Fixed CodeQL cs/log-forging alerts in ServiceBusClientWrapper.cs
  — Added LogRedactor.SanitiseForLog() helper that strips control characters
    (\r, \n, \t, other control chars) from user-derived values before logging
  — Applied sanitisation to all 65 log-forging taint paths: entityName,
    queueName, topicName, subscriptionName, reason, queueType
  — Long integer values (sequenceNumber, count) are exempt — value types
    cannot contain injection characters

---

## [2.1.1] - 2026-03-23

### Fixed
- Remote access: Vite now binds to 0.0.0.0 so UI is reachable from remote machines
- Remote access: Kestrel now binds to 0.0.0.0:5153 via --urls flag in run-api.sh
- Remote access: DevelopmentAllowAnyOrigin: true in appsettings.Development.json allows any hostname in dev
- Remote access: SERVICEHUB_ALLOWED_ORIGINS env var supported in CorsConfiguration for flexible origin config
- Remote access: client.ts fallback changed from hardcoded localhost:5153 to relative /api/v1 (uses Vite proxy)
- deploy.yml: Added unit test gate — deployment blocked if any test fails
- deploy.yml: Added post-deploy health check with 5 retries
- appsettings.Production.json: Added Cors:AllowedOrigins with Azure App Service URL
- appsettings.Production.json: Added DlqDatabase:DataDirectory and NamespaceRepository:DataDirectory pointing to /home/data for persistent storage on Azure App Service

### Added
- REMOTE_ACCESS.md: Complete guide for running ServiceHub on a remote Linux server
- run.sh: Startup banner now shows remote access URLs (IP and hostname) automatically
- run.sh: Firewall commands shown in banner when non-loopback IP detected

---

## [2.1.0] - 2026-03-22

### Changed
- Migrated from .NET 8 to .NET 10 across all projects
- C# language version upgraded from 12.0 to 14.0
- Replaced Swashbuckle with .NET 10 built-in OpenAPI + Scalar UI
- Updated all Microsoft.Extensions.* packages to 10.0.0
- Updated Microsoft.EntityFrameworkCore.Sqlite to 10.0.0
- Updated test infrastructure (xunit 2.9.3, Microsoft.NET.Test.Sdk 17.14.0)

### Fixed
- Created missing apps/web/.env.production file (CI pipeline was failing)

### Removed
- System.Text.Json NuGet package (now inbox in .NET 10)
- Swashbuckle.AspNetCore NuGet package (replaced by built-in OpenAPI)

---

## Version 2.0.1 (February 15, 2026) - Performance Optimization Update

### ⚡ Performance Improvements

#### Optimized Refresh Intervals
- **Rules UI Auto-Refresh** — Reduced from 30s to 10s to match DLQ polling interval
  - Users now see live `pendingMatchCount` updates in near-realtime
  - Added `refetchIntervalInBackground: false` to pause when tab inactive
  - Improves UX for rule creation and testing workflows

#### Verified Existing Optimizations
- ✅ **DLQ Polling**: Confirmed running at 10-second intervals (down from 60s)
- ✅ **Initial Startup**: 5-second delay (fast bootstrap)
- ✅ **Parallel Scanning**: 10 concurrent namespace scans
- ✅ **DLQ History UI**: 10-second auto-refresh already enabled
- ✅ **Database Indexes**: 5 strategic indexes for optimal query performance
- ✅ **Messages/Queues/Topics**: 7-second auto-refresh for active monitoring

**Total Latency Confirmed:** 5s (startup) + 10s (first poll) = **15 seconds maximum** from app start to first DLQ detection

### 📊 Performance Metrics

| Component | Before | After | Improvement |
|-----------|--------|-------|-------------|
| DLQ Polling | 60s | 10s | **6x faster** |
| Rules UI Refresh | 30s | 10s | **3x faster** |
| Startup Delay | 15s | 5s | **3x faster** |
| Total Detection Time | 75s | 15s | **5x faster** |

### 🔧 Technical Changes

**Files Modified:**
- `apps/web/src/hooks/useRules.ts` — Optimized refetch interval to 10s

**Files Verified (No Changes Needed):**
- `services/api/src/ServiceHub.Infrastructure/BackgroundServices/DlqMonitorWorker.cs` — Already optimized to 10s polling
- `apps/web/src/hooks/useDlqHistory.ts` — Already has 10s auto-refresh
- `services/api/src/ServiceHub.Infrastructure/Persistence/DlqDbContext.cs` — Comprehensive indexing already in place

---

## Version 2.0 (February 2026)

### 🎯 Major Features Added

#### DLQ Intelligence System
- **Persistent DLQ tracking** — All dead-letter messages stored in SQLite database for historical analysis
- **Failure categorization** — Auto-categorizes failures: Transient, MaxDelivery, Expired, DataQuality, Authorization, ProcessingError, ResourceNotFound, QuotaExceeded
- **Instant scanning** — "Scan Now" button bypasses 10-15s background schedule for immediate DLQ polling
- **Export capabilities** — Download DLQ data as JSON or CSV for reporting and analysis
- **Timeline view** — Complete audit trail of each message: FirstSeen, ReplayAttempts, StatusChanges
- **Status tracking** — Active → Replayed → ReplayFailed → Resolved

**Components:**
- `DlqDbContext` — Entity Framework Core context
- `DlqMonitorService` — Background worker (10-15s polling)
- `DlqHistoryController` — API endpoints
- `DlqHistoryPage` — Frontend React component

#### Auto-Replay Rules Engine
- **Conditional replay** — Define rules with multiple conditions (field, operator, value)
- **Live statistics** — Real-time evaluation showing:
  - **Pending** — Active DLQ messages matching conditions (amber badge)
  - **Replayed** — Total messages replayed via this rule
  - **Success** — Success count and percentage
- **Test before replay** — Preview matched messages before executing
- **Rate limiting** — Max replays per hour per rule (prevents overwhelming downstream services)
- **Target entity override** — Optionally replay to different queue/topic (not just original)

**Condition Operators:**
- Contains, NotContains, Equals, NotEquals, StartsWith, EndsWith, Regex, GreaterThan, LessThan, In

**Components:**
- `RuleEngine` — Core evaluation logic
- `RulesController` — API endpoints (CRUD, Test, Replay All)
- `AutoReplayExecutor` — Safety wrapper with rate limiting
- `RulesPage` — Frontend React component with rule cards

#### Batch Replay All System
- **Bulk replay** — Replay multiple DLQ messages with one click
- **Performance optimization** — O(N) batch processing with single DLQ receiver per entity
  - **Before:** O(N²) connections, 30s+ timeout for 7 messages
  - **After:** O(N) connections, 9 seconds for 7 messages
- **Safety confirmation** — Red danger header modal with 3 warnings before execution
- **Real-time results** — Shows matched/replayed/failed/skipped counts
- **Complete audit trail** — Every replay recorded in DLQ Intelligence history

**Safety Features:**
- Cancel button auto-focused (safer default)
- Test workflow encouraged ("Use Test button first")
- Clear warnings about irreversibility and potential loops

**Components:**
- `ServiceBusClientWrapper.ReplayMessagesAsync` — Batch replay method
- `ReplayAllConfirmDialog` — Safety confirmation UI
- `ReplayHistory` entity — Audit trail

### 🎨 UI Enhancements
- **Enhanced message rows** — Improved visual hierarchy and information density
- **Better property visibility** — Clearer metadata display
- **Optimized spacing** — Better readability for long debugging sessions

### 🐛 Bug Fixes
- **Entity name extraction** — Fixed handling of "topic/subscriptions/sub" paths (was passing full path, now extracts subscription name only)
- **Axios timeout** — Extended to 120s for batch replay operations (was 30s global timeout)
- **Inter-message delays** — Removed 5s delay between replays for manual operations (kept only for auto-replay)
- **O(N²) DLQ receivers** — Eliminated redundant Service Bus connections

### 📸 Screenshots Added
- `26-row-ui-new-feature.png` — Enhanced message row UI
- `27-dlq-enhancement.png` — DLQ enhancements
- `28-dlq-intelligence.png` — DLQ Intelligence dashboard
- `29-dlq-history-post-replay-message.png` — Replay history tracking
- `30-auto-replay-feature.png` — Auto-Replay Rules page
- `31-auto-relay-test-feature.png` — Rule test dialog
- `32-replay-all-messages.png` — Replay All confirmation
- `33-replay-all-process.png` — Batch replay progress
- `34-post-replay-all-messages.png` — Post-replay results
- `35-rdlq-intelligence-history-post-replay-all.png` — Complete audit trail

### 📚 Documentation Updates
- **README.md** — Added "What's New" section, DLQ Intelligence section, Auto-Replay section, Batch Replay section (now 699 lines, up from 560)
- **SCREENSHOTS.md** — Added 11 new screenshot entries (26-35), updated statistics (now 331 lines, up from 278)
- **COMPREHENSIVE-GUIDE.md** — Added DLQ Intelligence System section, Auto-Replay Rules Engine section (now 1467 lines, up from 1157)
- **CHANGELOG.md** — Created to track version history

### 🔧 Technical Improvements
- **Entity Framework Core** integration for persistent storage
- **SQLite database** for DLQ Intelligence data
- **React Query** optimization for rule statistics
- **Batch processing** with message grouping by entity
- **Rate limiting** algorithm for safe bulk operations

### 🔒 Security & Safety
- **Audit trail** — Every replay logged with timestamp, user, outcome, error details
- **Rate limiting** — Prevents overwhelming downstream services
- **Safety confirmations** — Multiple warnings before destructive operations
- **Cancel auto-focus** — Safer defaults in confirmation dialogs
- **Idempotent scanning** — Safe to re-scan DLQs without duplicates

---

## Version 1.0 (January 2026)

### Initial Release Features
- Message browsing (point-in-time snapshot)
- AI-powered pattern detection
- Dead-letter queue investigation
- Message details inspection (Properties, Custom Props, Body)
- Advanced search and filtering
- Message generator with 6 scenarios
- Single message replay
- Testing tools (send message, generate messages)
- Read-only safety (PeekMessagesAsync)
- Cross-platform support (macOS, Linux, Windows/WSL)

---

## Migration Notes

### Upgrading from v1.0 to v2.0

**Database:**
- SQLite database automatically created on first run (`servicehub.db`)
- No migration required — fresh install

**API Changes:**
- New endpoints: `/api/v1/dlq/**` (DLQ Intelligence)
- New endpoints: `/api/v1/dlq/rules/**` (Auto-Replay Rules)
- Existing endpoints unchanged (backward compatible)

**Frontend:**
- New pages: DLQ Intelligence, Auto-Replay Rules
- Enhanced sidebar navigation
- No breaking changes to existing pages

**Configuration:**
- No new environment variables required
- Optional: Configure rate limits in rule creation

**Performance:**
- Background DLQ monitoring uses 10-15s polling (minimal overhead)
- SQLite database grows with DLQ message history (typically <10 MB)
- Batch replay operations now complete 70% faster

---

## Known Issues

None at this time. Report issues at: https://github.com/debdevops/servicehub/issues

---

## Roadmap

See [README.md](README.md#roadmap) for planned features.
