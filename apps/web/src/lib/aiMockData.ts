// ============================================================================
// AI Insights Mock Data
// ============================================================================

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
    start: Date;
    end: Date;
    analysisTimestamp: Date;
  };
  scope: {
    namespaceId: string;
    queueOrTopicName: string;
    subscriptionName?: string;
  };
  status: 'active' | 'dismissed' | 'resolved';
}

// Generate affected message IDs from the mock messages
// These should match IDs in mockData.ts
function generateAffectedIds(_prefix: string, count: number, startIdx: number = 0): string[] {
  return Array.from({ length: count }, (_, i) => `msg-${(startIdx + i).toString().padStart(5, '0')}`);
}

export const MOCK_AI_INSIGHTS: AIInsight[] = [
  {
    id: 'insight-1',
    type: 'dlq-pattern',
    title: 'DLQ Pattern: JSON Deserialization Failure',
    description: '127 messages dead-lettered in last 30 minutes. 89% share error: Cannot deserialize "amount" field - expected decimal, got string.',
    confidence: {
      level: 'high',
      score: 89,
      reasoning: 'Based on 89% pattern match across 127 messages with identical error signature',
    },
    evidence: {
      sampleSize: 127,
      affectedMessageIds: generateAffectedIds('dlq', 127, 100),
      exampleMessageIds: generateAffectedIds('dlq', 3, 100),
      metrics: [
        { label: 'Affected Messages', value: 127, isAnomaly: true },
        { label: 'Match Rate', value: '89%', isAnomaly: true },
        { label: 'Time Window', value: '30 min', isAnomaly: false },
      ],
      patternSignature: 'JsonException: Cannot deserialize amount - expected decimal, got string',
    },
    recommendations: [
      {
        title: 'Validate "amount" field schema at producer',
        description: 'Add schema validation to ensure amount field is always a decimal type before enqueueing',
        priority: 'immediate',
      },
      {
        title: 'Update consumer deserialization logic',
        description: 'Add fallback parsing to handle string-formatted numbers gracefully',
        priority: 'short-term',
      },
      {
        title: 'Replay affected messages after fix',
        description: 'Once schema is corrected, bulk replay the 127 affected messages',
        priority: 'short-term',
      },
    ],
    timeWindow: {
      start: new Date(Date.now() - 30 * 60 * 1000),
      end: new Date(),
      analysisTimestamp: new Date(),
    },
    scope: {
      namespaceId: 'ns-prod-01',
      queueOrTopicName: 'orders-queue',
    },
    status: 'active',
  },
  {
    id: 'insight-2',
    type: 'retry-loop',
    title: 'Retry Loop Detected',
    description: '3 messages stuck in retry cycle. Average delivery count: 47 attempts over 4 hours with no progress.',
    confidence: {
      level: 'high',
      score: 95,
      reasoning: 'Clear retry pattern with no progress - messages have been attempted 47+ times without success',
    },
    evidence: {
      sampleSize: 3,
      affectedMessageIds: ['msg-00042', 'msg-00089', 'msg-00156'],
      exampleMessageIds: ['msg-00042'],
      metrics: [
        { label: 'Stuck Messages', value: 3, isAnomaly: true },
        { label: 'Avg Delivery Count', value: 47, isAnomaly: true },
        { label: 'Duration', value: '4 hours', isAnomaly: true },
      ],
      patternSignature: 'MaxDeliveryCountExceeded with consistent failure',
    },
    recommendations: [
      {
        title: 'Investigate blocking exception in consumer',
        description: 'Check consumer logs for the specific exception preventing message completion',
        priority: 'immediate',
      },
      {
        title: 'Consider dead-lettering after threshold',
        description: 'Configure max delivery count to 50 to prevent infinite retry loops',
        priority: 'short-term',
      },
      {
        title: 'Add circuit breaker pattern',
        description: 'Implement circuit breaker to fail fast when downstream service is unhealthy',
        priority: 'investigative',
      },
    ],
    timeWindow: {
      start: new Date(Date.now() - 4 * 60 * 60 * 1000),
      end: new Date(),
      analysisTimestamp: new Date(),
    },
    scope: {
      namespaceId: 'ns-prod-01',
      queueOrTopicName: 'payments-queue',
    },
    status: 'active',
  },
  {
    id: 'insight-3',
    type: 'error-cluster',
    title: 'Error Cluster: Payment Gateway Timeout',
    description: '47 timeout errors correlate with "gateway: stripe-eu". Messages with stripe-us gateway are unaffected.',
    confidence: {
      level: 'medium',
      score: 73,
      reasoning: 'Strong correlation between gateway region and timeout errors, but external factor involved',
    },
    evidence: {
      sampleSize: 47,
      affectedMessageIds: generateAffectedIds('timeout', 47, 500),
      exampleMessageIds: generateAffectedIds('timeout', 3, 500),
      metrics: [
        { label: 'Timeout Errors', value: 47, isAnomaly: true },
        { label: 'Affected Gateway', value: 'stripe-eu', isAnomaly: false },
        { label: 'Avg Response Time', value: '8.7s', isAnomaly: true },
      ],
      patternSignature: 'TimeoutException: Payment gateway response exceeded 30s threshold',
    },
    recommendations: [
      {
        title: 'Check Stripe EU region status',
        description: 'Verify Stripe EU service health at status.stripe.com',
        priority: 'immediate',
      },
      {
        title: 'Consider fallback to stripe-us',
        description: 'Route EU traffic temporarily through US gateway if EU issues persist',
        priority: 'investigative',
      },
      {
        title: 'Increase timeout threshold',
        description: 'Temporarily increase payment timeout from 30s to 60s during degradation',
        priority: 'short-term',
      },
    ],
    timeWindow: {
      start: new Date(Date.now() - 15 * 60 * 1000),
      end: new Date(),
      analysisTimestamp: new Date(),
    },
    scope: {
      namespaceId: 'ns-prod-01',
      queueOrTopicName: 'payments-queue',
    },
    status: 'active',
  },
];

// Helper to check if a message is part of any AI pattern
export function getMessagePatterns(messageId: string): AIInsight[] {
  return MOCK_AI_INSIGHTS.filter(
    (insight) =>
      insight.evidence.affectedMessageIds.includes(messageId) ||
      insight.evidence.exampleMessageIds.includes(messageId)
  );
}

// Helper to check if message is an example for a pattern
export function isExampleMessage(messageId: string, insightId: string): boolean {
  const insight = MOCK_AI_INSIGHTS.find((i) => i.id === insightId);
  return insight?.evidence.exampleMessageIds.includes(messageId) ?? false;
}

// Get summary counts for UI badges
export function getInsightsSummary() {
  return {
    total: MOCK_AI_INSIGHTS.filter((i) => i.status === 'active').length,
    byConfidence: {
      high: MOCK_AI_INSIGHTS.filter((i) => i.confidence.level === 'high' && i.status === 'active').length,
      medium: MOCK_AI_INSIGHTS.filter((i) => i.confidence.level === 'medium' && i.status === 'active').length,
      low: MOCK_AI_INSIGHTS.filter((i) => i.confidence.level === 'low' && i.status === 'active').length,
    },
    byType: {
      'dlq-pattern': MOCK_AI_INSIGHTS.filter((i) => i.type === 'dlq-pattern' && i.status === 'active').length,
      'retry-loop': MOCK_AI_INSIGHTS.filter((i) => i.type === 'retry-loop' && i.status === 'active').length,
      'error-cluster': MOCK_AI_INSIGHTS.filter((i) => i.type === 'error-cluster' && i.status === 'active').length,
      'latency-anomaly': MOCK_AI_INSIGHTS.filter((i) => i.type === 'latency-anomaly' && i.status === 'active').length,
      'poison-message': MOCK_AI_INSIGHTS.filter((i) => i.type === 'poison-message' && i.status === 'active').length,
    },
  };
}
