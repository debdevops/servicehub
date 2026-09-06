import { useNavigate } from 'react-router-dom';
import { CheckCircle2, AlertTriangle, TrendingUp, Clock, RefreshCw, ShieldCheck, ShieldOff, Bot, Ban } from 'lucide-react';
import { useAttentionQueue, type AttentionQueueItem } from '@servicehub/ui-shared/hooks/useAttentionQueue';
import { useOutcomeMetrics } from '@servicehub/ui-shared/hooks/useRecoveryLedger';
import { formatRelativeTime } from '@servicehub/ui-shared/lib/utils';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { EmptyState } from '@/components/EmptyState';

/** "1h 24m", "3d 2h", "42s" — coarsest two units, never more precise than seconds. */
function formatDuration(totalSeconds: number): string {
  const seconds = Math.max(0, Math.round(totalSeconds));
  const units: [string, number][] = [
    ['d', 86400],
    ['h', 3600],
    ['m', 60],
  ];
  for (let i = 0; i < units.length; i++) {
    const [label, size] = units[i];
    if (seconds >= size) {
      const whole = Math.floor(seconds / size);
      const remainder = seconds % size;
      const [nextLabel, nextSize] = units[i + 1] ?? ['s', 1];
      const nextWhole = Math.floor(remainder / nextSize);
      return nextWhole > 0 ? `${whole}${label} ${nextWhole}${nextLabel}` : `${whole}${label}`;
    }
  }
  return `${seconds}s`;
}

/**
 * What ServiceHub achieved this week (roadmap next-chapter M4.1) — never how autonomous it is.
 * Every figure comes straight from the outcome-metrics endpoint, which itself traces every number
 * to a RecoveryLedgerEntry/RecoveryEvent row. Renders nothing (not even a zero-state) until the
 * fleet has actually recovered or abandoned something in the window, since an all-zero row reads
 * as "broken" rather than "quiet" on a brand-new install.
 */
function OutcomesThisWeek() {
  const { data, isLoading, isError } = useOutcomeMetrics(7);

  if (isLoading || isError || !data) return null;
  if (data.messagesRecovered === 0 && data.messagesAbandoned === 0 && data.gateRefusals === 0) return null;

  const tiles = [
    {
      icon: ShieldCheck,
      color: 'text-emerald-600 bg-emerald-50',
      label: 'Recovered',
      value: data.messagesRecovered.toLocaleString(),
    },
    {
      icon: ShieldOff,
      color: 'text-gray-600 bg-gray-100',
      label: 'Written off',
      value: data.messagesAbandoned.toLocaleString(),
    },
    {
      icon: Clock,
      color: 'text-sky-600 bg-sky-50',
      label: 'Median time to recovered',
      value: data.medianSecondsToVerifiedRecovery != null ? formatDuration(data.medianSecondsToVerifiedRecovery) : '—',
    },
    {
      icon: Bot,
      color: 'text-indigo-600 bg-indigo-50',
      label: 'No human approval needed',
      value: data.autonomousRecoveries.toLocaleString(),
    },
    {
      icon: Ban,
      color: 'text-red-600 bg-red-50',
      label: 'Bad replays refused',
      value: data.gateRefusals.toLocaleString(),
    },
  ];

  return (
    <div className="mb-6 bg-white border border-gray-200 rounded-lg p-4">
      <p className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-3">This week</p>
      <div className="grid grid-cols-2 sm:grid-cols-5 gap-4">
        {tiles.map((tile) => (
          <div key={tile.label} className="flex items-start gap-2.5">
            <span className={`shrink-0 w-8 h-8 rounded-lg flex items-center justify-center ${tile.color}`}>
              <tile.icon className="w-4 h-4" />
            </span>
            <div className="min-w-0">
              <p className="text-lg font-semibold text-gray-900 leading-tight">{tile.value}</p>
              <p className="text-xs text-gray-500 leading-tight">{tile.label}</p>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

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
      onClick={() => navigate(`${navPrefix}/incidents/${item.signatureHash}?namespace=${item.namespaceId}`)}
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

      <OutcomesThisWeek />

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
