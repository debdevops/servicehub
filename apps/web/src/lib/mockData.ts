// ============================================================================
// Message Types
// ============================================================================

export type MessageStatus = 'success' | 'warning' | 'error';
export type QueueType = 'active' | 'deadletter';
export type ContentType = 'application/json' | 'text/plain' | 'application/xml';

export interface AIAnalysis {
  issue: string;
  recommendations: string[];
  detectedAt: Date;
}

export interface Message {
  id: string;
  enqueuedTime: Date;
  status: MessageStatus;
  preview: string;
  contentType: ContentType;
  deliveryCount: number;
  hasAIInsight: boolean;
  sequenceNumber: number;
  properties: Record<string, unknown>;
  // New fields for detail panel
  queueType: QueueType;
  body: string;
  headers: Record<string, string>;
  timeToLive: string;
  lockToken: string;
  // Event type extracted from message body
  eventType?: string;
  displayTitle?: string;
  // Dead-letter specific fields
  deadLetterReason?: string;
  deadLetterSource?: string;
  // AI Analysis (present when hasAIInsight is true)
  aiAnalysis?: AIAnalysis;
}

// ============================================================================
// Mock Data Templates
// ============================================================================

const ORDER_TEMPLATES = [
  { orderId: 'ORD-2024-12345', customerId: 'CUST-98765', amount: 299.99, currency: 'USD', paymentMethod: 'credit_card', status: 'pending_validation' },
  { orderId: 'ORD-2024-67890', customerId: 'CUST-11111', amount: 149.50, currency: 'EUR', paymentMethod: 'paypal', status: 'completed' },
  { orderId: 'ORD-2024-24680', customerId: 'CUST-22222', amount: 599.00, currency: 'USD', paymentMethod: 'bank_transfer', status: 'processing' },
  { orderId: 'ORD-2024-13579', customerId: 'CUST-33333', amount: 79.99, currency: 'GBP', paymentMethod: 'apple_pay', status: 'shipped' },
  { orderId: 'ORD-2024-11223', customerId: 'CUST-44444', amount: 1299.00, currency: 'USD', paymentMethod: 'credit_card', status: 'failed' },
];

const PAYMENT_TEMPLATES = [
  { transactionId: 'TXN-9876543210', amount: 250.00, currency: 'USD', gateway: 'stripe', status: 'authorized', retryCount: 0 },
  { transactionId: 'TXN-1234567890', amount: 99.99, currency: 'USD', gateway: 'paypal', status: 'captured', retryCount: 1 },
  { transactionId: 'TXN-5555555555', amount: 450.00, currency: 'EUR', gateway: 'adyen', status: 'pending', retryCount: 2 },
  { transactionId: 'TXN-7777777777', amount: 75.50, currency: 'GBP', gateway: 'stripe', status: 'declined', retryCount: 3 },
];

const INVENTORY_TEMPLATES = [
  { sku: 'SKU-LAPTOP-001', warehouse: 'WH-EAST-01', quantity: 150, threshold: 50, action: 'restock_alert' },
  { sku: 'SKU-PHONE-002', warehouse: 'WH-WEST-02', quantity: 0, threshold: 25, action: 'out_of_stock' },
  { sku: 'SKU-TABLET-003', warehouse: 'WH-CENTRAL-01', quantity: 500, threshold: 100, action: 'inventory_update' },
  { sku: 'SKU-WATCH-004', warehouse: 'WH-EAST-01', quantity: 75, threshold: 30, action: 'low_stock_warning' },
];

const NOTIFICATION_TEMPLATES = [
  { notificationId: 'NOTIF-001', type: 'email', recipient: 'user@example.com', template: 'order_confirmation', status: 'sent' },
  { notificationId: 'NOTIF-002', type: 'sms', recipient: '+1234567890', template: 'shipping_update', status: 'pending' },
  { notificationId: 'NOTIF-003', type: 'push', recipient: 'device-token-xyz', template: 'payment_received', status: 'delivered' },
  { notificationId: 'NOTIF-004', type: 'webhook', recipient: 'https://api.partner.com/webhook', template: 'inventory_sync', status: 'failed' },
];

const SHIPPING_TEMPLATES = [
  { shipmentId: 'SHIP-2024-001', carrier: 'FedEx', trackingNumber: 'FX123456789', status: 'in_transit', estimatedDelivery: '2024-01-20' },
  { shipmentId: 'SHIP-2024-002', carrier: 'UPS', trackingNumber: 'UPS987654321', status: 'delivered', estimatedDelivery: '2024-01-18' },
  { shipmentId: 'SHIP-2024-003', carrier: 'DHL', trackingNumber: 'DHL555555555', status: 'processing', estimatedDelivery: '2024-01-22' },
];

const PREVIEWS = [
  'Payment validation failed - retry logic needed',
  'Order processed successfully',
  'Expired timer - check TTL settings',
  'Inventory update completed',
  'High retry count detected',
  'Notification sent to user@example.com',
  'Stock level updated for SKU-12345',
  'Rate limit approaching threshold',
  'Database connection timeout',
  'Message deserialization successful',
  'Queue processing delayed',
  'Customer order #12345 confirmed',
  'Shipping label generated',
  'Email notification queued',
  'Webhook delivery attempted',
  'Payment gateway timeout',
  'Schema validation error',
  'Duplicate message detected',
  'Consumer processing slow',
  'Message size exceeds limit',
];

const AI_ISSUES = [
  { issue: 'Payment validation timeout. Message retried 3 times without success.', recommendations: ['Increase timeout from 30s to 60s for payment gateway', 'Implement circuit breaker to prevent cascade failures', 'Add dead-letter rule after 5 retry attempts'] },
  { issue: 'High retry count indicates downstream service instability.', recommendations: ['Check service health dashboard', 'Scale up consumer instances', 'Review error logs for root cause'] },
  { issue: 'Message schema mismatch detected. JSON deserialization failing.', recommendations: ['Update message consumer schema to handle new "shippingAddress" field', 'Replay affected messages after schema update', 'Implement schema validation before enqueueing messages'] },
  { issue: 'Processing latency increased by 340% in the last hour.', recommendations: ['Increase timeout from 30s to 90s to prevent cascading failures', 'Implement circuit breaker pattern with 5-retry threshold', 'Add redundant payment gateway with automatic failover'] },
  { issue: 'Dead-letter queue growing rapidly. 89% share same error pattern.', recommendations: ['Investigate common failure cause', 'Update consumer error handling', 'Consider batch replay after fix'] },
  { issue: 'Message ordering violation detected in sequence.', recommendations: ['Enable session-based ordering', 'Review partition key strategy', 'Add sequence validation in consumer'] },
  { issue: 'Resource exhaustion warning. Connection pool near limit.', recommendations: ['Increase connection pool size', 'Implement connection recycling', 'Add monitoring alerts for pool usage'] },
  { issue: 'Duplicate message IDs detected across partitions.', recommendations: ['Implement idempotency checks', 'Add deduplication window', 'Review message ID generation strategy'] },
];

const DEAD_LETTER_REASONS = [
  'MaxDeliveryCountExceeded',
  'TTLExpiredException',
  'MessageSizeExceeded',
  'SessionIdMismatch',
  'HeaderSizeExceeded',
  'TransactionAborted',
  'FilterEvaluationException',
];

const HEADER_KEYS = [
  'Content-Type',
  'Content-Encoding',
  'Message-Id',
  'Correlation-Id',
  'Session-Id',
  'Reply-To',
  'Reply-To-Session-Id',
  'Label',
  'Scheduled-Enqueue-Time-Utc',
  'Partition-Key',
  'Via-Partition-Key',
  'x-opt-enqueue-sequence-number',
  'x-opt-offset',
  'x-opt-locked-until',
];

// ============================================================================
// Helper Functions
// ============================================================================

function randomItem<T>(arr: T[]): T {
  return arr[Math.floor(Math.random() * arr.length)];
}

function generateMessageBody(): { body: string; contentType: ContentType } {
  const roll = Math.random();
  
  if (roll < 0.4) {
    // Order message
    const template = { ...randomItem(ORDER_TEMPLATES) };
    template.orderId = `ORD-2026-${Math.floor(Math.random() * 99999).toString().padStart(5, '0')}`;
    template.amount = Math.round(Math.random() * 1000 * 100) / 100;
    return { body: JSON.stringify(template, null, 2), contentType: 'application/json' };
  } else if (roll < 0.6) {
    // Payment message
    const template = { ...randomItem(PAYMENT_TEMPLATES) };
    template.transactionId = `TXN-${Math.floor(Math.random() * 9999999999)}`;
    template.amount = Math.round(Math.random() * 500 * 100) / 100;
    return { body: JSON.stringify(template, null, 2), contentType: 'application/json' };
  } else if (roll < 0.75) {
    // Inventory message
    const template = { ...randomItem(INVENTORY_TEMPLATES) };
    template.quantity = Math.floor(Math.random() * 1000);
    return { body: JSON.stringify(template, null, 2), contentType: 'application/json' };
  } else if (roll < 0.85) {
    // Notification message
    const template = { ...randomItem(NOTIFICATION_TEMPLATES) };
    template.notificationId = `NOTIF-${Math.floor(Math.random() * 99999).toString().padStart(5, '0')}`;
    return { body: JSON.stringify(template, null, 2), contentType: 'application/json' };
  } else if (roll < 0.95) {
    // Shipping message
    const template = { ...randomItem(SHIPPING_TEMPLATES) };
    template.shipmentId = `SHIP-2026-${Math.floor(Math.random() * 999).toString().padStart(3, '0')}`;
    return { body: JSON.stringify(template, null, 2), contentType: 'application/json' };
  } else {
    // Plain text message
    return { 
      body: `Event logged at ${new Date().toISOString()}\nSource: ServiceHub\nPriority: Normal\nMessage: System health check completed successfully.`, 
      contentType: 'text/plain' 
    };
  }
}

function generateHeaders(): Record<string, string> {
  const headers: Record<string, string> = {};
  const numHeaders = 4 + Math.floor(Math.random() * 6); // 4-9 headers
  const shuffled = [...HEADER_KEYS].sort(() => Math.random() - 0.5);
  
  for (let i = 0; i < numHeaders && i < shuffled.length; i++) {
    const key = shuffled[i];
    switch (key) {
      case 'Content-Type':
        headers[key] = 'application/json; charset=utf-8';
        break;
      case 'Content-Encoding':
        headers[key] = 'gzip';
        break;
      case 'Message-Id':
        headers[key] = crypto.randomUUID?.() || `msg-${Math.random().toString(36).substring(2, 15)}`;
        break;
      case 'Correlation-Id':
        headers[key] = `corr-${Math.random().toString(36).substring(2, 11)}`;
        break;
      case 'Session-Id':
        headers[key] = `session-${Math.floor(Math.random() * 10000)}`;
        break;
      case 'Reply-To':
        headers[key] = 'response-queue';
        break;
      case 'Label':
        headers[key] = randomItem(['OrderCreated', 'PaymentProcessed', 'InventoryUpdated', 'NotificationSent', 'ShipmentCreated']);
        break;
      case 'Partition-Key':
        headers[key] = `partition-${Math.floor(Math.random() * 16)}`;
        break;
      default:
        headers[key] = `value-${Math.random().toString(36).substring(2, 8)}`;
    }
  }
  
  return headers;
}

function generateTimeToLive(): string {
  const days = Math.floor(Math.random() * 30);
  const hours = Math.floor(Math.random() * 24);
  const minutes = Math.floor(Math.random() * 60);
  return `${days}d ${hours}h ${minutes}m 0s`;
}

// ============================================================================
// Main Generator
// ============================================================================

export function generateMockMessages(count: number): Message[] {
  const statusWeights = [0.65, 0.25, 0.10]; // 65% success, 25% warning, 10% error
  
  const messages: Message[] = [];
  const now = new Date();

  for (let i = 0; i < count; i++) {
    // Determine status based on weights
    const statusRoll = Math.random();
    let status: MessageStatus;
    if (statusRoll < statusWeights[0]) {
      status = 'success';
    } else if (statusRoll < statusWeights[0] + statusWeights[1]) {
      status = 'warning';
    } else {
      status = 'error';
    }
    
    // 10% chance of dead-letter, higher for errors
    const isDeadLetter = status === 'error' ? Math.random() < 0.4 : Math.random() < 0.1;
    const queueType: QueueType = isDeadLetter ? 'deadletter' : 'active';
    
    // Generate timing (spread across last 7 days for variety)
    const minutesAgo = Math.floor(Math.random() * 10080); // Up to 7 days
    const enqueuedTime = new Date(now.getTime() - minutesAgo * 60 * 1000);
    
    // Generate body and content type
    const { body, contentType } = generateMessageBody();
    
    // Delivery count higher for warnings/errors
    let deliveryCount: number;
    if (status === 'error') {
      deliveryCount = 3 + Math.floor(Math.random() * 7); // 3-9
    } else if (status === 'warning') {
      deliveryCount = 1 + Math.floor(Math.random() * 4); // 1-4
    } else {
      deliveryCount = Math.floor(Math.random() * 2); // 0-1
    }
    
    // AI insight more likely for warnings/errors
    const hasAIInsight = status === 'error' 
      ? Math.random() < 0.7 
      : status === 'warning' 
        ? Math.random() < 0.4 
        : Math.random() < 0.1;
    
    // Build message
    const message: Message = {
      id: `msg-${Math.random().toString(36).substring(2, 6)}-${String(i + 1).padStart(6, '0')}`,
      enqueuedTime,
      status,
      preview: randomItem(PREVIEWS),
      contentType,
      deliveryCount,
      hasAIInsight,
      sequenceNumber: 1000000 + i,
      properties: {
        correlationId: `corr-${Math.random().toString(36).substring(2, 11)}`,
        userId: `user-${Math.floor(Math.random() * 10000)}`,
        source: randomItem(['OrderService', 'PaymentService', 'InventoryService', 'NotificationService', 'ShippingService']),
        priority: randomItem(['low', 'normal', 'high', 'critical']),
      },
      queueType,
      body,
      headers: generateHeaders(),
      timeToLive: generateTimeToLive(),
      lockToken: `lock-${Math.random().toString(36).substring(2, 15)}-${Math.random().toString(36).substring(2, 15)}`,
    };
    
    // Add dead-letter specific fields
    if (queueType === 'deadletter') {
      message.deadLetterReason = randomItem(DEAD_LETTER_REASONS);
      message.deadLetterSource = randomItem(['OrdersQueue', 'PaymentsQueue', 'NotificationsQueue', 'InventoryQueue']);
    }
    
    // Add AI analysis if applicable
    if (hasAIInsight) {
      const aiTemplate = randomItem(AI_ISSUES);
      message.aiAnalysis = {
        issue: aiTemplate.issue,
        recommendations: [...aiTemplate.recommendations],
        detectedAt: new Date(enqueuedTime.getTime() + Math.random() * 300000), // Within 5 mins of enqueue
      };
    }
    
    messages.push(message);
  }

  // Sort by enqueued time (most recent first)
  messages.sort((a, b) => b.enqueuedTime.getTime() - a.enqueuedTime.getTime());
  
  return messages;
}

// ============================================================================
// Export 100,000 messages for UI stress testing
// ============================================================================

console.time('Generating 100,000 mock messages');
export const MOCK_MESSAGES = generateMockMessages(100000);
console.timeEnd('Generating 100,000 mock messages');

// Pre-computed counts for UI
export const MESSAGE_COUNTS = {
  total: MOCK_MESSAGES.length,
  active: MOCK_MESSAGES.filter(m => m.queueType === 'active').length,
  deadletter: MOCK_MESSAGES.filter(m => m.queueType === 'deadletter').length,
  withAIInsights: MOCK_MESSAGES.filter(m => m.hasAIInsight).length,
  byStatus: {
    success: MOCK_MESSAGES.filter(m => m.status === 'success').length,
    warning: MOCK_MESSAGES.filter(m => m.status === 'warning').length,
    error: MOCK_MESSAGES.filter(m => m.status === 'error').length,
  },
};
