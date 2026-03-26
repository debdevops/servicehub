// ────────────────────────────────────────────────────────────────
// Centralised help content for tooltips, tour steps, and the Help page.
// Every user-facing explanation lives here so it's easy to maintain.
// ────────────────────────────────────────────────────────────────

// ─── Azure Service Bus Glossary ──────────────────────────────
export const glossary: Record<string, { term: string; definition: string }> = {
  namespace: {
    term: 'Namespace',
    definition:
      'A container for Service Bus resources (queues, topics). Think of it as a logical grouping for all your messaging endpoints within a single Azure subscription.',
  },
  queue: {
    term: 'Queue',
    definition:
      'A first-in-first-out (FIFO) message channel. A producer sends messages to the queue and a consumer reads them. Each message is delivered to exactly one consumer.',
  },
  topic: {
    term: 'Topic',
    definition:
      'A publish-subscribe message channel. Unlike a queue, a topic delivers copies of each message to all of its subscriptions, enabling fan-out messaging.',
  },
  subscription: {
    term: 'Subscription',
    definition:
      'A named listener attached to a Topic. Each subscription receives its own copy of every message published to the topic and can apply filters.',
  },
  dlq: {
    term: 'Dead-Letter Queue (DLQ)',
    definition:
      'A secondary queue that automatically captures messages that cannot be delivered or processed. Messages land here after exceeding the maximum delivery count or failing validation.',
  },
  connectionString: {
    term: 'Connection String',
    definition:
      'A string containing the endpoint address, shared access key name, and key value needed to authenticate with your Service Bus namespace. Format: Endpoint=sb://<name>.servicebus.windows.net/;SharedAccessKeyName=…;SharedAccessKey=…',
  },
  replay: {
    term: 'Replay / Resubmit',
    definition:
      'Re-send a dead-lettered message back to the original queue or topic so it can be processed again. Useful for transient failures that have been resolved.',
  },
  environment: {
    term: 'Environment',
    definition:
      'Classifies a namespace as Development, UAT, or Production. Production namespaces have safety guards — the Quick Actions button (FAB), send, dead-letter, and replay actions are all disabled to prevent accidental data modification.',
  },
};

// ─── Per-screen tooltip content ──────────────────────────────
export interface TooltipContent {
  text: string;
  detail?: string;
  action?: string;
}

export const tooltips = {
  // ── Connect Page ────────────────────────────────
  connect: {
    displayName: {
      text: 'Friendly name for this connection',
      detail:
        'Give your namespace a recognisable name (e.g. "Order-Service-Dev"). This label appears in the sidebar and connection list.',
      action: 'Enter a name that your team will recognise.',
    } as TooltipContent,
    connectionString: {
      text: 'Azure Service Bus connection string',
      detail:
        'Paste your Service Bus connection string from the Azure Portal → Shared access policies. Use a Listen-only policy for read-only browsing, or a Manage policy for full Quick Actions (FAB) functionality including sending messages, generating test data, and dead-lettering.',
      action: 'Go to Azure Portal → Service Bus → Shared access policies → Copy the connection string.',
    } as TooltipContent,
    environment: {
      text: "Classify this namespace\u2019s environment",
      detail:
        'Production namespaces have safety guards: the Quick Actions button (FAB) is hidden, and send, generate, dead-letter, and replay actions are all disabled. Dev and UAT allow full operations when the SAS policy has Manage permission.',
      action: 'Select "Prod" for live namespaces, "Dev" or "Uat" for lower environments.',
    } as TooltipContent,
    savedConnections: {
      text: 'Previously connected namespaces',
      detail:
        'Click "Open" to switch to a namespace. The green dot means the namespace is currently active. You can delete connections you no longer need.',
      action: 'Click any connection to start browsing its queues and topics.',
    } as TooltipContent,
  },

  // ── Messages Page ──────────────────────────────
  messages: {
    queueTabs: {
      text: 'Switch between Active and Dead-Letter messages',
      detail:
        'Active messages are waiting to be processed. Dead-Letter messages have failed delivery and need attention — they won\'t be retried automatically.',
      action: 'Click "Dead-Letter" to see failed messages that may need replay.',
    } as TooltipContent,
    messageList: {
      text: 'Messages in the selected queue or subscription',
      detail:
        'Each card shows the message preview, status (green = OK, amber = retried, red = dead-lettered), enqueue time, and delivery count.',
      action: 'Click any message to inspect its properties, body, and AI analysis.',
    } as TooltipContent,
    detailPanel: {
      text: 'Message details and actions',
      detail:
        'Use the tabs to view Properties (metadata), Body (payload), AI Analysis (pattern detection), System Info, and Actions (replay / complete).',
    } as TooltipContent,
    replay: {
      text: 'Resubmit this dead-letter message',
      detail:
        'Sends the message back to the original queue for reprocessing. Only available in Dev/UAT environments. Production namespaces block this action.',
      action: 'Fix the root cause first, then click Replay.',
    } as TooltipContent,
    search: {
      text: 'Filter messages by content',
      detail: 'Type to search across message IDs, body text, and properties. Results update as you type.',
    } as TooltipContent,
    aiFindings: {
      text: 'AI-detected message patterns',
      detail:
        'ServiceHub analyses message bodies to detect common patterns like serialisation errors, timeout exceptions, or duplicate messages. Click to filter the list to affected messages.',
    } as TooltipContent,
    autoRefresh: {
      text: 'Automatic message polling',
      detail:
        'When enabled, ServiceHub fetches new messages every 30 seconds. Pause it when you want a stable view for investigation.',
      action: 'Click the play/pause button to toggle auto-refresh.',
    } as TooltipContent,
  },

  // ── Rules Page ─────────────────────────────────
  rules: {
    ruleBuilder: {
      text: 'Create auto-replay rules',
      detail:
        'Define conditions to automatically replay dead-lettered messages that match specific criteria. For example: "If deadLetterReason contains TimeoutException, replay up to 3 times per hour."',
      action: 'Click "+ New Rule" to start building a condition.',
    } as TooltipContent,
    conditions: {
      text: 'Rule matching conditions',
      detail:
        'Each condition checks a message field (like body, properties, or deadLetterReason) against a value using operators (contains, equals, regex). Multiple conditions are combined with AND logic.',
    } as TooltipContent,
    templates: {
      text: 'Pre-built rule templates',
      detail:
        'Browse a gallery of common scenarios (e.g. retry on transient failures, dead-letter poison messages). Templates pre-fill the condition builder — customise them to fit your use case.',
      action: 'Click "Templates" to browse, then "Use Template" to apply.',
    } as TooltipContent,
    rateLimit: {
      text: 'Maximum replays per hour',
      detail:
        'Limits how many messages this rule can replay per hour. Prevents runaway loops if a permanent failure keeps dead-lettering the same messages.',
      action: 'Set a conservative limit (e.g. 10/hour) and increase after observing behaviour.',
    } as TooltipContent,
    testRule: {
      text: 'Test rule against existing messages',
      detail:
        'Runs your rule conditions against current dead-letter messages without actually replaying them. Shows which messages would match.',
      action: 'Click "Test" to preview matches before enabling the rule.',
    } as TooltipContent,
  },

  // ── DLQ History Page ───────────────────────────
  dlqHistory: {
    trendChart: {
      text: '30-day dead-letter trend',
      detail:
        'The chart shows daily new dead-letter messages (red bars) vs. resolved messages (green bars). A rising trend may indicate a systemic issue that needs investigation.',
      action: 'Look for spikes — they often correlate with deployments or infrastructure changes.',
    } as TooltipContent,
    statusFilter: {
      text: 'Filter by message status',
      detail:
        'Active = still in the DLQ. Resolved = replayed or completed. Expired = TTL reached. Use filters to focus on messages that still need action.',
    } as TooltipContent,
    categoryFilter: {
      text: 'Filter by failure category',
      detail:
        'Categories like "Serialization Error", "Timeout", or "Permission Denied" help you triage failures by root cause.',
    } as TooltipContent,
    timeline: {
      text: 'Message event timeline',
      detail:
        'Chronological history of a single message: when it was enqueued, how many delivery attempts occurred, when it was dead-lettered, and any replay attempts.',
      action: 'Click a row to open its timeline drawer.',
    } as TooltipContent,
    export: {
      text: 'Export DLQ data',
      detail:
        'Download the filtered data as JSON or CSV for offline analysis, reporting, or importing into other tools.',
    } as TooltipContent,
  },

  // ── Sidebar ────────────────────────────────────
  sidebar: {
    namespaceTree: {
      text: 'Browse your namespace resources',
      detail:
        'Expand a namespace to see its queues and topics. Each queue shows active and dead-letter message counts. Click a queue or subscription to load its messages.',
    } as TooltipContent,
    quickAccess: {
      text: 'Quick navigation shortcuts',
      detail:
        'Jump directly to common views: active messages, dead-letter queue, DLQ forensics, auto-replay rules, or system health.',
    } as TooltipContent,
    activeMessages: {
      text: 'View messages waiting to be processed',
      action: "Click to navigate to the first queue\u2019s active messages.",
    } as TooltipContent,
    deadLetter: {
      text: 'View failed messages in the dead-letter queue',
      detail: 'Messages that exceeded the max delivery count or failed validation are moved here automatically.',
      action: 'Click to investigate and optionally replay.',
    } as TooltipContent,
  },

  // ── FAB ────────────────────────────────────────
  fab: {
    mainButton: {
      text: 'Quick actions menu',
      detail:
        'Open the floating action menu to send test messages, generate sample data, move messages to the DLQ for testing, or refresh all data. Hidden in Production environments and when the SAS policy lacks Manage permission.',
    } as TooltipContent,
    sendMessage: {
      text: 'Send a test message to this queue/topic',
      detail: 'Compose and send a JSON message for testing. Requires Manage SAS permission. Only available in Dev/UAT.',
    } as TooltipContent,
    generateMessages: {
      text: 'Generate random sample messages',
      detail: 'Creates multiple test messages with realistic payloads for load testing and demo purposes. Requires Manage SAS permission.',
    } as TooltipContent,
    testDlq: {
      text: 'Move messages to dead-letter queue',
      detail: 'Dead-letters up to 3 active messages — useful for testing DLQ monitoring and auto-replay rules. Requires Manage SAS permission.',
    } as TooltipContent,
  },

  // ── Health Page ────────────────────────────────
  health: {
    uptime: {
      text: 'How long the API has been running',
      detail: 'Time since the last restart. A recent restart may indicate a crash or deployment.',
    } as TooltipContent,
    memory: {
      text: 'Current API memory consumption',
      detail: 'Managed heap memory used by the .NET runtime. High values may indicate a memory leak.',
    } as TooltipContent,
    threads: {
      text: 'Active thread count',
      detail: 'Number of threads in the .NET thread pool. Spikes may indicate thread starvation from blocking calls.',
    } as TooltipContent,
  },
} as const;

// ─── Guided Tour Steps ───────────────────────────────────────
export interface TourStep {
  /** CSS selector to spotlight (e.g. '[data-tour="sidebar"]') */
  target: string;
  /** Title shown in the tour popover */
  title: string;
  /** Description text */
  content: string;
  /** Position of the popover relative to the target */
  placement: 'top' | 'bottom' | 'left' | 'right';
}

export const tourSteps: TourStep[] = [
  {
    target: '[data-tour="sidebar"]',
    title: 'Sidebar — Your Namespace Browser',
    content:
      'This panel shows all your connected namespaces. Expand one to see its queues and topics with live message counts. Click any queue to load its messages.',
    placement: 'right',
  },
  {
    target: '[data-tour="quick-access"]',
    title: 'Quick Access Shortcuts',
    content:
      'Jump directly to Active Messages, Dead-Letter Queue, DLQ Intelligence, Auto-Replay Rules, or System Health without navigating the tree.',
    placement: 'right',
  },
  {
    target: '[data-tour="header-connection"]',
    title: 'Connection Status',
    content:
      'Shows which namespace you\'re connected to and its environment (Dev / UAT / Prod). The coloured badge helps you stay aware of which environment you\'re operating in.',
    placement: 'bottom',
  },
  {
    target: '[data-tour="header-help"]',
    title: 'Help & Quick Reference',
    content:
      'Click the "?" button anytime to open the Help page with a searchable guide to every feature, Azure Service Bus concepts, and this tour.',
    placement: 'bottom',
  },
  {
    target: '[data-tour="messages-area"]',
    title: 'Message Inspector',
    content:
      'This is the main workspace. Select a queue from the sidebar to load messages. Click any message to see its full details — properties, body, AI analysis, and replay options.',
    placement: 'left',
  },
  {
    target: '[data-tour="add-connection"]',
    title: 'Add a Connection',
    content:
      'Click the "+" button to connect a new Azure Service Bus namespace. You\'ll need a connection string from the Azure Portal.',
    placement: 'bottom',
  },
];

// ─── Help Page Sections (for /help route) ────────────────────
export interface HelpSection {
  id: string;
  title: string;
  icon: string;
  items: { question: string; answer: string }[];
}

export const helpSections: HelpSection[] = [
  {
    id: 'getting-started',
    title: 'Getting Started',
    icon: '🚀',
    items: [
      {
        question: 'How do I connect to Azure Service Bus?',
        answer:
          'Go to the Connect page (click the "+" in the sidebar). Paste your connection string from Azure Portal → Service Bus → Shared Access Policies. Give it a friendly name and select the environment (Dev/UAT/Prod).',
      },
      {
        question: 'What connection string format is required?',
        answer:
          'The format is: Endpoint=sb://<namespace>.servicebus.windows.net/;SharedAccessKeyName=<policy>;SharedAccessKey=<key>. Use a Listen-only policy for read-only browsing, or a Manage policy for full Quick Actions (FAB) functionality.',
      },
      {
        question: 'What does the environment selector do?',
        answer:
          'It classifies the namespace as Dev, UAT, or Prod. Production namespaces have strict safety guards — the Quick Actions button (FAB) is completely hidden, and send, generate, dead-letter, and replay actions are all disabled. Dev and UAT allow full operations when the SAS policy has Manage permission.',
      },
      {
        question: 'How do I navigate between namespaces?',
        answer:
          'Expand namespaces in the sidebar to see queues and topics. Click any queue or subscription to load its messages. Use the "Open" button on the Connect page to switch namespaces.',
      },
    ],
  },
  {
    id: 'messages',
    title: 'Messages & Inspection',
    icon: '📨',
    items: [
      {
        question: 'How do I view messages in a queue?',
        answer:
          'Click a queue name in the sidebar. Messages appear on the left panel. Click any message to see its details (properties, body, AI analysis) on the right panel.',
      },
      {
        question: 'What do the message status colours mean?',
        answer:
          'Green = delivered successfully. Amber = retried (delivery count > 1, may indicate transient issues). Red = dead-lettered (failed processing, moved to DLQ).',
      },
      {
        question: 'What is the difference between Active and Dead-Letter tabs?',
        answer:
          'Active messages are waiting to be consumed. Dead-Letter messages have failed processing (exceeded max delivery count or failed validation) and won\'t be retried automatically — they need investigation.',
      },
      {
        question: 'What does "Body unavailable" mean?',
        answer:
          'The message body couldn\'t be retrieved — this usually happens when the body exceeds the API\'s size limit or the API is being rate-limited by Azure Service Bus.',
      },
      {
        question: 'How does Replay / Resubmit work?',
        answer:
          'Replay sends a dead-lettered message back to the original queue for reprocessing. It copies the body and properties. Requires a SAS policy with Send permission. Only available in Dev/UAT — Production blocks replays.',
      },
    ],
  },
  {
    id: 'dlq',
    title: 'Dead-Letter Queue (DLQ)',
    icon: '💀',
    items: [
      {
        question: 'What is a Dead-Letter Queue?',
        answer:
          'A DLQ is a secondary queue that captures messages that can\'t be delivered or processed. Messages land here after exceeding the max delivery count, failing validation, or being explicitly dead-lettered by the consumer.',
      },
      {
        question: 'What does the DLQ Intelligence page show?',
        answer:
          'A 30-day trend chart of new vs. resolved dead-letter messages, a searchable table with status and category filters, and a timeline drawer showing the lifecycle of individual messages.',
      },
      {
        question: 'What do the DLQ categories mean?',
        answer:
          'Categories like "Serialization Error", "Timeout", "Permission Denied" classify failures by root cause. They help you triage — e.g., timeouts may need infrastructure attention while serialisation errors need code fixes.',
      },
      {
        question: 'How do I export DLQ data?',
        answer:
          'Click the Export button on the DLQ Intelligence page. Choose JSON or CSV format. The export respects your current filters.',
      },
    ],
  },
  {
    id: 'rules',
    title: 'Auto-Replay Rules',
    icon: '⚡',
    items: [
      {
        question: 'What are auto-replay rules?',
        answer:
          'Rules that automatically replay dead-letter messages matching specific conditions. For example: "If deadLetterReason contains TimeoutException, replay automatically."',
      },
      {
        question: 'How do conditions work?',
        answer:
          'Each condition checks a message field (body, properties, deadLetterReason) against a value using an operator (contains, equals, regex, etc.). Multiple conditions are combined with AND logic — all must match.',
      },
      {
        question: 'What does rate limiting do?',
        answer:
          'Limits how many messages a rule can replay per hour. This prevents runaway loops if a permanent failure keeps dead-lettering the same messages repeatedly.',
      },
      {
        question: 'Can I test a rule before enabling it?',
        answer:
          'Yes — click "Test" to dry-run your conditions against current dead-letter messages. It shows which messages would match without actually replaying them.',
      },
      {
        question: 'What are rule templates?',
        answer:
          'Pre-built rule configurations for common scenarios (retry on transient failures, isolate poison messages, etc.). Click "Templates" to browse, then customise to fit your needs.',
      },
    ],
  },
  {
    id: 'fab',
    title: 'Quick Actions (FAB)',
    icon: '🎯',
    items: [
      {
        question: 'Where is the + button / quick actions menu?',
        answer:
          'The floating action button (FAB) appears in the bottom-right corner on the Messages page. It requires: (1) a non-Production namespace, and (2) a SAS policy with Manage permission. If either condition is not met, the FAB is hidden.',
      },
      {
        question: 'Why don\'t I see the + button?',
        answer:
          'The FAB is hidden in three cases: (1) you\'re not on the Messages page, (2) your namespace is classified as Production, or (3) your SAS policy lacks Manage permission. Switch to a Dev/UAT namespace with a Manage policy to see it.',
      },
      {
        question: 'What can I do from the FAB?',
        answer:
          'Send a test message, generate random sample messages (realistic business scenarios), move messages to the DLQ for testing, or refresh all data. These actions require a Manage SAS policy and are disabled in Production.',
      },
    ],
  },
  {
    id: 'health',
    title: 'System Health',
    icon: '💚',
    items: [
      {
        question: 'What does the Health page show?',
        answer:
          'Real-time backend metrics: API version, uptime, memory usage, thread count, and garbage collection stats. Useful for verifying the API is running correctly.',
      },
      {
        question: 'What does high memory usage mean?',
        answer:
          'Memory over 500 MB may indicate a leak. Check if it keeps rising after garbage collection. The GC stats show how often each generation is collected.',
      },
    ],
  },
  {
    id: 'permissions',
    title: 'Permissions & Security',
    icon: '🔒',
    items: [
      {
        question: 'What SAS permissions does ServiceHub need?',
        answer:
          'It depends on your use case. Listen-only is sufficient for browsing messages, inspecting DLQ, and viewing metrics. Manage permission enables the Quick Actions (FAB) button for sending messages, generating test data, dead-lettering, and replay operations.',
      },
      {
        question: 'Why is the Quick Actions (FAB) button hidden?',
        answer:
          'The FAB requires two conditions: (1) the namespace must be Dev or UAT (not Production), and (2) the SAS policy must have Manage permission. This prevents accidental modification of production data and ensures only authorized users can perform write operations.',
      },
      {
        question: 'What is the difference between Listen, Send, and Manage?',
        answer:
          'Listen: Read messages without removing them (peek). Send: Write messages to queues/topics and replay from DLQ. Manage: Full control including Send + Listen + administrative operations. ServiceHub recommends Manage for Dev/UAT and Listen-only for Production.',
      },
      {
        question: 'Is it safe to use ServiceHub with production namespaces?',
        answer:
          'Yes. When a namespace is classified as Production, ServiceHub operates in strict read-only mode — the FAB is hidden, send/dead-letter/replay actions are all blocked both in the UI and at the API level. A Listen-only SAS policy is all you need for Production.',
      },
      {
        question: 'What is Scalar API Docs (/scalar/v1)?',
        answer:
          'Scalar is an interactive API documentation viewer, only available in Development mode. It lets developers explore and test all API endpoints. It is automatically disabled in Production deployments for security.',
      },
    ],
  },
  {
    id: 'glossary',
    title: 'Azure Service Bus Glossary',
    icon: '📖',
    items: Object.values(glossary).map((g) => ({
      question: g.term,
      answer: g.definition,
    })),
  },
];
