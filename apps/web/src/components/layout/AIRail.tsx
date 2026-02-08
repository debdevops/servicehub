import { X, AlertTriangle, AlertCircle, Info, ExternalLink } from 'lucide-react';

type Severity = 'high' | 'medium' | 'low';

interface AIInsight {
  id: string;
  severity: Severity;
  title: string;
  summary: string;
  affectedCount: number;
  timestamp: string;
}

// Mock data - will be replaced with real data later
const mockInsights: AIInsight[] = [
  {
    id: '1',
    severity: 'high',
    title: 'Payment Gateway Timeout',
    summary: 'Payment validation failures increased by 340% in the last hour. 23 messages affected.',
    affectedCount: 23,
    timestamp: '15 min ago',
  },
  {
    id: '2',
    severity: 'high',
    title: 'DLQ Accumulation',
    summary: 'OrdersQueue DLQ grew from 2 to 47 messages. JSON deserialization failures detected.',
    affectedCount: 47,
    timestamp: '32 min ago',
  },
  {
    id: '3',
    severity: 'medium',
    title: 'High Retry Rate',
    summary: 'NotificationsQueue showing 15% retry rate, above normal threshold of 5%.',
    affectedCount: 156,
    timestamp: '1 hour ago',
  },
];

const severityConfig: Record<Severity, { color: string; bg: string; border: string; icon: typeof AlertTriangle }> = {
  high: {
    color: 'text-red-700',
    bg: 'bg-red-50',
    border: 'border-l-red-500',
    icon: AlertTriangle,
  },
  medium: {
    color: 'text-amber-700',
    bg: 'bg-amber-50',
    border: 'border-l-amber-500',
    icon: AlertCircle,
  },
  low: {
    color: 'text-primary-700',
    bg: 'bg-primary-50',
    border: 'border-l-primary-500',
    icon: Info,
  },
};

function InsightCard({ insight }: { insight: AIInsight }) {
  const config = severityConfig[insight.severity];
  const Icon = config.icon;

  return (
    <div
      className={`${config.bg} ${config.border} border-l-4 rounded-xl p-3 mb-3 backdrop-blur-sm transition-all hover:shadow-md`}
      style={{ boxShadow: '0 2px 10px rgba(14, 165, 233, 0.05)' }}
    >
      <div className="flex items-start justify-between gap-2 mb-1">
        <div className="flex items-center gap-1.5">
          <Icon className={`w-4 h-4 ${config.color}`} />
          <span className={`text-xs font-semibold uppercase ${config.color}`}>
            {insight.severity}
          </span>
        </div>
        <span className="text-xs text-gray-500">{insight.timestamp}</span>
      </div>

      <h4 className="font-medium text-sm text-gray-900 mb-1">{insight.title}</h4>
      <p className="text-xs text-gray-600 line-clamp-2 mb-2">{insight.summary}</p>

      <button className="flex items-center gap-1 text-xs text-primary-600 hover:text-primary-700 font-medium">
        View {insight.affectedCount} messages
        <ExternalLink className="w-3 h-3" />
      </button>
    </div>
  );
}

interface AIRailProps {
  isOpen: boolean;
  onClose: () => void;
}

export function AIRail({ isOpen, onClose }: AIRailProps) {
  if (!isOpen) return null;

  return (
    <aside className="w-80 bg-white/80 backdrop-blur-sm border-l border-white/40 flex flex-col h-full shadow-lg">
      {/* Header */}
      <div className="flex items-center justify-between p-4 border-b border-primary-100 bg-gradient-to-r from-primary-50 to-white">
        <div className="flex items-center gap-2">
          <span className="text-lg">🤖</span>
          <h2 className="font-semibold text-gray-900">AI Insights</h2>
          <span className="px-2 py-0.5 bg-primary-500 text-white text-xs font-bold rounded-full">
            {mockInsights.length}
          </span>
        </div>
        <button
          onClick={onClose}
          className="p-1 hover:bg-primary-100 rounded transition-colors"
          title="Close"
        >
          <X className="w-5 h-5 text-gray-500" />
        </button>
      </div>

      {/* Insights List */}
      <div className="flex-1 overflow-y-auto p-4">
        {mockInsights.length > 0 ? (
          mockInsights.map((insight) => (
            <InsightCard key={insight.id} insight={insight} />
          ))
        ) : (
          <div className="text-center py-8">
            <div className="w-12 h-12 bg-green-100 rounded-full flex items-center justify-center mx-auto mb-3">
              <span className="text-2xl">✓</span>
            </div>
            <h3 className="font-medium text-gray-900 mb-1">All Clear</h3>
            <p className="text-sm text-gray-500">No anomalies detected</p>
          </div>
        )}
      </div>

      {/* Footer */}
      <div className="border-t border-primary-100 p-4">
        <a
          href="/insights"
          className="flex items-center justify-center gap-2 w-full px-4 py-2 bg-white hover:bg-gray-50 border border-gray-200 rounded-lg text-sm font-medium text-gray-700 transition-colors"
        >
          View all insights
          <ExternalLink className="w-4 h-4" />
        </a>
      </div>
    </aside>
  );
}
