import { Sparkles, AlertCircle, Info } from 'lucide-react';
import { useSearchParams } from 'react-router-dom';
import type { Message } from '@/lib/mockData';
import { useClientSideInsights } from '@/hooks/useInsights';
import { useMessages } from '@/hooks/useMessages';
import type { AIInsight } from '@/lib/api/types';

// ============================================================================
// AIInsightsTab - Shows AI pattern membership for selected message
// 
// TRUST GUARANTEES:
// - All insights labeled as "ServiceHub Interpretation"
// - AI never presents inference as fact
// - Uncertainty explicitly stated
// - Evidence (counts, IDs) always cited
// ============================================================================

interface AIInsightsTabProps {
  message: Message;
  onViewPattern?: (messageIds: string[]) => void;
}

const PRIORITY_COLORS: Record<string, string> = {
  immediate: 'bg-red-100 text-red-700',
  'short-term': 'bg-amber-100 text-amber-700',
  investigative: 'bg-primary-100 text-primary-700',
};

function PatternCard({ 
  pattern, 
  messageId, 
  onViewPattern 
}: { 
  pattern: AIInsight; 
  messageId: string;
  onViewPattern?: (messageIds: string[]) => void;
}) {
  const isExample = pattern.evidence.exampleMessageIds.includes(messageId);
  const affectedMessageIds = pattern.evidence.affectedMessageIds;

  return (
    <div className="bg-primary-50 border border-primary-200 rounded-xl p-5">
      {/* Header */}
      <div className="flex items-start justify-between mb-3">
        <h4 className="font-semibold text-primary-900">{pattern.title}</h4>
        <span className="text-xs px-2 py-1 bg-white rounded-lg border border-primary-200 font-medium">
          {isExample ? '📌 Example' : '🔗 Affected'}
        </span>
      </div>

      {/* Description */}
      <p className="text-sm text-primary-700 mb-4 leading-relaxed">
        {pattern.description}
      </p>

      {/* Metrics */}
      <div className="flex items-center gap-4 text-xs text-primary-600 mb-4">
        <span>
          Confidence: <strong>{pattern.confidence.score}%</strong>
        </span>
        <span>•</span>
        <span>
          <strong>{affectedMessageIds.length}</strong> affected messages
        </span>
      </div>

      {/* Recommendations */}
      <div className="bg-white rounded-lg p-4 mb-4">
        <h5 className="text-xs font-semibold text-gray-700 mb-2 flex items-center gap-1">
          💡 Recommendations
        </h5>
        <ul className="space-y-2">
          {pattern.recommendations.slice(0, 3).map((rec, i) => (
            <li key={i} className="flex items-start gap-2">
              <span
                className={`shrink-0 text-xs px-2 py-0.5 rounded font-medium ${
                  PRIORITY_COLORS[rec.priority] || PRIORITY_COLORS['short-term']
                }`}
              >
                {rec.priority}
              </span>
              <span className="text-sm text-gray-700">{rec.title}</span>
            </li>
          ))}
        </ul>
      </div>

      {/* Action */}
      <button
        onClick={() => onViewPattern?.(affectedMessageIds)}
        className="text-sm text-primary-600 hover:text-primary-700 font-medium flex items-center gap-1"
      >
        View all {affectedMessageIds.length} affected messages
        <span>→</span>
      </button>
    </div>
  );
}

export function AIInsightsTab({ message, onViewPattern }: AIInsightsTabProps) {
  const [searchParams] = useSearchParams();
  const namespaceId = searchParams.get('namespace');
  const queueName = searchParams.get('queue');
  const topicName = searchParams.get('topic');
  const subscriptionName = searchParams.get('subscription');
  
  // Determine entity name
  const entityName = queueName || (topicName && subscriptionName ? `${topicName}/subscriptions/${subscriptionName}` : topicName) || '';
  const entityType: 'queue' | 'topic' = topicName ? 'topic' : 'queue';

  // Fetch messages for client-side AI analysis
  const { data: messagesData, isLoading: messagesLoading } = useMessages({
    namespaceId: namespaceId || '',
    queueOrTopicName: entityName,
    entityType,
    queueType: message.queueType || 'active',
    skip: 0,
    take: 1000,
  });

  // Perform client-side AI analysis
  const { data: insights, isLoading: insightsLoading, isError } = useClientSideInsights(
    messagesData?.items,
    {
      namespaceId: namespaceId || '',
      entityName,
      subscriptionName: subscriptionName || undefined,
      entityType,
    },
    !!namespaceId && !!entityName && !messagesLoading
  );

  const isLoading = messagesLoading || insightsLoading;

  // Find patterns this message belongs to
  const memberPatterns = (insights || []).filter((insight) => {
    const affectedIds = insight.evidence.affectedMessageIds;
    const exampleIds = insight.evidence.exampleMessageIds;
    return affectedIds.includes(message.id) || exampleIds.includes(message.id);
  });

  // Loading state
  if (isLoading) {
    return (
      <div className="p-6">
        <div className="flex flex-col items-center justify-center py-16 text-gray-500">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-primary-500"></div>
          <p className="text-sm text-gray-400 mt-4">Loading AI insights...</p>
        </div>
      </div>
    );
  }

  // Error state - AI insights not available
  if (isError || !insights) {
    return (
      <div className="p-6">
        <div className="flex flex-col items-center justify-center py-16 text-gray-500">
          <div className="w-16 h-16 bg-gray-100 rounded-full flex items-center justify-center mb-4">
            <Sparkles size={32} className="text-gray-300" />
          </div>
          <p className="text-lg font-medium text-gray-700">AI Insights Not Available</p>
          <p className="text-sm text-gray-400 mt-1 text-center max-w-sm">
            AI pattern analysis is not enabled for this namespace, or the AI service is currently unavailable.
          </p>
          <p className="text-xs text-gray-400 mt-3 text-center">
            The application works normally without AI features.
          </p>
        </div>
      </div>
    );
  }

  // Empty state when not part of any pattern
  if (memberPatterns.length === 0) {
    return (
      <div className="p-6">
        <div className="flex flex-col items-center justify-center py-16 text-gray-500">
          <div className="w-16 h-16 bg-green-50 rounded-full flex items-center justify-center mb-4">
            <Sparkles size={32} className="text-green-400" />
          </div>
          <p className="text-lg font-medium text-gray-700">No Patterns Detected</p>
          <p className="text-sm text-gray-400 mt-1 text-center max-w-sm">
            This message is not part of any AI-detected patterns. It appears to be processing normally.
          </p>
          <p className="text-xs text-gray-400 mt-3 text-center max-w-xs">
            AI analysis found no anomalies or recurring issues associated with this message.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="p-6">
      {/* Trust Disclaimer Banner */}
      <div className="mb-4 px-3 py-2 bg-blue-50 border border-blue-200 rounded-lg">
        <div className="flex items-start gap-2">
          <Info size={14} className="text-blue-500 mt-0.5 shrink-0" />
          <p className="text-xs text-blue-700">
            <strong>ServiceHub Interpretation:</strong> These patterns are AI-assisted analysis 
            based on message characteristics. They are not Azure Service Bus data. 
            Always verify findings before taking action.
          </p>
        </div>
      </div>

      {/* Header */}
      <div className="flex items-center gap-2 mb-4 text-sm text-gray-600">
        <AlertCircle size={16} className="text-primary-500" />
        <span>
          This message is part of <strong className="text-gray-900">{memberPatterns.length}</strong> detected pattern{memberPatterns.length > 1 ? 's' : ''}
        </span>
      </div>

      {/* Pattern Cards */}
      <div className="space-y-4">
        {memberPatterns.map((pattern) => (
          <PatternCard
            key={pattern.id}
            pattern={pattern}
            messageId={message.id}
            onViewPattern={onViewPattern}
          />
        ))}
      </div>
    </div>
  );
}
