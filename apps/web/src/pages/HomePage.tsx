import { useNavigate } from 'react-router-dom';
import { CheckCircle2, AlertTriangle, TrendingUp, Clock, RefreshCw } from 'lucide-react';
import { useAttentionQueue, type AttentionQueueItem } from '@servicehub/ui-shared/hooks/useAttentionQueue';
import { formatRelativeTime } from '@servicehub/ui-shared/lib/utils';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { EmptyState } from '@/components/EmptyState';

const SEVERITY_STYLES: Record<AttentionQueueItem['severity'], { bg: string; text: string; border: string; dot: string }> = {
  Critical: { bg: 'bg-red-50', text: 'text-red-700', border: 'border-red-200', dot: 'bg-red-500' },
  Warning: { bg: 'bg-amber-50', text: 'text-amber-700', border: 'border-amber-200', dot: 'bg-amber-500' },
  Healthy: { bg: 'bg-green-50', text: 'text-green-700', border: 'border-green-200', dot: 'bg-green-500' },
  Unknown: { bg: 'bg-gray-50', text: 'text-gray-600', border: 'border-gray-200', dot: 'bg-gray-400' },
};

function AttentionCard({ item }: { item: AttentionQueueItem }) {
  const navigate = useNavigate();
  const { isDemoMode, cloudProvider } = useDemoContext();
  const navPrefix = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';
  const severity = SEVERITY_STYLES[item.severity] ?? SEVERITY_STYLES.Unknown;
  const isBlocked = item.pendingDecisionCount > 0;

  return (
    <button
      onClick={() => navigate(`${navPrefix}/signatures/${item.signatureHash}?namespace=${item.namespaceId}`)}
      className={`text-left w-full bg-white border-2 rounded-lg p-5 hover:shadow-md transition-shadow focus:outline-none focus:ring-2 focus:ring-primary-500 ${
        isBlocked ? 'border-primary-300' : severity.border
      }`}
    >
      <div className="flex items-start justify-between gap-3 mb-3">
        <span className={`inline-flex items-center gap-1.5 px-2 py-1 rounded-full text-xs font-semibold ${severity.bg} ${severity.text}`}>
          <span className={`w-1.5 h-1.5 rounded-full ${severity.dot}`} />
          {item.severity}
        </span>
        {isBlocked && (
          <span className="inline-flex items-center gap-1 px-2 py-1 rounded-full text-xs font-semibold bg-primary-100 text-primary-700">
            {item.pendingDecisionCount} pending {item.pendingDecisionCount === 1 ? 'decision' : 'decisions'}
          </span>
        )}
      </div>

      <h3 className="font-semibold text-gray-900 mb-1 line-clamp-2">{item.displayName}</h3>
      {item.namespaceName && <p className="text-xs text-gray-500 mb-3">{item.namespaceName}</p>}

      <div className="flex items-center gap-4 text-xs text-gray-500 mb-4">
        <span className="flex items-center gap-1">
          <AlertTriangle className="w-3.5 h-3.5" />
          {item.blastRadius} message{item.blastRadius === 1 ? '' : 's'}
        </span>
        {item.isRecurring && (
          <span className="flex items-center gap-1 text-red-600 font-medium">
            <TrendingUp className="w-3.5 h-3.5" />
            Recurring
          </span>
        )}
        <span className="flex items-center gap-1">
          <Clock className="w-3.5 h-3.5" />
          {formatRelativeTime(new Date(item.lastSeenAt))}
        </span>
      </div>

      <div className="pt-3 border-t border-gray-100">
        <p className="text-xs font-semibold text-gray-500 mb-0.5">Recommended</p>
        <p className="text-sm font-medium text-gray-900">{item.recommendedAction}</p>
      </div>
    </button>
  );
}

/**
 * Home as a ranked attention queue (roadmap W2.2). Three cards maximum, ordered by severity,
 * blast radius, recurrence, and whether a human decision is blocking — the "what needs me right
 * now" landing, downstream of the W2.1 Incident read-model.
 */
export function HomePage() {
  const { data, isLoading, isError, refetch, isFetching } = useAttentionQueue();

  return (
    <div className="p-6 max-w-5xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-xl font-semibold text-gray-900">Home</h1>
          <p className="text-sm text-gray-500">What needs your attention across every namespace you own.</p>
        </div>
        <button
          onClick={() => refetch()}
          disabled={isFetching}
          className="flex items-center gap-2 px-3 py-1.5 text-sm text-gray-600 hover:text-gray-900 border border-gray-200 rounded-lg hover:bg-gray-50 disabled:opacity-50"
        >
          <RefreshCw className={`w-3.5 h-3.5 ${isFetching ? 'animate-spin' : ''}`} />
          Refresh
        </button>
      </div>

      {isLoading && (
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          {[0, 1, 2].map((i) => (
            <div key={i} className="h-40 bg-gray-100 rounded-lg animate-pulse" />
          ))}
        </div>
      )}

      {isError && (
        <EmptyState
          icon={AlertTriangle}
          heading="Couldn't load the attention queue"
          subtext="Something went wrong fetching what needs your attention. Try again."
          action={{ label: 'Retry', onClick: () => refetch(), icon: RefreshCw }}
        />
      )}

      {!isLoading && !isError && data?.isEmpty && (
        <EmptyState
          icon={CheckCircle2}
          heading="Everything looks healthy"
          subtext="No failure signatures across your namespaces need attention right now."
        />
      )}

      {!isLoading && !isError && data && !data.isEmpty && (
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          {data.items.map((item) => (
            <AttentionCard key={`${item.namespaceId}-${item.signatureHash}`} item={item} />
          ))}
        </div>
      )}
    </div>
  );
}

export default HomePage;
