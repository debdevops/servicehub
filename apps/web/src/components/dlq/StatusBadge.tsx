interface StatusBadgeProps {
  status: string;
  size?: 'sm' | 'md';
}

const statusStyles: Record<string, { bg: string; text: string; dot: string }> = {
  Active: { bg: 'bg-red-100', text: 'text-red-700', dot: 'bg-red-500' },
  Replayed: { bg: 'bg-green-100', text: 'text-green-700', dot: 'bg-green-500' },
  Archived: { bg: 'bg-gray-100', text: 'text-gray-700', dot: 'bg-gray-500' },
  Discarded: { bg: 'bg-yellow-100', text: 'text-yellow-700', dot: 'bg-yellow-500' },
  ReplayFailed: { bg: 'bg-orange-100', text: 'text-orange-700', dot: 'bg-orange-500' },
  Resolved: { bg: 'bg-sky-100', text: 'text-sky-700', dot: 'bg-sky-500' },
  Suppressed: { bg: 'bg-purple-100', text: 'text-purple-700', dot: 'bg-purple-500' },
  Reopened: { bg: 'bg-amber-100', text: 'text-amber-700', dot: 'bg-amber-500' },
  // Transient claim states: a worker holds this message and is calling the provider right now.
  // Briefly visible if the UI refreshes mid-operation. Without these they fell through to the
  // Active style, showing a message as red/"needs attention" while it was actively being handled.
  Replaying: { bg: 'bg-blue-100', text: 'text-blue-700', dot: 'bg-blue-500' },
  Purging: { bg: 'bg-blue-100', text: 'text-blue-700', dot: 'bg-blue-500' },
};

const trendStyles: Record<string, { bg: string; text: string }> = {
  New: { bg: 'bg-amber-100', text: 'text-amber-700' },
  Recurring: { bg: 'bg-blue-100', text: 'text-blue-700' },
  Escalating: { bg: 'bg-red-100', text: 'text-red-700' },
};

const categoryStyles: Record<string, { bg: string; text: string }> = {
  Transient: { bg: 'bg-blue-100', text: 'text-blue-700' },
  MaxDelivery: { bg: 'bg-red-100', text: 'text-red-700' },
  Expired: { bg: 'bg-amber-100', text: 'text-amber-700' },
  DataQuality: { bg: 'bg-purple-100', text: 'text-purple-700' },
  Authorization: { bg: 'bg-pink-100', text: 'text-pink-700' },
  ProcessingError: { bg: 'bg-orange-100', text: 'text-orange-700' },
  ResourceNotFound: { bg: 'bg-indigo-100', text: 'text-indigo-700' },
  QuotaExceeded: { bg: 'bg-rose-100', text: 'text-rose-700' },
  Unknown: { bg: 'bg-gray-100', text: 'text-gray-600' },
};

export function StatusBadge({ status, size = 'sm' }: StatusBadgeProps) {
  const style = statusStyles[status] || statusStyles.Active;
  const sizeClass = size === 'md' ? 'px-3 py-1 text-sm' : 'px-2 py-0.5 text-xs';

  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full font-medium ${style.bg} ${style.text} ${sizeClass}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${style.dot}`} />
      {status}
    </span>
  );
}

interface CategoryBadgeProps {
  category: string;
  confidence?: number;
  size?: 'sm' | 'md';
}

export function CategoryBadge({ category, confidence, size = 'sm' }: CategoryBadgeProps) {
  const style = categoryStyles[category] || categoryStyles.Unknown;
  const sizeClass = size === 'md' ? 'px-3 py-1 text-sm' : 'px-2 py-0.5 text-xs';

  return (
    <span
      className={`inline-flex items-center gap-1 rounded-full font-medium ${style.bg} ${style.text} ${sizeClass}`}
      title={confidence !== undefined ? `Confidence: ${(confidence * 100).toFixed(0)}%` : undefined}
    >
      {category}
      {confidence !== undefined && confidence > 0 && (
        <span className="opacity-60">({(confidence * 100).toFixed(0)}%)</span>
      )}
    </span>
  );
}

interface TrendBadgeProps {
  trend: string;
  size?: 'sm' | 'md';
}

export function TrendBadge({ trend, size = 'sm' }: TrendBadgeProps) {
  const style = trendStyles[trend] || trendStyles.Recurring;
  const sizeClass = size === 'md' ? 'px-3 py-1 text-sm' : 'px-2 py-0.5 text-xs';

  return (
    <span className={`inline-flex items-center rounded-full font-medium ${style.bg} ${style.text} ${sizeClass}`}>
      {trend}
    </span>
  );
}
