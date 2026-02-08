// Namespace DTOs (match your backend CreateNamespaceRequest, NamespaceResponse)
export interface Namespace {
  id: string;
  name: string;
  connectionString?: string; // Encrypted, may not be returned
  displayName?: string;
  description?: string;
  isActive: boolean;
  createdAt: string;
  lastUsedAt?: string;
  hasListenPermission?: boolean;
  hasSendPermission?: boolean;
  hasManagePermission?: boolean;
}

export interface CreateNamespaceRequest {
  name: string;
  connectionString: string;
  displayName?: string;
  description?: string;
}

// Message DTOs (match your backend MessageResponse)
export interface Message {
  messageId: string;  // Primary identifier from Service Bus
  sequenceNumber: number;
  enqueuedTime: string;
  deliveryCount: number;
  state: 'Active' | 'Scheduled' | 'Deferred';
  contentType: string | null;
  body: string | null; // JSON string or text
  correlationId?: string | null;
  sessionId?: string | null;
  partitionKey?: string | null;
  subject?: string | null;
  replyTo?: string | null;
  replyToSessionId?: string | null;
  to?: string | null;
  timeToLive?: string | null;
  scheduledEnqueueTime?: string | null;
  expiresAt?: string | null;
  lockedUntil?: string | null;
  deadLetterSource?: string | null;
  deadLetterReason?: string | null;
  deadLetterErrorDescription?: string | null;
  applicationProperties?: Record<string, any> | null;
  sizeInBytes?: number;
  entityName?: string | null;
  subscriptionName?: string | null;
  isFromDeadLetter?: boolean;
}

export interface PaginatedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export interface GetMessagesParams {
  namespaceId: string;
  queueOrTopicName: string;
  entityType?: 'queue' | 'topic'; // Support both queues and topics
  queueType?: 'active' | 'deadletter';
  skip?: number;
  take?: number;
  searchTerm?: string;
  sortBy?: string;
  sortOrder?: 'asc' | 'desc';
}

// Queue/Topic DTOs
export interface Queue {
  name: string;
  activeMessageCount: number;
  deadLetterMessageCount: number;
  scheduledMessageCount: number;
  maxSizeInMegabytes: number;
  sizeInBytes: number;
  status: string;
}

export interface Topic {
  name: string;
  subscriptionCount: number;
  sizeInBytes: number;
  maxSizeInMegabytes: number;
  status: string;
}

// AI Insights DTOs
export type InsightType = 
  | 'dlq-pattern'
  | 'retry-loop'
  | 'error-cluster'
  | 'latency-anomaly'
  | 'poison-message';

export type ConfidenceLevel = 'high' | 'medium' | 'low';

export interface InsightMetric {
  label: string;
  value: string | number;
  comparison?: string;
  isAnomaly: boolean;
}

export interface InsightRecommendation {
  title: string;
  description: string;
  priority: 'immediate' | 'short-term' | 'investigative';
}

export interface InsightEvidence {
  sampleSize: number;
  affectedMessageIds: string[];
  exampleMessageIds: string[];
  metrics: InsightMetric[];
  patternSignature?: string;
}

export interface AIInsight {
  id: string;
  type: InsightType;
  title: string;
  description: string;
  confidence: {
    level: ConfidenceLevel;
    score: number;
    reasoning: string;
  };
  evidence: InsightEvidence;
  recommendations: InsightRecommendation[];
  timeWindow: {
    start: string;
    end: string;
    analysisTimestamp: string;
  };
  scope: {
    namespaceId: string;
    queueOrTopicName: string;
    subscriptionName?: string;
  };
  status: 'active' | 'dismissed' | 'resolved';
}

export interface GetInsightsParams {
  namespaceId: string;
  queueOrTopicName?: string;
  status?: 'active' | 'dismissed' | 'resolved';
  insightType?: InsightType;
}
