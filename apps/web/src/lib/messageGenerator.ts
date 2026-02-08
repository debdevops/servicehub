// ============================================================================
// Message Generator - Realistic Azure Service Bus Message Generation
// ============================================================================
// This module generates production-like messages for demo, testing, and AI validation.
// All generated messages are tagged with metadata for identification and cleanup.

// UUID v4 generator using crypto API
function generateUUID(): string {
  return crypto.randomUUID();
}

// ============================================================================
// Types
// ============================================================================

export type MessageScenario = 
  | 'order-processing'
  | 'payment-gateway'
  | 'notification-service'
  | 'inventory-update'
  | 'user-activity'
  | 'error-handling';

export type AnomalyType = 
  | 'none'
  | 'dlq-candidate'
  | 'retry-loop'
  | 'poison-message'
  | 'latency-spike';

export interface GeneratedMessage {
  body: string;
  contentType: string;
  properties: Record<string, any>;
  correlationId: string;
  sessionId?: string;
  scenario: MessageScenario;
  anomalyType: AnomalyType;
}

export interface GenerationConfig {
  targetType: 'queue' | 'topic' | 'both';
  queueName?: string;
  topicName?: string;
  subscriptionName?: string;
  volume: number;
  scenarios: MessageScenario[];
  anomalyRate: number; // 0-100 percentage
  includeStructuredData: boolean;
}

// ============================================================================
// Constants
// ============================================================================

const GENERATOR_TAG = 'ServiceHub-Generated';
const GENERATOR_VERSION = '1.0.0';

// Realistic company/service names
const COMPANIES = [
  'Contoso', 'Fabrikam', 'Northwind', 'AdventureWorks', 'TailSpin', 
  'WideWorld', 'GraphicDesign', 'LitWare', 'Proseware', 'VanArsdel'
];

const REGIONS = ['us-east-1', 'us-west-2', 'eu-west-1', 'ap-southeast-1', 'eu-central-1'];
const ENVIRONMENTS = ['production', 'staging', 'development'];

// ============================================================================
// Scenario-Specific Message Generators
// ============================================================================

function generateOrderProcessingMessage(isAnomaly: boolean, anomalyType: AnomalyType): GeneratedMessage {
  const orderId = `ORD-${Date.now().toString(36).toUpperCase()}-${Math.random().toString(36).substring(2, 6).toUpperCase()}`;
  const customerId = `CUST-${Math.random().toString(36).substring(2, 10).toUpperCase()}`;
  const company = COMPANIES[Math.floor(Math.random() * COMPANIES.length)];
  const correlationId = generateUUID();
  
  const items = Array.from({ length: Math.floor(Math.random() * 5) + 1 }, () => ({
    sku: `SKU-${Math.random().toString(36).substring(2, 8).toUpperCase()}`,
    name: ['Wireless Mouse', 'USB-C Hub', 'Mechanical Keyboard', 'Monitor Stand', '4K Webcam', 'Desk Lamp', 'Ergonomic Chair'][Math.floor(Math.random() * 7)],
    quantity: Math.floor(Math.random() * 5) + 1,
    unitPrice: parseFloat((Math.random() * 200 + 10).toFixed(2)),
  }));

  const subtotal = items.reduce((sum, item) => sum + (item.quantity * item.unitPrice), 0);
  const tax = subtotal * 0.08;
  const total = subtotal + tax;

  let status = 'pending';
  let errorDetails = null;
  let deliveryCount = 1;

  if (isAnomaly) {
    switch (anomalyType) {
      case 'dlq-candidate':
        status = 'validation-failed';
        errorDetails = {
          code: 'INVALID_SHIPPING_ADDRESS',
          message: 'The shipping address could not be validated. ZIP code does not match city/state combination.',
          timestamp: new Date().toISOString(),
          retryable: false,
        };
        break;
      case 'retry-loop':
        status = 'processing';
        deliveryCount = Math.floor(Math.random() * 8) + 3; // 3-10 retries
        errorDetails = {
          code: 'INVENTORY_LOCK_TIMEOUT',
          message: `Failed to acquire inventory lock for SKU ${items[0].sku}. Concurrent update detected.`,
          timestamp: new Date().toISOString(),
          retryable: true,
          attemptNumber: deliveryCount,
        };
        break;
      case 'poison-message':
        status = 'corrupted';
        errorDetails = {
          code: 'SCHEMA_VALIDATION_FAILED',
          message: 'Message body contains malformed JSON. Expected number for "quantity", received string.',
          timestamp: new Date().toISOString(),
          retryable: false,
        };
        break;
      case 'latency-spike':
        status = 'processing-slow';
        errorDetails = {
          code: 'DOWNSTREAM_LATENCY',
          message: 'Payment gateway response time exceeded 30s threshold. Circuit breaker triggered.',
          timestamp: new Date().toISOString(),
          latencyMs: Math.floor(Math.random() * 45000) + 30000,
        };
        break;
    }
  }

  const body = JSON.stringify({
    eventType: 'OrderCreated',
    eventVersion: '2.1',
    timestamp: new Date().toISOString(),
    source: `${company.toLowerCase()}-order-service`,
    data: {
      orderId,
      customerId,
      customerEmail: `customer.${customerId.toLowerCase()}@${company.toLowerCase()}.com`,
      status,
      items,
      pricing: {
        subtotal: parseFloat(subtotal.toFixed(2)),
        tax: parseFloat(tax.toFixed(2)),
        total: parseFloat(total.toFixed(2)),
        currency: 'USD',
      },
      shipping: {
        method: ['standard', 'express', 'overnight'][Math.floor(Math.random() * 3)],
        address: {
          street: `${Math.floor(Math.random() * 9999) + 1} ${['Main', 'Oak', 'Pine', 'Maple', 'Cedar'][Math.floor(Math.random() * 5)]} Street`,
          city: ['Seattle', 'Portland', 'San Francisco', 'Los Angeles', 'Denver'][Math.floor(Math.random() * 5)],
          state: ['WA', 'OR', 'CA', 'CA', 'CO'][Math.floor(Math.random() * 5)],
          zipCode: String(Math.floor(Math.random() * 90000) + 10000),
          country: 'US',
        },
        estimatedDelivery: new Date(Date.now() + (Math.random() * 7 + 3) * 24 * 60 * 60 * 1000).toISOString(),
      },
      metadata: {
        userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36',
        ipAddress: `${Math.floor(Math.random() * 255)}.${Math.floor(Math.random() * 255)}.${Math.floor(Math.random() * 255)}.${Math.floor(Math.random() * 255)}`,
        sessionId: generateUUID(),
      },
      ...(errorDetails && { error: errorDetails }),
    },
  }, null, 2);

  return {
    body,
    contentType: 'application/json',
    properties: {
      [GENERATOR_TAG]: 'true',
      generatorVersion: GENERATOR_VERSION,
      generatedAt: new Date().toISOString(),
      scenario: 'order-processing',
      anomalyType,
      messageType: 'OrderCreated',
      source: `${company.toLowerCase()}-order-service`,
      environment: ENVIRONMENTS[Math.floor(Math.random() * ENVIRONMENTS.length)],
      region: REGIONS[Math.floor(Math.random() * REGIONS.length)],
      priority: items.some(i => i.unitPrice > 100) ? 'high' : 'normal',
      customerId,
      orderId,
    },
    correlationId,
    sessionId: customerId,
    scenario: 'order-processing',
    anomalyType,
  };
}

function generatePaymentMessage(isAnomaly: boolean, anomalyType: AnomalyType): GeneratedMessage {
  const transactionId = `TXN-${Date.now().toString(36).toUpperCase()}-${Math.random().toString(36).substring(2, 6).toUpperCase()}`;
  const orderId = `ORD-${Math.random().toString(36).substring(2, 10).toUpperCase()}`;
  const correlationId = generateUUID();
  const company = COMPANIES[Math.floor(Math.random() * COMPANIES.length)];
  
  const amount = parseFloat((Math.random() * 2000 + 10).toFixed(2));
  const paymentMethod = ['credit_card', 'debit_card', 'paypal', 'bank_transfer', 'apple_pay'][Math.floor(Math.random() * 5)];
  
  let status = 'completed';
  let errorDetails = null;

  if (isAnomaly) {
    switch (anomalyType) {
      case 'dlq-candidate':
        status = 'declined';
        errorDetails = {
          code: 'CARD_DECLINED',
          message: 'The card was declined by the issuing bank. Reason: insufficient funds.',
          declineCode: 'insufficient_funds',
          timestamp: new Date().toISOString(),
        };
        break;
      case 'retry-loop':
        status = 'pending';
        errorDetails = {
          code: 'GATEWAY_TIMEOUT',
          message: 'Payment gateway did not respond within timeout period. Request will be retried.',
          timestamp: new Date().toISOString(),
          retryCount: Math.floor(Math.random() * 5) + 2,
        };
        break;
      case 'poison-message':
        status = 'error';
        errorDetails = {
          code: 'INVALID_CARD_NUMBER',
          message: 'Card number failed Luhn check validation. Possible data corruption.',
          timestamp: new Date().toISOString(),
        };
        break;
      case 'latency-spike':
        status = 'processing';
        errorDetails = {
          code: 'FRAUD_CHECK_DELAY',
          message: 'Extended fraud analysis required for high-value transaction.',
          timestamp: new Date().toISOString(),
          analysisTimeMs: Math.floor(Math.random() * 60000) + 30000,
        };
        break;
    }
  }

  const body = JSON.stringify({
    eventType: 'PaymentProcessed',
    eventVersion: '1.3',
    timestamp: new Date().toISOString(),
    source: `${company.toLowerCase()}-payment-gateway`,
    data: {
      transactionId,
      orderId,
      amount,
      currency: 'USD',
      paymentMethod,
      status,
      processorResponse: {
        code: status === 'completed' ? 'APPROVED' : 'DECLINED',
        message: status === 'completed' ? 'Transaction approved' : errorDetails?.message,
        authorizationCode: status === 'completed' ? Math.random().toString(36).substring(2, 8).toUpperCase() : null,
        avsResult: 'Y',
        cvvResult: 'M',
      },
      billing: {
        name: `${['John', 'Jane', 'Michael', 'Sarah', 'David'][Math.floor(Math.random() * 5)]} ${['Smith', 'Johnson', 'Williams', 'Brown', 'Jones'][Math.floor(Math.random() * 5)]}`,
        email: `billing@${company.toLowerCase()}.com`,
        phone: `+1-${Math.floor(Math.random() * 900) + 100}-${Math.floor(Math.random() * 900) + 100}-${Math.floor(Math.random() * 9000) + 1000}`,
      },
      riskScore: Math.floor(Math.random() * 100),
      ...(errorDetails && { error: errorDetails }),
    },
  }, null, 2);

  return {
    body,
    contentType: 'application/json',
    properties: {
      [GENERATOR_TAG]: 'true',
      generatorVersion: GENERATOR_VERSION,
      generatedAt: new Date().toISOString(),
      scenario: 'payment-gateway',
      anomalyType,
      messageType: 'PaymentProcessed',
      source: `${company.toLowerCase()}-payment-gateway`,
      environment: ENVIRONMENTS[Math.floor(Math.random() * ENVIRONMENTS.length)],
      region: REGIONS[Math.floor(Math.random() * REGIONS.length)],
      transactionId,
      orderId,
      paymentStatus: status,
      amount: String(amount),
    },
    correlationId,
    scenario: 'payment-gateway',
    anomalyType,
  };
}

function generateNotificationMessage(isAnomaly: boolean, anomalyType: AnomalyType): GeneratedMessage {
  const notificationId = `NOTIF-${Date.now().toString(36).toUpperCase()}-${Math.random().toString(36).substring(2, 6).toUpperCase()}`;
  const correlationId = generateUUID();
  const company = COMPANIES[Math.floor(Math.random() * COMPANIES.length)];
  
  const notificationTypes = ['email', 'sms', 'push', 'webhook'];
  const notificationType = notificationTypes[Math.floor(Math.random() * notificationTypes.length)];
  
  const templates = {
    email: ['order-confirmation', 'shipping-update', 'password-reset', 'welcome', 'invoice'],
    sms: ['otp-verification', 'delivery-alert', 'payment-confirmation', 'appointment-reminder'],
    push: ['new-message', 'price-drop', 'back-in-stock', 'flash-sale'],
    webhook: ['order-status', 'inventory-update', 'customer-event', 'refund-processed'],
  };

  const template = templates[notificationType as keyof typeof templates][
    Math.floor(Math.random() * templates[notificationType as keyof typeof templates].length)
  ];

  let status = 'delivered';
  let errorDetails = null;

  if (isAnomaly) {
    switch (anomalyType) {
      case 'dlq-candidate':
        status = 'bounced';
        errorDetails = {
          code: 'RECIPIENT_NOT_FOUND',
          message: 'Email address does not exist or mailbox is full.',
          bounceType: 'hard',
          timestamp: new Date().toISOString(),
        };
        break;
      case 'retry-loop':
        status = 'retrying';
        errorDetails = {
          code: 'SMTP_TEMPORARY_FAILURE',
          message: 'Recipient server temporarily unavailable. Will retry.',
          timestamp: new Date().toISOString(),
          retryCount: Math.floor(Math.random() * 6) + 2,
        };
        break;
      case 'poison-message':
        status = 'failed';
        errorDetails = {
          code: 'TEMPLATE_RENDER_ERROR',
          message: 'Failed to render notification template. Missing required variable: {{customerName}}',
          timestamp: new Date().toISOString(),
        };
        break;
      case 'latency-spike':
        status = 'queued';
        errorDetails = {
          code: 'RATE_LIMITED',
          message: 'Notification rate limit exceeded. Message queued for delayed delivery.',
          timestamp: new Date().toISOString(),
          estimatedDeliveryMs: Math.floor(Math.random() * 300000) + 60000,
        };
        break;
    }
  }

  const recipients = Array.from(
    { length: Math.floor(Math.random() * 3) + 1 },
    () => `user${Math.floor(Math.random() * 10000)}@${company.toLowerCase()}.com`
  );

  const body = JSON.stringify({
    eventType: 'NotificationSent',
    eventVersion: '1.0',
    timestamp: new Date().toISOString(),
    source: `${company.toLowerCase()}-notification-service`,
    data: {
      notificationId,
      type: notificationType,
      template,
      status,
      recipients,
      subject: notificationType === 'email' 
        ? `[${company}] Your ${template.replace('-', ' ')} notification`
        : undefined,
      content: {
        preview: `This is a ${template} notification from ${company}. Your recent activity has triggered this automated message.`,
        variables: {
          companyName: company,
          timestamp: new Date().toISOString(),
          actionUrl: `https://${company.toLowerCase()}.com/action/${notificationId}`,
        },
      },
      delivery: {
        attempts: status === 'delivered' ? 1 : Math.floor(Math.random() * 5) + 1,
        provider: ['sendgrid', 'mailgun', 'twilio', 'firebase'][Math.floor(Math.random() * 4)],
        sentAt: new Date().toISOString(),
        deliveredAt: status === 'delivered' ? new Date().toISOString() : null,
      },
      ...(errorDetails && { error: errorDetails }),
    },
  }, null, 2);

  return {
    body,
    contentType: 'application/json',
    properties: {
      [GENERATOR_TAG]: 'true',
      generatorVersion: GENERATOR_VERSION,
      generatedAt: new Date().toISOString(),
      scenario: 'notification-service',
      anomalyType,
      messageType: 'NotificationSent',
      source: `${company.toLowerCase()}-notification-service`,
      environment: ENVIRONMENTS[Math.floor(Math.random() * ENVIRONMENTS.length)],
      region: REGIONS[Math.floor(Math.random() * REGIONS.length)],
      notificationType,
      template,
      notificationStatus: status,
    },
    correlationId,
    scenario: 'notification-service',
    anomalyType,
  };
}

function generateInventoryMessage(isAnomaly: boolean, anomalyType: AnomalyType): GeneratedMessage {
  const eventId = `INV-${Date.now().toString(36).toUpperCase()}-${Math.random().toString(36).substring(2, 6).toUpperCase()}`;
  const correlationId = generateUUID();
  const company = COMPANIES[Math.floor(Math.random() * COMPANIES.length)];
  
  const skus = Array.from({ length: Math.floor(Math.random() * 3) + 1 }, () => ({
    sku: `SKU-${Math.random().toString(36).substring(2, 8).toUpperCase()}`,
    productName: ['Laptop', 'Tablet', 'Headphones', 'Smartwatch', 'Camera', 'Speaker'][Math.floor(Math.random() * 6)],
    warehouse: ['WH-EAST', 'WH-WEST', 'WH-CENTRAL', 'WH-SOUTH'][Math.floor(Math.random() * 4)],
    previousQuantity: Math.floor(Math.random() * 100),
    newQuantity: Math.floor(Math.random() * 100),
    changeType: ['receipt', 'shipment', 'adjustment', 'transfer'][Math.floor(Math.random() * 4)],
  }));

  let status = 'committed';
  let errorDetails = null;

  if (isAnomaly) {
    switch (anomalyType) {
      case 'dlq-candidate':
        status = 'rejected';
        errorDetails = {
          code: 'NEGATIVE_INVENTORY',
          message: `Cannot reduce inventory below zero for SKU ${skus[0].sku}. Current: ${skus[0].previousQuantity}, Requested change: -${skus[0].previousQuantity + 10}`,
          timestamp: new Date().toISOString(),
        };
        break;
      case 'retry-loop':
        status = 'pending';
        errorDetails = {
          code: 'WAREHOUSE_SYNC_CONFLICT',
          message: 'Optimistic locking failure. Inventory was modified by another process.',
          timestamp: new Date().toISOString(),
          retryCount: Math.floor(Math.random() * 7) + 3,
        };
        break;
      case 'poison-message':
        status = 'invalid';
        errorDetails = {
          code: 'UNKNOWN_SKU',
          message: `SKU ${skus[0].sku} does not exist in the product catalog.`,
          timestamp: new Date().toISOString(),
        };
        break;
      case 'latency-spike':
        status = 'processing';
        errorDetails = {
          code: 'CROSS_REGION_SYNC',
          message: 'Multi-region inventory synchronization in progress.',
          timestamp: new Date().toISOString(),
          syncDurationMs: Math.floor(Math.random() * 120000) + 60000,
        };
        break;
    }
  }

  const body = JSON.stringify({
    eventType: 'InventoryUpdated',
    eventVersion: '2.0',
    timestamp: new Date().toISOString(),
    source: `${company.toLowerCase()}-inventory-service`,
    data: {
      eventId,
      status,
      items: skus.map(sku => ({
        ...sku,
        delta: sku.newQuantity - sku.previousQuantity,
        timestamp: new Date().toISOString(),
      })),
      summary: {
        totalItemsAffected: skus.length,
        totalUnitsChanged: skus.reduce((sum, s) => sum + Math.abs(s.newQuantity - s.previousQuantity), 0),
      },
      audit: {
        initiatedBy: `system@${company.toLowerCase()}.com`,
        reason: ['customer-order', 'supplier-delivery', 'inventory-count', 'damaged-goods'][Math.floor(Math.random() * 4)],
        referenceNumber: `REF-${Math.random().toString(36).substring(2, 10).toUpperCase()}`,
      },
      ...(errorDetails && { error: errorDetails }),
    },
  }, null, 2);

  return {
    body,
    contentType: 'application/json',
    properties: {
      [GENERATOR_TAG]: 'true',
      generatorVersion: GENERATOR_VERSION,
      generatedAt: new Date().toISOString(),
      scenario: 'inventory-update',
      anomalyType,
      messageType: 'InventoryUpdated',
      source: `${company.toLowerCase()}-inventory-service`,
      environment: ENVIRONMENTS[Math.floor(Math.random() * ENVIRONMENTS.length)],
      region: REGIONS[Math.floor(Math.random() * REGIONS.length)],
      inventoryStatus: status,
      warehouseId: skus[0].warehouse,
    },
    correlationId,
    scenario: 'inventory-update',
    anomalyType,
  };
}

function generateUserActivityMessage(isAnomaly: boolean, anomalyType: AnomalyType): GeneratedMessage {
  const eventId = `UA-${Date.now().toString(36).toUpperCase()}-${Math.random().toString(36).substring(2, 6).toUpperCase()}`;
  const userId = `USER-${Math.random().toString(36).substring(2, 10).toUpperCase()}`;
  const correlationId = generateUUID();
  const company = COMPANIES[Math.floor(Math.random() * COMPANIES.length)];
  
  const activityTypes = ['login', 'logout', 'page_view', 'button_click', 'form_submit', 'search', 'purchase'];
  const activityType = activityTypes[Math.floor(Math.random() * activityTypes.length)];

  let status = 'recorded';
  let errorDetails = null;

  if (isAnomaly) {
    switch (anomalyType) {
      case 'dlq-candidate':
        status = 'invalid';
        errorDetails = {
          code: 'USER_NOT_FOUND',
          message: `User ${userId} does not exist in the system.`,
          timestamp: new Date().toISOString(),
        };
        break;
      case 'retry-loop':
        status = 'pending';
        errorDetails = {
          code: 'ANALYTICS_UNAVAILABLE',
          message: 'Analytics ingestion service temporarily unavailable.',
          timestamp: new Date().toISOString(),
          retryCount: Math.floor(Math.random() * 4) + 2,
        };
        break;
      case 'poison-message':
        status = 'corrupted';
        errorDetails = {
          code: 'INVALID_TIMESTAMP',
          message: 'Event timestamp is in the future or malformed.',
          timestamp: new Date().toISOString(),
        };
        break;
      case 'latency-spike':
        status = 'buffered';
        errorDetails = {
          code: 'HIGH_VOLUME_PERIOD',
          message: 'Event buffered due to high ingestion volume.',
          timestamp: new Date().toISOString(),
          bufferTimeMs: Math.floor(Math.random() * 30000) + 10000,
        };
        break;
    }
  }

  const body = JSON.stringify({
    eventType: 'UserActivityTracked',
    eventVersion: '1.2',
    timestamp: new Date().toISOString(),
    source: `${company.toLowerCase()}-analytics-service`,
    data: {
      eventId,
      userId,
      sessionId: generateUUID(),
      activityType,
      status,
      context: {
        page: `/${['home', 'products', 'cart', 'checkout', 'account', 'orders'][Math.floor(Math.random() * 6)]}`,
        referrer: ['google.com', 'facebook.com', 'direct', 'email-campaign', 'partner-site'][Math.floor(Math.random() * 5)],
        device: {
          type: ['desktop', 'mobile', 'tablet'][Math.floor(Math.random() * 3)],
          os: ['Windows', 'macOS', 'iOS', 'Android'][Math.floor(Math.random() * 4)],
          browser: ['Chrome', 'Firefox', 'Safari', 'Edge'][Math.floor(Math.random() * 4)],
        },
        geo: {
          country: 'US',
          region: ['California', 'Texas', 'New York', 'Florida', 'Washington'][Math.floor(Math.random() * 5)],
          city: ['Los Angeles', 'Houston', 'New York', 'Miami', 'Seattle'][Math.floor(Math.random() * 5)],
        },
      },
      metrics: {
        timeOnPage: Math.floor(Math.random() * 300),
        scrollDepth: Math.floor(Math.random() * 100),
        interactionCount: Math.floor(Math.random() * 20),
      },
      ...(errorDetails && { error: errorDetails }),
    },
  }, null, 2);

  return {
    body,
    contentType: 'application/json',
    properties: {
      [GENERATOR_TAG]: 'true',
      generatorVersion: GENERATOR_VERSION,
      generatedAt: new Date().toISOString(),
      scenario: 'user-activity',
      anomalyType,
      messageType: 'UserActivityTracked',
      source: `${company.toLowerCase()}-analytics-service`,
      environment: ENVIRONMENTS[Math.floor(Math.random() * ENVIRONMENTS.length)],
      region: REGIONS[Math.floor(Math.random() * REGIONS.length)],
      activityType,
      userId,
    },
    correlationId,
    scenario: 'user-activity',
    anomalyType,
  };
}

function generateErrorHandlingMessage(isAnomaly: boolean, anomalyType: AnomalyType): GeneratedMessage {
  const errorId = `ERR-${Date.now().toString(36).toUpperCase()}-${Math.random().toString(36).substring(2, 6).toUpperCase()}`;
  const correlationId = generateUUID();
  const company = COMPANIES[Math.floor(Math.random() * COMPANIES.length)];
  
  const errorTypes = [
    { code: 'DATABASE_CONNECTION_FAILED', severity: 'critical', service: 'database-proxy' },
    { code: 'EXTERNAL_API_TIMEOUT', severity: 'warning', service: 'integration-gateway' },
    { code: 'VALIDATION_ERROR', severity: 'info', service: 'api-gateway' },
    { code: 'RATE_LIMIT_EXCEEDED', severity: 'warning', service: 'rate-limiter' },
    { code: 'AUTHENTICATION_FAILED', severity: 'warning', service: 'auth-service' },
    { code: 'RESOURCE_EXHAUSTED', severity: 'critical', service: 'compute-service' },
  ];

  const errorType = errorTypes[Math.floor(Math.random() * errorTypes.length)];

  // Error messages are inherently "anomalies" in a sense, but we can make them worse
  let status = 'logged';
  let escalationDetails = null;

  if (isAnomaly) {
    switch (anomalyType) {
      case 'dlq-candidate':
        status = 'unrecoverable';
        escalationDetails = {
          level: 'P1',
          team: 'platform-oncall',
          escalatedAt: new Date().toISOString(),
        };
        break;
      case 'retry-loop':
        status = 'recurring';
        escalationDetails = {
          occurrenceCount: Math.floor(Math.random() * 50) + 10,
          firstSeen: new Date(Date.now() - Math.random() * 3600000).toISOString(),
          lastSeen: new Date().toISOString(),
        };
        break;
      case 'poison-message':
        status = 'circuit-breaker-open';
        escalationDetails = {
          circuitBreakerId: `CB-${errorType.service}`,
          openedAt: new Date().toISOString(),
          failureThreshold: 5,
          currentFailures: Math.floor(Math.random() * 10) + 5,
        };
        break;
      case 'latency-spike':
        status = 'degraded';
        escalationDetails = {
          p99Latency: Math.floor(Math.random() * 10000) + 5000,
          normalP99: 200,
          degradationFactor: Math.floor(Math.random() * 50) + 10,
        };
        break;
    }
  }

  const body = JSON.stringify({
    eventType: 'ErrorOccurred',
    eventVersion: '1.1',
    timestamp: new Date().toISOString(),
    source: `${company.toLowerCase()}-${errorType.service}`,
    data: {
      errorId,
      code: errorType.code,
      severity: errorType.severity,
      service: errorType.service,
      status,
      message: `${errorType.code.replace(/_/g, ' ').toLowerCase()}: The ${errorType.service} encountered an issue while processing the request.`,
      stackTrace: `Error: ${errorType.code}\n    at processRequest (/app/src/handlers/${errorType.service}.ts:142:15)\n    at handleMessage (/app/src/core/messageProcessor.ts:87:22)\n    at async ServiceBusReceiver.processMessage (/app/node_modules/@azure/service-bus/src/receivers/receiver.ts:234:9)`,
      context: {
        requestId: generateUUID(),
        traceId: generateUUID().replace(/-/g, ''),
        spanId: Math.random().toString(16).substring(2, 18),
        userId: Math.random() > 0.5 ? `USER-${Math.random().toString(36).substring(2, 10).toUpperCase()}` : null,
      },
      metadata: {
        hostname: `${errorType.service}-${Math.floor(Math.random() * 10)}.${company.toLowerCase()}.internal`,
        podId: `${errorType.service}-${Math.random().toString(36).substring(2, 10)}`,
        containerId: Math.random().toString(16).substring(2, 14),
        kubernetesNamespace: 'production',
      },
      ...(escalationDetails && { escalation: escalationDetails }),
    },
  }, null, 2);

  return {
    body,
    contentType: 'application/json',
    properties: {
      [GENERATOR_TAG]: 'true',
      generatorVersion: GENERATOR_VERSION,
      generatedAt: new Date().toISOString(),
      scenario: 'error-handling',
      anomalyType,
      messageType: 'ErrorOccurred',
      source: `${company.toLowerCase()}-${errorType.service}`,
      environment: ENVIRONMENTS[Math.floor(Math.random() * ENVIRONMENTS.length)],
      region: REGIONS[Math.floor(Math.random() * REGIONS.length)],
      errorCode: errorType.code,
      severity: errorType.severity,
      errorStatus: status,
    },
    correlationId,
    scenario: 'error-handling',
    anomalyType,
  };
}

// ============================================================================
// Main Generator Function
// ============================================================================

const scenarioGenerators: Record<MessageScenario, (isAnomaly: boolean, anomalyType: AnomalyType) => GeneratedMessage> = {
  'order-processing': generateOrderProcessingMessage,
  'payment-gateway': generatePaymentMessage,
  'notification-service': generateNotificationMessage,
  'inventory-update': generateInventoryMessage,
  'user-activity': generateUserActivityMessage,
  'error-handling': generateErrorHandlingMessage,
};

export function generateMessages(config: GenerationConfig): GeneratedMessage[] {
  const { volume, scenarios, anomalyRate } = config;
  const messages: GeneratedMessage[] = [];

  const anomalyTypes: AnomalyType[] = ['dlq-candidate', 'retry-loop', 'poison-message', 'latency-spike'];

  for (let i = 0; i < volume; i++) {
    // Pick a random scenario from the selected ones
    const scenario = scenarios[Math.floor(Math.random() * scenarios.length)];
    const generator = scenarioGenerators[scenario];

    // Determine if this message should be an anomaly
    const isAnomaly = Math.random() * 100 < anomalyRate;
    const anomalyType: AnomalyType = isAnomaly 
      ? anomalyTypes[Math.floor(Math.random() * anomalyTypes.length)]
      : 'none';

    const message = generator(isAnomaly, anomalyType);
    messages.push(message);
  }

  return messages;
}

// ============================================================================
// Helper Functions
// ============================================================================

export const GENERATOR_PROPERTY_KEY = GENERATOR_TAG;

export function isGeneratedMessage(properties: Record<string, any>): boolean {
  return properties[GENERATOR_TAG] === 'true';
}

export function getDefaultScenarios(): MessageScenario[] {
  return ['order-processing', 'payment-gateway', 'notification-service', 'inventory-update', 'user-activity', 'error-handling'];
}

export const VOLUME_PRESETS = [30, 50, 100, 150, 200] as const;
export type VolumePreset = typeof VOLUME_PRESETS[number];
