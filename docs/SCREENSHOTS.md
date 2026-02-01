# ServiceHub Screenshots Guide

This guide explains the 24 screenshots used in the README and how they demonstrate ServiceHub capabilities.

---

## 📸 Screenshot Organization

All screenshots are located in `docs/screenshots/` with descriptive filenames that indicate their purpose and placement in the documentation.

### Screenshot Mapping

| # | Filename | Original | Shows | Used In Section |
|---|----------|----------|-------|-----------------|
| 1 | `01-problem-empty-state.png` | `Untitled.png` | Empty state - no connections | Problem (before state) |
| 2 | `02-quickstart-connection-form.png` | `Untitled 2.png` | Connection dialog with form | Quick Start Step 3 |
| 3 | `03-quickstart-connected-namespace.png` | `Untitled 3.png` | Connected namespace sidebar | Quick Start Step 3 |
| 4 | `04-feature-message-browser-empty.png` | `Untitled 4.png` | Message browser (empty) | Not actively used |
| 5 | `05-feature-fab-menu.png` | `Untitled 5.png` | FAB menu opened | Not actively used |
| 6 | `06-feature-message-generator-basic.png` | `Untitled 6.png` | Message generator dialog | Quick Start Step 4, Features |
| 7 | `07-feature-message-generator-scenarios.png` | `Untitled 7.png` | Generator scenarios | Quick Start Step 4, Features |
| 8 | `08-hero-message-browser-loaded.png` | `Untitled 8.png` | **HERO - 50 messages loaded** | Hero, Features, Workflows (7+ uses) |
| 9 | `09-feature-send-message-topic.png` | `Untitled 9.png` | Send message to topic | Features - Send Message |
| 10 | `10-workflow-topic-subscription-step1.png` | `Untitled 10.png` | Topic subscription delivery | Workflows - Topics |
| 11 | `11-feature-message-details-properties.png` | `Untitled 11.png` | Message Properties tab | Features - Message Inspection |
| 12 | `12-feature-message-details-custom-props.png` | `Untitled 12.png` | Custom Properties tab | Features - Message Inspection |
| 13 | `13-feature-message-details-body.png` | `Untitled 13.png` | Message Body (JSON) | Features - Message Inspection |
| 14 | `14-feature-ai-findings-badge.png` | `Untitled 14.png` | AI Findings: 2 badge | Features - AI Insights |
| 15 | `15-feature-ai-insights-error-cluster.png` | `Untitled 15.png` | AI error cluster detection | Features - AI Insights |
| 16 | `16-feature-ai-insights-multiple-patterns.png` | `Untitled 16.png` | Multiple AI patterns | Features - AI Insights |
| 17 | `17-feature-ai-patterns-popup.png` | `Untitled 17.png` | AI patterns summary popup | Features - AI Insights |
| 18 | `18-feature-dlq-tab-with-ai.png` | `Untitled 18.png` | DLQ tab with AI indicators | Features - DLQ, Workflows |
| 19 | `19-workflow-dlq-investigation-step1.png` | `Untitled 19.png` | DLQ message Properties | Workflows - DLQ Investigation |
| 20 | `20-workflow-dlq-investigation-step2.png` | `Untitled 20.png` | DLQ AI Insights tab | Workflows - DLQ Investigation |
| 21 | `21-workflow-dlq-replay-step3.png` | `Untitled 21.png` | Replay message dialog | Workflows - DLQ Investigation |
| 22 | `22-workflow-dlq-replay-step4.png` | `Untitled 22.png` | After replay (message counts) | Workflows - DLQ Investigation |
| 23 | `23-feature-search-functionality.png` | `Untitled 23.png` | Search results for "bank" | Features - Search, Workflows |
| 24 | `24-feature-dlq-multiple-deliveries.png` | `Untitled 24.png` | DLQ with delivery count: 3 | Features - DLQ Forensics |

---

## 🎯 Key Screenshots Explained

### Hero Image (Most Important)
**File:** `08-hero-message-browser-loaded.png`

**Shows:**
- ServiceHub message browser with 50 active messages in testqueue
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
| **Total screenshots** | 24 files |
| **Most used screenshot** | `08-hero-message-browser-loaded.png` (7+ times) |
| **Total size** | ~5.2 MB |
| **Average file size** | ~217 KB |
| **Largest file** | 300 KB (hero image) |
| **Smallest file** | 95 KB (empty state) |
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

**Last updated:** February 1, 2026  
**Screenshots version:** v1.0 (initial release)
