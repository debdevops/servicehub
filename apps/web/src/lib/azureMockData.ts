// ============================================================================
// Azure Service Bus Mock Data — Contoso Commerce Platform
// Scenario: Black Friday incident — inventory DB lock contention and session locks
// All messages conform to the existing `Message` interface from mockData.ts
// ============================================================================

import type {
  Message,
  MessageStatus,
  QueueType,
  ContentType,
} from './mockData';

export type { Message };

// Helper to generate a random item from array
function randomFrom<T>(arr: T[]): T {
  return arr[Math.floor(Math.random() * arr.length)];
}

// Helper to generate date relative to now
function timeAgo(minutesAgo: number): Date {
  return new Date(Date.now() - minutesAgo * 60_000);
}

// ---------------------------------------------------------------------------
// Seed data
// ---------------------------------------------------------------------------

export const AZURE_QUEUES = [
  { name: 'orders-queue', dlq: 'orders-queue/$DeadLetterQueue', type: 'Queue' },
  { name: 'checkout-queue', dlq: 'checkout-queue/$DeadLetterQueue', type: 'Session' },
  { name: 'fulfillment-queue', dlq: 'fulfillment-queue/$DeadLetterQueue', type: 'Queue' },
  { name: 'customer-notifications', dlq: 'customer-notifications/$DeadLetterQueue', type: 'Queue' },
];

export const AZURE_TOPICS = [
  {
    name: 'order-events',
    subscriptions: [
      { name: 'inventory-sub', dlq: 'order-events/Subscriptions/inventory-sub/$DeadLetterQueue' },
      { name: 'email-sub', dlq: 'order-events/Subscriptions/email-sub/$DeadLetterQueue' },
      { name: 'billing-sub', dlq: 'order-events/Subscriptions/billing-sub/$DeadLetterQueue' },
    ],
  },
];

const CUSTOMERS = [
  { id: 'CONT-5821', name: 'Alice Smith', region: 'eastus' },
  { id: 'CONT-9173', name: 'Robert Johnson', region: 'westeurope' },
  { id: 'CONT-3344', name: 'David Lee', region: 'southeastasia' },
  { id: 'CONT-7651', name: 'Emily Davis', region: 'eastus2' },
  { id: 'CONT-2209', name: 'Sophia Chen', region: 'centralus' },
];

const AZURE_ERROR_TYPES = [
  'Microsoft.Azure.ServiceBus.MessageLockLostException: The lock on the message has expired. Message processing exceeded LockDuration of 60s.',
  'Microsoft.Azure.ServiceBus.SessionLockLostException: Session lock for session id "sess-orders" was lost because it was not renewed.',
  'System.TimeoutException: The operation did not complete within the allocated timeout of 00:01:00.',
  'Microsoft.Azure.ServiceBus.MessageSizeExceededException: The message size (284 KB) exceeds the maximum allowed limit of 256 KB.',
  'System.InvalidOperationException: JSON deserialization failed. Mandatory field "CustomerName" is missing.',
];

const PRODUCTS = [
  { sku: 'CONTOSO-FIT-X2', name: 'Contoso Active Band v2', price: 99.99 },
  { sku: 'CONTOSO-HOME-HUB', name: 'Contoso Home Hub Mini', price: 129.99 },
  { sku: 'CONTOSO-KITCH-01', name: 'Contoso Smart Oven', price: 349.99 },
  { sku: 'CONTOSO-ELEC-4K', name: 'Contoso UltraHD Monitor 27"', price: 299.99 },
];

// AI issue patterns for Azure demo
const AZURE_AI_ISSUES = [
  {
    issue: 'Message Lock Lost Exception in inventory-sub — 22 messages dead-lettered because processing took 64.2 seconds, exceeding the LockDuration of 60 seconds. Root cause: InventorySyncDB lock contention during peak updates.',
    recommendations: [
      'Increase the LockDuration property of the subscription to 120 seconds.',
      'Optimize InventorySyncDB indexing on inventory-status table to speed up updates.',
      'Replay the 22 dead-lettered messages in batches of 5 to avoid triggering database lock contention again.',
    ],
  },
  {
    issue: 'Session Lock Lost Exception in checkout-queue — 12 dead-lettered messages share session id "sess-orders". The consumer failed to renew the session lock in time due to an unhandled retry loop block in CheckoutService.',
    recommendations: [
      'Implement transient fault policy with jitter in CheckoutService database calls.',
      'Ensure the SessionHandler automatically renews the session lock dynamically.',
      'Replay the 12 messages; they will process successfully now that the CheckoutService has restarted.',
    ],
  },
  {
    issue: 'Message Size Exceeded Exception in orders-queue — 5 dead-lettered order-batch payloads exceed the 256 KB Standard tier limit. The payload contains base64 image data that should be stored in Azure Blob Storage.',
    recommendations: [
      'Configure the Checkout API to upload order attachments to Azure Blob Storage and pass the URL in the message body instead.',
      'Upgrade Service Bus namespace to Premium tier (supports up to 100 MB message size) if larger payloads are unavoidable.',
      'Clean/strip the attachments and resubmit the 5 dead-lettered messages.',
    ],
  },
];

function makeOrderMessage(isError: boolean, idx: number): object {
  const customer = randomFrom(CUSTOMERS);
  const product = randomFrom(PRODUCTS);
  const orderId = `CONT-ORD-${987200 + idx}`;
  return {
    MessageType: 'OrderReceived',
    OrderId: orderId,
    CustomerId: customer.id,
    CustomerName: isError && idx % 2 === 0 ? null : customer.name,
    Items: [{ Sku: product.sku, Name: product.name, Quantity: 1, Price: product.price }],
    Total: product.price,
    Currency: 'USD',
    ShippingDetails: {
      AddressLine1: `${50 + idx} Microsoft Way`,
      City: 'Redmond',
      State: 'WA',
      Zip: '98052',
    },
    SystemMetadata: {
      SessionId: 'sess-orders',
      CorrelationId: `corr-${idx}-azure`,
      Region: customer.region,
    },
  };
}

function makeBillingMessage(isError: boolean, idx: number): object {
  const customer = randomFrom(CUSTOMERS);
  return {
    MessageType: 'BillingInvoiced',
    InvoiceId: `INV-AZ-${Date.now() + idx}`,
    CustomerId: customer.id,
    BillingAmount: (50 + idx * 15).toFixed(2),
    PaymentProvider: 'AzurePay',
    ResponseCode: isError ? 'Timeout' : 'Success',
    ErrorMessage: isError ? 'Timeout connection to bank gateway' : undefined,
    Timestamp: new Date().toISOString(),
  };
}

/**
 * Generates a realistic set of Azure Service Bus demo messages for the AzureDemoPage.
 */
export function generateAzureMockMessages(count = 50): Message[] {
  const messages: Message[] = [];

  for (let i = 0; i < count; i++) {
    const isError = i < 10;
    const isWarning = !isError && i < 20;
    const isDeadletter = i < 15;
    
    // Distribute messages among entities
    let entityName = 'orders-queue';
    let subscriptionName: string | undefined;

    const mod = i % 4;
    if (mod === 0) {
      entityName = 'orders-queue';
    } else if (mod === 1) {
      entityName = 'checkout-queue';
    } else if (mod === 2) {
      entityName = 'fulfillment-queue';
    } else {
      entityName = 'order-events';
      subscriptionName = ['inventory-sub', 'email-sub', 'billing-sub'][i % 3];
    }

    const status: MessageStatus = isError ? 'error' : isWarning ? 'warning' : 'success';
    const queueType: QueueType = isDeadletter ? 'deadletter' : 'active';

    let bodyObj: object;
    if (entityName.includes('billing') || (subscriptionName && subscriptionName.includes('billing'))) {
      bodyObj = makeBillingMessage(isError, i);
    } else {
      bodyObj = makeOrderMessage(isError, i);
    }

    const body = JSON.stringify(bodyObj, null, 2);
    const msgId = `azure-sb-msg-${Math.random().toString(36).substring(2, 12)}`;
    const minutesAgo = i * 4 + Math.floor(Math.random() * 6);

    const aiIssueTemplate = isError ? AZURE_AI_ISSUES[i % AZURE_AI_ISSUES.length] : undefined;

    messages.push({
      id: msgId,
      sequenceNumber: 200000 + i,
      enqueuedTime: timeAgo(minutesAgo),
      status,
      preview: isError
        ? `[ERROR] ${AZURE_ERROR_TYPES[i % AZURE_ERROR_TYPES.length].substring(0, 80)}`
        : isWarning
        ? `[WARN] ${entityName} — Lock renewal delay, message pending for ${minutesAgo}m`
        : `[OK] ${entityName} — Message successfully locked and processed.`,
      contentType: 'application/json' as ContentType,
      deliveryCount: isDeadletter ? 10 : 1,
      hasAIInsight: !!aiIssueTemplate,
      properties: {
        'servicebus:MessageId': msgId,
        'servicebus:SequenceNumber': 200000 + i,
        'servicebus:DeliveryCount': isDeadletter ? 10 : 1,
        'servicebus:TimeToLive': '14 days',
        'servicebus:CorrelationId': `corr-${Math.random().toString(36).substring(2, 10)}`,
        'servicebus:SessionId': entityName === 'checkout-queue' ? 'sess-orders' : undefined,
        'x-source-service': 'contoso-storefront',
        'x-client-ip': '127.0.0.1',
        'x-azure-region': 'eastus',
      } as Record<string, unknown>,
      queueType,
      body,
      headers: {
        'Content-Type': 'application/json',
        'BrokerProperties': JSON.stringify({
          MessageId: msgId,
          SequenceNumber: 200000 + i,
          DeliveryCount: isDeadletter ? 10 : 1,
          SessionId: entityName === 'checkout-queue' ? 'sess-orders' : undefined,
        }),
        'UserProperties': JSON.stringify({
          SourceService: 'contoso-storefront',
          Environment: 'production',
        }),
      },
      timeToLive: '14 days',
      lockToken: `lock-azure-${Math.random().toString(36).substring(2, 15)}`,
      eventType: (bodyObj as Record<string, string>)['MessageType'] ?? 'OrderReceived',
      displayTitle: `${(bodyObj as Record<string, string>)['MessageType'] ?? 'Message'} • ${entityName}`,
      deadLetterReason: isDeadletter ? 'MaxDeliveryCountExceeded' : undefined,
      deadLetterSource: isDeadletter ? (subscriptionName ? `${entityName}/Subscriptions/${subscriptionName}` : entityName) : undefined,
      aiAnalysis: aiIssueTemplate
        ? {
            issue: aiIssueTemplate.issue,
            recommendations: [...aiIssueTemplate.recommendations],
            detectedAt: new Date(timeAgo(minutesAgo).getTime() + 45_000),
          }
        : undefined,
    });
  }

  return messages;
}
