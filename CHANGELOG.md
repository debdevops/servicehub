# ServiceHub Changelog

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
