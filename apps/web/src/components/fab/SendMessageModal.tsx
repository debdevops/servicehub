import { useState, useEffect } from 'react';
import { Send, X, Plus, Trash2 } from 'lucide-react';
import { useNamespaces } from '@/hooks/useNamespaces';
import { useQueues } from '@/hooks/useQueues';
import { useSendMessage } from '@/hooks/useMessages';

// ============================================================================
// SendMessageModal - Modal for composing and sending messages
// ============================================================================

interface SendMessageModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSend: (data: MessagePayload) => void;
  defaultNamespaceId?: string | null;
  defaultQueueName?: string | null;
}

interface MessageProperty {
  key: string;
  value: string;
}

export interface MessagePayload {
  entity: string;
  entityType: 'queue' | 'topic';
  body: string;
  contentType: string;
  properties: Record<string, string>;
  correlationId?: string;
  sessionId?: string;
  timeToLive?: string;
  scheduledEnqueueTime?: string;
  messageCount: number;
}

export function SendMessageModal({ 
  isOpen, 
  onClose, 
  onSend,
  defaultNamespaceId,
  defaultQueueName 
}: SendMessageModalProps) {
  const { data: namespaces } = useNamespaces();
  const [selectedNamespace, setSelectedNamespace] = useState(defaultNamespaceId || '');
  const { data: queues } = useQueues(selectedNamespace);
  const sendMessage = useSendMessage();

  const [entity, setEntity] = useState(defaultQueueName || '');
  const [body, setBody] = useState('{\n  "orderId": "ORD-2026-12345",\n  "amount": 99.99,\n  "currency": "USD"\n}');
  const [contentType, setContentType] = useState('application/json');
  const [properties, setProperties] = useState<MessageProperty[]>([
    { key: 'source', value: 'ServiceHub' },
  ]);
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [correlationId, setCorrelationId] = useState('');
  const [sessionId, setSessionId] = useState('');
  const [timeToLive, setTimeToLive] = useState('');
  const [scheduledEnqueueTime, setScheduledEnqueueTime] = useState('');
  const [sendMultiple, setSendMultiple] = useState(false);
  const [messageCount, setMessageCount] = useState(1);

  // Update selected namespace and queue when defaults change
  useEffect(() => {
    if (defaultNamespaceId) setSelectedNamespace(defaultNamespaceId);
    if (defaultQueueName) setEntity(defaultQueueName);
  }, [defaultNamespaceId, defaultQueueName]);

  if (!isOpen) return null;

  const addProperty = () => {
    setProperties([...properties, { key: '', value: '' }]);
  };

  const removeProperty = (index: number) => {
    setProperties(properties.filter((_, i) => i !== index));
  };

  const updateProperty = (index: number, field: 'key' | 'value', value: string) => {
    const updated = [...properties];
    updated[index][field] = value;
    setProperties(updated);
  };

  const handleSend = async () => {
    if (!selectedNamespace || !entity) {
      return;
    }

    const propsObject: Record<string, string> = {};
    properties.forEach((p) => {
      if (p.key.trim()) {
        propsObject[p.key.trim()] = p.value;
      }
    });

    try {
      // Send messages (supports multiple copies)
      const count = sendMultiple ? messageCount : 1;
      
      for (let i = 0; i < count; i++) {
        await sendMessage.mutateAsync({
          namespaceId: selectedNamespace,
          queueOrTopicName: entity,
          message: {
            body,
            contentType,
            properties: propsObject,
            correlationId: correlationId || undefined,
            sessionId: sessionId || undefined,
            timeToLive: timeToLive ? parseInt(timeToLive) : undefined,
            scheduledEnqueueTime: scheduledEnqueueTime || undefined,
          },
        });
      }

      // Call parent callback with payload for UI update
      onSend({
        entity,
        entityType: 'queue',
        body,
        contentType,
        properties: propsObject,
        correlationId,
        sessionId,
        timeToLive,
        scheduledEnqueueTime,
        messageCount: count,
      });
    } catch (error) {
      // Error handled by mutation hook
    }
  };

  const validateJson = () => {
    if (contentType !== 'application/json') return true;
    try {
      JSON.parse(body);
      return true;
    } catch {
      return false;
    }
  };

  const isValid = selectedNamespace && entity && body.trim().length > 0 && validateJson();

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      {/* Backdrop */}
      <div
        className="absolute inset-0 bg-black/50"
        onClick={onClose}
      />

      {/* Modal */}
      <div className="relative bg-white rounded-xl shadow-2xl w-full max-w-2xl max-h-[90vh] overflow-hidden flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-semibold text-gray-900">Send Message</h2>
          <button
            onClick={onClose}
            className="p-1 hover:bg-gray-100 rounded-lg transition-colors"
          >
            <X className="w-5 h-5 text-gray-500" />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto p-6 space-y-6">
          {/* Namespace Selection */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">
              Namespace
            </label>
            <select
              value={selectedNamespace}
              onChange={(e) => setSelectedNamespace(e.target.value)}
              className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-primary-300"
            >
              <option value="">Select namespace...</option>
              {namespaces?.map((ns) => (
                <option key={ns.id} value={ns.id}>
                  {ns.displayName || ns.name}
                </option>
              ))}
            </select>
          </div>

          {/* Queue Selection */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">
              Queue
            </label>
            <select
              value={entity}
              onChange={(e) => setEntity(e.target.value)}
              disabled={!selectedNamespace}
              className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-primary-300 disabled:bg-gray-50 disabled:cursor-not-allowed"
            >
              <option value="">Select queue...</option>
              {queues?.map((q) => (
                <option key={q.name} value={q.name}>
                  📥 {q.name}
                </option>
              ))}
            </select>
          </div>

          {/* Content Type */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">
              Content Type
            </label>
            <select
              value={contentType}
              onChange={(e) => setContentType(e.target.value)}
              className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-primary-300"
            >
              <option value="application/json">application/json</option>
              <option value="text/plain">text/plain</option>
              <option value="application/xml">application/xml</option>
            </select>
          </div>

          {/* Message Body */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">
              Message Body
            </label>
            <div className="relative">
              <textarea
                value={body}
                onChange={(e) => setBody(e.target.value)}
                rows={8}
                className={`w-full px-3 py-2 border rounded-lg text-sm font-mono focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-primary-300 ${
                  !validateJson() && contentType === 'application/json'
                    ? 'border-red-300 bg-red-50'
                    : 'border-gray-200'
                }`}
                placeholder="Enter message body..."
              />
              {!validateJson() && contentType === 'application/json' && (
                <p className="text-xs text-red-600 mt-1">Invalid JSON format</p>
              )}
            </div>
          </div>

          {/* Custom Properties */}
          <div>
            <div className="flex items-center justify-between mb-2">
              <label className="text-sm font-medium text-gray-700">
                Custom Properties
              </label>
              <button
                onClick={addProperty}
                className="flex items-center gap-1 text-xs text-primary-600 hover:text-primary-700"
              >
                <Plus className="w-3 h-3" />
                Add Property
              </button>
            </div>
            <div className="space-y-2">
              {properties.map((prop, index) => (
                <div key={index} className="flex items-center gap-2">
                  <input
                    type="text"
                    value={prop.key}
                    onChange={(e) => updateProperty(index, 'key', e.target.value)}
                    placeholder="Key"
                    className="flex-1 px-3 py-2 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-primary-400"
                  />
                  <input
                    type="text"
                    value={prop.value}
                    onChange={(e) => updateProperty(index, 'value', e.target.value)}
                    placeholder="Value"
                    className="flex-1 px-3 py-2 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-primary-400"
                  />
                  <button
                    onClick={() => removeProperty(index)}
                    className="p-2 text-gray-400 hover:text-red-500 hover:bg-red-50 rounded-lg transition-colors"
                  >
                    <Trash2 className="w-4 h-4" />
                  </button>
                </div>
              ))}
            </div>
          </div>

          {/* Advanced Options Toggle */}
          <button
            onClick={() => setShowAdvanced(!showAdvanced)}
            className="text-sm text-primary-600 hover:text-primary-700 font-medium"
          >
            {showAdvanced ? '▼' : '▶'} Advanced Options
          </button>

          {/* Advanced Options */}
          {showAdvanced && (
            <div className="space-y-4 pl-4 border-l-2 border-gray-100">
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">
                    Correlation ID
                  </label>
                  <input
                    type="text"
                    value={correlationId}
                    onChange={(e) => setCorrelationId(e.target.value)}
                    placeholder="Optional"
                    className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-primary-400"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">
                    Session ID
                  </label>
                  <input
                    type="text"
                    value={sessionId}
                    onChange={(e) => setSessionId(e.target.value)}
                    placeholder="Optional"
                    className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-primary-400"
                  />
                </div>
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">
                    Time To Live
                  </label>
                  <input
                    type="text"
                    value={timeToLive}
                    onChange={(e) => setTimeToLive(e.target.value)}
                    placeholder="e.g., 00:30:00"
                    className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-primary-400"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">
                    Scheduled Enqueue Time
                  </label>
                  <input
                    type="datetime-local"
                    value={scheduledEnqueueTime}
                    onChange={(e) => setScheduledEnqueueTime(e.target.value)}
                    className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-primary-400"
                  />
                </div>
              </div>
            </div>
          )}

          {/* Bulk Send Option */}
          <div className="bg-gray-50 rounded-lg p-4">
            <label className="flex items-center gap-3">
              <input
                type="checkbox"
                checked={sendMultiple}
                onChange={(e) => setSendMultiple(e.target.checked)}
                className="w-4 h-4 text-primary-500 rounded focus:ring-primary-400"
              />
              <span className="text-sm text-gray-700">Send multiple copies</span>
            </label>
            {sendMultiple && (
              <div className="mt-3 flex items-center gap-3">
                <label className="text-sm text-gray-600">How many?</label>
                <input
                  type="number"
                  min={1}
                  max={1000}
                  value={messageCount}
                  onChange={(e) => setMessageCount(Math.min(1000, Math.max(1, parseInt(e.target.value) || 1)))}
                  className="w-24 px-3 py-1.5 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-primary-400"
                />
                <span className="text-xs text-gray-500">(max 1000)</span>
              </div>
            )}
            {sendMultiple && messageCount > 1 && (
              <p className="mt-2 text-sm text-amber-600">
                ⚠️ This will send <strong>{messageCount}</strong> messages to <strong>{entity}</strong>
              </p>
            )}
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-end gap-3 px-6 py-4 border-t border-gray-200 bg-gray-50">
          <button
            onClick={onClose}
            className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={handleSend}
            disabled={!isValid || sendMessage.isPending}
            className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-primary-500 hover:bg-primary-600 disabled:bg-gray-300 disabled:cursor-not-allowed rounded-lg transition-colors"
          >
            {sendMessage.isPending ? (
              <>
                <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                Sending...
              </>
            ) : (
              <>
                <Send className="w-4 h-4" />
                {sendMultiple && messageCount > 1 ? `Send ${messageCount} Messages` : 'Send Message'}
              </>
            )}
          </button>
        </div>
      </div>
    </div>
  );
}
