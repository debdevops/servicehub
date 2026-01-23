import { Sparkles, AlertCircle } from 'lucide-react';
import { useSearchParams } from 'react-router-dom';
import type { Message } from '@/lib/mockData';
import { useInsights } from '@/hooks/useInsights';
import type { AIInsight } from '@/lib/api/types';

// ============================================================================
// AIInsightsTab - Shows AI pattern membership for selected message
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

  // Fetch insights for this queue
  const { data: insights, isLoading } = useInsights({
    namespaceId: namespaceId || '',
    queueOrTopicName: queueName || undefined,
    status: 'active',
  });

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

  // Empty state when not part of any pattern
  if (memberPatterns.length === 0) {
    return (
      <div className="p-6">
        <div className="flex flex-col items-center justify-center py-16 text-gray-500">
          <Sparkles size={48} className="text-gray-300 mb-4" />
          <p className="text-lg font-medium text-gray-700">No Patterns Detected</p>
          <p className="text-sm text-gray-400 mt-1 text-center max-w-sm">
            This message is not part of any AI-detected patterns. It appears to be processing normally.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="p-6">
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
