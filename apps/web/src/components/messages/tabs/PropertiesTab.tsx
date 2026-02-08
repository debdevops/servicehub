import { AlertTriangle, Info, ChevronRight, HelpCircle } from 'lucide-react';
import type { Message } from '@/lib/mockData';

// ============================================================================
// PropertiesTab - Display message metadata in a property grid
// ============================================================================

interface PropertiesTabProps {
  message: Message;
}

function PropertyRow({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="py-3 grid grid-cols-[180px_1fr] gap-4 border-b border-gray-100 last:border-0">
      <dt className="text-sm font-medium text-gray-500">{label}</dt>
      <dd className={`text-sm text-gray-900 break-all ${mono ? 'font-mono' : ''}`}>{value || '-'}</dd>
    </div>
  );
}

// ============================================================================
// Severity Classification with Explanation
// NOTE: This is ServiceHub's heuristic assessment, not Azure data
// ============================================================================

const SEVERITY_EXPLANATIONS = {
  test: {
    label: 'Test/Manual',
    description: 'This message was manually moved to DLQ for testing or inspection purposes. Not a production failure.',
    color: 'gray',
  },
  warning: {
    label: 'Warning',
    description: 'Real dead-letter message with normal retry behavior (delivery count ≤ 5).',
    color: 'amber',
  },
  critical: {
    label: 'Critical',
    description: 'Message failed 6+ delivery attempts - indicates persistent processing failure.',
    color: 'red',
  },
} as const;

// Determine severity based on message characteristics
function getDLQSeverity(message: Message): 'test' | 'warning' | 'critical' {
  const reason = (message.deadLetterReason || '').toLowerCase();
  const description = (message.deadLetterSource || '').toLowerCase();
  
  // Test/manual scenarios
  if (reason.includes('test') || reason.includes('demo') || reason.includes('manual') ||
      description.includes('servicehub') || description.includes('testing')) {
    return 'test';
  }
  
  // High delivery count = critical
  if (message.deliveryCount > 5) {
    return 'critical';
  }
  
  // Default to warning for real DLQ messages
  return 'warning';
}

// Extract DLQ details and generate context-aware guidance
function extractDLQDetails(message: Message): { 
  reason: string; 
  description: string; 
  interpretation: string;
  guidance: string[];
  severity: 'test' | 'warning' | 'critical';
  hasIncompleteData: boolean;
} | null {
  const isDLQ = message.queueType === 'deadletter' || !!message.deadLetterReason;
  if (!isDLQ) return null;
  
  // Explicit null checks with defensive defaults
  const rawReason = message.deadLetterReason?.trim();
  const rawDescription = message.deadLetterSource?.trim();
  
  const reason = rawReason || 'Not provided by Azure Service Bus';
  const description = rawDescription || 'Not provided by Azure Service Bus';
  const hasIncompleteData = !rawReason || !rawDescription;
  
  const severity = getDLQSeverity(message);
  let interpretation = '';
  const guidance: string[] = [];
  
  // Handle incomplete metadata first
  if (hasIncompleteData) {
    interpretation = 'Azure Service Bus did not provide complete dead-letter metadata. The message state information is incomplete.';
    guidance.push('Check Azure Portal for additional message properties');
    guidance.push('Verify Service Bus SDK version supports complete metadata');
    guidance.push('Consider checking application logs for the original failure context');
    return { reason, description, interpretation, guidance, severity: 'warning', hasIncompleteData };
  }
  
  // Generate interpretation based on reason pattern
  if (severity === 'test') {
    interpretation = 'This appears to be a test or manually dead-lettered message, likely used for inspection or system validation.';
    guidance.push('Review whether this test message should be purged or kept for reference');
    guidance.push('Verify test data is not mixed with production messages');
  } else if (message.deliveryCount > 5) {
    interpretation = 'This message failed multiple delivery attempts, indicating a persistent processing issue.';
    guidance.push('Check application logs for repeated failure patterns');
    guidance.push('Review error details in the message body');
    guidance.push('Verify downstream service availability and health');
  } else if (message.deliveryCount > 1) {
    interpretation = 'This message was retried before being dead-lettered, suggesting a transient or resolvable issue.';
    guidance.push('Review the message body for error details');
    guidance.push('Check if the underlying issue has been resolved before replaying');
  } else {
    interpretation = 'This message was dead-lettered on the first delivery attempt.';
    guidance.push('Review the message body for validation or schema errors');
    guidance.push('Check application logs for the original processing failure');
  }
  
  return { reason, description, interpretation, guidance, severity, hasIncompleteData };
}

export function PropertiesTab({ message }: PropertiesTabProps) {
  const dlqDetails = extractDLQDetails(message);
  const severityInfo = dlqDetails ? SEVERITY_EXPLANATIONS[dlqDetails.severity] : null;
  
  return (
    <div className="p-4 space-y-4">
      {/* DLQ Information Panel - shown prominently at top */}
      {dlqDetails && severityInfo && (
        <>
        {dlqDetails.hasIncompleteData && (
          <div className="mb-3 rounded-lg border-2 border-yellow-300 bg-yellow-50 px-4 py-3">
            <div className="flex items-center gap-2 text-yellow-800 text-sm font-medium">
              <AlertTriangle className="w-4 h-4" />
              <span>Incomplete Azure Data</span>
            </div>
            <p className="text-xs text-yellow-700 mt-1">
              Azure Service Bus did not provide complete dead-letter metadata. Analysis may be limited.
            </p>
          </div>
        )}
        <div className={`mb-4 rounded-lg overflow-hidden border-2 ${
          dlqDetails.severity === 'test' 
            ? 'bg-gray-50 border-gray-200' 
            : dlqDetails.severity === 'critical'
            ? 'bg-red-50 border-red-300'
            : 'bg-amber-50 border-amber-300'
        }`}>
          {/* Header with Severity Badge */}
          <div className={`px-4 py-3 border-b flex items-center gap-2 ${
            dlqDetails.severity === 'test'
              ? 'bg-gray-100 border-gray-200'
              : dlqDetails.severity === 'critical'
              ? 'bg-red-100 border-red-200'
              : 'bg-amber-100 border-amber-200'
          }`}>
            <AlertTriangle className={`w-5 h-5 ${
              dlqDetails.severity === 'test'
                ? 'text-gray-500'
                : dlqDetails.severity === 'critical'
                ? 'text-red-600'
                : 'text-amber-600'
            }`} />
            <span className={`font-semibold ${
              dlqDetails.severity === 'test'
                ? 'text-gray-700'
                : dlqDetails.severity === 'critical'
                ? 'text-red-800'
                : 'text-amber-800'
            }`}>Dead-Letter Queue Message</span>
            
            {/* Severity Badge with Tooltip - Clearly labeled as ServiceHub assessment */}
            <span 
              className={`ml-auto text-xs px-2 py-1 rounded-full font-medium cursor-help flex items-center gap-1 ${
                dlqDetails.severity === 'test'
                  ? 'bg-gray-200 text-gray-700'
                  : dlqDetails.severity === 'critical'
                  ? 'bg-red-200 text-red-800'
                  : 'bg-amber-200 text-amber-800'
              }`}
              title={`⚠️ ServiceHub Assessment (Not Azure Data): ${severityInfo.description}`}
            >
              {severityInfo.label}
              <HelpCircle className="w-3 h-3 opacity-70" />
            </span>
          </div>
          
          <div className="p-4 space-y-4">
            {/* Section 1: Azure Service Bus Properties (FACTS) */}
            <div>
              <div className="text-xs font-semibold uppercase tracking-wide mb-2 flex items-center gap-1 text-gray-700">
                <span className="bg-green-100 text-green-700 px-1.5 py-0.5 rounded text-[10px] font-bold">FACT</span>
                Azure Service Bus Data
              </div>
              <div className="bg-white border border-gray-200 rounded-lg p-3 space-y-2">
                <div>
                  <div className="text-xs text-gray-500 mb-0.5">DeadLetterReason</div>
                  <div className="text-sm font-mono text-gray-900 break-all">{dlqDetails.reason}</div>
                </div>
                <div>
                  <div className="text-xs text-gray-500 mb-0.5">DeadLetterErrorDescription</div>
                  <div className="text-sm font-mono text-gray-900 break-all">{dlqDetails.description}</div>
                </div>
                <div>
                  <div className="text-xs text-gray-500 mb-0.5">Delivery Count</div>
                  <div className="text-sm text-gray-900">{message.deliveryCount}</div>
                </div>
              </div>
            </div>
            
            {/* Visual Separator */}
            <div className="border-t-2 border-dashed border-gray-300 my-4" />
            
            {/* Section 2: ServiceHub Interpretation (INFERENCE) - Clearly marked */}
            <div className="bg-gray-50 rounded-lg p-3 border border-gray-200">
              <div className="text-xs font-semibold uppercase tracking-wide mb-2 flex items-center gap-1 text-gray-600">
                <span className="bg-blue-100 text-blue-700 px-1.5 py-0.5 rounded text-[10px] font-bold">ASSESSMENT</span>
                <Info className="w-3 h-3" /> 
                ServiceHub Interpretation
              </div>
              <div className="text-sm text-gray-700 leading-relaxed">
                {dlqDetails.interpretation}
              </div>
            </div>
            
            {/* Section 3: Suggested Actions (GUIDANCE) */}
            <details className="group bg-gray-50 rounded-lg border border-gray-200 overflow-hidden" open={dlqDetails.severity !== 'test'}>
              <summary className="px-3 py-2 text-xs font-semibold uppercase tracking-wide cursor-pointer select-none list-none text-gray-600 bg-gray-100 hover:bg-gray-150">
                <span className="inline-flex items-center gap-1">
                  <ChevronRight className="w-3 h-3 transition-transform group-open:rotate-90" />
                  <span className="bg-amber-100 text-amber-700 px-1.5 py-0.5 rounded text-[10px] font-bold">GUIDANCE</span>
                  Suggested Actions
                </span>
              </summary>
              <ul className="p-3 space-y-1.5 text-sm text-gray-700">
                {dlqDetails.guidance.map((item, idx) => (
                  <li key={idx} className="flex items-start gap-2">
                    <span className="text-gray-400 mt-0.5">•</span>
                    <span>{item}</span>
                  </li>
                ))}
              </ul>
            </details>
          </div>
        </div>
        </>
      )}
      
      {/* Detailed Properties */}
      <div className="bg-white rounded-xl border-2 border-sky-100 shadow-sm overflow-hidden">
        <div className="bg-gradient-to-r from-sky-500 to-sky-600 px-4 py-2.5">
          <h3 className="text-sm font-semibold text-white">Complete Message Properties</h3>
        </div>
        <dl className="p-4">
        <PropertyRow 
          label="Message ID" 
          value={message.id} 
          mono 
        />
        <PropertyRow 
          label="Enqueued Time" 
          value={message.enqueuedTime.toISOString()} 
          mono 
        />
        <PropertyRow 
          label="Delivery Count" 
          value={`${message.deliveryCount} (current session)`} 
        />
        <div className="py-2 px-3 bg-gray-50 rounded border-l-2 border-gray-300 my-2">
          <p className="text-xs text-gray-600 leading-relaxed">
            <span className="font-medium">Note:</span> Delivery count reflects attempts in the current session. 
            This value resets when messages move between queues, sessions expire, or manual intervention occurs.
            Total historical delivery attempts may be higher.
          </p>
        </div>
        <PropertyRow 
          label="Time To Live" 
          value={message.timeToLive} 
        />
        <PropertyRow 
          label="Sequence Number" 
          value={message.sequenceNumber.toLocaleString()} 
          mono 
        />
        <PropertyRow 
          label="Content Type" 
          value={message.contentType} 
        />
        <PropertyRow 
          label="Lock Token" 
          value={message.lockToken} 
          mono 
        />
        
        {/* Dead-letter specific fields */}
        {message.queueType === 'deadletter' && (
          <>
            <div className="pt-4 pb-2 border-t border-gray-200 mt-2">
              <span className="text-xs font-semibold text-red-600 uppercase tracking-wide">
                Dead-Letter Information
              </span>
            </div>
            {message.deadLetterReason && (
              <PropertyRow 
                label="Dead-Letter Reason" 
                value={message.deadLetterReason} 
              />
            )}
            {message.deadLetterSource && (
              <PropertyRow 
                label="Dead-Letter Source" 
                value={message.deadLetterSource} 
              />
            )}
          </>
        )}
        </dl>
      </div>

      {/* Custom Properties */}
      {Object.keys(message.properties || {}).length > 0 && (
        <div className="bg-white rounded-xl border-2 border-sky-100 shadow-sm overflow-hidden">
          <div className="bg-gradient-to-r from-sky-400 to-sky-500 px-4 py-2.5 flex items-center gap-2">
            <Info className="w-4 h-4 text-white" />
            <h4 className="text-sm font-semibold text-white">Custom Application Properties</h4>
          </div>
          <dl className="p-4">
            {Object.entries(message.properties).map(([key, value]) => (
              <PropertyRow 
                key={key}
                label={key} 
                value={String(value)} 
                mono 
              />
            ))}
          </dl>
        </div>
      )}
    </div>
  );
}
