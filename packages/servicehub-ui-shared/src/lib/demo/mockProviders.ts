/**
 * mockProviders.ts — Unified Demo Data Adapter
 *
 * Normalizes cloud-specific mock data (Azure / AWS / GCP) into the same
 * API response shapes used by the real backend. This lets all existing hooks
 * and pages consume mock data without any changes to their rendering logic.
 *
 * Each function returns data matching the exact TypeScript types from
 * ../api/types so the real pages work identically in demo mode.
 */

import type { CloudProviderType, Namespace, Queue, Topic, Message as APIMessage, PaginatedResponse } from '../api/types';
import type { Subscription } from '../../hooks/useSubscriptions';
import { generateAzureMockMessages, AZURE_QUEUES, AZURE_TOPICS } from '../azureMockData';
import { generateAwsMockMessages } from '../awsMockData';
import { generateGcpMockMessages } from '../gcpMockData';
import type { Message as MockMessage } from '../mockData';
import type { ProviderCapabilities, ProviderCapabilitiesMap } from '../api/cloudBridge';
import type {
  DlqSignaturesResponse,
  DlqClusterSignature,
  DlqSignatureDetail,
  SignatureTimelineResponse,
  FailureKnowledge,
  RootCauseExplorerResponse,
  RootCauseMatch,
} from '../api/dlqSignatures';
import type { DlqTimelineEvent, DlqHistoryItem, PaginatedResponse as DlqHistoryPage } from '../api/dlqHistory';
import type { BulkOperationJob, PaginatedBulkOperationJobs } from '../api/bulkOperations';
import type {
  RecoveryOperation,
  RecoveryOperationDetail,
  RecoveryLedgerEntry,
  RecoveryEvent,
  RecoveryEntryState,
} from '../api/recovery';
import type { AuditLogItem, AuditPageResponse } from '../api/audit';
import type { FleetOverview, FleetNamespaceHealth, FleetHealthSeverity } from '../api/fleet';
import type { RuleResponse } from '../api/rules';
import type {
  InvestigationCenterResponse,
  CompactMetricsSummary,
  InvestigationQueueItem,
  FailedReplayItem,
  KnowledgeReviewItem,
  NewSignatureItem,
} from '../../hooks/useInvestigationQueue';

// ─── Namespace IDs ──────────────────────────────────────────────────────────
// Stable IDs used in URL query params and as namespace identifiers in demo mode
export const DEMO_NAMESPACE_IDS: Record<CloudProviderType, string> = {
  azure: 'demo-azure-contoso-prod',
  aws: 'demo-aws-acme-prod',
  gcp: 'demo-gcp-medstream-prod',
};

// ─── Namespace Definitions ───────────────────────────────────────────────────

export function getMockNamespaces(provider: CloudProviderType): Namespace[] {
  const id = DEMO_NAMESPACE_IDS[provider];

  const definitions: Record<CloudProviderType, Namespace> = {
    azure: {
      id,
      name: 'contoso-prod-bus',
      displayName: 'Contoso Commerce (Demo)',
      description: 'Contoso Commerce Platform — Black Friday incident demo',
      isActive: true,
      createdAt: new Date(Date.now() - 30 * 24 * 60 * 60 * 1000).toISOString(),
      cloudProvider: 'azure',
      environment: 'prod',
      hasListenPermission: true,
      hasSendPermission: false, // Demo: read-only
      hasManagePermission: false,
    },
    aws: {
      id,
      name: 'acme-prod',
      displayName: 'AcmeRetail E-Commerce (Demo)',
      description: 'AcmeRetail Global — Payment gateway cascade failure demo',
      isActive: true,
      createdAt: new Date(Date.now() - 30 * 24 * 60 * 60 * 1000).toISOString(),
      cloudProvider: 'aws',
      awsRegion: 'us-east-1',
      environment: 'prod',
      hasListenPermission: true,
      hasSendPermission: false,
      hasManagePermission: false,
    },
    gcp: {
      id,
      name: 'medstream-prod',
      displayName: 'MedStream Healthcare (Demo)',
      description: 'MedStream Healthcare Analytics — FHIR schema mismatch demo',
      isActive: true,
      createdAt: new Date(Date.now() - 30 * 24 * 60 * 60 * 1000).toISOString(),
      cloudProvider: 'gcp',
      gcpProjectId: 'medstream-healthcare-prod',
      environment: 'prod',
      hasListenPermission: true,
      hasSendPermission: false,
      hasManagePermission: false,
    },
  };

  return [definitions[provider]];
}

// ─── Provider Capabilities ───────────────────────────────────────────────────
// Mirrors services/api/src/ServiceHub.Core/Models/ProviderCapabilities.cs field-for-field.
// Demo Mode is a prospect's only hands-on experience of the product — presenting AWS/GCP
// with Azure's capabilities here would set expectations the real app then violates (e.g.
// showing repeatable Live Tail on a demo SQS queue when real SQS has no non-destructive peek).
// A shared generated contract is the right long-term fix; this literal mirror is deliberate
// duplication until that exists (see the C# file above for the authoritative source and the
// full rationale behind each flag).

const DEMO_CAPABILITIES: Record<CloudProviderType, ProviderCapabilities> = {
  azure: {
    supportsMessageCounts: true,
    supportsManualDeadLetter: true,
    supportsPurge: false,
    supportsScheduledMessages: true,
    supportsRepeatablePeek: true,
    notes: 'Purge is not supported — the SDK has no reliable single-message delete by sequence number.',
  },
  aws: {
    supportsMessageCounts: true,
    supportsManualDeadLetter: true,
    supportsPurge: true,
    supportsScheduledMessages: false,
    supportsRepeatablePeek: false,
    notes:
      'Scheduled messages are not supported — SQS only offers DelaySeconds (max 15 minutes) at send time. ' +
      "Repeated/live polling is also not supported — SQS has no non-destructive peek, so every call is a receive that counts toward the queue's maxReceiveCount.",
  },
  gcp: {
    supportsMessageCounts: false,
    supportsManualDeadLetter: false,
    supportsPurge: true,
    supportsScheduledMessages: false,
    supportsRepeatablePeek: false,
    notes:
      'Message counts and manual dead-lettering are not supported — Pub/Sub has no count API and dead-lettering is policy-driven via MaxDeliveryAttempts. ' +
      "Scheduled messages are not supported either. Repeated/live polling is also not supported — every pull-then-release still counts as a delivery attempt toward the subscription's MaxDeliveryAttempts.",
  },
};

/** Capabilities for a single demo provider, keyed on the provider DemoModeProvider already carries. */
export function getMockCapabilities(provider: CloudProviderType): ProviderCapabilities {
  return DEMO_CAPABILITIES[provider];
}

/**
 * The full capabilities map in the same shape `GET /api/v1/cloud-bridge/capabilities` returns,
 * so demo mode can feed `useProviderCapabilities` without a real network round-trip.
 */
export function getMockCapabilitiesMap(): ProviderCapabilitiesMap {
  return {
    Azure: DEMO_CAPABILITIES.azure,
    Aws: DEMO_CAPABILITIES.aws,
    Gcp: DEMO_CAPABILITIES.gcp,
  };
}

// ─── Queues ─────────────────────────────────────────────────────────────────

const AZURE_QUEUE_DEFS: Queue[] = AZURE_QUEUES.map((q, i) => ({
  name: q.name,
  activeMessageCount: [12, 8, 15, 5][i] ?? 8,
  deadLetterMessageCount: [22, 12, 5, 3][i] ?? 5,
  scheduledMessageCount: 0,
  maxSizeInMegabytes: 1024,
  sizeInBytes: ([q.name].length * 1024 * 50) + i * 10000,
  status: 'Active',
}));

const AWS_QUEUE_DEFS: Queue[] = [
  { name: 'order-processing', activeMessageCount: 18, deadLetterMessageCount: 14, scheduledMessageCount: 0, maxSizeInMegabytes: 256, sizeInBytes: 180000, status: 'Active' },
  { name: 'payment-gateway-events', activeMessageCount: 5, deadLetterMessageCount: 22, scheduledMessageCount: 0, maxSizeInMegabytes: 256, sizeInBytes: 220000, status: 'Active' },
  { name: 'notification-service', activeMessageCount: 31, deadLetterMessageCount: 6, scheduledMessageCount: 0, maxSizeInMegabytes: 256, sizeInBytes: 310000, status: 'Active' },
  { name: 'fraud-detection', activeMessageCount: 9, deadLetterMessageCount: 4, scheduledMessageCount: 0, maxSizeInMegabytes: 256, sizeInBytes: 90000, status: 'Active' },
  { name: 'inventory-sync', activeMessageCount: 24, deadLetterMessageCount: 8, scheduledMessageCount: 0, maxSizeInMegabytes: 256, sizeInBytes: 240000, status: 'Active' },
  { name: 'cart-abandonment', activeMessageCount: 47, deadLetterMessageCount: 0, scheduledMessageCount: 2, maxSizeInMegabytes: 256, sizeInBytes: 470000, status: 'Active' },
];

// GCP has topics+subscriptions but no standalone "queues" — return empty for Pub/Sub
const GCP_QUEUE_DEFS: Queue[] = [];

export function getMockQueues(provider: CloudProviderType): Queue[] {
  switch (provider) {
    case 'azure': return AZURE_QUEUE_DEFS;
    case 'aws': return AWS_QUEUE_DEFS;
    case 'gcp': return GCP_QUEUE_DEFS;
  }
}

// ─── Topics ─────────────────────────────────────────────────────────────────

const AZURE_TOPIC_DEFS: Topic[] = AZURE_TOPICS.map((t) => ({
  name: t.name,
  subscriptionCount: t.subscriptions.length,
  sizeInBytes: 500000,
  maxSizeInMegabytes: 1024,
  status: 'Active',
}));

const AWS_TOPIC_DEFS: Topic[] = [
  { name: 'order-events-topic', subscriptionCount: 3, sizeInBytes: 300000, maxSizeInMegabytes: 256, status: 'Active' },
  { name: 'payment-alerts-topic', subscriptionCount: 2, sizeInBytes: 200000, maxSizeInMegabytes: 256, status: 'Active' },
  { name: 'customer-notifications-topic', subscriptionCount: 4, sizeInBytes: 400000, maxSizeInMegabytes: 256, status: 'Active' },
];

const GCP_TOPIC_DEFS: Topic[] = [
  { name: 'patient-intake', subscriptionCount: 2, sizeInBytes: 200000, maxSizeInMegabytes: 256, status: 'Active' },
  { name: 'lab-results', subscriptionCount: 3, sizeInBytes: 350000, maxSizeInMegabytes: 256, status: 'Active' },
  { name: 'billing-events', subscriptionCount: 2, sizeInBytes: 180000, maxSizeInMegabytes: 256, status: 'Active' },
  { name: 'appointment-reminders', subscriptionCount: 1, sizeInBytes: 80000, maxSizeInMegabytes: 256, status: 'Active' },
  { name: 'medication-orders', subscriptionCount: 2, sizeInBytes: 120000, maxSizeInMegabytes: 256, status: 'Active' },
  { name: 'clinical-alerts', subscriptionCount: 2, sizeInBytes: 90000, maxSizeInMegabytes: 256, status: 'Active' },
];

export function getMockTopics(provider: CloudProviderType): Topic[] {
  switch (provider) {
    case 'azure': return AZURE_TOPIC_DEFS;
    case 'aws': return AWS_TOPIC_DEFS;
    case 'gcp': return GCP_TOPIC_DEFS;
  }
}

// ─── Subscriptions ───────────────────────────────────────────────────────────

const AZURE_SUBSCRIPTIONS: Record<string, Subscription[]> = {
  'order-events': AZURE_TOPICS[0]?.subscriptions.map((s, i) => ({
    name: s.name,
    activeMessageCount: [8, 3, 12][i] ?? 5,
    deadLetterMessageCount: [4, 1, 8][i] ?? 2,
    topicName: 'order-events',
    status: 'Active',
  })) ?? [],
};

const AWS_SUBSCRIPTIONS: Record<string, Subscription[]> = {
  'order-events-topic': [
    { name: 'order-processor-sub', activeMessageCount: 10, deadLetterMessageCount: 5, topicName: 'order-events-topic', status: 'Active' },
    { name: 'analytics-sub', activeMessageCount: 8, deadLetterMessageCount: 2, topicName: 'order-events-topic', status: 'Active' },
    { name: 'fulfillment-sub', activeMessageCount: 15, deadLetterMessageCount: 7, topicName: 'order-events-topic', status: 'Active' },
  ],
  'payment-alerts-topic': [
    { name: 'fraud-monitor-sub', activeMessageCount: 4, deadLetterMessageCount: 3, topicName: 'payment-alerts-topic', status: 'Active' },
    { name: 'risk-engine-sub', activeMessageCount: 6, deadLetterMessageCount: 1, topicName: 'payment-alerts-topic', status: 'Active' },
  ],
  'customer-notifications-topic': [
    { name: 'email-service-sub', activeMessageCount: 20, deadLetterMessageCount: 4, topicName: 'customer-notifications-topic', status: 'Active' },
    { name: 'sms-service-sub', activeMessageCount: 15, deadLetterMessageCount: 2, topicName: 'customer-notifications-topic', status: 'Active' },
    { name: 'push-notify-sub', activeMessageCount: 12, deadLetterMessageCount: 0, topicName: 'customer-notifications-topic', status: 'Active' },
    { name: 'webhook-sub', activeMessageCount: 9, deadLetterMessageCount: 1, topicName: 'customer-notifications-topic', status: 'Active' },
  ],
};

const GCP_SUBSCRIPTIONS: Record<string, Subscription[]> = {
  'patient-intake': [
    { name: 'intake-processor-sub', activeMessageCount: 14, deadLetterMessageCount: 3, topicName: 'patient-intake', status: 'Active' },
    { name: 'ehr-sync-sub', activeMessageCount: 8, deadLetterMessageCount: 7, topicName: 'patient-intake', status: 'Active' },
  ],
  'lab-results': [
    { name: 'results-router-sub', activeMessageCount: 22, deadLetterMessageCount: 9, topicName: 'lab-results', status: 'Active' },
    { name: 'physician-notify-sub', activeMessageCount: 11, deadLetterMessageCount: 4, topicName: 'lab-results', status: 'Active' },
    { name: 'hl7-export-sub', activeMessageCount: 6, deadLetterMessageCount: 18, topicName: 'lab-results', status: 'Active' },
  ],
  'billing-events': [
    { name: 'insurance-claims-sub', activeMessageCount: 9, deadLetterMessageCount: 5, topicName: 'billing-events', status: 'Active' },
    { name: 'patient-billing-sub', activeMessageCount: 13, deadLetterMessageCount: 2, topicName: 'billing-events', status: 'Active' },
  ],
  'appointment-reminders': [
    { name: 'sms-gateway-sub', activeMessageCount: 31, deadLetterMessageCount: 1, topicName: 'appointment-reminders', status: 'Active' },
  ],
  'medication-orders': [
    { name: 'pharmacy-sub', activeMessageCount: 17, deadLetterMessageCount: 4, topicName: 'medication-orders', status: 'Active' },
    { name: 'dea-audit-sub', activeMessageCount: 7, deadLetterMessageCount: 2, topicName: 'medication-orders', status: 'Active' },
  ],
  'clinical-alerts': [
    { name: 'oncall-pager-sub', activeMessageCount: 4, deadLetterMessageCount: 8, topicName: 'clinical-alerts', status: 'Active' },
    { name: 'dashboard-sub', activeMessageCount: 19, deadLetterMessageCount: 1, topicName: 'clinical-alerts', status: 'Active' },
  ],
};

export function getMockSubscriptions(provider: CloudProviderType, topicName: string): Subscription[] {
  const map =
    provider === 'azure' ? AZURE_SUBSCRIPTIONS :
    provider === 'aws' ? AWS_SUBSCRIPTIONS :
    GCP_SUBSCRIPTIONS;
  return map[topicName] ?? [];
}

// ─── Namespace Stats ─────────────────────────────────────────────────────────

export interface MockNamespaceStats {
  totalQueues: number;
  totalTopics: number;
  totalSubscriptions: number;
  totalActive: number;
  totalDlq: number;
  totalScheduled: number;
}

export function getMockStats(provider: CloudProviderType): MockNamespaceStats {
  const queues = getMockQueues(provider);
  const topics = getMockTopics(provider);
  const allSubscriptions = Object.values(
    provider === 'azure' ? AZURE_SUBSCRIPTIONS :
    provider === 'aws' ? AWS_SUBSCRIPTIONS :
    GCP_SUBSCRIPTIONS
  ).flat();

  const queueActive = queues.reduce((s, q) => s + q.activeMessageCount, 0);
  const queueDlq = queues.reduce((s, q) => s + q.deadLetterMessageCount, 0);
  const subActive = allSubscriptions.reduce((s, sub) => s + sub.activeMessageCount, 0);
  const subDlq = allSubscriptions.reduce((s, sub) => s + sub.deadLetterMessageCount, 0);

  return {
    totalQueues: queues.length,
    totalTopics: topics.length,
    totalSubscriptions: allSubscriptions.length,
    totalActive: queueActive + subActive,
    totalDlq: queueDlq + subDlq,
    totalScheduled: queues.reduce((s, q) => s + q.scheduledMessageCount, 0),
  };
}

// ─── Messages ────────────────────────────────────────────────────────────────

/** Convert the internal mock Message shape to the API message shape expected by pages */
function mockToAPIMessage(msg: MockMessage): APIMessage {
  return {
    messageId: msg.id,
    sequenceNumber: msg.sequenceNumber,
    enqueuedTime: msg.enqueuedTime.toISOString(),
    deliveryCount: msg.deliveryCount,
    state: 'Active',
    contentType: msg.contentType,
    body: msg.body,
    correlationId: (msg.properties?.['servicebus:CorrelationId'] as string) ?? null,
    sessionId: (msg.properties?.['servicebus:SessionId'] as string) ?? null,
    timeToLive: msg.timeToLive ?? null,
    deadLetterSource: msg.deadLetterSource ?? null,
    deadLetterReason: msg.deadLetterReason ?? null,
    applicationProperties: msg.properties ?? null,
    isFromDeadLetter: msg.queueType === 'deadletter',
    entityName: msg.displayTitle ?? null,
  };
}

// Cache generated messages so they're stable across re-renders
const messageCache = new Map<string, MockMessage[]>();

function getCachedMessages(provider: CloudProviderType): MockMessage[] {
  if (!messageCache.has(provider)) {
    let messages: MockMessage[];
    switch (provider) {
      case 'azure': messages = generateAzureMockMessages(50); break;
      case 'aws': messages = generateAwsMockMessages(50); break;
      case 'gcp': messages = generateGcpMockMessages(50); break;
    }
    messageCache.set(provider, messages);
  }
  return messageCache.get(provider)!;
}

export interface MockMessagesResult {
  items: APIMessage[];
  totalCount: number;
  page: number;
  pageSize: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

/**
 * Get mock messages for a specific entity and queue type.
 * Returns data in the same paginated shape as the real API (PaginatedResponse).
 */
export function getMockMessages(
  provider: CloudProviderType,
  _entityName: string,
  queueType: 'active' | 'deadletter' = 'active',
  skip = 0,
  take = 50,
): PaginatedResponse<APIMessage> {
  const all = getCachedMessages(provider);

  // Filter by queue type
  const typed = all.filter((m) => m.queueType === queueType);

  // For the demo, show all messages regardless of entity — provides
  // a rich browsing experience without per-entity bucketing.
  const items = typed.slice(skip, skip + take).map(mockToAPIMessage);
  const page = Math.floor(skip / take) + 1;

  return {
    items,
    totalCount: typed.length,
    page,
    pageSize: take,
    hasNextPage: skip + take < typed.length,
    hasPreviousPage: skip > 0,
  };
}

// ─── Failure Signatures & Knowledge ─────────────────────────────────────────
// A small, curated set of failure signatures demonstrating ServiceHub's core
// differentiator — Failure Intelligence — with populated operational knowledge,
// version-appropriate review status, and a short timeline. Identical across all
// three demo providers (the failure taxonomy here is cloud-agnostic); read-only,
// matching the rest of Demo Mode. Not derived from the generated message fixtures
// above — these are self-contained illustrative clusters, not tied to specific
// generated message IDs.

const DAY_MS = 24 * 60 * 60 * 1000;

interface DemoSignatureDefinition {
  hash: string;
  dominantDeadletterReason: string;
  topTerms: string[];
  explanation: string;
  size: number;
  isNew: boolean;
  daysSinceFirstSeen: number;
  status: DlqClusterSignature['status'];
  trend: DlqClusterSignature['trend'];
  knowledge: FailureKnowledge | null;
  /** Past signature-replay job outcomes, most recent first — mirrors `knowledge`'s per-signature fixture pattern. */
  replayHistory: BulkOperationJob[];
  /** Occurrences of this same signature hash in other (fictional) namespaces in the fleet, for Root Cause Explorer. */
  rootCauseMatches: RootCauseMatch[];
  /** Namespace-wide audit entries in the 24h before firstSeenAt, for the Recent Changes Before Failure panel. */
  recentChanges: AuditLogItem[];
}

const DEMO_SIGNATURE_DEFS: DemoSignatureDefinition[] = [
  {
    hash: 'demo-max-delivery-count-exceeded',
    dominantDeadletterReason: 'MaxDeliveryCountExceeded',
    topTerms: ['inventory-service', 'timeout', 'max-delivery-count'],
    explanation:
      'Messages exceeded the maximum delivery count without being completed — the consumer repeatedly abandons them.',
    size: 42,
    isNew: false,
    daysSinceFirstSeen: 18,
    status: 'Resolved',
    trend: 'Recurring',
    knowledge: {
      rootCause:
        'Downstream inventory service times out under load, causing the consumer to abandon the message repeatedly until MaxDeliveryCount is exceeded.',
      resolutionNotes:
        'Increased consumer visibility timeout and added exponential backoff; downstream inventory service was also scaled out.',
      operationalNotes: 'Recurs during flash-sale traffic spikes — watch during planned promotions.',
      runbookLink: 'https://wiki.example.com/runbooks/max-delivery-count',
      owner: 'platform-team@example.com',
      replayGuidance: 'Safe',
      lastUpdatedAt: new Date(Date.now() - 3 * DAY_MS).toISOString(),
      knowledgeVersion: 3,
      reviewDueAt: new Date(Date.now() + 30 * DAY_MS).toISOString(),
      tags: 'delivery,timeout,inventory',
      updatedBy: 'alice@example.com',
      isReviewOverdue: false,
    },
    replayHistory: [
      {
        id: 'demo-replay-job-max-delivery-1',
        operationType: 'Replay',
        status: 'Completed',
        namespaceId: '',
        namespaceDisplayName: '',
        entityNameFilter: null,
        statusFilter: null,
        categoryFilter: null,
        from: null,
        to: null,
        totalMatched: 42,
        processedCount: 42,
        successCount: 42,
        failureCount: 0,
        skippedCount: 0,
        failureSample: null,
        errorSummary: null,
        createdAt: new Date(Date.now() - 3 * DAY_MS).toISOString(),
        startedAt: new Date(Date.now() - 3 * DAY_MS).toISOString(),
        completedAt: new Date(Date.now() - 3 * DAY_MS + 5 * 60_000).toISOString(),
        isCancellable: false,
      },
    ],
    rootCauseMatches: [],
    recentChanges: [
      {
        id: 'demo-audit-max-delivery-1',
        timestamp: new Date(Date.now() - 18 * DAY_MS - 5 * 60 * 60 * 1000).toISOString(),
        userIdentity: 'alice@example.com',
        action: 'Rule.Toggle',
        outcome: 'Success',
        namespaceId: null,
        namespaceName: null,
        entityName: null,
        cloudProvider: null,
        environment: null,
        resourceName: 'Retry-on-timeout',
        sequenceNumber: null,
        detailsJson: null,
        errorDetails: null,
        clientIp: null,
        userAgent: null,
        correlationId: null,
        httpMethod: null,
        httpPath: null,
      },
      {
        id: 'demo-audit-max-delivery-2',
        timestamp: new Date(Date.now() - 18 * DAY_MS - 16 * 60 * 60 * 1000).toISOString(),
        userIdentity: 'bob@example.com',
        action: 'Namespace.Create',
        outcome: 'Success',
        namespaceId: null,
        namespaceName: null,
        entityName: null,
        cloudProvider: null,
        environment: null,
        resourceName: 'prod-orders-eastus',
        sequenceNumber: null,
        detailsJson: null,
        errorDetails: null,
        clientIp: null,
        userAgent: null,
        correlationId: null,
        httpMethod: null,
        httpPath: null,
      },
    ],
  },
  {
    hash: 'demo-poison-message',
    dominantDeadletterReason: 'PoisonMessage',
    topTerms: ['order-payload', 'schema-violation', 'sku'],
    explanation: 'A single malformed message crashes the consumer on every delivery attempt.',
    size: 7,
    isNew: false,
    daysSinceFirstSeen: 9,
    status: 'Reopened',
    trend: 'Escalating',
    knowledge: {
      rootCause: 'A malformed order payload (missing required `sku` field) crashes the consumer on every delivery attempt.',
      resolutionNotes: 'Added schema validation at the producer boundary; the malformed payload was manually purged.',
      operationalNotes: 'Reopened — a second, similarly malformed payload was seen from a different producer.',
      runbookLink: 'https://wiki.example.com/runbooks/poison-message',
      owner: 'checkout-team@example.com',
      replayGuidance: 'Unsafe',
      lastUpdatedAt: new Date(Date.now() - 1 * DAY_MS).toISOString(),
      knowledgeVersion: 2,
      reviewDueAt: new Date(Date.now() - 5 * DAY_MS).toISOString(),
      tags: 'poison,schema,validation',
      updatedBy: 'bob@example.com',
      isReviewOverdue: true,
    },
    replayHistory: [
      {
        id: 'demo-replay-job-poison-message-1',
        operationType: 'Replay',
        status: 'Failed',
        namespaceId: '',
        namespaceDisplayName: '',
        entityNameFilter: null,
        statusFilter: null,
        categoryFilter: null,
        from: null,
        to: null,
        totalMatched: 7,
        processedCount: 1,
        successCount: 0,
        failureCount: 1,
        skippedCount: 0,
        failureSample: [
          { messageId: 'msg-poison-1', entityName: 'orders-processing', reason: 'Schema violation: missing sku field' },
        ],
        errorSummary: 'Replay rejected — signature is marked Unsafe for replay',
        createdAt: new Date(Date.now() - 1 * DAY_MS).toISOString(),
        startedAt: new Date(Date.now() - 1 * DAY_MS).toISOString(),
        completedAt: new Date(Date.now() - 1 * DAY_MS + 60_000).toISOString(),
        isCancellable: false,
      },
    ],
    rootCauseMatches: [],
    recentChanges: [],
  },
  {
    hash: 'demo-deserialization-failure',
    dominantDeadletterReason: 'DeserializationError',
    topTerms: ['schema-version', 'json', 'consumer-mismatch'],
    explanation: 'The producer emitted a message shape the consumer cannot deserialize.',
    size: 15,
    isNew: true,
    daysSinceFirstSeen: 1,
    status: 'Active',
    trend: 'New',
    knowledge: {
      rootCause:
        'Producer upgraded to a new message schema version without a compatible consumer deployed, so JSON deserialization throws on the new field shape.',
      resolutionNotes: null,
      operationalNotes: 'Under investigation — coordinating a compatible consumer rollout with the producer team.',
      runbookLink: null,
      owner: 'data-platform@example.com',
      replayGuidance: 'Investigate',
      lastUpdatedAt: new Date(Date.now() - 4 * 60 * 60 * 1000).toISOString(),
      knowledgeVersion: 1,
      reviewDueAt: null,
      tags: 'schema,deserialization',
      updatedBy: null,
      isReviewOverdue: false,
    },
    replayHistory: [],
    rootCauseMatches: [
      {
        namespaceId: 'demo-gcp-medstream-staging',
        occurrenceCount: 9,
        firstSeenAt: new Date(Date.now() - 60 * DAY_MS).toISOString(),
        lastSeenAt: new Date(Date.now() - 55 * DAY_MS).toISOString(),
        lifecycleStatus: 'Resolved',
        knowledge: {
          rootCause:
            'Producer upgraded to a new message schema version without a compatible consumer deployed, so JSON deserialization throws on the new field shape.',
          resolutionNotes: 'Pinned the consumer to the prior schema version until the compatible rollout shipped; then upgraded both together.',
          operationalNotes: null,
          runbookLink: 'https://wiki.example.com/runbooks/schema-deserialization',
          owner: 'data-platform@example.com',
          replayGuidance: 'Safe',
          lastUpdatedAt: new Date(Date.now() - 55 * DAY_MS).toISOString(),
          knowledgeVersion: 2,
          reviewDueAt: null,
          tags: 'schema,deserialization',
          updatedBy: 'data-platform@example.com',
          isReviewOverdue: false,
        },
        lastReplayOutcome: {
          status: 'Completed',
          createdAt: new Date(Date.now() - 54 * DAY_MS).toISOString(),
        },
      },
    ],
    recentChanges: [
      {
        id: 'demo-audit-deserialization-1',
        timestamp: new Date(Date.now() - 1 * DAY_MS - 12 * 60 * 60 * 1000).toISOString(),
        userIdentity: 'data-platform@example.com',
        action: 'Rule.Create',
        outcome: 'Success',
        namespaceId: null,
        namespaceName: null,
        entityName: null,
        cloudProvider: null,
        environment: null,
        resourceName: 'Schema-version-gate',
        sequenceNumber: null,
        detailsJson: null,
        errorDetails: null,
        clientIp: null,
        userAgent: null,
        correlationId: null,
        httpMethod: null,
        httpPath: null,
      },
    ],
  },
  {
    hash: 'demo-authentication-failure',
    dominantDeadletterReason: 'AuthenticationFailure',
    topTerms: ['managed-identity', 'unauthorized', 'role-assignment'],
    explanation: "The consumer's identity was rejected by a downstream dependency on every call.",
    size: 63,
    isNew: false,
    daysSinceFirstSeen: 41,
    status: 'Resolved',
    trend: 'Recurring',
    knowledge: {
      rootCause:
        "Consumer's managed identity role assignment was revoked during a permissions audit, causing every downstream call to fail with 401.",
      resolutionNotes: 'Role assignment restored; added an alert on identity permission changes for this app.',
      operationalNotes: 'Caused a full processing outage for ~40 minutes.',
      runbookLink: 'https://wiki.example.com/runbooks/auth-failure',
      owner: 'security-team@example.com',
      replayGuidance: 'Safe',
      lastUpdatedAt: new Date(Date.now() - 20 * DAY_MS).toISOString(),
      knowledgeVersion: 2,
      reviewDueAt: new Date(Date.now() + 14 * DAY_MS).toISOString(),
      tags: 'auth,identity,security',
      updatedBy: 'security-team@example.com',
      isReviewOverdue: false,
    },
    replayHistory: [],
    rootCauseMatches: [],
    recentChanges: [],
  },
  {
    hash: 'demo-duplicate-detection',
    dominantDeadletterReason: 'DuplicateMessage',
    topTerms: ['retry', 'duplicate-window', 'idempotency'],
    explanation: 'Retried messages are arriving faster than the duplicate-detection window allows.',
    size: 28,
    isNew: false,
    daysSinceFirstSeen: 25,
    status: 'Suppressed',
    trend: 'Recurring',
    knowledge: {
      rootCause:
        'Producer retry logic re-sends the same message on ambiguous network timeouts, and the duplicate-detection window was shorter than the retry interval.',
      resolutionNotes: 'Extended the duplicate-detection window from 10 minutes to 1 hour.',
      operationalNotes: 'Known noisy signature — suppressed rather than resolved since occasional duplicates are expected and harmless.',
      runbookLink: 'https://wiki.example.com/runbooks/duplicate-detection',
      owner: 'platform-team@example.com',
      replayGuidance: 'Safe',
      lastUpdatedAt: new Date(Date.now() - 10 * DAY_MS).toISOString(),
      knowledgeVersion: 2,
      reviewDueAt: new Date(Date.now() + 60 * DAY_MS).toISOString(),
      tags: 'duplicate,idempotency,retry',
      updatedBy: 'alice@example.com',
      isReviewOverdue: false,
    },
    replayHistory: [],
    rootCauseMatches: [],
    recentChanges: [],
  },
];

function buildDemoCluster(def: DemoSignatureDefinition): DlqClusterSignature {
  const firstSeenAt = new Date(Date.now() - def.daysSinceFirstSeen * DAY_MS);
  const windowEnd = new Date();
  return {
    size: def.size,
    messageIds: Array.from({ length: Math.min(def.size, 5) }, (_, i) => 900000 + i),
    dominantEntity: 'orders-processing',
    dominantDeadletterReason: def.dominantDeadletterReason,
    dominantDeadletterReasonCount: def.size,
    topTerms: def.topTerms,
    isNew: def.isNew,
    firstSeenAt: firstSeenAt.toISOString(),
    occurrenceCount: def.size,
    windowStart: firstSeenAt.toISOString(),
    windowEnd: windowEnd.toISOString(),
    explanation: def.explanation,
    knowledge: def.knowledge,
    signatureHash: def.hash,
    status: def.status,
    trend: def.trend,
  };
}

let demoClusterCache: DlqClusterSignature[] | null = null;

function getDemoClusters(): DlqClusterSignature[] {
  if (!demoClusterCache) {
    demoClusterCache = DEMO_SIGNATURE_DEFS.map(buildDemoCluster);
  }
  return demoClusterCache;
}

/**
 * Get a namespace's mock DLQ failure signatures. Identical curated set across
 * providers — read-only, matching the rest of Demo Mode.
 */
export function getMockDlqSignatures(_provider: CloudProviderType): DlqSignaturesResponse {
  return {
    available: true,
    method: 'demo',
    batchSize: 200,
    clusters: getDemoClusters(),
    singletons: [],
  };
}

/**
 * Get full mock detail for a single demo failure signature, or undefined if the
 * hash doesn't match one of the curated demo signatures.
 */
export function getMockDlqSignatureDetail(
  provider: CloudProviderType,
  signatureHash: string,
): DlqSignatureDetail | undefined {
  const cluster = getDemoClusters().find((c) => c.signatureHash === signatureHash);
  if (!cluster) return undefined;

  return {
    ...cluster,
    namespaceId: DEMO_NAMESPACE_IDS[provider],
    confidence: 'High',
    isCurrentlyClustered: true,
    relatedMessages: [],
  };
}

/**
 * Get a mock lifecycle timeline for a demo failure signature, or undefined if the
 * hash doesn't match one of the curated demo signatures.
 */
export function getMockSignatureTimeline(signatureHash: string): SignatureTimelineResponse | undefined {
  const cluster = getDemoClusters().find((c) => c.signatureHash === signatureHash);
  if (!cluster) return undefined;

  const events: DlqTimelineEvent[] = [
    {
      eventType: 'SignatureFirstObserved',
      description: 'Signature first observed in this namespace\'s DLQ',
      timestamp: cluster.firstSeenAt,
      details: null,
    },
  ];

  if (cluster.knowledge?.lastUpdatedAt) {
    events.push({
      eventType: 'KnowledgeRecorded',
      description: 'Operational knowledge recorded for this signature',
      timestamp: cluster.knowledge.lastUpdatedAt,
      details: null,
    });
  }

  if (cluster.status !== 'Active') {
    events.push({
      eventType: 'StatusChanged',
      description: `Status changed to ${cluster.status}`,
      timestamp: cluster.windowEnd,
      details: { From: 'Active', To: cluster.status, Notes: '' },
    });
  }

  return {
    signatureHash,
    events: events.sort((a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime()),
  };
}

/**
 * Get a mock replay-job history page for a demo failure signature, or undefined if the hash
 * doesn't match one of the curated demo signatures. Backs the Replay Safety & History panel in
 * demo mode, where real replay mutations reject but this read-only history list still renders.
 */
export function getMockSignatureReplayHistory(signatureHash: string): PaginatedBulkOperationJobs | undefined {
  const def = DEMO_SIGNATURE_DEFS.find((d) => d.hash === signatureHash);
  if (!def) return undefined;

  return {
    items: def.replayHistory,
    totalCount: def.replayHistory.length,
    page: 1,
    pageSize: 20,
    hasNextPage: false,
    hasPreviousPage: false,
  };
}

/**
 * Root Cause Explorer, demo mode: this signature's occurrences in other (fictional) namespaces
 * in the fleet, or undefined if the hash doesn't match one of the curated demo signatures.
 * Demo mode simulates a single namespace per cloud provider, so most demo signatures have no
 * fleet matches — one curated signature (`demo-deserialization-failure`) has a fixture match to
 * demonstrate the populated state.
 */
export function getMockRootCauseMatches(signatureHash: string): RootCauseExplorerResponse | undefined {
  const def = DEMO_SIGNATURE_DEFS.find((d) => d.hash === signatureHash);
  if (!def) return undefined;

  return {
    signatureHash: def.hash,
    dominantDeadletterReason: def.dominantDeadletterReason,
    topTerms: def.topTerms,
    totalOccurrencesAcrossFleet: def.size + def.rootCauseMatches.reduce((sum, m) => sum + m.occurrenceCount, 0),
    matches: def.rootCauseMatches,
  };
}

/**
 * Recent Changes Before Failure, demo mode: this signature's fixture audit entries in the 24h
 * before its firstSeenAt, or undefined if the hash doesn't match one of the curated demo
 * signatures. Backs the Recent Changes Before Failure panel in demo mode, where `useAuditLogs`
 * is disabled entirely (`enabled: !isDemoMode`) — most demo signatures have no fixture changes,
 * matching `rootCauseMatches`' mostly-empty curated pattern.
 */
export function getMockRecentChanges(signatureHash: string): AuditPageResponse | undefined {
  const def = DEMO_SIGNATURE_DEFS.find((d) => d.hash === signatureHash);
  if (!def) return undefined;

  return {
    items: def.recentChanges,
    totalCount: def.recentChanges.length,
    page: 1,
    pageSize: 20,
    hasNextPage: false,
    hasPreviousPage: false,
  };
}

// ─── Fleet Health ────────────────────────────────────────────────────────────
// Demo mode simulates a single namespace per cloud provider, so fleet-wide health collapses to
// that one namespace's rollup — derived from the same curated signature fixtures backing
// Signature List/Details, so the two surfaces never disagree in demo mode.

function buildDemoFleetNamespaceHealth(provider: CloudProviderType): FleetNamespaceHealth {
  const clusters = getDemoClusters();
  const activeClusters = clusters.filter((c) => c.status === 'Active' || c.status === 'Reopened');
  const activeCount = activeClusters.reduce((sum, c) => sum + c.size, 0);
  const newInWindow = clusters.filter((c) => c.isNew).reduce((sum, c) => sum + c.size, 0);
  const totalCount = clusters.reduce((sum, c) => sum + c.size, 0);
  const topActive = [...activeClusters].sort((a, b) => b.size - a.size)[0] ?? null;
  const oldestActive =
    [...activeClusters].sort((a, b) => new Date(a.firstSeenAt).getTime() - new Date(b.firstSeenAt).getTime())[0] ??
    null;
  // Mirrors FleetOverviewService.DetermineSeverity's thresholds (services/api).
  const severity: FleetHealthSeverity =
    newInWindow >= 10 || activeCount >= 50 ? 'critical' : activeCount > 0 || newInWindow > 0 ? 'warning' : 'healthy';
  const namespace = getMockNamespaces(provider)[0];

  return {
    namespaceId: namespace.id,
    namespaceName: namespace.displayName ?? namespace.name,
    provider,
    environment: namespace.environment ?? 'prod',
    activeCount,
    newInWindow,
    resolvedInWindow: 0,
    totalCount,
    topEntity: topActive?.dominantEntity ?? null,
    topEntityCount: topActive?.size ?? 0,
    topCategory: topActive?.dominantDeadletterReason ?? null,
    oldestActiveDetectedAt: oldestActive?.firstSeenAt ?? null,
    severity,
    // Demo Mode simulates a fully-monitored namespace for every provider — there is no real scan
    // to be honest about here, unlike the live FleetOverviewService.
    coverage: 'scanned',
    coverageNote: null,
  };
}

/** Get the mock cross-namespace fleet overview for the standalone Fleet page. */
export function getMockFleetOverview(provider: CloudProviderType): FleetOverview {
  const nsHealth = buildDemoFleetNamespaceHealth(provider);
  const topCategories = getDemoClusters()
    .filter((c) => c.status === 'Active' || c.status === 'Reopened')
    .reduce<Record<string, number>>((acc, c) => {
      acc[c.dominantDeadletterReason] = (acc[c.dominantDeadletterReason] ?? 0) + c.size;
      return acc;
    }, {});

  return {
    generatedAt: new Date().toISOString(),
    windowHours: 24,
    namespaceCount: 1,
    totalActive: nsHealth.activeCount,
    totalNewInWindow: nsHealth.newInWindow,
    totalResolvedInWindow: nsHealth.resolvedInWindow,
    namespaces: [nsHealth],
    topCategories,
    dailyTrend: [],
  };
}

// ─── Investigation Center (Incident Center) ─────────────────────────────────

/**
 * Get the mock Incident Center payload — derived entirely from the same curated
 * `DEMO_SIGNATURE_DEFS` fixtures backing Signature List/Details, so the two surfaces never
 * disagree in demo mode.
 */
export function getMockInvestigationQueue(provider: CloudProviderType): InvestigationCenterResponse {
  const namespaceId = DEMO_NAMESPACE_IDS[provider];
  const clusters = getDemoClusters();
  const displayName = (c: DlqClusterSignature) => `${c.dominantDeadletterReason} · ${c.dominantEntity}`;

  const metrics: CompactMetricsSummary = {
    totalSignatures: clusters.length,
    activeSignatures: clusters.filter((c) => c.status === 'Active' || c.status === 'Reopened').length,
    resolvedSignatures: clusters.filter((c) => c.status === 'Resolved').length,
    suppressedSignatures: clusters.filter((c) => c.status === 'Suppressed').length,
    archivedSignatures: clusters.filter((c) => c.status === 'Archived').length,
    requiresAction: clusters.filter((c) => c.status === 'Active' || c.status === 'Reopened').length,
  };

  const investigationQueue: InvestigationQueueItem[] = clusters
    .filter((c) => c.status === 'Active' || c.status === 'Reopened')
    .map((c) => ({
      signatureHash: c.signatureHash,
      namespaceId,
      displayName: displayName(c),
      dominantDeadletterReason: c.dominantDeadletterReason,
      messageCount: c.size,
      status: c.status,
      trend: c.trend,
      priorityScore: c.trend === 'Escalating' ? 18 : c.isNew ? 8 : 5,
      hasKnowledge: c.knowledge != null,
      isEscalating: c.trend === 'Escalating',
      owner: c.knowledge?.owner ?? null,
      recommendedNextAction:
        c.trend === 'Escalating'
          ? 'Escalating — review Replay Safety before replaying.'
          : 'New signature — record root-cause knowledge.',
      explanation: c.explanation,
    }))
    .sort((a, b) => b.priorityScore - a.priorityScore);

  const failedReplays: FailedReplayItem[] = DEMO_SIGNATURE_DEFS.flatMap((def) =>
    def.replayHistory
      .filter((job) => job.status === 'Failed')
      .map((job) => ({
        jobId: job.id,
        namespaceId,
        signatureHash: def.hash,
        signatureName: `${def.dominantDeadletterReason} · orders-processing`,
        jobStatus: job.status,
        failureReason: job.errorSummary,
        createdAt: job.createdAt,
        completedAt: job.completedAt,
        attemptedCount: job.processedCount,
        failedCount: job.failureCount,
        recommendedNextAction: 'Review Replay Safety before retrying.',
      })),
  );

  const knowledgeReview: KnowledgeReviewItem[] = DEMO_SIGNATURE_DEFS.filter(
    (def) => def.knowledge?.isReviewOverdue,
  ).map((def) => {
    const cluster = clusters.find((c) => c.signatureHash === def.hash)!;
    return {
      signatureHash: def.hash,
      namespaceId,
      displayName: displayName(cluster),
      messageCount: def.size,
      status: def.status,
      owner: def.knowledge?.owner ?? null,
      hasKnowledge: def.knowledge != null,
      isReviewOverdue: true,
      reviewDueAt: def.knowledge?.reviewDueAt ?? null,
      lastUpdatedAt: def.knowledge?.lastUpdatedAt ?? null,
      recommendedNextAction: 'Knowledge review is overdue — confirm root cause is still accurate.',
    };
  });

  const newSignatures: NewSignatureItem[] = DEMO_SIGNATURE_DEFS.filter((def) => def.isNew).map((def) => {
    const cluster = clusters.find((c) => c.signatureHash === def.hash)!;
    return {
      signatureHash: def.hash,
      namespaceId,
      displayName: displayName(cluster),
      dominantDeadletterReason: def.dominantDeadletterReason,
      messageCount: def.size,
      firstSeenAt: cluster.firstSeenAt,
      lastSeenAt: cluster.windowEnd,
      explanation: def.explanation,
      recommendedNextAction: 'No knowledge on file yet — record root cause.',
    };
  });

  const nsHealth = buildDemoFleetNamespaceHealth(provider);

  return {
    metrics,
    investigationQueue,
    failedReplays,
    knowledgeReview,
    newSignatures,
    recentlyChanged: [],
    fleetHealth: {
      namespaceCount: 1,
      totalActive: nsHealth.activeCount,
      totalNewInWindow: nsHealth.newInWindow,
      totalResolvedInWindow: nsHealth.resolvedInWindow,
      topUnhealthyNamespaces: nsHealth.severity === 'healthy' ? [] : [nsHealth],
    },
  };
}

// ─── Auto Replay Rules ───────────────────────────────────────────────────────
// No auto-replay rules are pre-configured in demo mode — this returns the real empty list so
// the Rules page renders its own empty state instead of a query that never runs.
export function getMockRules(): RuleResponse[] {
  return [];
}

// ─── DLQ History ─────────────────────────────────────────────────────────────
// Demo mode's DLQ story lives in Signature List/Details' curated clusters, not a separate
// per-message history feed — this returns an empty page so the DLQ History table renders its
// real empty state instead of a query that never runs.
export function getMockDlqHistory(): DlqHistoryPage<DlqHistoryItem> {
  return { items: [], totalCount: 0, page: 1, pageSize: 20, hasNextPage: false, hasPreviousPage: false };
}

// ─── Audit Trail ─────────────────────────────────────────────────────────────
// General namespace-wide audit log, distinct from `getMockRecentChanges`' per-signature
// pre-failure window — no fixture audit trail beyond the curated `recentChanges` entries exists
// yet, so this returns an empty page so the Audit page renders its real empty state.
export function getMockAuditLogs(): AuditPageResponse {
  return { items: [], totalCount: 0, page: 1, pageSize: 20, hasNextPage: false, hasPreviousPage: false };
}

// ─── Recovery Evidence Ledger ────────────────────────────────────────────────
//
// One curated story (roadmap §20): 214 messages dead-lettered on `orders-processing` from a
// downstream timeout. Operator alex@contoso.com replays them with reason "Downstream payment
// API restored — INC-4471". Outcomes are chosen to demonstrate honesty rather than success —
// Unverified is a first-class, non-apologetic result here, not an embarrassment hidden from the
// demo. Every exported artefact built from this fixture carries `demoMode: true` and a
// `demo-`-prefixed filename (roadmap principle: Demo Mode never presents fabricated data as real
// evidence).

export const DEMO_RECOVERY_OPERATION_ID = 'demo-recovery-op-orders-processing';
const DEMO_RECOVERY_ACTOR = 'alex@contoso.com';
const DEMO_RECOVERY_REASON = 'Downstream payment API restored — INC-4471';
const DEMO_RECOVERY_ENTITY = 'orders-processing';
const DEMO_RECOVERY_OPENED_AT = '2026-08-10T09:14:00Z';

interface DemoRecoverySpec {
  count: number;
  state: RecoveryEntryState;
  disposition: string;
  verificationResult: RecoveryLedgerEntry['verificationResult'];
  verificationConfidence: RecoveryLedgerEntry['verificationConfidence'];
  provider: string;
  markerApplied: boolean;
}

const DEMO_RECOVERY_SPECS: DemoRecoverySpec[] = [
  { count: 187, state: 'Recovered', disposition: 'Recovered', verificationResult: 'Recovered', verificationConfidence: null, provider: 'azure', markerApplied: true },
  { count: 11, state: 'Returned', disposition: 'Returned', verificationResult: 'Returned', verificationConfidence: 'Exact', provider: 'azure', markerApplied: true },
  { count: 14, state: 'Unverified', disposition: 'Unverified', verificationResult: 'Unverified', verificationConfidence: null, provider: 'aws', markerApplied: false },
  { count: 2, state: 'ExecutionFailed', disposition: 'Failed', verificationResult: null, verificationConfidence: null, provider: 'azure', markerApplied: true },
];

function buildDemoRecoveryEntries(operationId: string): RecoveryLedgerEntry[] {
  const entries: RecoveryLedgerEntry[] = [];
  let index = 0;
  for (const spec of DEMO_RECOVERY_SPECS) {
    for (let i = 0; i < spec.count; i++) {
      index += 1;
      const begunAt = new Date(new Date(DEMO_RECOVERY_OPENED_AT).getTime() + index * 15_000).toISOString();
      entries.push({
        id: `${operationId}-entry-${index}`,
        operationId,
        dlqMessageId: 90_000 + index,
        namespaceId: DEMO_NAMESPACE_IDS[spec.provider as CloudProviderType],
        namespaceNameSnapshot: spec.provider === 'aws' ? 'acme-prod (AWS)' : 'contoso-prod (Azure)',
        providerSnapshot: spec.provider,
        environmentSnapshot: 'prod',
        entityNameSnapshot: DEMO_RECOVERY_ENTITY,
        entityTypeSnapshot: 'Queue',
        topicNameSnapshot: null,
        bodyHash: `sha256-demo-${index.toString(16).padStart(8, '0')}`,
        failureCategorySnapshot: 'TransientDependency',
        deadLetterReasonSnapshot: 'MaxDeliveryCountExceeded',
        signatureHashSnapshot: null,
        targetEntity: DEMO_RECOVERY_ENTITY,
        begunAt,
        markerApplied: spec.markerApplied,
        state: spec.state,
        disposition: spec.disposition,
        verificationResult: spec.verificationResult,
        verificationConfidence: spec.verificationConfidence,
        observationWindowEndsAt: spec.state === 'Executing' || spec.state === 'Observing'
          ? new Date(new Date(begunAt).getTime() + 24 * 3_600_000).toISOString()
          : null,
        closedAt: spec.state === 'Executing' || spec.state === 'Observing' ? null : begunAt,
      });
    }
  }
  return entries;
}

function buildDemoRecoveryOperation(): RecoveryOperation {
  return {
    id: DEMO_RECOVERY_OPERATION_ID,
    kind: 'Replay',
    trigger: 'Manual',
    actorIdentity: DEMO_RECOVERY_ACTOR,
    actorKind: 'User',
    reason: DEMO_RECOVERY_REASON,
    namespaceId: DEMO_NAMESPACE_IDS.azure,
    namespaceNameSnapshot: 'contoso-prod (Azure)',
    providerSnapshot: 'azure',
    environmentSnapshot: 'prod',
    scopeDescription: `entity=${DEMO_RECOVERY_ENTITY}; category=TransientDependency`,
    sourceRuleId: null,
    sourceJobId: null,
    serviceVersion: '3.7.0',
    openedAt: DEMO_RECOVERY_OPENED_AT,
    targetCount: 214,
    entryCount: 214,
  };
}

function buildDemoRecoveryEvents(operationId: string, entries: RecoveryLedgerEntry[]): RecoveryEvent[] {
  let seq = 1;
  const events: RecoveryEvent[] = [];
  const push = (entryId: string | null, eventType: string, occurredAt: string, detailJson: string | null = null) => {
    events.push({
      id: `${operationId}-event-${seq}`,
      ownerId: 'demo-owner',
      seq,
      entryId,
      operationId,
      eventType,
      occurredAt,
      actorIdentity: DEMO_RECOVERY_ACTOR,
      actorKind: 'User',
      detailJson,
      prevHash: seq === 1 ? '0'.repeat(64) : `demo-hash-${seq - 1}`,
      entryHash: `demo-hash-${seq}`,
      schemaVersion: 1,
    });
    seq += 1;
  };

  push(null, 'OperationOpened', DEMO_RECOVERY_OPENED_AT);
  for (const entry of entries) {
    push(entry.id, 'EntryBegun', entry.begunAt);
    push(entry.id, entry.state === 'ExecutionFailed' ? 'ProviderRejected' : 'ProviderAccepted', entry.begunAt);
    if (entry.state === 'Returned') {
      push(entry.id, 'RecurrenceObserved', entry.closedAt ?? entry.begunAt);
    } else if (entry.state === 'Recovered') {
      push(entry.id, 'NoRecurrenceObserved', entry.closedAt ?? entry.begunAt);
    } else if (entry.state === 'Unverified') {
      // All Unverified demo entries are AWS (DEMO_RECOVERY_SPECS) — a real, accurate reason
      // code for this fixture, not an invented one, so Demo Mode exercises the same honesty
      // path a real AWS deployment hits (docs/RECOVERY-EVIDENCE.md §4).
      push(entry.id, 'ObservationUnavailable', entry.closedAt ?? entry.begunAt, JSON.stringify({ reason: 'AWS_NO_ABSENCE_PROOF' }));
    }
  }
  return events;
}

const DEMO_STUCK_OPERATION_ID = 'demo-recovery-op-payments-reconciliation';
const DEMO_STUCK_ENTITY = 'payments-reconciliation';
const DEMO_STUCK_BEGUN_AT = new Date(Date.now() - 12 * 86_400_000).toISOString();

function buildDemoStuckOperation(): RecoveryOperation {
  return {
    id: DEMO_STUCK_OPERATION_ID,
    kind: 'Replay',
    trigger: 'Manual',
    actorIdentity: DEMO_RECOVERY_ACTOR,
    actorKind: 'User',
    reason: 'Retrying after a transient gateway timeout — INC-4502',
    namespaceId: DEMO_NAMESPACE_IDS.azure,
    namespaceNameSnapshot: 'contoso-prod (Azure)',
    providerSnapshot: 'azure',
    environmentSnapshot: 'prod',
    scopeDescription: `entity=${DEMO_STUCK_ENTITY}; category=TransientDependency`,
    sourceRuleId: null,
    sourceJobId: null,
    serviceVersion: '3.7.0',
    openedAt: DEMO_STUCK_BEGUN_AT,
    targetCount: 1,
    entryCount: 1,
  };
}

function buildDemoStuckEntry(): RecoveryLedgerEntry {
  return {
    id: 'demo-stuck-entry-1',
    operationId: DEMO_STUCK_OPERATION_ID,
    dlqMessageId: 80_001,
    namespaceId: DEMO_NAMESPACE_IDS.azure,
    namespaceNameSnapshot: 'contoso-prod (Azure)',
    providerSnapshot: 'azure',
    environmentSnapshot: 'prod',
    entityNameSnapshot: DEMO_STUCK_ENTITY,
    entityTypeSnapshot: 'Queue',
    topicNameSnapshot: null,
    bodyHash: 'sha256-demo-stuck-0001',
    failureCategorySnapshot: 'TransientDependency',
    deadLetterReasonSnapshot: 'MaxDeliveryCountExceeded',
    signatureHashSnapshot: null,
    targetEntity: DEMO_STUCK_ENTITY,
    begunAt: DEMO_STUCK_BEGUN_AT,
    markerApplied: true,
    // ExecutionUnknown — the process died mid-call (roadmap §7.1) and no one has resolved it
    // since. This is exactly the kind of entry the ageing worker exists to surface: still
    // non-terminal, past the default 7-day threshold, waiting for an operator to write it off
    // or for it to resolve on its own.
    state: 'ExecutionUnknown',
    disposition: null,
    verificationResult: null,
    verificationConfidence: null,
    observationWindowEndsAt: null,
    closedAt: null,
  };
}

/** Demo Mode fixture for `GET /recovery/operations` — the curated story plus the stuck-entry demo. */
export function getMockRecoveryOperations(): RecoveryOperation[] {
  return [buildDemoRecoveryOperation(), buildDemoStuckOperation()];
}

/**
 * Demo Mode fixture for `GET /recovery/operations/{id}` — the full curated story by default, or
 * the single stuck-entry operation when `operationId` names it (the ageing report's "View
 * operation" link, and the `11-recovery-ageing` e2e spec, both navigate there).
 */
export function getMockRecoveryOperationDetail(operationId?: string): RecoveryOperationDetail {
  if (operationId === DEMO_STUCK_OPERATION_ID) {
    const operation = buildDemoStuckOperation();
    const entries = [buildDemoStuckEntry()];
    const events = buildDemoRecoveryEvents(operation.id, entries);
    return { operation, entries, events };
  }

  const operation = buildDemoRecoveryOperation();
  const entries = buildDemoRecoveryEntries(operation.id);
  const events = buildDemoRecoveryEvents(operation.id, entries);
  return { operation, entries, events };
}

/**
 * Demo Mode fixture for `GET /recovery/ageing` — one entry: an `ExecutionUnknown` replay from an
 * unrelated, smaller incident that has sat open for 12 days. The curated 214-message story's
 * entries all reach a terminal state by design (roadmap §20's demonstrate-honesty story), so a
 * genuinely open entry needs its own, separately-reasoned fixture rather than an arbitrary one
 * invented just to populate this page.
 */
export function getMockRecoveryAgeing(): RecoveryLedgerEntry[] {
  return [buildDemoStuckEntry()];
}

/**
 * Builds a watermarked evidence export entirely client-side, from fixture data only — Demo Mode
 * makes no backend calls (CLAUDE.md), so the real `IRecoveryEvidenceExporter` is never involved.
 * Every field the real manifest carries is present, plus `demoMode: true`, so nothing here can be
 * mistaken for real evidence if it leaves the browser. `format=package` is not a real zip in Demo
 * Mode (no client zip dependency exists in this repo) — it downloads the same bundle with a
 * `.json` extension, which the filename and content both disclose.
 */
export function buildDemoRecoveryExportBundle(operationId?: string): { filename: string; content: string; mimeType: string } {
  const { operation, entries, events } = getMockRecoveryOperationDetail(operationId);
  const counts: Record<string, number> = {};
  for (const entry of entries) {
    counts[entry.state] = (counts[entry.state] ?? 0) + 1;
  }

  const manifest = {
    schemaVersion: 1,
    serviceVersion: operation.serviceVersion,
    exportedAt: new Date().toISOString(),
    exportedBy: 'demo-mode',
    ownerId: 'demo-owner',
    operationId: operation.id,
    demoMode: true,
    chain: {
      scope: 'owner',
      firstSeq: events[0]?.seq ?? 0,
      lastSeq: events[events.length - 1]?.seq ?? 0,
      verified: true,
      algorithm: 'SHA-256 over pipe-delimited canonical fields plus PrevHash (see docs/RECOVERY-EVIDENCE.md)',
      tamperEvidentNotTamperProof: true,
    },
    counts,
    whatServiceHubKnows: [
      'Who initiated each recovery decision and under what stated reason',
      'What ServiceHub asked the provider to do, and the provider\'s response',
    ],
    whatServiceHubObserved: [
      'Whether a message carrying this operation\'s recovery marker re-entered the dead-letter queue',
      'The scan coverage over each entry\'s observation window',
    ],
    whatServiceHubDoesNotKnow: [
      'Whether any consumer processed the message successfully',
      'Whether the corresponding business transaction completed',
      'Anything about messages removed by systems other than ServiceHub',
      'This entire export is Demo Mode fixture data — it does not describe any real recovery',
    ],
    limitations: [
      {
        code: 'DEMO_DATA_NOT_REAL_EVIDENCE',
        appliesToEntries: entries.length,
        text: 'This package was generated from Demo Mode fixture data, not the Recovery Evidence Ledger. It does not describe any real recovery operation.',
      },
    ],
  };

  const bundle = { manifest, operation, entries, events };
  return {
    filename: `demo-recovery-evidence-${operation.id}.json`,
    content: JSON.stringify(bundle, null, 2),
    mimeType: 'application/json',
  };
}
