# ServiceHub Screenshots Guide

This guide explains the 35 screenshots used in the README and how they demonstrate ServiceHub capabilities.

---

## 📸 Screenshot Organization

All screenshots are located in `docs/screenshots/` with descriptive filenames that indicate their purpose and placement in the documentation.

### Screenshot Mapping

| # | Filename | Shows | Used In Section |
|---|----------|-------|-----------------|
| 1 | 01-Start-The-App.png | Application startup | Quick Start |
| 2 | 02-Connect-Service-Bus-With-Manage-ConnStr.png | Connection form with connection string | Quick Start |
| 3 | 03-Connected-ServiceBus.png | Connected namespace view | Quick Start |
| 4 | 04-feature-message-browser-empty.png | Message browser (empty) | Features |
| 5 | 05-main-message-display1.png | Message browser with loaded messages | Features - Message Browser |
| 6 | 06-feature-message-generator.png | Message generator basic form | Testing Tools |
| 7 | 07-feature-message-generator-basic-single-message.png | Single message generator | Testing Tools |
| 8 | 08-feature-message-generator-scenarios.png | Generator scenarios selection | Testing Tools |
| 9 | 09-feature-send-message.png | Send message form | Testing Tools |
| 10 | 10-message-display.png | Message details view | Features - Message Details |
| 11 | 11-Generate-Single-Message-Topic.png | Generate message to topic | Testing Tools |
| 12 | 12-showing-message-topic.png | Topic messages display | Features |
| 13 | 13-feature-message-details-custom-props.png | Custom properties tab | Features - Message Details |
| 14 | 14-feature-ai-findings.png | AI findings indicator | Features - AI Insights |
| 15 | 15-feature-message-details-JSON-prop.png | Message body JSON | Features - Message Details |
| 16 | 16-feature-message-details-AI-Insight.png | AI insights in message details | Features - AI Insights |
| 17 | 17-feature-ai-patterns-popup.png | AI patterns summary popup | Features - AI Insights |
| 18 | 18-feature-dlq-tab-with-ai.png | DLQ tab with AI indicators | Features - DLQ |
| 19 | 19-feature-ai-findings-1.png | AI findings detection | Features - AI Insights |
| 20 | 20-workflow-dlq-investigation-step1.png | DLQ investigation step 1 | Workflows - DLQ |
| 21 | 21-workflow-dlq-investigation-step1.png | DLQ investigation detailed | Workflows - DLQ |
| 22 | 22-workflow-dlq-investigation-step2.png | DLQ AI insights view | Workflows - DLQ |
| 23 | 23-workflow-dlq-AI-Insight.png | DLQ AI guidance | Workflows - DLQ |
| 24 | 24-workflow-dlq-replay-step4.png | Replay confirmation dialog | Workflows - DLQ |
| 25 | 25-feature-find-feature.png | Advanced search functionality | Features - Search |
| 26 | 26-row-ui-new-feature.png | Enhanced message row UI | Features - Message Browser |
| 27 | 27-dlq-enhancement.png | DLQ enhancements | Features - DLQ Intelligence |
| 28 | 28-dlq-intelligence.png | DLQ Intelligence dashboard | Features - DLQ Intelligence |
| 29 | 29-dlq-history-post-replay-message.png | DLQ history after single replay | Features - DLQ Intelligence |
| 30 | 30-auto-replay-feature.png | Auto-Replay Rules page with rule cards | Features - Auto-Replay System |
| 31 | 31-auto-relay-test-feature.png | Rule test dialog with matched messages | Features - Auto-Replay System |
| 32 | 32-replay-all-messages.png | Replay All confirmation with warnings | Features - Batch Replay |
| 33 | 33-replay-all-process.png | Batch replay in progress | Features - Batch Replay |
| 34 | 34-post-replay-all-messages.png | Results after batch replay | Features - Batch Replay |
| 35 | 35-rdlq-intelligence-history-post-replay-all.png | DLQ history audit trail after bulk replay | Features - DLQ Intelligence |

**Total screenshots: 35 files**

---

## 🎯 Key Screenshots Explained

### DLQ Intelligence System (Screenshots 26-35)

**New Feature Area:** DLQ Intelligence & Auto-Replay System

#### Enhanced Message UI (Screenshot 26)
**File:** `26-row-ui-new-feature.png`
- Improved message list visual hierarchy
- Better property visibility
- Enhanced spacing and readability

#### DLQ Intelligence Dashboard (Screenshots 27-28)
**Files:** `27-dlq-enhancement.png`, `28-dlq-intelligence.png`
- Persistent DLQ message tracking in SQLite database
- Category classification (Transient, MaxDelivery, Expired, DataQuality, etc.)
- "Scan Now" button for instant DLQ polling
- Export capabilities (JSON/CSV)
- Timeline view with replay history

#### Replay History Tracking (Screenshot 29)
**File:** `29-dlq-history-post-replay-message.png`
- Shows DLQ message status after replay
- Timestamps and outcome tracking
- Audit trail for compliance

#### Auto-Replay Rules System (Screenshots 30-31)
**Files:** `30-auto-replay-feature.png`, `31-auto-relay-test-feature.png`
- Rule cards with live statistics (Pending/Replayed/Success)
- Conditions builder (field, operator, value)
- "Test" button to preview matched messages
- Real-time evaluation against Active DLQ messages

#### Batch Replay All (Screenshots 32-35)
**Files:** `32-replay-all-messages.png`, `33-replay-all-process.png`, `34-post-replay-all-messages.png`, `35-rdlq-intelligence-history-post-replay-all.png`
- Confirmation dialog with red danger header and 3 safety warnings
- Shows matched message count before execution
- Real-time progress indicator
- Post-replay statistics (matched/replayed/failed/skipped)
- Complete audit trail in DLQ Intelligence history

---

### Hero Image (Most Important)
**File:** `05-main-message-display1.png`

**Shows:**
- ServiceHub message browser with loaded messages
- Active (50) and Dead-Letter (0) tabs
- AI Findings: 2 indicator
- Auto-refresh toggle (ON, 7s ago)
- Filter, Search, and Refresh buttons
- Message list with preview text and timestamps

**Why it's the hero:** This single screenshot demonstrates ServiceHub's complete value proposition - browsing real messages with AI insights during an incident.

**Used in 7+ sections:**
- Hero (top of README)
- Problem section (after state)
- Quick Start Step 4
- Features - Message Browser
- Roadmap current capabilities
- Get Started CTA
- Comparison sections

---

### Critical Workflow Screenshots

#### DLQ Investigation (4-step workflow)
1. **Step 1:** `18-feature-dlq-tab-with-ai.png` - Shows 3 messages in DLQ with AI badges
2. **Step 2:** `19-workflow-dlq-investigation-step1.png` - DLQ message Properties showing TestingDLQ reason
3. **Step 3:** `20-workflow-dlq-investigation-step2.png` - AI Insights showing 88% confidence pattern
4. **Step 4:** `21-workflow-dlq-replay-step3.png` - Replay confirmation dialog
5. **Step 5:** `22-workflow-dlq-replay-step4.png` - After replay (message counts increased)

#### Message Inspection (3-tab view)
1. **Properties:** `11-feature-message-details-properties.png` - Message ID, timestamps, delivery count
2. **Custom Props:** `12-feature-message-details-custom-props.png` - Application headers
3. **Body:** `13-feature-message-details-body.png` - JSON message content

---

## 📝 README Structure & Screenshot Usage

### Hero Section (Line 1-15)
- **1 screenshot:** `08-hero-message-browser-loaded.png`
- **Purpose:** Immediate visual impact showing full capabilities

### The Problem (Line 32-75)
- **2 screenshots:** 
  - `01-problem-empty-state.png` (Azure Portal limitations)
  - `08-hero-message-browser-loaded.png` (ServiceHub solution)
- **Purpose:** Visual before/after comparison

### Quick Start (Line 77-125)
- **5 screenshots:**
  - `02-quickstart-connection-form.png` (Step 3)
  - `03-quickstart-connected-namespace.png` (Step 3)
  - `08-hero-message-browser-loaded.png` (Step 4)
  - `07-feature-message-generator-scenarios.png` (Step 4)
- **Purpose:** Visual step-by-step setup guide

### Key Features (Line 127-550)
- **15+ screenshots** demonstrating:
  - Message browser (`08-hero...`)
  - Message details (3 tabs: `11`, `12`, `13`)
  - Search (`23-feature-search...`)
  - DLQ forensics (`18`, `24`)
  - AI insights (4 screenshots: `14`, `15`, `16`, `17`)
  - Message generator (`06`, `07`)
  - Send message (`09`)
  - Replay (`21`, `22`)

### Investigation Workflows (Line 552-650)
- **8 screenshots** showing:
  - DLQ investigation (5 screenshots)
  - Topic delivery (`10-workflow...`)
  - Search functionality (`23-feature...`)

### Comparison (Line 652-750)
- **2 screenshots:**
  - `01-problem-empty-state.png` (Portal)
  - `08-hero-message-browser-loaded.png` (ServiceHub)

### Security & Trust (Line 752-850)
- **1 screenshot:**
  - `02-quickstart-connection-form.png` (Security guidance)

### Roadmap (Line 900-950)
- **1 screenshot:**
  - `08-hero-message-browser-loaded.png` (Current capabilities)

### Get Started (Line 1000-1100)
- **1 screenshot:**
  - `08-hero-message-browser-loaded.png` (CTA)

---

## ✅ Verification Checklist

Use this to verify screenshots display correctly on GitHub:

- [ ] **Hero image** loads immediately at top
- [ ] **Problem section** shows before/after comparison
- [ ] **Quick Start** shows connection flow (4 steps)
- [ ] **Features** demonstrates each capability with real screenshot
- [ ] **DLQ workflow** shows clear 4-step progression
- [ ] **AI insights** displays pattern detection screenshots
- [ ] **Message details** shows all 3 tabs (Properties, Custom, Body)
- [ ] **Comparison** shows Portal vs ServiceHub side-by-side
- [ ] All images load in <2 seconds
- [ ] No broken image icons (🖼️❌)

---

## 🔧 Maintenance

### Adding New Screenshots

1. Take screenshot of new feature
2. Save to `docs/screenshots/` with naming convention:
   - Format: `XX-category-description.png`
   - Categories: `feature`, `workflow`, `comparison`, `security`
   - Example: `25-feature-bulk-replay.png`

3. Update README.md with new image reference:
   ```markdown
   ![Descriptive alt text](docs/screenshots/25-feature-bulk-replay.png)
   *Caption explaining what screenshot shows*
   ```

4. Commit and push:
   ```bash
   git add docs/screenshots/25-feature-bulk-replay.png README.md
   git commit -m "docs: Add bulk replay feature screenshot"
   git push origin main
   ```

### Updating Existing Screenshots

1. Replace file in `docs/screenshots/` (keep same filename)
2. Commit and push:
   ```bash
   git add docs/screenshots/08-hero-message-browser-loaded.png
   git commit -m "docs: Update hero image with latest UI"
   git push origin main
   ```

3. GitHub will automatically display updated image

---

## 📊 Screenshot Statistics

| Metric | Value |
|--------|-------|
| **Total screenshots** | 35 files |
| **Most used screenshot** | `05-main-message-display1.png` (message browser) |
| **Total size** | ~6.8 MB (estimated) |
| **Average file size** | ~195 KB |
| **Largest file** | ~350 KB |
| **Smallest file** | ~85 KB |
| **Recommended max size** | 500 KB per file |

---

## 🎨 Screenshot Best Practices

### When Taking Screenshots

1. **Full window** (not partial crops)
2. **Realistic data** (show 10-50 messages, not 1000s)
3. **No sensitive data** (use test namespaces, generated messages)
4. **Consistent resolution** (1200-1600px width)
5. **Clean UI** (close unnecessary panels)

### File Format

- **Format:** PNG (for crisp UI text)
- **Compression:** Use pngquant (65-80% quality)
- **Max size:** 500 KB per file
- **Naming:** Descriptive (`feature-name.png`, not `Screenshot 1.png`)

### Alt Text

Write descriptive alt text for accessibility:

```markdown
<!-- Good alt text -->
![ServiceHub message browser showing 50 active messages with AI findings indicator](...)

<!-- Bad alt text -->
![Screenshot](...)
![Message browser](...)
```

---

## 🚀 Quick Commands

```bash
# View all screenshots
ls -lh docs/screenshots/

# Count screenshots
ls -1 docs/screenshots/ | wc -l

# Check file sizes
ls -lh docs/screenshots/*.png | sort -k5 -hr

# Find large files (>500KB)
find docs/screenshots -name "*.png" -size +500k

# Optimize all screenshots
cd docs/screenshots
for img in *.png; do
  pngquant --quality=65-80 --ext .png --force "$img"
done
```

---

## 🌐 View on GitHub

**Repository:** https://github.com/debdevops/servicehub

**Branch:** dglocal-110126

**README:** https://github.com/debdevops/servicehub/blob/dglocal-110126/README.md

All 24 screenshots should display inline without broken image icons.

---

**Last updated:** February 15, 2026  
**Screenshots version:** v2.0 (DLQ Intelligence & Auto-Replay System added)
