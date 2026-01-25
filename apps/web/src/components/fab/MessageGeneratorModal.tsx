import { useState, useEffect } from 'react';
import { 
  X, 
  Wand2, 
  AlertTriangle, 
  Package, 
  CreditCard, 
  Bell, 
  Boxes, 
  Users, 
  Bug,
  Check,
  Loader2,
  Trash2,
  Info,
} from 'lucide-react';
import { useNamespaces } from '@/hooks/useNamespaces';
import { useQueues } from '@/hooks/useQueues';
import { useTopics } from '@/hooks/useTopics';
import { messagesApi } from '@/lib/api/messages';
import toast from 'react-hot-toast';
import {
  generateMessages,
  getDefaultScenarios,
  VOLUME_PRESETS,
  type MessageScenario,
  type GenerationConfig,
  type VolumePreset,
  GENERATOR_PROPERTY_KEY,
} from '@/lib/messageGenerator';

// ============================================================================
// Types
// ============================================================================

interface MessageGeneratorModalProps {
  isOpen: boolean;
  onClose: () => void;
  defaultNamespaceId?: string | null;
  defaultEntityName?: string | null;
  onGenerated?: () => void;
}

type TargetType = 'queue' | 'topic';

// ============================================================================
// Scenario Config
// ============================================================================

const SCENARIO_INFO: Record<MessageScenario, { icon: typeof Package; label: string; description: string }> = {
  'order-processing': {
    icon: Package,
    label: 'Order Processing',
    description: 'E-commerce orders with items, shipping, and fulfillment data',
  },
  'payment-gateway': {
    icon: CreditCard,
    label: 'Payment Gateway',
    description: 'Payment transactions, authorizations, and refunds',
  },
  'notification-service': {
    icon: Bell,
    label: 'Notifications',
    description: 'Email, SMS, push notifications, and webhooks',
  },
  'inventory-update': {
    icon: Boxes,
    label: 'Inventory Updates',
    description: 'Stock levels, warehouse transfers, and adjustments',
  },
  'user-activity': {
    icon: Users,
    label: 'User Activity',
    description: 'Logins, page views, clicks, and user actions',
  },
  'error-handling': {
    icon: Bug,
    label: 'Error Events',
    description: 'Application errors, exceptions, and alerts',
  },
};

// ============================================================================
// Component
// ============================================================================

export function MessageGeneratorModal({
  isOpen,
  onClose,
  defaultNamespaceId,
  defaultEntityName,
  onGenerated,
}: MessageGeneratorModalProps) {
  // Data fetching
  const { data: namespaces } = useNamespaces();
  const [selectedNamespace, setSelectedNamespace] = useState(defaultNamespaceId || '');
  const { data: queues } = useQueues(selectedNamespace);
  const { data: topics } = useTopics(selectedNamespace);

  // Form state
  const [targetType, setTargetType] = useState<TargetType>('queue');
  const [selectedEntity, setSelectedEntity] = useState(defaultEntityName || '');
  const [volume, setVolume] = useState<VolumePreset>(50);
  const [selectedScenarios, setSelectedScenarios] = useState<MessageScenario[]>(getDefaultScenarios());
  const [anomalyRate, setAnomalyRate] = useState(15); // 15% default
  const [isGenerating, setIsGenerating] = useState(false);
  const [showCleanup, setShowCleanup] = useState(false);

  // Update defaults when props change
  useEffect(() => {
    if (defaultNamespaceId) setSelectedNamespace(defaultNamespaceId);
    if (defaultEntityName) setSelectedEntity(defaultEntityName);
  }, [defaultNamespaceId, defaultEntityName]);

  // Reset entity when namespace changes
  useEffect(() => {
    if (!defaultEntityName) {
      setSelectedEntity('');
    }
  }, [selectedNamespace, defaultEntityName]);

  if (!isOpen) return null;

  const entities = targetType === 'queue' ? queues : topics;

  const toggleScenario = (scenario: MessageScenario) => {
    setSelectedScenarios((prev) =>
      prev.includes(scenario)
        ? prev.filter((s) => s !== scenario)
        : [...prev, scenario]
    );
  };

  const handleGenerate = async () => {
    if (!selectedNamespace || !selectedEntity || selectedScenarios.length === 0) {
      toast.error('Please select a namespace, entity, and at least one scenario');
      return;
    }

    setIsGenerating(true);
    
    // Show initial toast with progress
    const toastId = toast.loading('Generating messages...');

    try {
      const config: GenerationConfig = {
        targetType,
        queueName: targetType === 'queue' ? selectedEntity : undefined,
        topicName: targetType === 'topic' ? selectedEntity : undefined,
        volume,
        scenarios: selectedScenarios,
        anomalyRate,
        includeStructuredData: true,
      };

      const messages = generateMessages(config);

      // Send messages to the API
      let successCount = 0;
      let errorCount = 0;
      const errors: string[] = [];

      // Send in batches to avoid overwhelming the API
      const batchSize = 10;
      for (let i = 0; i < messages.length; i += batchSize) {
        const batch = messages.slice(i, i + batchSize);
        
        // Update progress
        const progress = Math.round((i / messages.length) * 100);
        toast.loading(`Generating messages... ${progress}%`, { id: toastId });
        
        const results = await Promise.allSettled(
          batch.map(async (msg) => {
            await messagesApi.send(
              selectedNamespace, 
              selectedEntity, 
              {
                body: msg.body,
                contentType: msg.contentType,
                properties: msg.properties,
                correlationId: msg.correlationId,
                sessionId: msg.sessionId,
              },
              targetType
            );
          })
        );

        // Count successes and failures
        results.forEach((result) => {
          if (result.status === 'fulfilled') {
            successCount++;
          } else {
            errorCount++;
            const error = result.reason;
            const errorMsg = error?.response?.data?.message || error?.message || 'Unknown error';
            errors.push(errorMsg);
          }
        });

        // Small delay between batches
        if (i + batchSize < messages.length) {
          await new Promise((resolve) => setTimeout(resolve, 100));
        }
      }

      // Dismiss loading toast
      toast.dismiss(toastId);

      // Show final result
      if (errorCount === 0) {
        toast.success(`✅ Generated ${successCount} messages successfully!`, {
          duration: 4000,
        });
      } else if (successCount > 0) {
        toast.success(
          `Generated ${successCount} messages (${errorCount} failed).\nCheck console for details.`,
          { duration: 5000 }
        );
        console.error('Message generation errors:', errors);
      } else {
        toast.error(
          `Failed to generate messages. ${errors[0] || 'Unknown error'}`,
          { duration: 5000 }
        );
        console.error('All messages failed:', errors);
      }

      // Only call onGenerated if at least some messages succeeded
      if (successCount > 0) {
        onGenerated?.();
      }
      
      // Close modal on success
      if (errorCount === 0 || successCount > 0) {
        onClose();
      }
    } catch (error) {
      toast.dismiss(toastId);
      console.error('Generation failed:', error);
      const errorMsg = error instanceof Error ? error.message : 'Unknown error';
      toast.error(`Failed to generate messages: ${errorMsg}`, { duration: 5000 });
    } finally {
      setIsGenerating(false);
    }
  };

  const isValid = selectedNamespace && selectedEntity && selectedScenarios.length > 0;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      {/* Backdrop */}
      <div className="absolute inset-0 bg-black/50" onClick={onClose} />

      {/* Modal */}
      <div className="relative bg-white rounded-xl shadow-2xl w-full max-w-2xl max-h-[90vh] overflow-hidden flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200 bg-gradient-to-r from-primary-50 to-blue-50">
          <div className="flex items-center gap-3">
            <div className="p-2 bg-primary-100 rounded-lg">
              <Wand2 className="w-5 h-5 text-primary-600" />
            </div>
            <div>
              <h2 className="text-lg font-semibold text-gray-900">Message Generator</h2>
              <p className="text-sm text-gray-500">Generate realistic test messages for demo & validation</p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="p-1 hover:bg-gray-100 rounded-lg transition-colors"
          >
            <X className="w-5 h-5 text-gray-500" />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto p-6 space-y-6">
          {/* Info Banner */}
          <div className="flex items-start gap-3 p-4 bg-primary-50 border border-primary-200 rounded-lg">
            <Info className="w-5 h-5 text-primary-600 shrink-0 mt-0.5" />
            <div className="text-sm text-primary-800">
              <p className="font-medium mb-1">About Generated Messages</p>
              <p className="text-primary-700">
                All generated messages are tagged with <code className="bg-primary-100 px-1 rounded">{GENERATOR_PROPERTY_KEY}</code> property 
                for easy identification. Messages include realistic business scenarios with structured JSON bodies, 
                headers, and configurable anomalies for AI Insights testing.
              </p>
            </div>
          </div>

          {/* Target Selection */}
          <div className="space-y-4">
            <h3 className="text-sm font-semibold text-gray-700 uppercase tracking-wider">Target</h3>
            
            {/* Namespace */}
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">Namespace</label>
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

            {/* Entity Type Toggle */}
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">Entity Type</label>
              <div className="flex gap-2">
                <button
                  onClick={() => setTargetType('queue')}
                  className={`flex-1 px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
                    targetType === 'queue'
                      ? 'bg-primary-500 text-white'
                      : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
                  }`}
                >
                  📥 Queue
                </button>
                <button
                  onClick={() => setTargetType('topic')}
                  className={`flex-1 px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
                    targetType === 'topic'
                      ? 'bg-primary-500 text-white'
                      : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
                  }`}
                >
                  📢 Topic
                </button>
              </div>
            </div>

            {/* Entity Selection */}
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">
                {targetType === 'queue' ? 'Queue' : 'Topic'}
              </label>
              <select
                value={selectedEntity}
                onChange={(e) => setSelectedEntity(e.target.value)}
                disabled={!selectedNamespace}
                className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-primary-300 disabled:bg-gray-50 disabled:cursor-not-allowed"
              >
                <option value="">Select {targetType}...</option>
                {entities?.map((e) => (
                  <option key={e.name} value={e.name}>
                    {targetType === 'queue' ? '📥' : '📢'} {e.name}
                  </option>
                ))}
              </select>
            </div>
          </div>

          {/* Volume Selection */}
          <div className="space-y-3">
            <h3 className="text-sm font-semibold text-gray-700 uppercase tracking-wider">Volume</h3>
            <div className="flex gap-2">
              {VOLUME_PRESETS.map((preset) => (
                <button
                  key={preset}
                  onClick={() => setVolume(preset)}
                  className={`flex-1 px-4 py-3 rounded-lg text-sm font-semibold transition-colors ${
                    volume === preset
                      ? 'bg-primary-500 text-white shadow-md'
                      : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
                  }`}
                >
                  {preset}
                </button>
              ))}
            </div>
            <p className="text-xs text-gray-500">Messages will be generated with varied timestamps and content</p>
          </div>

          {/* Scenario Selection */}
          <div className="space-y-3">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-semibold text-gray-700 uppercase tracking-wider">Scenarios</h3>
              <button
                onClick={() => setSelectedScenarios(
                  selectedScenarios.length === Object.keys(SCENARIO_INFO).length
                    ? []
                    : getDefaultScenarios()
                )}
                className="text-xs text-primary-600 hover:text-primary-700 font-medium"
              >
                {selectedScenarios.length === Object.keys(SCENARIO_INFO).length ? 'Deselect All' : 'Select All'}
              </button>
            </div>
            <div className="grid grid-cols-2 gap-2">
              {(Object.entries(SCENARIO_INFO) as [MessageScenario, typeof SCENARIO_INFO[MessageScenario]][]).map(
                ([scenario, info]) => {
                  const Icon = info.icon;
                  const isSelected = selectedScenarios.includes(scenario);
                  return (
                    <button
                      key={scenario}
                      onClick={() => toggleScenario(scenario)}
                      className={`flex items-start gap-3 p-3 rounded-lg text-left transition-all ${
                        isSelected
                          ? 'bg-primary-50 border-2 border-primary-400 shadow-sm'
                          : 'bg-gray-50 border-2 border-transparent hover:bg-gray-100'
                      }`}
                    >
                      <div className={`p-1.5 rounded-md ${isSelected ? 'bg-primary-100' : 'bg-gray-200'}`}>
                        <Icon className={`w-4 h-4 ${isSelected ? 'text-primary-600' : 'text-gray-500'}`} />
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2">
                          <span className={`text-sm font-medium ${isSelected ? 'text-primary-700' : 'text-gray-700'}`}>
                            {info.label}
                          </span>
                          {isSelected && <Check className="w-3.5 h-3.5 text-primary-600" />}
                        </div>
                        <p className="text-xs text-gray-500 truncate">{info.description}</p>
                      </div>
                    </button>
                  );
                }
              )}
            </div>
          </div>

          {/* Anomaly Rate */}
          <div className="space-y-3">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-semibold text-gray-700 uppercase tracking-wider flex items-center gap-2">
                <AlertTriangle className="w-4 h-4 text-amber-500" />
                Anomaly Rate
              </h3>
              <span className="text-sm font-semibold text-amber-600">{anomalyRate}%</span>
            </div>
            <input
              type="range"
              min="0"
              max="50"
              value={anomalyRate}
              onChange={(e) => setAnomalyRate(parseInt(e.target.value))}
              className="w-full h-2 bg-gray-200 rounded-lg appearance-none cursor-pointer accent-amber-500"
            />
            <div className="flex justify-between text-xs text-gray-500">
              <span>0% (Normal)</span>
              <span>25% (Moderate)</span>
              <span>50% (Stress Test)</span>
            </div>
            <p className="text-xs text-gray-500">
              Anomalous messages simulate: DLQ candidates, retry loops, poison messages, and latency spikes.
              These help test AI Insights pattern detection.
            </p>
          </div>

          {/* Cleanup Section */}
          <div className="border-t border-gray-200 pt-4">
            <button
              onClick={() => setShowCleanup(!showCleanup)}
              className="flex items-center gap-2 text-sm text-gray-600 hover:text-gray-800"
            >
              <Trash2 className="w-4 h-4" />
              {showCleanup ? 'Hide Cleanup Options' : 'Show Cleanup Options'}
            </button>
            
            {showCleanup && (
              <div className="mt-3 p-4 bg-red-50 border border-red-200 rounded-lg">
                <p className="text-sm text-red-800 mb-3">
                  <strong>Cleanup Generated Messages</strong><br />
                  This will purge all messages with the <code className="bg-red-100 px-1 rounded">{GENERATOR_PROPERTY_KEY}</code> property.
                  Real messages will NOT be affected.
                </p>
                <button
                  className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white text-sm font-medium rounded-lg transition-colors"
                  onClick={() => {
                    toast('Cleanup feature requires backend support. Messages can be identified by the ServiceHub-Generated property.', {
                      icon: '🧹',
                      duration: 5000,
                    });
                  }}
                >
                  Purge Generated Messages
                </button>
              </div>
            )}
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between px-6 py-4 border-t border-gray-200 bg-gray-50">
          <div className="text-sm text-gray-500">
            {selectedScenarios.length} scenario(s) × {volume} messages = <strong>{selectedScenarios.length > 0 ? volume : 0}</strong> total
          </div>
          <div className="flex gap-3">
            <button
              onClick={onClose}
              className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg transition-colors"
            >
              Cancel
            </button>
            <button
              onClick={handleGenerate}
              disabled={!isValid || isGenerating}
              className={`flex items-center gap-2 px-6 py-2 text-sm font-medium rounded-lg transition-colors ${
                isValid && !isGenerating
                  ? 'bg-primary-500 hover:bg-primary-600 text-white'
                  : 'bg-gray-200 text-gray-400 cursor-not-allowed'
              }`}
            >
              {isGenerating ? (
                <>
                  <Loader2 className="w-4 h-4 animate-spin" />
                  Generating...
                </>
              ) : (
                <>
                  <Wand2 className="w-4 h-4" />
                  Generate Messages
                </>
              )}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
