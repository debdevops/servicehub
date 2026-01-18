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
      <dd className={`text-sm text-gray-900 ${mono ? 'font-mono' : ''}`}>{value}</dd>
    </div>
  );
}

export function PropertiesTab({ message }: PropertiesTabProps) {
  return (
    <div className="p-4">
      <dl className="bg-white rounded-lg border border-gray-200 p-4">
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
          value={message.deliveryCount.toString()} 
        />
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

      {/* Custom Properties */}
      <div className="mt-4">
        <h4 className="text-sm font-semibold text-gray-700 mb-2">Custom Properties</h4>
        <dl className="bg-white rounded-lg border border-gray-200 p-4">
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
    </div>
  );
}
