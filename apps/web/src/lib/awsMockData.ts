// ============================================================================
// AWS SQS Mock Data — AcmeRetail Global E-Commerce Platform
// Scenario: Black Friday incident — payment gateway cascade failure on SQS
// All messages conform to the existing `Message` interface from mockData.ts
// so they can be used directly in the MessagesPage demo mode.
// ============================================================================

import type {
  Message,
  MessageStatus,
  QueueType,
  ContentType,
} from './mockData';

// Re-export for convenience
export type { Message };

// ---------------------------------------------------------------------------
// Seed data
// ---------------------------------------------------------------------------

const QUEUE_NAMES = [
  'order-processing',
  'payment-gateway-events',
  'notification-service',
  'fraud-detection',
  'inventory-sync',
  'cart-abandonment',
];

const CUSTOMERS = [
  { id: 'CUST-4821', name: 'Sarah Mitchell', region: 'us-east-1' },
  { id: 'CUST-9173', name: 'James Okafor', region: 'eu-west-1' },
  { id: 'CUST-3344', name: 'Priya Sharma', region: 'ap-south-1' },
  { id: 'CUST-7651', name: 'Lucas Ferreira', region: 'sa-east-1' },
  { id: 'CUST-2209', name: 'Hana Nakamura', region: 'ap-northeast-1' },
  { id: 'CUST-5582', name: 'Carlos Mendoza', region: 'us-west-2' },
  { id: 'CUST-8847', name: 'Emma Thornton', region: 'eu-central-1' },
  { id: 'CUST-1134', name: 'Ananya Kapoor', region: 'ap-south-1' },
];

const PAYMENT_GATEWAYS = ['Stripe', 'Braintree', 'Adyen', 'PayPal Commerce'];

const SQS_ERROR_TYPES = [
  'com.acmeretail.payment.PaymentDeclinedException: Card declined (code: insufficient_funds)',
  'com.acmeretail.order.OrderValidationException: Required field "shippingPostcode" is null',
  'io.awssdk.sqs.SqsException: Request timeout after 30000ms (MaxReceiveCount exceeded)',
  'com.acmeretail.fraud.FraudScoreException: Transaction risk score 94/100 exceeds threshold',
  'com.acmeretail.inventory.StockReservationException: SKU ACME-ELEC-PRO-4K out of stock',
  'com.acmeretail.notification.EmailDeliveryException: Bounce rate limit exceeded (SendGrid)',
];

const PRODUCTS = [
  { sku: 'ACME-ELEC-PRO-4K', name: '4K Pro Monitor 32"', price: 699.99 },
  { sku: 'ACME-AUDIO-TWS-X1', name: 'Wireless Earbuds X1', price: 149.99 },
  { sku: 'ACME-KITCHEN-BREW5', name: 'Smart Coffee Maker 5-cup', price: 89.99 },
  { sku: 'ACME-FITNESS-BAND3', name: 'Fitness Tracker Band 3', price: 59.99 },
  { sku: 'ACME-HOME-THERMO2', name: 'Smart Thermostat Gen2', price: 199.99 },
];

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function randomFrom<T>(arr: T[]): T {
  return arr[Math.floor(Math.random() * arr.length)];
}

function uuid(): string {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
    const r = (Math.random() * 16) | 0;
    return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
  });
}

function sqsMessageId(): string {
  return uuid().replace(/-/g, '').substring(0, 24).toUpperCase();
}

function timeAgo(minutes: number): Date {
  return new Date(Date.now() - minutes * 60_000);
}

// ---------------------------------------------------------------------------
// AWS SQS Queue metadata (used in sidebar / demo queue list)
// ---------------------------------------------------------------------------

export interface AwsDemoQueue {
  name: string;
  url: string;
  activeMessageCount: number;
  deadLetterMessageCount: number;
  region: string;
  isFifo: boolean;
  visibilityTimeout: number;
  messageRetentionPeriod: number;
}

export const AWS_DEMO_QUEUES: AwsDemoQueue[] = [
  {
    name: 'order-processing',
    url: 'https://sqs.us-east-1.amazonaws.com/123456789012/order-processing',
    activeMessageCount: 47,
    deadLetterMessageCount: 15,
    region: 'us-east-1',
    isFifo: false,
    visibilityTimeout: 30,
    messageRetentionPeriod: 345600,
  },
  {
    name: 'payment-gateway-events',
    url: 'https://sqs.us-east-1.amazonaws.com/123456789012/payment-gateway-events',
    activeMessageCount: 23,
    deadLetterMessageCount: 38,
    region: 'us-east-1',
    isFifo: false,
    visibilityTimeout: 60,
    messageRetentionPeriod: 86400,
  },
  {
    name: 'notification-service',
    url: 'https://sqs.us-east-1.amazonaws.com/123456789012/notification-service',
    activeMessageCount: 91,
    deadLetterMessageCount: 7,
    region: 'us-east-1',
    isFifo: false,
    visibilityTimeout: 30,
    messageRetentionPeriod: 345600,
  },
  {
    name: 'fraud-detection',
    url: 'https://sqs.us-east-1.amazonaws.com/123456789012/fraud-detection',
    activeMessageCount: 12,
    deadLetterMessageCount: 4,
    region: 'us-east-1',
    isFifo: false,
    visibilityTimeout: 120,
    messageRetentionPeriod: 259200,
  },
  {
    name: 'inventory-sync.fifo',
    url: 'https://sqs.us-east-1.amazonaws.com/123456789012/inventory-sync.fifo',
    activeMessageCount: 156,
    deadLetterMessageCount: 0,
    region: 'us-east-1',
    isFifo: true,
    visibilityTimeout: 30,
    messageRetentionPeriod: 345600,
  },
  {
    name: 'cart-abandonment',
    url: 'https://sqs.us-east-1.amazonaws.com/123456789012/cart-abandonment',
    activeMessageCount: 203,
    deadLetterMessageCount: 2,
    region: 'us-east-1',
    isFifo: false,
    visibilityTimeout: 30,
    messageRetentionPeriod: 172800,
  },
];

// ---------------------------------------------------------------------------
// AI issue patterns for AWS demo
// ---------------------------------------------------------------------------

const AWS_AI_ISSUES = [
  {
    issue: 'Payment Gateway Cascade Failure — 38 of 61 dead-lettered messages from payment-gateway-events share identical Braintree 504 timeout. MaxReceiveCount (5) exceeded after exponential retry. Root cause: Braintree US-East endpoint degradation since 14:32 UTC.',
    recommendations: [
      'Activate Braintree EU-West failover endpoint immediately (appsettings.Payment.FailoverUrl)',
      'Replay the 38 DLQ messages via SQS SendMessage after failover is confirmed healthy',
      'Add SQS ApproximateReceiveCount check to stop retrying after 3 attempts — not 5',
    ],
  },
  {
    issue: 'Schema mismatch in order-processing — 15 DLQ messages contain null shippingPostcode field. OrderService v2.3 introduced mandatory postcode validation but 3 legacy client apps (iOS v4.1, Android v4.0, Web v2.0) still submit v2.2 schema without it.',
    recommendations: [
      'Deploy OrderService v2.4 with backward-compatible null-safe postcode fallback (default to "N/A")',
      'After deployment, replay all 15 DLQ messages from order-processing — they will now pass',
      'Add schema version negotiation (x-schema-version header) to all client SDKs',
    ],
  },
  {
    issue: 'Fraud detection false positives — 4 legitimate orders from ap-south-1 region dead-lettered. FraudService misconfigured VPN-detection heuristic flags Reliance Jio mobile IPs (AS55836) as VPN traffic, inflating risk scores by 30–40 points.',
    recommendations: [
      'Add Reliance Jio ASN range (AS55836, 45.126.0.0/17) to fraud whitelist',
      'Replay the 4 DLQ orders after whitelist update — all have clean payment histories',
      'Review fraud rule #FR-119 for other Indian ISP ASNs that may be similarly misclassified',
    ],
  },
];

// ---------------------------------------------------------------------------
// Message body generators
// ---------------------------------------------------------------------------

function makeOrderProcessingMessage(isError: boolean, idx: number): object {
  const customer = randomFrom(CUSTOMERS);
  const product = randomFrom(PRODUCTS);
  const orderId = `ORD-SQS-${2026100 + idx}`;
  const gateway = randomFrom(PAYMENT_GATEWAYS);
  return {
    MessageType: 'OrderCreated',
    MessageSchemaVersion: isError ? '2.2' : '2.3',
    OrderId: orderId,
    CustomerId: customer.id,
    CustomerName: customer.name,
    Items: [{ Sku: product.sku, Name: product.name, Quantity: 1, UnitPrice: product.price }],
    TotalAmount: product.price,
    Currency: 'USD',
    PaymentGateway: gateway,
    ShippingAddress: {
      Line1: `${100 + idx} Commerce Street`,
      City: 'New York',
      State: 'NY',
      PostalCode: isError ? null : '10001',
      Country: 'US',
    },
    SqsMessageAttributes: {
      CorrelationId: uuid(),
      SourceService: 'checkout-api',
      SchemaVersion: isError ? '2.2' : '2.3',
      ApproximateReceiveCount: isError ? String(5 + (idx % 3)) : '1',
    },
  };
}

function makePaymentEventMessage(isError: boolean, idx: number): object {
  const customer = randomFrom(CUSTOMERS);
  const gateway = randomFrom(PAYMENT_GATEWAYS);
  const txnId = `TXN-${Date.now() + idx}`;
  return {
    MessageType: isError ? 'PaymentFailed' : 'PaymentCapture',
    TransactionId: txnId,
    CustomerId: customer.id,
    Amount: (49.99 + idx * 12.5).toFixed(2),
    Currency: 'USD',
    Gateway: gateway,
    GatewayResponseCode: isError ? '504' : '200',
    GatewayResponseMessage: isError
      ? 'Gateway timeout after 30000ms — Braintree US-East endpoint unavailable'
      : 'Payment captured successfully',
    RetryCount: isError ? 5 : 0,
    ProcessedAt: new Date(Date.now() - idx * 45000).toISOString(),
    SqsMessageAttributes: {
      CorrelationId: uuid(),
      SourceService: 'payment-service',
      RetryReason: isError ? 'GatewayTimeout' : undefined,
    },
  };
}

function makeNotificationMessage(isError: boolean, idx: number): object {
  const customer = randomFrom(CUSTOMERS);
  const channels = ['email', 'sms', 'push'];
  const channel = randomFrom(channels);
  return {
    MessageType: isError ? 'NotificationFailed' : 'NotificationQueued',
    NotificationId: `NOTIF-${Date.now() + idx}`,
    CustomerId: customer.id,
    CustomerEmail: `${customer.id.toLowerCase()}@acmeretail-demo.com`,
    Channel: channel,
    Template: 'order_confirmation_v3',
    DeliveryError: isError ? 'SendGrid bounce rate 12% exceeds 5% threshold — delivery paused' : undefined,
    RetryAfter: isError ? new Date(Date.now() + 3600_000).toISOString() : undefined,
    SentAt: new Date(Date.now() - idx * 30000).toISOString(),
  };
}

// ---------------------------------------------------------------------------
// Public: generate AWS demo messages
// ---------------------------------------------------------------------------

/**
 * Generates a realistic set of AWS SQS demo messages for the MessagesPage
 * demo mode (?demo=aws). Returns 50 messages mapped to the Message interface.
 */
export function generateAwsMockMessages(count = 50): Message[] {
  const messages: Message[] = [];

  for (let i = 0; i < count; i++) {
    const isError = i < 10;
    const isWarning = !isError && i < 20;
    const isDeadletter = i < 15;
    const queueName = QUEUE_NAMES[i % QUEUE_NAMES.length];

    const status: MessageStatus = isError ? 'error' : isWarning ? 'warning' : 'success';
    const queueType: QueueType = isDeadletter ? 'deadletter' : 'active';

    let bodyObj: object;
    if (queueName === 'order-processing') {
      bodyObj = makeOrderProcessingMessage(isError, i);
    } else if (queueName === 'payment-gateway-events') {
      bodyObj = makePaymentEventMessage(isError, i);
    } else {
      bodyObj = makeNotificationMessage(isError, i);
    }

    const body = JSON.stringify(bodyObj, null, 2);
    const msgId = sqsMessageId();
    const minutesAgo = i * 3 + Math.floor(Math.random() * 5);

    const aiIssueTemplate = isError ? AWS_AI_ISSUES[i % AWS_AI_ISSUES.length] : undefined;

    messages.push({
      id: msgId,
      sequenceNumber: 1000 + i,
      enqueuedTime: timeAgo(minutesAgo),
      status,
      preview: isError
        ? `[ERROR] ${SQS_ERROR_TYPES[i % SQS_ERROR_TYPES.length].substring(0, 80)}`
        : isWarning
        ? `[WARN] ${queueName} — message queued ${minutesAgo}m ago, consumer lag growing`
        : `[OK] ${queueName} — message processed successfully`,
      contentType: 'application/json' as ContentType,
      deliveryCount: isDeadletter ? 5 + (i % 3) : 1,
      hasAIInsight: !!aiIssueTemplate,
      properties: {
        'sqs:MessageId': msgId,
        'sqs:ApproximateReceiveCount': String(isDeadletter ? 5 : 1),
        'sqs:SentTimestamp': String(timeAgo(minutesAgo).getTime()),
        'sqs:ApproximateFirstReceiveTimestamp': String(timeAgo(minutesAgo - 1).getTime()),
        'x-correlation-id': uuid(),
        'x-source-service': queueName.split('-')[0] + '-service',
        'x-schema-version': isError ? '2.2' : '2.3',
        'x-aws-region': 'us-east-1',
        'x-message-group': queueName.includes('fifo') ? `group-${i % 5}` : undefined,
      } as Record<string, unknown>,
      queueType,
      body,
      headers: {
        'Content-Type': 'application/json',
        'X-SQS-Queue': `https://sqs.us-east-1.amazonaws.com/123456789012/${queueName}`,
        'X-AWS-Account': '123456789012',
        'X-AWS-Region': 'us-east-1',
        'X-Correlation-Id': uuid(),
      },
      timeToLive: '4 days',
      lockToken: uuid(),
      eventType: (bodyObj as Record<string, string>)['MessageType'] ?? 'UnknownEvent',
      displayTitle: `${(bodyObj as Record<string, string>)['MessageType'] ?? 'SQS Message'} • ${queueName}`,
      deadLetterReason: isDeadletter ? 'MaxReceiveCount exceeded' : undefined,
      deadLetterSource: isDeadletter ? queueName : undefined,
      aiAnalysis: aiIssueTemplate
        ? {
            issue: aiIssueTemplate.issue,
            recommendations: [...aiIssueTemplate.recommendations],
            detectedAt: new Date(timeAgo(minutesAgo).getTime() + 60_000),
          }
        : undefined,
    });
  }

  return messages;
}
