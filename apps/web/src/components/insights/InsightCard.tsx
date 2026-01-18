import { formatRelativeTime } from '@/lib/utils';
import type { InsightDetail, InsightSeverity } from '@/lib/insightsMockData';

// ============================================================================
// InsightCard - Individual insight display with metrics and recommendations
// ============================================================================

interface InsightCardProps {
  insight: InsightDetail;
}

// Severity configuration
const SEVERITY_CONFIG: Record<InsightSeverity, { 
  label: string; 
  textColor: string; 
  bgColor: string; 
  borderColor: string;
  dotColor: string;
}> = {
  high: {
    label: 'HIGH SEVERITY',
    textColor: 'text-red-600',
    bgColor: 'bg-red-50',
    borderColor: 'border-l-red-500',
    dotColor: 'bg-red-500',
  },
  medium: {
    label: 'MEDIUM SEVERITY',
    textColor: 'text-amber-600',
    bgColor: 'bg-amber-50',
    borderColor: 'border-l-amber-500',
    dotColor: 'bg-amber-500',
  },
  low: {
    label: 'LOW SEVERITY',
    textColor: 'text-blue-600',
    bgColor: 'bg-blue-50',
    borderColor: 'border-l-blue-500',
    dotColor: 'bg-blue-500',
  },
};

// Priority labels
const PRIORITY_LABELS: Record<string, { label: string; color: string }> = {
  immediate: { label: 'Immediate', color: 'bg-red-100 text-red-700' },
  'short-term': { label: 'Short-term', color: 'bg-amber-100 text-amber-700' },
  'long-term': { label: 'Long-term', color: 'bg-blue-100 text-blue-700' },
  prevention: { label: 'Prevention', color: 'bg-green-100 text-green-700' },
};

export function InsightCard({ insight }: InsightCardProps) {
  const severity = SEVERITY_CONFIG[insight.severity];

  return (
    <div
      className={`
        bg-white rounded-xl border border-gray-200 overflow-hidden
        shadow-sm border-l-4 ${severity.borderColor}
      `}
    >
      {/* Header */}
      <div className="px-6 py-4 border-b border-gray-100 bg-white">
        <div className="flex items-center justify-between mb-2">
          <div className="flex items-center gap-2">
            <span className={`w-2 h-2 rounded-full ${severity.dotColor}`} />
            <span className={`text-xs font-semibold uppercase tracking-wide ${severity.textColor}`}>
              {severity.label}
            </span>
          </div>
          <span className="text-xs text-gray-500">
            Detected {formatRelativeTime(insight.detectedAt)}
          </span>
        </div>
        <h3 className="text-lg font-semibold text-gray-900">{insight.title}</h3>
      </div>

      {/* Description */}
      <div className="px-6 py-4 border-b border-gray-100">
        <p className="text-sm text-gray-600 leading-relaxed">{insight.description}</p>
      </div>

      {/* Metrics Grid */}
      <div className="px-6 py-4 border-b border-gray-100 bg-gray-50">
        <div className="grid grid-cols-3 gap-4">
          {insight.metrics.map((metric, index) => (
            <div key={index} className="text-center">
              <div className="text-xs font-medium text-gray-500 uppercase tracking-wide mb-1">
                {metric.label}
              </div>
              <div
                className={`text-2xl font-bold ${metric.highlight ? 'text-red-600' : 'text-gray-900'}`}
              >
                {metric.value}
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Recommendations */}
      <div className="px-6 py-4">
        <div className="flex items-center gap-2 mb-3">
          <span className="text-yellow-500">💡</span>
          <span className="text-sm font-semibold text-primary-600">Recommended Actions</span>
        </div>
        <ul className="space-y-2">
          {insight.recommendations.map((rec, index) => {
            const priorityConfig = PRIORITY_LABELS[rec.priority] || PRIORITY_LABELS['short-term'];
            return (
              <li key={index} className="flex items-start gap-3">
                <span
                  className={`
                    shrink-0 px-2 py-0.5 rounded text-xs font-medium
                    ${priorityConfig.color}
                  `}
                >
                  {priorityConfig.label}
                </span>
                <span className="text-sm text-gray-700">{rec.text}</span>
              </li>
            );
          })}
        </ul>
      </div>
    </div>
  );
}
