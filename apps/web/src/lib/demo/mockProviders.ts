/**
 * mockProviders.ts — Unified Demo Data Adapter
 *
 * Normalizes cloud-specific mock data (Azure / AWS / GCP) into the same
 * API response shapes used by the real backend. This lets all existing hooks
 * and pages consume mock data without any changes to their rendering logic.
 *
 * Each function returns data matching the exact TypeScript types from
 * @/lib/api/types so the real pages work identically in demo mode.
 */

import type { CloudProviderType, Namespace, Queue, Topic, Message as APIMessage, PaginatedResponse } from '@/lib/api/types';
import type { Subscription } from '@/hooks/useSubscriptions';
import { generateAzureMockMessages, AZURE_QUEUES, AZURE_TOPICS } from '@/lib/azureMockData';
import { generateAwsMockMessages } from '@/lib/awsMockData';
import { generateGcpMockMessages } from '@/lib/gcpMockData';
import type { Message as MockMessage } from '@/lib/mockData';

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
