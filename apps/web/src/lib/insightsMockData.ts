// ============================================================================
// AI Insights Mock Data
// ============================================================================

export type InsightCategory = 'critical' | 'warnings' | 'patterns' | 'performance' | 'security';
export type InsightSeverity = 'high' | 'medium' | 'low';

export interface InsightMetric {
  label: string;
  value: string;
  highlight?: boolean;
}

export interface InsightRecommendation {
  priority: 'immediate' | 'short-term' | 'long-term' | 'prevention';
  text: string;
}

export interface InsightDetail {
  id: string;
  severity: InsightSeverity;
  category: InsightCategory;
  title: string;
  description: string;
  detectedAt: Date;
  metrics: InsightMetric[];
  recommendations: InsightRecommendation[];
  affectedMessages: number;
}

// ============================================================================
// Category Metadata
// ============================================================================

export interface CategoryInfo {
  id: InsightCategory;
  label: string;
  description: string;
  icon: string; // Icon name from lucide-react
}

export const INSIGHT_CATEGORIES: CategoryInfo[] = [
  {
    id: 'critical',
    label: 'Critical Issues',
    description: 'Requires immediate attention',
    icon: 'AlertOctagon',
  },
  {
    id: 'warnings',
    label: 'Warnings',
    description: 'Performance degradation',
    icon: 'AlertTriangle',
  },
  {
    id: 'patterns',
    label: 'Patterns Detected',
    description: 'Recurring behaviors',
    icon: 'BarChart3',
  },
  {
    id: 'performance',
    label: 'Performance',
    description: 'Optimization opportunities',
    icon: 'Zap',
  },
  {
    id: 'security',
    label: 'Security',
    description: 'No issues detected',
    icon: 'Shield',
  },
];

// ============================================================================
// Generate Mock Insights
// ============================================================================

const now = new Date();

export const MOCK_INSIGHTS: InsightDetail[] = [
  // Critical Issues (5)
  {
    id: 'insight-001',
    severity: 'high',
    category: 'critical',
    title: 'Payment Gateway Timeout Spike',
    description: 'Payment validation failures increased by 340% in the last hour. 23 messages failed with timeout errors, affecting order processing pipeline. Root cause: Payment gateway response time degraded from 2.1s to 8.7s.',
    detectedAt: new Date(now.getTime() - 15 * 60 * 1000), // 15 mins ago
    metrics: [
      { label: 'FAILED MESSAGES', value: '23' },
      { label: 'AVG RESPONSE TIME', value: '8.7s' },
      { label: 'IMPACT SCORE', value: '9.2/10', highlight: true },
    ],
    recommendations: [
      { priority: 'immediate', text: 'Increase timeout from 30s to 90s to prevent cascading failures' },
      { priority: 'short-term', text: 'Implement circuit breaker pattern with 5-retry threshold' },
      { priority: 'long-term', text: 'Add redundant payment gateway with automatic failover' },
    ],
    affectedMessages: 23,
  },
  {
    id: 'insight-002',
    severity: 'high',
    category: 'critical',
    title: 'Dead-Letter Queue Accumulation',
    description: 'OrdersQueue DLQ grew from 2 to 47 messages in 45 minutes. Primary cause: JSON deserialization failures due to schema mismatch in order payload. 89% of DLQ messages share the same error pattern.',
    detectedAt: new Date(now.getTime() - 32 * 60 * 1000), // 32 mins ago
    metrics: [
      { label: 'DLQ MESSAGES', value: '47' },
      { label: 'GROWTH RATE', value: '+2250%', highlight: true },
      { label: 'PATTERN MATCH', value: '89%' },
    ],
    recommendations: [
      { priority: 'immediate', text: 'Update message consumer schema to handle new "shippingAddress" field' },
      { priority: 'immediate', text: 'Replay affected messages after schema update' },
      { priority: 'prevention', text: 'Implement schema validation before enqueueing messages' },
    ],
    affectedMessages: 47,
  },
  {
    id: 'insight-003',
    severity: 'high',
    category: 'critical',
    title: 'Message Processing Latency Degradation',
    description: 'End-to-end message processing time increased from 450ms to 3.2s. Bottleneck identified in database connection pool exhaustion. 156 messages currently experiencing delays exceeding SLA threshold.',
    detectedAt: new Date(now.getTime() - 60 * 60 * 1000), // 1 hour ago
    metrics: [
      { label: 'AVG LATENCY', value: '3.2s' },
      { label: 'SLA BREACHES', value: '156' },
      { label: 'POOL USAGE', value: '98%', highlight: true },
    ],
    recommendations: [
      { priority: 'immediate', text: 'Increase database connection pool size from 50 to 100' },
      { priority: 'short-term', text: 'Implement connection recycling every 5 minutes' },
      { priority: 'long-term', text: 'Add read replicas for query distribution' },
    ],
    affectedMessages: 156,
  },
  {
    id: 'insight-004',
    severity: 'high',
    category: 'critical',
    title: 'Consumer Group Rebalancing Storm',
    description: 'Consumer group "orders-processor" experienced 12 rebalances in 10 minutes. Likely cause: Unstable consumer instances due to memory pressure. Processing effectively halted during rebalance periods.',
    detectedAt: new Date(now.getTime() - 45 * 60 * 1000), // 45 mins ago
    metrics: [
      { label: 'REBALANCES', value: '12' },
      { label: 'DOWNTIME', value: '8m 30s', highlight: true },
      { label: 'MEMORY USAGE', value: '94%' },
    ],
    recommendations: [
      { priority: 'immediate', text: 'Increase consumer memory allocation to 4GB' },
      { priority: 'short-term', text: 'Configure longer session timeout (60s → 120s)' },
      { priority: 'prevention', text: 'Add health checks before consumer registration' },
    ],
    affectedMessages: 340,
  },
  {
    id: 'insight-005',
    severity: 'high',
    category: 'critical',
    title: 'Message Ordering Violation',
    description: 'Detected out-of-order message delivery in partition 3 of InventoryTopic. 8 inventory updates processed before their triggering orders, causing negative stock counts.',
    detectedAt: new Date(now.getTime() - 2 * 60 * 60 * 1000), // 2 hours ago
    metrics: [
      { label: 'OUT OF ORDER', value: '8' },
      { label: 'PARTITION', value: '3' },
      { label: 'DATA ERRORS', value: '5', highlight: true },
    ],
    recommendations: [
      { priority: 'immediate', text: 'Enable session-based message ordering' },
      { priority: 'short-term', text: 'Review partition key strategy for inventory updates' },
      { priority: 'prevention', text: 'Add sequence number validation in consumer' },
    ],
    affectedMessages: 8,
  },
  
  // Warnings (12)
  {
    id: 'insight-006',
    severity: 'medium',
    category: 'warnings',
    title: 'High Retry Rate on NotificationsQueue',
    description: 'Average retry count increased from 1.2 to 3.8 per message. Email service intermittently returning 429 (rate limited). Messages eventually succeed but with significant delay.',
    detectedAt: new Date(now.getTime() - 25 * 60 * 1000),
    metrics: [
      { label: 'AVG RETRIES', value: '3.8' },
      { label: 'AFFECTED', value: '234' },
      { label: 'SUCCESS RATE', value: '97%' },
    ],
    recommendations: [
      { priority: 'short-term', text: 'Implement exponential backoff with jitter' },
      { priority: 'long-term', text: 'Add rate limiting awareness to notification service' },
    ],
    affectedMessages: 234,
  },
  {
    id: 'insight-007',
    severity: 'medium',
    category: 'warnings',
    title: 'Message Size Approaching Limit',
    description: '15 messages in PaymentsQueue exceed 200KB (75% of 256KB limit). Large base64-encoded attachments detected in payment receipts.',
    detectedAt: new Date(now.getTime() - 40 * 60 * 1000),
    metrics: [
      { label: 'LARGE MESSAGES', value: '15' },
      { label: 'AVG SIZE', value: '218KB' },
      { label: 'LIMIT', value: '256KB' },
    ],
    recommendations: [
      { priority: 'short-term', text: 'Move attachments to blob storage, pass reference in message' },
      { priority: 'prevention', text: 'Add message size validation at producer' },
    ],
    affectedMessages: 15,
  },
  {
    id: 'insight-008',
    severity: 'medium',
    category: 'warnings',
    title: 'Scheduled Message Backlog Growing',
    description: '1,245 scheduled messages pending delivery, up from 300 yesterday. Scheduling service may be creating messages faster than execution capacity.',
    detectedAt: new Date(now.getTime() - 50 * 60 * 1000),
    metrics: [
      { label: 'PENDING', value: '1,245' },
      { label: 'GROWTH', value: '+315%', highlight: true },
      { label: 'OLDEST', value: '4h 20m' },
    ],
    recommendations: [
      { priority: 'immediate', text: 'Review scheduling service for runaway job creation' },
      { priority: 'short-term', text: 'Increase scheduled message processing throughput' },
    ],
    affectedMessages: 1245,
  },
  {
    id: 'insight-009',
    severity: 'medium',
    category: 'warnings',
    title: 'TTL Expiration Risk',
    description: '89 messages have less than 1 hour remaining before TTL expiration. These messages may be lost if not processed soon.',
    detectedAt: new Date(now.getTime() - 15 * 60 * 1000),
    metrics: [
      { label: 'AT RISK', value: '89' },
      { label: 'TIME REMAINING', value: '<1h', highlight: true },
      { label: 'QUEUE', value: 'OrdersQueue' },
    ],
    recommendations: [
      { priority: 'immediate', text: 'Prioritize processing of oldest messages' },
      { priority: 'short-term', text: 'Consider extending TTL for critical queues' },
    ],
    affectedMessages: 89,
  },
  {
    id: 'insight-010',
    severity: 'medium',
    category: 'warnings',
    title: 'Consumer Lag Increasing',
    description: 'Consumer lag on InventoryTopic partition 2 growing at 50 messages/minute. Current lag: 2,340 messages behind.',
    detectedAt: new Date(now.getTime() - 30 * 60 * 1000),
    metrics: [
      { label: 'CURRENT LAG', value: '2,340' },
      { label: 'GROWTH RATE', value: '50/min' },
      { label: 'PARTITION', value: '2' },
    ],
    recommendations: [
      { priority: 'immediate', text: 'Scale up consumer instances for partition 2' },
      { priority: 'short-term', text: 'Review consumer processing logic for bottlenecks' },
    ],
    affectedMessages: 2340,
  },
  {
    id: 'insight-011',
    severity: 'medium',
    category: 'warnings',
    title: 'Duplicate Message IDs Detected',
    description: '23 duplicate message IDs found across last 10,000 messages. Producer may be retrying without proper idempotency checks.',
    detectedAt: new Date(now.getTime() - 55 * 60 * 1000),
    metrics: [
      { label: 'DUPLICATES', value: '23' },
      { label: 'SAMPLE SIZE', value: '10,000' },
      { label: 'RATE', value: '0.23%' },
    ],
    recommendations: [
      { priority: 'short-term', text: 'Implement idempotency key validation at producer' },
      { priority: 'prevention', text: 'Add deduplication window at consumer level' },
    ],
    affectedMessages: 23,
  },
  {
    id: 'insight-012',
    severity: 'low',
    category: 'warnings',
    title: 'Session Lock Contention',
    description: 'Multiple consumers attempting to claim same session ID. This suggests misconfigured consumer group or race condition.',
    detectedAt: new Date(now.getTime() - 70 * 60 * 1000),
    metrics: [
      { label: 'CONTENTION EVENTS', value: '12' },
      { label: 'SESSION ID', value: 'sess-order-001' },
      { label: 'CONSUMERS', value: '3' },
    ],
    recommendations: [
      { priority: 'short-term', text: 'Review consumer group configuration' },
      { priority: 'prevention', text: 'Implement session affinity in load balancer' },
    ],
    affectedMessages: 45,
  },
  {
    id: 'insight-013',
    severity: 'low',
    category: 'warnings',
    title: 'Intermittent Connection Drops',
    description: 'Service Bus connection dropped and reconnected 8 times in past hour. Network instability or connection pool misconfiguration suspected.',
    detectedAt: new Date(now.getTime() - 45 * 60 * 1000),
    metrics: [
      { label: 'RECONNECTS', value: '8' },
      { label: 'AVG DOWNTIME', value: '2.3s' },
      { label: 'MESSAGES DELAYED', value: '67' },
    ],
    recommendations: [
      { priority: 'short-term', text: 'Review network configuration and firewall rules' },
      { priority: 'long-term', text: 'Implement connection pooling with health checks' },
    ],
    affectedMessages: 67,
  },
  {
    id: 'insight-014',
    severity: 'low',
    category: 'warnings',
    title: 'Uneven Partition Distribution',
    description: 'Partition 0 has 45% of all messages while other partitions average 11%. Hot partition may cause processing bottlenecks.',
    detectedAt: new Date(now.getTime() - 90 * 60 * 1000),
    metrics: [
      { label: 'PARTITION 0', value: '45%', highlight: true },
      { label: 'OTHERS AVG', value: '11%' },
      { label: 'PARTITIONS', value: '5' },
    ],
    recommendations: [
      { priority: 'short-term', text: 'Review partition key selection strategy' },
      { priority: 'long-term', text: 'Consider increasing partition count' },
    ],
    affectedMessages: 4500,
  },
  {
    id: 'insight-015',
    severity: 'low',
    category: 'warnings',
    title: 'Slow Consumer Acknowledgment',
    description: 'Average time between receive and acknowledge increased to 4.5s. Consumers may be doing too much work before acknowledging.',
    detectedAt: new Date(now.getTime() - 80 * 60 * 1000),
    metrics: [
      { label: 'AVG ACK TIME', value: '4.5s' },
      { label: 'BASELINE', value: '0.8s' },
      { label: 'INCREASE', value: '+462%' },
    ],
    recommendations: [
      { priority: 'short-term', text: 'Move heavy processing to after acknowledgment' },
      { priority: 'prevention', text: 'Implement async processing pattern' },
    ],
    affectedMessages: 890,
  },
  {
    id: 'insight-016',
    severity: 'low',
    category: 'warnings',
    title: 'Expiring Subscriptions',
    description: '2 topic subscriptions will expire in 7 days due to inactivity. Review if these are still needed or should be renewed.',
    detectedAt: new Date(now.getTime() - 2 * 60 * 60 * 1000),
    metrics: [
      { label: 'EXPIRING', value: '2' },
      { label: 'DAYS LEFT', value: '7' },
      { label: 'TOPICS', value: 'AuditTopic, LogTopic' },
    ],
    recommendations: [
      { priority: 'short-term', text: 'Review subscription necessity with team' },
      { priority: 'prevention', text: 'Configure auto-renewal for critical subscriptions' },
    ],
    affectedMessages: 0,
  },
  {
    id: 'insight-017',
    severity: 'low',
    category: 'warnings',
    title: 'Message Property Bloat',
    description: 'Average custom properties per message increased to 18. Large property counts impact serialization performance.',
    detectedAt: new Date(now.getTime() - 100 * 60 * 1000),
    metrics: [
      { label: 'AVG PROPERTIES', value: '18' },
      { label: 'RECOMMENDED', value: '<10' },
      { label: 'SERIALIZATION OVERHEAD', value: '+12%' },
    ],
    recommendations: [
      { priority: 'short-term', text: 'Move non-essential properties to message body' },
      { priority: 'prevention', text: 'Document and enforce property guidelines' },
    ],
    affectedMessages: 3400,
  },

  // Patterns Detected (8)
  {
    id: 'insight-018',
    severity: 'medium',
    category: 'patterns',
    title: 'Daily Traffic Spike Pattern',
    description: 'Consistent 3x traffic increase detected between 9:00-11:00 AM UTC. Current infrastructure may not scale appropriately for peak hours.',
    detectedAt: new Date(now.getTime() - 4 * 60 * 60 * 1000),
    metrics: [
      { label: 'PEAK MULTIPLIER', value: '3x' },
      { label: 'PEAK WINDOW', value: '9-11 AM UTC' },
      { label: 'CONFIDENCE', value: '94%' },
    ],
    recommendations: [
      { priority: 'short-term', text: 'Configure auto-scaling to anticipate morning peak' },
      { priority: 'long-term', text: 'Implement predictive scaling based on historical data' },
    ],
    affectedMessages: 15000,
  },
  {
    id: 'insight-019',
    severity: 'low',
    category: 'patterns',
    title: 'Weekend Traffic Reduction',
    description: 'Message volume consistently drops 65% on weekends. Consider scheduling maintenance and deployments during low-traffic periods.',
    detectedAt: new Date(now.getTime() - 5 * 60 * 60 * 1000),
    metrics: [
      { label: 'WEEKEND DROP', value: '65%' },
      { label: 'PATTERN DURATION', value: '8 weeks' },
      { label: 'CONFIDENCE', value: '98%' },
    ],
    recommendations: [
      { priority: 'long-term', text: 'Schedule non-critical maintenance for weekends' },
      { priority: 'prevention', text: 'Adjust scaling policies for weekend periods' },
    ],
    affectedMessages: 0,
  },
  {
    id: 'insight-020',
    severity: 'medium',
    category: 'patterns',
    title: 'Correlated Failure Cascade',
    description: 'Failures in PaymentService trigger downstream failures in NotificationService within 30 seconds. 87% correlation detected.',
    detectedAt: new Date(now.getTime() - 3 * 60 * 60 * 1000),
    metrics: [
      { label: 'CORRELATION', value: '87%' },
      { label: 'CASCADE DELAY', value: '30s' },
      { label: 'AFFECTED SERVICES', value: '2' },
    ],
    recommendations: [
      { priority: 'short-term', text: 'Add circuit breaker between Payment and Notification services' },
      { priority: 'long-term', text: 'Implement saga pattern for distributed transactions' },
    ],
    affectedMessages: 234,
  },
  {
    id: 'insight-021',
    severity: 'low',
    category: 'patterns',
    title: 'Retry Storm After Deployments',
    description: 'Spike in retry attempts consistently observed 5-10 minutes after deployments. Cold start or configuration propagation delay suspected.',
    detectedAt: new Date(now.getTime() - 6 * 60 * 60 * 1000),
    metrics: [
      { label: 'RETRY SPIKE', value: '+250%' },
      { label: 'DURATION', value: '5-10 min' },
      { label: 'DEPLOYMENTS ANALYZED', value: '12' },
    ],
    recommendations: [
      { priority: 'short-term', text: 'Implement gradual traffic shifting during deployments' },
      { priority: 'prevention', text: 'Add readiness probes to prevent premature traffic' },
    ],
    affectedMessages: 450,
  },
  {
    id: 'insight-022',
    severity: 'low',
    category: 'patterns',
    title: 'Message Batch Size Optimization',
    description: 'Current batch size of 10 messages results in suboptimal throughput. Analysis suggests batch size of 50 would improve throughput by 35%.',
    detectedAt: new Date(now.getTime() - 8 * 60 * 60 * 1000),
    metrics: [
      { label: 'CURRENT BATCH', value: '10' },
      { label: 'OPTIMAL BATCH', value: '50' },
      { label: 'POTENTIAL GAIN', value: '+35%' },
    ],
    recommendations: [
      { priority: 'short-term', text: 'Increase batch size to 50 in consumer configuration' },
      { priority: 'long-term', text: 'Implement adaptive batching based on queue depth' },
    ],
    affectedMessages: 0,
  },
  {
    id: 'insight-023',
    severity: 'low',
    category: 'patterns',
    title: 'Geographic Traffic Distribution',
    description: '78% of messages originate from US-East region. Consider regional message routing for latency optimization.',
    detectedAt: new Date(now.getTime() - 12 * 60 * 60 * 1000),
    metrics: [
      { label: 'US-EAST', value: '78%' },
      { label: 'EU-WEST', value: '15%' },
      { label: 'APAC', value: '7%' },
    ],
    recommendations: [
      { priority: 'long-term', text: 'Deploy regional Service Bus namespaces' },
      { priority: 'prevention', text: 'Implement geo-routing at producer level' },
    ],
    affectedMessages: 0,
  },
  {
    id: 'insight-024',
    severity: 'low',
    category: 'patterns',
    title: 'Message Type Distribution Shift',
    description: 'Order messages increased from 30% to 45% of total traffic over past week. Inventory messages decreased proportionally.',
    detectedAt: new Date(now.getTime() - 10 * 60 * 60 * 1000),
    metrics: [
      { label: 'ORDER MESSAGES', value: '45%' },
      { label: 'PREVIOUS', value: '30%' },
      { label: 'CHANGE PERIOD', value: '7 days' },
    ],
    recommendations: [
      { priority: 'short-term', text: 'Review order processing capacity' },
      { priority: 'prevention', text: 'Set up alerts for traffic distribution anomalies' },
    ],
    affectedMessages: 0,
  },
  {
    id: 'insight-025',
    severity: 'low',
    category: 'patterns',
    title: 'Seasonal Traffic Correlation',
    description: 'Historical data shows 40% traffic increase during last week of each month. Correlates with billing and reporting cycles.',
    detectedAt: new Date(now.getTime() - 24 * 60 * 60 * 1000),
    metrics: [
      { label: 'END OF MONTH SPIKE', value: '+40%' },
      { label: 'DATA POINTS', value: '6 months' },
      { label: 'CONFIDENCE', value: '91%' },
    ],
    recommendations: [
      { priority: 'long-term', text: 'Pre-scale infrastructure before month end' },
      { priority: 'prevention', text: 'Add capacity planning alerts for recurring patterns' },
    ],
    affectedMessages: 0,
  },

  // Performance (3)
  {
    id: 'insight-026',
    severity: 'low',
    category: 'performance',
    title: 'Suboptimal Prefetch Configuration',
    description: 'Current prefetch count of 0 (disabled) causes excessive round trips. Enabling prefetch could reduce latency by 45%.',
    detectedAt: new Date(now.getTime() - 6 * 60 * 60 * 1000),
    metrics: [
      { label: 'CURRENT PREFETCH', value: '0' },
      { label: 'RECOMMENDED', value: '100' },
      { label: 'LATENCY REDUCTION', value: '45%' },
    ],
    recommendations: [
      { priority: 'short-term', text: 'Enable prefetch with count of 100' },
      { priority: 'prevention', text: 'Monitor memory usage after enabling prefetch' },
    ],
    affectedMessages: 0,
  },
  {
    id: 'insight-027',
    severity: 'low',
    category: 'performance',
    title: 'Compression Opportunity',
    description: 'Average message size is 45KB with high text content ratio. GZIP compression could reduce bandwidth by 70%.',
    detectedAt: new Date(now.getTime() - 8 * 60 * 60 * 1000),
    metrics: [
      { label: 'AVG SIZE', value: '45KB' },
      { label: 'COMPRESSIBILITY', value: '70%' },
      { label: 'BANDWIDTH SAVINGS', value: '31.5KB/msg' },
    ],
    recommendations: [
      { priority: 'short-term', text: 'Enable GZIP compression at producer' },
      { priority: 'prevention', text: 'Configure automatic decompression at consumer' },
    ],
    affectedMessages: 0,
  },
  {
    id: 'insight-028',
    severity: 'low',
    category: 'performance',
    title: 'Connection Multiplexing Disabled',
    description: 'Each consumer creates separate AMQP connection. Enabling multiplexing could reduce connection overhead by 80%.',
    detectedAt: new Date(now.getTime() - 10 * 60 * 60 * 1000),
    metrics: [
      { label: 'ACTIVE CONNECTIONS', value: '48' },
      { label: 'WITH MULTIPLEXING', value: '10' },
      { label: 'OVERHEAD REDUCTION', value: '80%' },
    ],
    recommendations: [
      { priority: 'short-term', text: 'Enable AMQP connection multiplexing' },
      { priority: 'prevention', text: 'Update SDK to latest version with improved multiplexing' },
    ],
    affectedMessages: 0,
  },
];

// ============================================================================
// Pre-computed Category Counts
// ============================================================================

export const INSIGHT_COUNTS: Record<InsightCategory, number> = {
  critical: MOCK_INSIGHTS.filter(i => i.category === 'critical').length,
  warnings: MOCK_INSIGHTS.filter(i => i.category === 'warnings').length,
  patterns: MOCK_INSIGHTS.filter(i => i.category === 'patterns').length,
  performance: MOCK_INSIGHTS.filter(i => i.category === 'performance').length,
  security: MOCK_INSIGHTS.filter(i => i.category === 'security').length,
};
