# Screenshot Renaming Guide

## Renaming Map (Old → New)

| Order | Original | New Filename | Purpose | Section |
|-------|----------|--------------|---------|---------|
| 1 | screenshot-01.png | `01-problem-empty-state.png` | Shows initial empty state | Problem/Before State |
| 2 | screenshot-02.png | `02-quickstart-connection-form.png` | Connection setup dialog | Quick Start Step 1 |
| 3 | screenshot-03.png | `03-quickstart-connected-namespace.png` | Successfully connected namespace | Quick Start Step 2 |
| 4 | screenshot-04.png | `04-feature-message-browser-empty.png` | Message browser interface (empty queue) | Features - UI Overview |
| 5 | screenshot-05.png | `05-feature-fab-menu.png` | Floating action button with tools | Features - Tooling |
| 6 | screenshot-06.png | `06-feature-message-generator-basic.png` | Message generator dialog (target selection) | Features - Testing Tools |
| 7 | screenshot-07.png | `07-feature-message-generator-scenarios.png` | Realistic scenario templates | Features - Testing Tools |
| 8 | screenshot-08.png | `08-hero-message-browser-loaded.png` | **HERO IMAGE** - Browser with 50 messages | Hero Section |
| 9 | screenshot-09.png | `09-feature-send-message-topic.png` | Manual message sending to topics | Features - Message Operations |
| 10 | screenshot-10.png | `10-workflow-topic-subscription-step1.png` | Topic subscription message delivery | Workflow - Topics |
| 11 | screenshot-11.png | `11-feature-message-details-properties.png` | Message details panel (Properties tab) | Features - Message Inspection |
| 12 | screenshot-12.png | `12-feature-message-details-custom-props.png` | Custom application properties view | Features - Message Inspection |
| 13 | screenshot-13.png | `13-feature-message-details-body.png` | JSON message body viewer | Features - Message Inspection |
| 14 | screenshot-14.png | `14-feature-ai-findings-badge.png` | AI pattern detection indicator | Features - AI Insights |
| 15 | screenshot-15.png | `15-feature-ai-insights-error-cluster.png` | AI detected error cluster pattern | Features - AI Insights |
| 16 | screenshot-16.png | `16-feature-ai-insights-multiple-patterns.png` | Multiple AI patterns detected | Features - AI Insights |
| 17 | screenshot-17.png | `17-feature-ai-patterns-popup.png` | Active AI patterns summary popup | Features - AI Insights |
| 18 | screenshot-18.png | `18-feature-dlq-tab-with-ai.png` | Dead-letter queue tab with AI indicators | Features - DLQ Forensics |
| 19 | screenshot-19.png | `19-workflow-dlq-investigation-step1.png` | DLQ message with warning assessment | Workflow - DLQ Investigation |
| 20 | screenshot-20.png | `20-workflow-dlq-investigation-step2.png` | DLQ AI insights and recommendations | Workflow - DLQ Investigation |
| 21 | screenshot-21.png | `21-workflow-dlq-replay-step3.png` | Message replay confirmation dialog | Workflow - DLQ Investigation |
| 22 | screenshot-22.png | `22-workflow-dlq-replay-step4.png` | Post-replay state showing message counts | Workflow - DLQ Investigation |
| 23 | screenshot-23.png | `23-feature-search-functionality.png` | Search across message content | Features - Search & Filter |
| 24 | screenshot-24.png | `24-feature-dlq-multiple-deliveries.png` | DLQ with delivery count tracking | Features - DLQ Forensics |

## Key Screenshots by Section

### Hero Section
- **`08-hero-message-browser-loaded.png`** - Primary hero image showing full UI with messages

### Problem/Solution
- `01-problem-empty-state.png` - Before state (problem)
- `08-hero-message-browser-loaded.png` - After state (solution)

### Quick Start (Visual Step-by-Step)
1. `02-quickstart-connection-form.png` - Connect to namespace
2. `03-quickstart-connected-namespace.png` - See your entities
3. `06-feature-message-generator-basic.png` - Generate test messages
4. `08-hero-message-browser-loaded.png` - Browse messages

### Key Features (One Per Feature)
- **Message Browser**: `08-hero-message-browser-loaded.png`
- **Message Details**: `11-feature-message-details-properties.png`, `13-feature-message-details-body.png`
- **Search**: `23-feature-search-functionality.png`
- **DLQ Investigation**: `18-feature-dlq-tab-with-ai.png`, `24-feature-dlq-multiple-deliveries.png`
- **AI Insights**: `15-feature-ai-insights-error-cluster.png`, `16-feature-ai-insights-multiple-patterns.png`
- **Testing Tools**: `07-feature-message-generator-scenarios.png`
- **Message Operations**: `09-feature-send-message-topic.png`

### Investigation Workflows
**Workflow 1: DLQ Investigation** (4 screenshots)
1. `19-workflow-dlq-investigation-step1.png` - Identify DLQ message
2. `20-workflow-dlq-investigation-step2.png` - Review AI insights
3. `21-workflow-dlq-replay-step3.png` - Replay message
4. `22-workflow-dlq-replay-step4.png` - Verify replay success

**Workflow 2: Topic Monitoring**
- `10-workflow-topic-subscription-step1.png`

### Security & Trust
- `02-quickstart-connection-form.png` - Shows connection string security guidance

## Implementation Notes

- All screenshots should be placed in `/docs/images/` directory
- Use relative paths in README: `![Alt text](docs/images/filename.png)`
- Add italicized captions below each image
- Keep alt text descriptive for accessibility
- Use side-by-side layout for comparison sections
