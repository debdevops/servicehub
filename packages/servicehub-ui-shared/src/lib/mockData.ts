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
  // Scheduled delivery
  scheduledEnqueueTime?: string;
  // AI Analysis (present when hasAIInsight is true)
  aiAnalysis?: AIAnalysis;
  // DLQ Intelligence history ID (when sourced from DLQ History)
  dlqId?: number;
  // Forensic replay safety verdict (when analysed through forensic engine)
  replaySafety?: 'Safe' | 'Unsafe' | 'RequiresReview' | null;
}

// ============================================================================
// Enterprise Demo Data — Realistic production-grade scenarios
// Company: Contoso Commerce Platform (B2B marketplace, 500k orders/month)
// ============================================================================

const CUSTOMER_COMPANIES = [
  'Northwind Trading Ltd', 'Fabrikam Corporation', 'Adventure Works LLC',
  'Blue Yonder Airlines', 'Tailspin Toys Inc', 'Wide World Importers',
  'Datum Corp', 'Wingtip Toys', 'Contoso Manufacturing', 'Litware Global',
  'Proseware Inc', 'A. Datum Corporation', 'Alpine Ski House',
];

const CUSTOMER_REFS: Record<string, string> = {
  'Northwind Trading Ltd': 'NW',
  'Fabrikam Corporation': 'FAB',
  'Adventure Works LLC': 'AW',
  'Blue Yonder Airlines': 'BYA',
  'Tailspin Toys Inc': 'TST',
  'Wide World Importers': 'WWI',
  'Datum Corp': 'DAT',
  'Wingtip Toys': 'WTT',
  'Contoso Manufacturing': 'CM',
  'Litware Global': 'LWG',
  'Proseware Inc': 'PRW',
  'A. Datum Corporation': 'ADC',
  'Alpine Ski House': 'ASH',
};

const ORDER_LINE_ITEMS = [
  { product: 'Enterprise Analytics Suite (Annual)', unitPrice: 4800.00, category: 'software' },
  { product: 'Cloud API Gateway Pro — 10M calls/mo', unitPrice: 1250.00, category: 'infrastructure' },
  { product: 'Business Intelligence Dashboard (3 seats)', unitPrice: 2400.00, category: 'software' },
  { product: 'Managed SQL Database — Standard S4', unitPrice: 890.00, category: 'database' },
  { product: 'IoT Device Management Platform (500 devices)', unitPrice: 3200.00, category: 'iot' },
  { product: 'Security & Compliance Module', unitPrice: 1800.00, category: 'security' },
  { product: 'Automated Workflow Engine — 50k runs/mo', unitPrice: 750.00, category: 'automation' },
  { product: 'Distributed Cache Cluster (16-node)', unitPrice: 5400.00, category: 'infrastructure' },
  { product: 'Real-time Event Streaming (10 partitions)', unitPrice: 2100.00, category: 'streaming' },
  { product: 'Identity & Access Management Suite', unitPrice: 1650.00, category: 'security' },
];

const PAYMENT_GATEWAYS = [
  { gateway: 'Stripe', code: 'STR' },
  { gateway: 'PayPal Commerce', code: 'PPL' },
  { gateway: 'Adyen', code: 'ADY' },
  { gateway: 'Braintree', code: 'BRT' },
  { gateway: 'Square', code: 'SQR' },
  { gateway: 'Worldpay', code: 'WPY' },
];

const PAYMENT_ERROR_CODES: Record<string, string> = {
  'insufficient_funds': 'Card declined — insufficient funds',
  'do_not_honor': 'Card declined — do_not_honor (bank refused)',
  'gateway_timeout': 'Gateway timeout after 30s — connection reset by peer',
  'duplicate_transaction': 'Duplicate transaction detected — idempotency key collision',
  'card_velocity_exceeded': 'Card velocity limit exceeded — 3 transactions in 60s',
  'invalid_cvv': 'CVV validation failed — card not present verification error',
  'expired_card': 'Card expired — expiry date 03/24 is in the past',
  'stolen_card': 'Card flagged as stolen — issuer declined with code 41',
};

const INVENTORY_WAREHOUSES = [
  { id: 'WH-LHR-01', location: 'London Heathrow Logistics Hub', region: 'EMEA' },
  { id: 'WH-JFK-02', location: 'Newark Distribution Centre', region: 'AMER' },
  { id: 'WH-SIN-03', location: 'Singapore Changi Fulfilment', region: 'APAC' },
  { id: 'WH-FRA-04', location: 'Frankfurt Rhine Warehouse', region: 'EMEA' },
  { id: 'WH-SEA-05', location: 'Seattle West Coast Hub', region: 'AMER' },
];

const NOTIFICATION_CHANNELS = [
  { type: 'email', provider: 'SendGrid', template: 'order_confirmation_v3' },
  { type: 'email', provider: 'SendGrid', template: 'invoice_ready_v2' },
  { type: 'sms', provider: 'Twilio', template: 'shipping_update' },
  { type: 'push', provider: 'Firebase FCM', template: 'payment_received' },
  { type: 'webhook', provider: 'Partner ERP', template: 'inventory_sync_v2' },
  { type: 'teams', provider: 'Microsoft Teams', template: 'ops_alert' },
];

const FRAUD_RISK_FACTORS = [
  ['unusual_geo', 'high_velocity'], ['card_testing_pattern'], ['multiple_failed_attempts'],
  ['ip_mismatch', 'device_fingerprint_anomaly'], ['account_age_low', 'high_order_value'],
  ['vpn_detected', 'unusual_geo'], ['chargeback_history', 'velocity_exceeded'],
];

const SHIPPING_CARRIERS = [
  { name: 'FedEx Priority', code: 'FEDEX', prefix: 'FX' },
  { name: 'DHL Express', code: 'DHL', prefix: 'DE' },
  { name: 'UPS Next Day Air', code: 'UPS', prefix: 'UPS' },
  { name: 'Royal Mail Special', code: 'RMSPL', prefix: 'RM' },
  { name: 'Parcelforce 48', code: 'PF48', prefix: 'PF' },
];

const INVOICE_STATUSES = ['awaiting_approval', 'approved', 'queued_for_payment', 'paid', 'disputed'];
const APPROVAL_TIERS = ['L1_Manager', 'L2_Finance', 'L3_CFO', 'Procurement_Committee'];

// ============================================================================
// Realistic preview texts — specific, feel like real production incidents
// ============================================================================

const PREVIEWS_SUCCESS = [
  'Order ORD-2026-%s processed — Northwind Trading Ltd, £14,850 — payment confirmed via Stripe',
  'Payment TXN-%s captured — Fabrikam Corporation, $6,240 — Adyen gateway, 0.8s latency',
  'Invoice INV-2026-%s approved by L2_Finance — Wide World Importers, £22,400 — queued for payment',
  'Inventory sync complete — WH-LHR-01 → WH-JFK-02 — 1,240 units of Enterprise Analytics Suite',
  'Shipping label generated — Blue Yonder Airlines, FedEx FX%s, estimated delivery D+2',
  'Fraud check cleared — Datum Corp, risk score 12/100 — all 6 factors within threshold',
  'Auto-replay rule triggered — 28 payment messages replayed successfully, 0 failures',
  'Webhook delivered — Tailspin Toys ERP (v2.1), 204ms, all 142 line items synced',
  'Order ORD-2026-%s shipped — Adventure Works LLC — DHL DE%s, in transit via Frankfurt',
  'Consumer group caught up — adventure-works-consumer lag 0ms after processing 3,241 messages',
];

const PREVIEWS_WARNING = [
  'Payment retry #2 — TXN-%s — Fabrikam Corp, Worldpay gateway, timeout after 30s (retry 3 of 5)',
  'Invoice INV-2026-%s pending — awaiting L3_CFO approval for 18h — SLA breach in 6h',
  'Inventory low stock alert — SKU:EDS-PRO-2026 — 12 units remain, 9 open orders pending',
  'Shipping delay — UPS UPS%s — customs hold at Frankfurt (HS code mismatch on 3 of 8 items)',
  'Duplicate order detected — ORD-2026-%s submitted twice in same customer session (60s apart)',
  'Consumer lag growing — notification-queue — 1,843 unprocessed messages, 12min behind real-time',
  'Fraud score elevated — Litware Global — riskScore 68/100 (vpn_detected, unusual_geo) — review',
  'Webhook delivering slowly — Proseware ERP — attempt 3 of 5, 5.2s timeout, retrying in 30s',
  'Schema warning — NotificationService received order body missing new "recipientTimezone" field',
  'Rate limit approaching — Stripe API at 87% of 100 req/s quota — throttling risk in ~8 min',
];

const PREVIEWS_ERROR = [
  'DLQ flood — payment-queue — 847 messages dead-lettered: MaxDeliveryCountExceeded after 5 retries',
  'Payment declined — TXN-%s — Northwind Trading Ltd £14,850 — do_not_honor (bank code 05)',
  'Order validation failed — ORD-2026-%s — OrderService rejected: "shippingAddress.postcode" null',
  'Fraud alert — Wingtip Toys — riskScore 94/100 — card_testing_pattern, 11 attempts in 4 minutes',
  'Shipping failed — DHL DE%s — address undeliverable: postcode SW1A 2AA not valid for carrier zone',
  'Invoice processing error — INV-2026-%s — Accounts Payable API returned 503 Service Unavailable',
  'TTL expired — ORD-2026-%s — message in queue 14 days without consumer pickup — moved to DLQ',
  'Schema mismatch — NotificationService v2.1 cannot deserialise OrderCreated v3.0 payload',
  'Database deadlock — OrderService.ProcessAsync() — 3 concurrent writes to Orders table, tx aborted',
  'Session lock expired — checkout-session-%s held 5 min — consumer reconnecting, message re-enqueued',
];

export const AI_ISSUES = [
  {
    issue: 'DLQ flood — 847 of 862 dead-lettered messages share the identical error signature: null reference in OrderValidationService.ProcessAsync() at line 312. Root cause: "shippingAddress.postcode" field introduced in order-schema v3.0 but consumer still running v2.8.',
    recommendations: [
      'Deploy OrderValidationService v2.9+ (null-safe postcode handling) — ETA 15 min',
      'After deployment, use Auto-Replay to bulk-replay the 847 affected messages',
      'Add schema version negotiation to prevent cross-version incompatibility in future',
    ],
  },
  {
    issue: 'Payment gateway cascade failure — Worldpay returning 504 Gateway Timeout consistently since 14:32 UTC. Retry backoff not working: all 5 retry attempts fire within 2 seconds instead of exponential spacing. This is flooding the retry queue.',
    recommendations: [
      'Fix retry backoff: implement exponential delay (2s, 4s, 8s, 16s, 32s) not fixed 0.5s',
      'Add circuit breaker: after 3 consecutive failures, route to Stripe fallback gateway',
      'Dead-letter after 5 attempts with DeadLetterReason="GatewayDown" for targeted replay later',
    ],
  },
  {
    issue: 'Message ordering violation in orders-queue — 23 messages processed out-of-sequence. OrderUpdated events arriving before OrderCreated for the same OrderId, causing foreign key violations in the Orders database.',
    recommendations: [
      'Enable Azure Service Bus Sessions keyed on OrderId to guarantee FIFO per customer order',
      'Add idempotency check in consumer: skip if OrderCreated not yet in DB, re-enqueue with delay',
      'Review partition key strategy — currently random, should be set to customerId',
    ],
  },
  {
    issue: 'Consumer group saturation — notification-queue has 1,843 backlogged messages. Single consumer instance processing at 2.3 msg/sec; at current rate backlog will clear in 13 minutes. However a new DLQ spike at 15:20 UTC could overwhelm it.',
    recommendations: [
      'Scale notification consumer to 3 instances immediately (Azure Container Apps: min replicas 3)',
      'Set queue max delivery count to 3 (currently 10) to reduce retry amplification',
      'Add dead-letter monitoring alert: notify #ops-alerts if DLQ count exceeds 50',
    ],
  },
  {
    issue: 'Fraud detection bypassed — 11 high-risk transactions from Wingtip Toys processed without fraud check completion. FraudCheckService timed out (60s) and the order pipeline proceeded without awaiting the fraud verdict. Risk exposure: £47,230.',
    recommendations: [
      'Make fraud check synchronous in the critical payment path — do not proceed on timeout',
      'Add compensating transaction: if FraudCheck times out, place order in "pending_review" state',
      'Increase FraudCheckService timeout budget from 60s to 120s and add dedicated node pool',
    ],
  },
  {
    issue: 'Shipping address validation failures concentrated in UK postcodes — Royal Mail API rejecting 38 of 42 UK addresses. Root cause: postcode format changed in service update (space stripped), "SW1A2AA" failing Royal Mail format validation expecting "SW1A 2AA".',
    recommendations: [
      'Fix postcode normalisation: add space before last 3 chars for UK postcodes before API call',
      'Replay the 38 failed shipping label messages after deploying the fix',
      'Add postcode format unit test with all Royal Mail edge-case formats',
    ],
  },
  {
    issue: 'Invoice approval SLA breach risk — 14 invoices from Wide World Importers and Fabrikam Corporation awaiting L3_CFO approval for 18+ hours. SLA requires approval within 24h. Breach in < 6h for 9 of them.',
    recommendations: [
      'Trigger escalation webhook to CFO TEAMS channel immediately (auto-alert rule)',
      'Configure approval reminder automation: resend after 12h, 20h, 23h intervals',
      'Review L3_CFO approval delegation — apply L2_Finance auto-approve for amounts < £25,000',
    ],
  },
  {
    issue: 'Duplicate message delivery — message deduplication window expired (60s) causing 34 OrderCreated events to be processed twice. Orders created for customers Datum Corp and Proseware Inc have duplicate line items. Billing impact: £18,400 over-charged.',
    recommendations: [
      'Extend deduplication window to 10 minutes (Service Bus property: DuplicateDetectionHistoryTimeWindow)',
      'Add idempotency guard in OrderService — check order exists before insert (upsert pattern)',
      'Reconcile and reverse the 34 duplicate charges before end of business today',
    ],
  },
];

const DEAD_LETTER_REASONS = [
  'MaxDeliveryCountExceeded',
  'TTLExpiredException',
  'MessageSizeExceeded',
  'SessionIdMismatch',
  'HeaderSizeExceeded',
  'FilterEvaluationException',
  'GatewayTimeout_RetryExhausted',
  'SchemaValidationFailure',
  'DownstreamServiceUnavailable',
];

// ============================================================================
// Helper Functions
// ============================================================================

function randomItem<T>(arr: T[]): T {
  return arr[Math.floor(Math.random() * arr.length)];
}

function randomInt(min: number, max: number): number {
  return Math.floor(Math.random() * (max - min + 1)) + min;
}

function seqId(): string {
  return String(randomInt(10000, 99999));
}

function generateOrderBody(): { body: string; contentType: ContentType; eventType: string } {
  const company = randomItem(CUSTOMER_COMPANIES);
  const ref = CUSTOMER_REFS[company] || 'CTM';
  const item = randomItem(ORDER_LINE_ITEMS);
  const qty = randomInt(1, 5);
  const amount = Math.round(item.unitPrice * qty * 100) / 100;
  const currency = randomItem(['USD', 'GBP', 'EUR']);
  const orderId = `ORD-2026-${String(randomInt(100000, 999999))}`;
  const customerRef = `${ref}-PO-2026-${String(randomInt(1000, 9999))}`;

  const body = {
    eventType: 'OrderCreated',
    schemaVersion: '3.1',
    orderId,
    customerRef,
    customer: {
      company,
      accountId: `ACC-${randomInt(10000, 99999)}`,
      billingRegion: randomItem(['EMEA', 'AMER', 'APAC']),
      accountManager: randomItem(['Sarah Chen', 'James Okafor', 'Priya Nair', 'Tom Bergström', 'Maria Santos']),
    },
    lineItems: [
      {
        lineId: 1,
        product: item.product,
        category: item.category,
        quantity: qty,
        unitPrice: item.unitPrice,
        currency,
        total: amount,
      },
    ],
    orderTotal: { amount, currency, vatIncluded: currency === 'GBP' || currency === 'EUR' },
    paymentMethod: randomItem(['bank_transfer', 'credit_card', 'purchase_order', 'direct_debit']),
    status: randomItem(['pending_validation', 'validated', 'processing', 'shipped', 'delivered']),
    timestamps: {
      created: new Date(Date.now() - randomInt(60000, 86400000)).toISOString(),
      lastUpdated: new Date().toISOString(),
    },
    sourceService: 'OrderManagementService',
    correlationId: `corr-${Math.random().toString(36).substring(2, 11)}`,
  };
  return { body: JSON.stringify(body, null, 2), contentType: 'application/json', eventType: 'OrderCreated' };
}

function generatePaymentBody(): { body: string; contentType: ContentType; eventType: string } {
  const gwInfo = randomItem(PAYMENT_GATEWAYS);
  const company = randomItem(CUSTOMER_COMPANIES);
  const amount = Math.round(randomInt(500, 50000) * 100) / 100;
  const currency = randomItem(['USD', 'GBP', 'EUR']);
  const errorCode = randomItem(Object.keys(PAYMENT_ERROR_CODES));
  const txnId = `${gwInfo.code}-TXN-${String(randomInt(1000000000, 9999999999))}`;

  const body = {
    eventType: 'PaymentProcessed',
    schemaVersion: '2.4',
    transactionId: txnId,
    externalRef: `${gwInfo.code}-${String(randomInt(100000000, 999999999))}`,
    payer: { company, accountId: `ACC-${randomInt(10000, 99999)}` },
    amount: { value: amount, currency },
    gateway: {
      provider: gwInfo.gateway,
      environment: 'production',
      processingTimeMs: randomInt(120, 8500),
    },
    status: randomItem(['authorized', 'captured', 'declined', 'pending_3ds', 'refunded']),
    failureDetail: Math.random() < 0.4 ? {
      errorCode,
      message: PAYMENT_ERROR_CODES[errorCode],
      retryCount: randomInt(1, 5),
      retryable: !['stolen_card', 'do_not_honor'].includes(errorCode),
    } : undefined,
    timestamps: {
      initiated: new Date(Date.now() - randomInt(30000, 3600000)).toISOString(),
      completed: new Date().toISOString(),
    },
    sourceService: 'PaymentProcessingService',
  };
  return { body: JSON.stringify(body, null, 2), contentType: 'application/json', eventType: 'PaymentProcessed' };
}

function generateInventoryBody(): { body: string; contentType: ContentType; eventType: string } {
  const warehouse = randomItem(INVENTORY_WAREHOUSES);
  const item = randomItem(ORDER_LINE_ITEMS);
  const current = randomInt(0, 2000);
  const threshold = randomInt(50, 200);

  const body = {
    eventType: 'InventoryUpdated',
    schemaVersion: '1.8',
    warehouseId: warehouse.id,
    warehouseName: warehouse.location,
    region: warehouse.region,
    product: {
      sku: `SKU-${item.category.toUpperCase().substring(0, 3)}-${String(randomInt(1000, 9999))}`,
      name: item.product,
      category: item.category,
    },
    stockLevel: { current, threshold, unit: 'licenses' },
    alert: current === 0 ? 'out_of_stock' : current < threshold ? 'low_stock' : 'normal',
    pendingOrders: randomInt(0, 25),
    transitStock: randomInt(0, 500),
    lastPhysicalCount: new Date(Date.now() - randomInt(86400000, 604800000)).toISOString(),
    sourceService: 'InventoryManagementService',
  };
  return { body: JSON.stringify(body, null, 2), contentType: 'application/json', eventType: 'InventoryUpdated' };
}

function generateNotificationBody(): { body: string; contentType: ContentType; eventType: string } {
  const channel = randomItem(NOTIFICATION_CHANNELS);
  const company = randomItem(CUSTOMER_COMPANIES);

  const body = {
    eventType: 'NotificationDispatched',
    schemaVersion: '2.0',
    notificationId: `NOTIF-${String(randomInt(100000, 999999))}`,
    channel: channel.type,
    provider: channel.provider,
    template: channel.template,
    recipient: channel.type === 'email'
      ? `accounts@${company.toLowerCase().replace(/[^a-z]/g, '').substring(0, 10)}.com`
      : channel.type === 'sms' ? `+44${randomInt(7700000000, 7799999999)}`
      : channel.type === 'webhook' ? `https://erp.${company.toLowerCase().replace(/[^a-z]/g, '').substring(0, 8)}.com/api/v2/webhook`
      : `team-${randomItem(['ops', 'finance', 'procurement', 'engineering'])}`,
    subject: randomItem([
      'Your order has been confirmed', 'Invoice ready for review', 'Shipment dispatched',
      'Payment received — thank you', 'Action required: PO approval', 'Account statement ready',
    ]),
    deliveryStatus: randomItem(['sent', 'delivered', 'failed', 'bounced', 'pending']),
    attemptCount: randomInt(1, 4),
    latencyMs: randomInt(80, 12000),
    sourceService: 'CustomerEngagementService',
  };
  return { body: JSON.stringify(body, null, 2), contentType: 'application/json', eventType: 'NotificationDispatched' };
}

function generateFraudBody(): { body: string; contentType: ContentType; eventType: string } {
  const company = randomItem(CUSTOMER_COMPANIES);
  const riskScore = randomInt(10, 98);
  const riskFactors = randomItem(FRAUD_RISK_FACTORS);
  const amount = randomInt(2000, 80000);

  const body = {
    eventType: 'FraudCheckResult',
    schemaVersion: '1.3',
    alertId: `FRD-${String(randomInt(100000, 999999))}`,
    orderId: `ORD-2026-${String(randomInt(100000, 999999))}`,
    customer: { company, accountId: `ACC-${randomInt(10000, 99999)}` },
    transaction: { amount, currency: 'GBP', gatewayRef: `STR-${randomInt(100000000, 999999999)}` },
    riskAssessment: {
      score: riskScore,
      verdict: riskScore > 80 ? 'HIGH_RISK' : riskScore > 50 ? 'MEDIUM_RISK' : 'LOW_RISK',
      riskFactors,
      modelVersion: 'fraud-ml-v4.2',
      processingTimeMs: randomInt(200, 2000),
    },
    action: riskScore > 80 ? 'block_and_review' : riskScore > 50 ? 'review_required' : 'approve',
    sourceService: 'FraudDetectionService',
    timestamp: new Date().toISOString(),
  };
  return { body: JSON.stringify(body, null, 2), contentType: 'application/json', eventType: 'FraudCheckResult' };
}

function generateInvoiceBody(): { body: string; contentType: ContentType; eventType: string } {
  const company = randomItem(CUSTOMER_COMPANIES);
  const ref = CUSTOMER_REFS[company] || 'CTM';
  const amount = randomInt(5000, 150000);
  const tier = randomItem(APPROVAL_TIERS);
  const invoiceId = `INV-2026-${String(randomInt(10000, 99999))}`;

  const body = {
    eventType: 'InvoiceProcessing',
    schemaVersion: '1.5',
    invoiceId,
    vendor: company,
    purchaseOrder: `${ref}-PO-2026-${String(randomInt(1000, 9999))}`,
    amount: { value: amount, currency: randomItem(['GBP', 'USD', 'EUR']) },
    approvalWorkflow: {
      requiredTier: tier,
      currentStatus: randomItem(INVOICE_STATUSES),
      submittedAt: new Date(Date.now() - randomInt(3600000, 86400000 * 3)).toISOString(),
      slaHours: randomInt(8, 48),
      hoursElapsed: randomInt(1, 50),
      escalated: Math.random() < 0.3,
    },
    lineItems: randomInt(1, 12),
    attachmentCount: randomInt(1, 4),
    sourceService: 'InvoiceProcessingService',
  };
  return { body: JSON.stringify(body, null, 2), contentType: 'application/json', eventType: 'InvoiceProcessing' };
}

function generateShippingBody(): { body: string; contentType: ContentType; eventType: string } {
  const carrier = randomItem(SHIPPING_CARRIERS);
  const warehouse = randomItem(INVENTORY_WAREHOUSES);
  const company = randomItem(CUSTOMER_COMPANIES);
  const tracking = `${carrier.prefix}${randomInt(1000000000, 9999999999)}`;

  const body = {
    eventType: 'ShipmentCreated',
    schemaVersion: '2.2',
    shipmentId: `SHIP-2026-${String(randomInt(10000, 99999))}`,
    orderId: `ORD-2026-${String(randomInt(100000, 999999))}`,
    recipient: { company, deliveryContact: randomItem(['Goods_In', 'Reception', 'IT_Department', 'Finance']) },
    carrier: { name: carrier.name, code: carrier.code, trackingNumber: tracking },
    origin: { warehouseId: warehouse.id, name: warehouse.location, region: warehouse.region },
    destination: {
      country: randomItem(['GB', 'US', 'DE', 'FR', 'NL', 'SG', 'AU']),
      postcode: randomItem(['EC2A 4NE', 'W1D 3QZ', '10001', '75001', '60311', '048624', '2000']),
    },
    estimatedDelivery: new Date(Date.now() + randomInt(86400000, 86400000 * 5)).toISOString().split('T')[0],
    status: randomItem(['label_created', 'collected', 'in_transit', 'customs_hold', 'out_for_delivery', 'delivered']),
    packageDetails: { count: randomInt(1, 8), totalWeightKg: Math.round(randomInt(1, 200) * 0.5) / 1 },
    sourceService: 'LogisticsService',
  };
  return { body: JSON.stringify(body, null, 2), contentType: 'application/json', eventType: 'ShipmentCreated' };
}

function generateMessageBody(): { body: string; contentType: ContentType; eventType?: string } {
  const roll = Math.random();
  if (roll < 0.30) return generateOrderBody();
  if (roll < 0.50) return generatePaymentBody();
  if (roll < 0.62) return generateInventoryBody();
  if (roll < 0.72) return generateNotificationBody();
  if (roll < 0.82) return generateFraudBody();
  if (roll < 0.91) return generateInvoiceBody();
  return generateShippingBody();
}

function generateHeaders(eventType: string): Record<string, string> {
  const headers: Record<string, string> = {};
  const service = {
    OrderCreated: 'OrderManagementService',
    PaymentProcessed: 'PaymentProcessingService',
    InventoryUpdated: 'InventoryManagementService',
    NotificationDispatched: 'CustomerEngagementService',
    FraudCheckResult: 'FraudDetectionService',
    InvoiceProcessing: 'InvoiceProcessingService',
    ShipmentCreated: 'LogisticsService',
  }[eventType] || 'ContosoCommerceBackend';

  headers['Content-Type'] = 'application/json; charset=utf-8';
  headers['Message-Id'] = crypto.randomUUID?.() || `msg-${Math.random().toString(36).substring(2, 15)}`;
  headers['Correlation-Id'] = `corr-${Math.random().toString(36).substring(2, 11)}`;
  headers['x-contoso-source-service'] = service;
  headers['x-contoso-schema-version'] = randomItem(['1.0', '2.0', '2.4', '3.0', '3.1']);
  headers['x-contoso-event-version'] = '1';
  headers['Label'] = eventType;
  headers['Partition-Key'] = `partition-${randomInt(0, 15)}`;
  if (Math.random() < 0.6) headers['Session-Id'] = `session-${randomInt(10000, 99999)}`;
  if (Math.random() < 0.4) headers['Reply-To'] = 'response-processed-queue';

  return headers;
}

function generateTimeToLive(): string {
  const options = ['1d 0h 0m 0s', '7d 0h 0m 0s', '14d 0h 0m 0s', '0d 4h 0m 0s', '0d 12h 0m 0s'];
  return randomItem(options);
}

function getPreview(status: MessageStatus): string {
  const s = seqId();
  const templates =
    status === 'success' ? PREVIEWS_SUCCESS :
    status === 'warning' ? PREVIEWS_WARNING :
    PREVIEWS_ERROR;

  const template = randomItem(templates);
  return template.replace(/%s/g, s);
}

// ============================================================================
// Main Generator
// ============================================================================

export function generateMockMessages(count: number): Message[] {
  const statusWeights = [0.55, 0.30, 0.15]; // 55% success, 30% warning, 15% error

  const messages: Message[] = [];
  const now = new Date();

  for (let i = 0; i < count; i++) {
    const statusRoll = Math.random();
    let status: MessageStatus;
    if (statusRoll < statusWeights[0]) {
      status = 'success';
    } else if (statusRoll < statusWeights[0] + statusWeights[1]) {
      status = 'warning';
    } else {
      status = 'error';
    }

    const isDeadLetter = status === 'error' ? Math.random() < 0.5 : Math.random() < 0.08;
    const queueType: QueueType = isDeadLetter ? 'deadletter' : 'active';

    // Ensure status is always semantically consistent with queue location.
    // An active-queue message can never be 'error' (Dead-Letter) — it can only be
    // 'success' (normal delivery) or 'warning' (retried, multiple delivery attempts).
    // The 'error' status exclusively indicates a DLQ message.
    const effectiveStatus: MessageStatus = queueType === 'active' && status === 'error' ? 'warning' : status;

    // Spread across last 72 hours — concentrated in last 4h (more realistic peak traffic)
    const recentBias = Math.random() < 0.6;
    const minutesAgo = recentBias ? randomInt(0, 240) : randomInt(240, 4320);
    const enqueuedTime = new Date(now.getTime() - minutesAgo * 60 * 1000);

    const { body, contentType, eventType } = generateMessageBody();

    let deliveryCount: number;
    if (effectiveStatus === 'error') deliveryCount = randomInt(3, 10);
    else if (effectiveStatus === 'warning') deliveryCount = randomInt(2, 5);
    else deliveryCount = randomInt(0, 1);

    const hasAIInsight = effectiveStatus === 'error'
      ? Math.random() < 0.75
      : effectiveStatus === 'warning'
        ? Math.random() < 0.45
        : Math.random() < 0.08;

    const message: Message = {
      id: `msg-${Math.random().toString(36).substring(2, 6)}-${String(i + 1).padStart(6, '0')}`,
      enqueuedTime,
      status: effectiveStatus,
      preview: getPreview(effectiveStatus),
      contentType,
      deliveryCount,
      hasAIInsight,
      sequenceNumber: 10000000 + i,
      properties: {
        correlationId: `corr-${Math.random().toString(36).substring(2, 11)}`,
        sourceService: randomItem([
          'OrderManagementService', 'PaymentProcessingService', 'InventoryManagementService',
          'CustomerEngagementService', 'FraudDetectionService', 'InvoiceProcessingService', 'LogisticsService',
        ]),
        schemaVersion: randomItem(['2.4', '3.0', '3.1']),
        priority: randomItem(['normal', 'high', 'critical']),
        region: randomItem(['EMEA', 'AMER', 'APAC']),
        environment: 'production',
      },
      queueType,
      body,
      headers: generateHeaders(eventType || 'GenericEvent'),
      timeToLive: generateTimeToLive(),
      lockToken: `lock-${Math.random().toString(36).substring(2, 15)}-${Math.random().toString(36).substring(2, 15)}`,
      eventType,
    };

    if (queueType === 'deadletter') {
      message.deadLetterReason = randomItem(DEAD_LETTER_REASONS);
      message.deadLetterSource = randomItem([
        'OrderManagementService', 'PaymentProcessingService', 'InventoryManagementService',
        'CustomerEngagementService', 'FraudDetectionService',
      ]);
    }

    if (hasAIInsight) {
      const aiTemplate = randomItem(AI_ISSUES);
      message.aiAnalysis = {
        issue: aiTemplate.issue,
        recommendations: [...aiTemplate.recommendations],
        detectedAt: new Date(enqueuedTime.getTime() + randomInt(30000, 300000)),
      };
    }

    messages.push(message);
  }

  messages.sort((a, b) => b.enqueuedTime.getTime() - a.enqueuedTime.getTime());
  return messages;
}

// ============================================================================
// Export 100,000 messages for UI stress testing
// ============================================================================

export const MOCK_MESSAGES = generateMockMessages(100000);
