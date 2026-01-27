import { X } from 'lucide-react';
import { useEffect } from 'react';
import type { AIInsight } from '@/lib/api/types';

// ============================================================================
// AIFindingsDropdown - Dropdown showing active AI patterns
// ============================================================================

interface AIFindingsDropdownProps {
  insights: AIInsight[];
  onClose: () => void;
  onViewEvidence: (messageIds: string[]) => void;
}

const TYPE_ICONS: Record<string, string> = {
  'dlq-pattern': '📥',
  'retry-loop': '🔄',
  'error-cluster': '⚠️',
  'latency-anomaly': '⏱️',
  'poison-message': '☠️',
};

const CONFIDENCE_COLORS: Record<string, string> = {
  high: 'bg-green-100 text-green-700',
  medium: 'bg-amber-100 text-amber-700',
  low: 'bg-gray-100 text-gray-600',
};

export function AIFindingsDropdown({ insights, onClose, onViewEvidence }: AIFindingsDropdownProps) {
  const activeInsights = insights.filter((i) => i.status === 'active');

  // Handle Escape key to close dropdown
  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
      }
    };
    
    window.addEventListener('keydown', handleEscape);
    return () => window.removeEventListener('keydown', handleEscape);
  }, [onClose]);

  return (
    <>
      {/* Backdrop */}
      <div 
        className="fixed inset-0 z-40" 
        onClick={onClose}
      />
      
      {/* Dropdown */}
      <div className="absolute top-full right-0 mt-2 w-[420px] bg-white rounded-xl shadow-xl border border-gray-200 z-50 overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-200 bg-gray-50">
          <div>
            <h3 className="font-semibold text-gray-900">Active AI Patterns</h3>
            <p className="text-xs text-gray-500 mt-0.5">
              {activeInsights.length} pattern{activeInsights.length !== 1 ? 's' : ''} detected
            </p>
          </div>
          <button
            onClick={onClose}
            className="p-1 hover:bg-gray-200 rounded-lg transition-colors"
          >
            <X className="w-4 h-4 text-gray-500" />
          </button>
        </div>

        {/* Insights List */}
        <div className="max-h-[400px] overflow-y-auto">
          {activeInsights.length === 0 ? (
            <div className="p-8 text-center text-gray-500">
              <p className="text-sm">No active patterns detected</p>
              <p className="text-xs mt-1">AI is monitoring your queues</p>
            </div>
          ) : (
            activeInsights.map((insight) => (
              <div
                key={insight.id}
                className="p-4 border-b border-gray-100 hover:bg-gray-50 transition-colors"
              >
                {/* Title Row */}
                <div className="flex items-start justify-between gap-3 mb-2">
                  <div className="flex items-start gap-2">
                    <span className="text-lg" title={insight.type}>
                      {TYPE_ICONS[insight.type] || '🔍'}
                    </span>
                    <h4 className="font-medium text-sm text-gray-900 leading-tight">
                      {insight.title}
                    </h4>
                  </div>
                  <span
                    className={`shrink-0 text-xs px-2 py-0.5 rounded-full font-medium ${
                      CONFIDENCE_COLORS[insight.confidence.level]
                    }`}
                  >
                    {insight.confidence.score}%
                  </span>
                </div>

                {/* Description */}
                <p className="text-sm text-gray-600 mb-3 leading-relaxed">
                  {insight.description}
                </p>

                {/* Metrics */}
                <div className="flex items-center gap-4 text-xs text-gray-500 mb-3">
                  {insight.evidence.metrics.slice(0, 3).map((metric, idx) => (
                    <span key={idx} className={metric.isAnomaly ? 'text-red-600 font-medium' : ''}>
                      {metric.label}: <strong>{metric.value}</strong>
                    </span>
                  ))}
                </div>

                {/* Action */}
                <button
                  onClick={() => onViewEvidence(insight.evidence.affectedMessageIds)}
                  className="text-sm text-primary-600 hover:text-primary-700 font-medium flex items-center gap-1"
                >
                  View {insight.evidence.affectedMessageIds.length} affected messages
                  <span>→</span>
                </button>
              </div>
            ))
          )}
        </div>

        {/* Footer */}
        {activeInsights.length > 0 && (
          <div className="px-4 py-3 border-t border-gray-200 bg-gray-50">
            <p className="text-xs text-gray-500">
              💡 Click a pattern to filter messages and investigate
            </p>
            <p className="text-xs text-gray-400 mt-1 italic">
              ServiceHub Interpretation — AI-assisted patterns, not Azure data
            </p>
          </div>
        )}
      </div>
    </>
  );
}
