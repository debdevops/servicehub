// ============================================================================
// GCP Pub/Sub Mock Data — MedStream Healthcare Analytics Platform
// Scenario: HIPAA-compliant lab results pipeline on GCP — message schema
// version mismatch causing Pub/Sub dead-letter flood
// All messages conform to the existing `Message` interface from mockData.ts
// ============================================================================

import type {
  Message,
  MessageStatus,
  QueueType,
  ContentType,
} from './mockData';

export type { Message };

// ---------------------------------------------------------------------------
// Seed data
// ---------------------------------------------------------------------------

const TOPIC_NAMES = [
  'patient-intake',
  'lab-results',
  'billing-events',
  'appointment-reminders',
  'medication-orders',
  'clinical-alerts',
];

const SUBSCRIPTION_NAMES: Record<string, string[]> = {
  'patient-intake': ['intake-processor-sub', 'ehr-sync-sub'],
  'lab-results': ['results-router-sub', 'physician-notify-sub', 'hl7-export-sub'],
  'billing-events': ['insurance-claims-sub', 'patient-billing-sub'],
  'appointment-reminders': ['sms-gateway-sub'],
  'medication-orders': ['pharmacy-sub', 'dea-audit-sub'],
  'clinical-alerts': ['oncall-pager-sub', 'dashboard-sub'],
};

const DEPARTMENTS = [
  { code: 'CARDIO', name: 'Cardiology' },
  { code: 'ONCO', name: 'Oncology' },
  { code: 'NEURO', name: 'Neurology' },
  { code: 'PATH', name: 'Pathology' },
  { code: 'RADIO', name: 'Radiology' },
  { code: 'ICU', name: 'Intensive Care' },
];

const LAB_TEST_CODES = [
  { code: 'CBC', name: 'Complete Blood Count' },
  { code: 'CMP', name: 'Comprehensive Metabolic Panel' },
  { code: 'HBA1C', name: 'Hemoglobin A1c' },
  { code: 'TROPONIN', name: 'Cardiac Troponin I' },
  { code: 'LIPID', name: 'Lipid Panel' },
  { code: 'TSH', name: 'Thyroid Stimulating Hormone' },
];

const GCP_ERROR_TYPES = [
  'google.api.core.exceptions.NotFound: Subscription "hl7-export-sub" not found — subscriber offline',
  'com.medstream.fhir.SchemaValidationException: FHIR R4 required field "subject.reference" missing',
  'google.pubsub.v1.AcknowledgeError: Ack deadline exceeded — consumer processing took 620s (limit: 600s)',
  'com.medstream.hl7.HL7ParseException: Invalid HL7 v2.8 segment MSH-9 code "LAB^R01^RESULTS"',
  'com.medstream.billing.ClaimsException: ICD-10 code Z87.39 not found in payer EDI 837 mapping',
  'com.medstream.alert.EscalationException: On-call pager delivery failed — PagerDuty API returned 503',
];

// ---------------------------------------------------------------------------
// GCP Pub/Sub Topic metadata
// ---------------------------------------------------------------------------

export interface GcpDemoTopic {
  name: string;
  fullName: string;
  subscriptions: string[];
  messageRetentionDuration: string;
  schemaVersion: string;
  deadLetterTopic: string;
}

export const GCP_DEMO_TOPICS: GcpDemoTopic[] = [
  {
    name: 'patient-intake',
    fullName: 'projects/medstream-healthcare-prod/topics/patient-intake',
    subscriptions: SUBSCRIPTION_NAMES['patient-intake'],
    messageRetentionDuration: '7 days',
    schemaVersion: 'FHIR-R4-v2.1',
    deadLetterTopic: 'patient-intake-deadletter',
  },
  {
    name: 'lab-results',
    fullName: 'projects/medstream-healthcare-prod/topics/lab-results',
    subscriptions: SUBSCRIPTION_NAMES['lab-results'],
    messageRetentionDuration: '7 days',
    schemaVersion: 'HL7-v2.8',
    deadLetterTopic: 'lab-results-deadletter',
  },
  {
    name: 'billing-events',
    fullName: 'projects/medstream-healthcare-prod/topics/billing-events',
    subscriptions: SUBSCRIPTION_NAMES['billing-events'],
    messageRetentionDuration: '3 days',
    schemaVersion: 'X12-EDI-837',
    deadLetterTopic: 'billing-events-deadletter',
  },
  {
    name: 'appointment-reminders',
    fullName: 'projects/medstream-healthcare-prod/topics/appointment-reminders',
    subscriptions: SUBSCRIPTION_NAMES['appointment-reminders'],
    messageRetentionDuration: '1 day',
    schemaVersion: 'JSON-v1.0',
    deadLetterTopic: 'appointment-reminders-deadletter',
  },
  {
    name: 'medication-orders',
    fullName: 'projects/medstream-healthcare-prod/topics/medication-orders',
    subscriptions: SUBSCRIPTION_NAMES['medication-orders'],
    messageRetentionDuration: '7 days',
    schemaVersion: 'NCPDP-SCRIPT-10.6',
    deadLetterTopic: 'medication-orders-deadletter',
  },
  {
    name: 'clinical-alerts',
    fullName: 'projects/medstream-healthcare-prod/topics/clinical-alerts',
    subscriptions: SUBSCRIPTION_NAMES['clinical-alerts'],
    messageRetentionDuration: '1 day',
    schemaVersion: 'JSON-v2.0',
    deadLetterTopic: 'clinical-alerts-deadletter',
  },
];

// ---------------------------------------------------------------------------
// AI issue patterns for GCP demo
// ---------------------------------------------------------------------------

const GCP_AI_ISSUES = [
  {
    issue: 'HL7 Schema Mismatch Flood — 31 of 41 dead-letter messages in lab-results-deadletter share the same HL7ParseException. Publisher upgraded to HL7 v2.8 (segment MSH-9 changed format) but hl7-export-sub consumer still parses v2.5. Dead-letter policy triggered after 5 nack retries.',
    recommendations: [
      'Deploy MedStream HL7 Consumer v3.2 which adds v2.5/v2.8 dual-schema support',
      'After deployment, re-publish the 31 dead-lettered messages from lab-results-deadletter to lab-results',
      'Add message attribute "hl7-version" to all publishers so consumers can select the right parser',
    ],
  },
  {
    issue: 'Pub/Sub Ack Deadline Exceeded — 12 messages from lab-results breached the 600s ack deadline in hl7-export-sub. HL7 export to legacy hospital EHR (Epic on-prem) is timing out due to VPN connectivity degradation. Messages nacked and re-delivered 5 times before dead-lettering.',
    recommendations: [
      'Increase ack deadline for hl7-export-sub from 600s to 1200s (max allowed)',
      'Add circuit breaker in HL7 export service: after 3 consecutive Epic timeouts, skip to nack immediately',
      'Investigate VPN tunnel stability — Epic on-prem has shown 38% packet loss since 11:45 UTC',
    ],
  },
  {
    issue: 'Billing claims rejection — 9 billing-events messages dead-lettered due to invalid ICD-10 code Z87.39 in payer EDI 837 mapping. Code was deprecated in ICD-10 2025 update but MedStream claims engine still references the 2024 code table. Affects United HealthCare claims only.',
    recommendations: [
      'Update ICD-10 code table to 2025 revision in claims-engine configuration',
      'Replay the 9 dead-lettered billing events after code table update',
      'Add ICD-10 code validation step before message publish to catch deprecations early',
    ],
  },
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

function gcpMessageId(): string {
  return String(Math.floor(Math.random() * 9_000_000_000_000) + 1_000_000_000_000);
}

function timeAgo(minutes: number): Date {
  return new Date(Date.now() - minutes * 60_000);
}

// ---------------------------------------------------------------------------
// Message body generators
// ---------------------------------------------------------------------------

function makeLabResultsMessage(isError: boolean, idx: number): object {
  const lab = randomFrom(LAB_TEST_CODES);
  const dept = randomFrom(DEPARTMENTS);
  const patientId = `PAT-${10000 + idx}`;
  return {
    resourceType: isError ? 'DiagnosticReport' : 'DiagnosticReport',
    schemaVersion: isError ? '2.5' : '2.8',
    id: uuid(),
    status: isError ? 'partial' : 'final',
    category: [{ coding: [{ system: 'http://loinc.org', code: '11502-2' }] }],
    code: { text: lab.name, code: lab.code },
    subject: isError
      ? { display: `Patient ${patientId}` }
      : { reference: `Patient/${patientId}`, display: `Patient ${patientId}` },
    effectiveDateTime: new Date(Date.now() - idx * 120_000).toISOString(),
    issued: new Date().toISOString(),
    performer: [{ reference: `Organization/${dept.code}`, display: dept.name }],
    result: [{ reference: `Observation/obs-${uuid().substring(0, 8)}` }],
    messageAttributes: {
      correlationId: uuid(),
      sourceSystem: 'lims-central',
      hl7Version: isError ? '2.5' : '2.8',
      departmentCode: dept.code,
    },
  };
}

function makeBillingEventMessage(isError: boolean, idx: number): object {
  const patientId = `PAT-${20000 + idx}`;
  return {
    eventType: isError ? 'ClaimRejected' : 'ClaimSubmitted',
    claimId: `CLM-${Date.now() + idx}`,
    patientId,
    dateOfService: new Date(Date.now() - 3 * 24 * 3600_000).toISOString().split('T')[0],
    diagnosisCodes: isError ? ['Z87.39'] : ['Z87.891', 'I10'],
    procedureCodes: ['99213', '93000'],
    totalBilled: (150 + idx * 25).toFixed(2),
    payer: isError ? 'UnitedHealthCare' : 'BlueCross BlueShield',
    rejectionReason: isError ? 'ICD-10 code Z87.39 not valid in payer 2025 code table' : undefined,
    messageAttributes: {
      correlationId: uuid(),
      sourceSystem: 'billing-engine',
      ediVersion: '837-5010',
    },
  };
}

function makeClinicalAlertMessage(isError: boolean, idx: number): object {
  const dept = randomFrom(DEPARTMENTS);
  return {
    alertType: isError ? 'CriticalLabAlert' : 'RoutineAlert',
    alertId: uuid(),
    severity: isError ? 'critical' : 'informational',
    department: dept.code,
    message: isError
      ? `Critical value detected — immediate physician notification required (attempt ${idx % 5 + 1}/5 failed)`
      : `Routine lab completed — results available in patient portal`,
    escalationState: isError ? 'pager_delivery_failed' : 'delivered',
    timestamp: new Date(Date.now() - idx * 90_000).toISOString(),
    messageAttributes: {
      correlationId: uuid(),
      sourceSystem: 'clinical-decision-support',
      priority: isError ? 'P1' : 'P3',
    },
  };
}

// ---------------------------------------------------------------------------
// Public: generate GCP demo messages
// ---------------------------------------------------------------------------

/**
 * Generates a realistic set of GCP Pub/Sub demo messages for the MessagesPage
 * demo mode (?demo=gcp). Returns 50 messages mapped to the Message interface.
 */
export function generateGcpMockMessages(count = 50): Message[] {
  const messages: Message[] = [];

  for (let i = 0; i < count; i++) {
    const isError = i < 12;
    const isWarning = !isError && i < 22;
    const isDeadletter = i < 18;
    const topicName = TOPIC_NAMES[i % TOPIC_NAMES.length];
    const subName = randomFrom(SUBSCRIPTION_NAMES[topicName] ?? ['default-sub']);

    const status: MessageStatus = isError ? 'error' : isWarning ? 'warning' : 'success';
    const queueType: QueueType = isDeadletter ? 'deadletter' : 'active';

    let bodyObj: object;
    if (topicName === 'lab-results' || topicName === 'patient-intake') {
      bodyObj = makeLabResultsMessage(isError, i);
    } else if (topicName === 'billing-events') {
      bodyObj = makeBillingEventMessage(isError, i);
    } else {
      bodyObj = makeClinicalAlertMessage(isError, i);
    }

    const body = JSON.stringify(bodyObj, null, 2);
    const msgId = gcpMessageId();
    const minutesAgo = i * 3 + Math.floor(Math.random() * 5);
    const aiIssueTemplate = isError ? GCP_AI_ISSUES[i % GCP_AI_ISSUES.length] : undefined;

    messages.push({
      id: msgId,
      sequenceNumber: 2000 + i,
      enqueuedTime: timeAgo(minutesAgo),
      status,
      preview: isError
        ? `[ERROR] ${GCP_ERROR_TYPES[i % GCP_ERROR_TYPES.length].substring(0, 80)}`
        : isWarning
        ? `[WARN] ${topicName} → ${subName} — ack deadline approaching (${minutesAgo}m in flight)`
        : `[OK] ${topicName} → ${subName} — message acknowledged`,
      contentType: 'application/json' as ContentType,
      deliveryCount: isDeadletter ? 5 : 1,
      hasAIInsight: !!aiIssueTemplate,
      properties: {
        'pubsub:MessageId': msgId,
        'pubsub:PublishTime': timeAgo(minutesAgo).toISOString(),
        'pubsub:AckDeadline': '600s',
        'pubsub:DeliveryAttempt': String(isDeadletter ? 5 : 1),
        'pubsub:Topic': `projects/medstream-healthcare-prod/topics/${topicName}`,
        'pubsub:Subscription': `projects/medstream-healthcare-prod/subscriptions/${subName}`,
        'x-correlation-id': uuid(),
        'x-source-system': 'medstream-platform',
        'x-hl7-version': topicName === 'lab-results' ? (isError ? '2.5' : '2.8') : undefined,
        'x-fhir-version': topicName === 'patient-intake' ? 'R4' : undefined,
      } as Record<string, unknown>,
      queueType,
      body,
      headers: {
        'Content-Type': 'application/json',
        'X-Goog-Pubsub-Topic': `projects/medstream-healthcare-prod/topics/${topicName}`,
        'X-Goog-Pubsub-Subscription': `projects/medstream-healthcare-prod/subscriptions/${subName}`,
        'X-Correlation-Id': uuid(),
        'X-GCP-Project': 'medstream-healthcare-prod',
        'X-GCP-Region': 'us-central1',
      },
      timeToLive: '7 days',
      lockToken: uuid(),
      eventType: (bodyObj as Record<string, string>)['eventType'] ?? (bodyObj as Record<string, string>)['resourceType'] ?? 'PubSubMessage',
      displayTitle: `${topicName} → ${subName}`,
      deadLetterReason: isDeadletter ? 'max_delivery_attempts exceeded' : undefined,
      deadLetterSource: isDeadletter ? `${topicName}-deadletter` : undefined,
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
