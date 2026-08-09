import { useNavigate } from 'react-router-dom';
import { useRef, useState, useEffect, useMemo } from 'react';
import {
  Globe,
  RefreshCw,
  AlertTriangle,
  CheckCircle,
  Plus,
  Activity,
  Inbox,
  Clock,
  BarChart2,
  Flame,
  GitMerge,
  TrendingUp,
  Zap,
  Info,
} from 'lucide-react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { LineChart, Line, ResponsiveContainer } from 'recharts';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useQueues, useAllNamespacesQueues, useNamespaceStats, NamespaceQueueStats } from '@servicehub/ui-shared/hooks/useQueues';
import { useTopics } from '@servicehub/ui-shared/hooks/useTopics';
import { useEventStream } from '@servicehub/ui-shared/hooks/useEventStream';
import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { getProviderCapabilities } from '@servicehub/ui-shared/lib/api/cloudBridge';
import { ProviderBadge, getProviderStyle } from '@servicehub/ui-shared/lib/providerStyles';
import { EnvironmentBadge } from '@/components/EnvironmentBadge';
import { EmptyState } from '@/components/EmptyState';
import { setThemeProvider } from '@servicehub/ui-shared/lib/providerTheme';
import { Namespace } from '@servicehub/ui-shared/lib/api/types';
import { apiClient } from '@servicehub/ui-shared/lib/api/client';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { getHealthGrade } from '@servicehub/ui-shared/lib/healthGrade';

const DLQ_SPIKE_THRESHOLD = 10;

// While the SSE event stream is connected, events invalidate queries the moment
// something happens, so polling is only a staleness safety net — relax it.
// On disconnect the full-speed intervals resume automatically.
const QUEUES_POLL_MS = { normal: 30_000, relaxed: 180_000 };
const STATS_POLL_MS = { normal: 60_000, relaxed: 300_000 };
const TREND_POLL_MS = { normal: 120_000, relaxed: 600_000 };

// ============================================================================
// Live refresh hook — tracks seconds since last successful data fetch
// ============================================================================

function useSecondsSince(triggerAt: number | null): number {
  const [seconds, setSeconds] = useState(0);
  useEffect(() => {
    if (triggerAt === null) return;
    setSeconds(0);
    const id = setInterval(() => setSeconds(s => s + 1), 1_000);
    return () => clearInterval(id);
  }, [triggerAt]);
  return seconds;
}

// ============================================================================
// Live Badge — pulsing dot + "last updated Xs ago"
// ============================================================================

// Owns its own 1-second ticker so re-renders stay local to this small badge instead of
// propagating up to DashboardPage, which would otherwise re-run every aggregation
// (.sort()/.filter()/.reduce() over every namespace) once a second for no reason.
function LiveBadge({ triggerAt }: { triggerAt: number | null }) {
  const secondsAgo = useSecondsSince(triggerAt);
  const label =
    secondsAgo < 5 ? 'just now' : secondsAgo < 60 ? `${secondsAgo}s ago` : `${Math.floor(secondsAgo / 60)}m ago`;
  return (
    <span className="flex items-center gap-1.5 text-xs text-white/80">
      <span className="relative flex h-2 w-2">
        <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-300 opacity-75" />
        <span className="relative inline-flex rounded-full h-2 w-2 bg-emerald-400" />
      </span>
      Live · {label}
    </span>
  );
}

// ============================================================================
// Aggregate Stats Bar
// ============================================================================

interface AggregateStats {
  totalNamespaces: number;
  loadedNamespaces: number;
  totalActive: number;
  totalDlq: number;
  totalScheduled: number;
  spikeCount: number;
  isLoading: boolean;
}

function AggregateSummaryBar({ stats }: { stats: AggregateStats }) {
  const cells = [
    {
      icon: <Globe className="w-4 h-4 text-indigo-400" />,
      label: 'Namespaces',
      value: stats.totalNamespaces,
      colorClass: 'text-indigo-700',
      bg: 'bg-indigo-50 border-indigo-100',
    },
    {
      icon: <Inbox className="w-4 h-4 text-sky-400" />,
      label: 'Active',
      value: stats.isLoading ? '…' : stats.totalActive.toLocaleString(),
      colorClass: 'text-sky-700',
      bg: 'bg-sky-50 border-sky-100',
    },
    {
      icon: <AlertTriangle className="w-4 h-4 text-red-400" />,
      label: 'Dead Letter',
      value: stats.isLoading ? '…' : stats.totalDlq.toLocaleString(),
      colorClass: stats.totalDlq > 0 ? 'text-red-700 font-bold' : 'text-gray-500',
      bg: stats.totalDlq > 0 ? 'bg-red-50 border-red-200' : 'bg-gray-50 border-gray-100',
    },
    {
      icon: <Clock className="w-4 h-4 text-purple-400" />,
      label: 'Scheduled',
      value: stats.isLoading ? '…' : stats.totalScheduled.toLocaleString(),
      colorClass: 'text-purple-700',
      bg: 'bg-purple-50 border-purple-100',
    },
    {
      icon: <Activity className="w-4 h-4 text-orange-400" />,
      label: 'DLQ Spikes',
      value: stats.isLoading ? '…' : stats.spikeCount,
      colorClass: stats.spikeCount > 0 ? 'text-orange-700 font-bold' : 'text-gray-500',
      bg: stats.spikeCount > 0 ? 'bg-orange-50 border-orange-200' : 'bg-gray-50 border-gray-100',
    },
  ];

  return (
    <div className="grid grid-cols-2 sm:grid-cols-5 gap-3 mb-5">
      {cells.map((cell) => (
        <div
          key={cell.label}
          className={`flex items-center gap-3 px-4 py-3 rounded-xl border ${cell.bg}`}
        >
          {cell.icon}
          <div>
            <p className="text-xs text-gray-500 leading-none mb-1">{cell.label}</p>
            <p className={`text-xl leading-none ${cell.colorClass}`}>{cell.value}</p>
          </div>
        </div>
      ))}
    </div>
  );
}

// ============================================================================
// DLQ Hot Spots Panel
// ============================================================================

interface HotSpot {
  namespace: Namespace;
  totalDlq: number;
  isError: boolean;
}

function DlqHotSpotsPanel({
  hotspots,
  maxDlq,
}: {
  hotspots: HotSpot[];
  maxDlq: number;
}) {
  const navigate = useNavigate();

  if (hotspots.length === 0) return null;

  return (
    <div className="mb-5 bg-white border border-red-200 rounded-xl shadow-sm overflow-hidden">
      {/* Panel Header */}
      <div className="flex items-center gap-2 px-5 py-3 bg-red-50 border-b border-red-200">
        <Flame className="w-4 h-4 text-red-500" />
        <span className="text-sm font-semibold text-red-700">
          DLQ Hot Spots — {hotspots.length} namespace{hotspots.length !== 1 ? 's' : ''} need
          {hotspots.length === 1 ? 's' : ''} attention
        </span>
      </div>

      {/* Ranked List */}
      <div className="divide-y divide-gray-100">
        {hotspots.map((spot, idx) => {
          const barWidth = maxDlq > 0 ? Math.max(3, (spot.totalDlq / maxDlq) * 100) : 0;
          const displayName = spot.namespace.displayName || spot.namespace.name;
          return (
            <div
              key={spot.namespace.id}
              className="flex items-center gap-4 px-5 py-3 hover:bg-red-50/50 transition-colors"
            >
              {/* Rank */}
              <span className="text-xs font-bold text-gray-400 w-5 shrink-0">{idx + 1}</span>

              {/* Env + Name */}
              <div className="flex items-center gap-2 w-48 shrink-0">
                <EnvironmentBadge env={spot.namespace.environment} />
                <span
                  className="text-sm font-medium text-gray-800 truncate"
                  title={displayName}
                >
                  {displayName}
                </span>
              </div>

              {/* Bar */}
              <div className="flex-1 h-2 bg-red-100 rounded-full overflow-hidden">
                <div
                  className="h-full bg-red-500 rounded-full transition-all duration-500"
                  style={{ width: `${barWidth}%` }}
                />
              </div>

              {/* DLQ count */}
              <span className="text-sm font-bold text-red-700 w-16 text-right shrink-0">
                {spot.totalDlq.toLocaleString()} DLQ
              </span>

              {/* Action */}
              <button
                onClick={() =>
                  navigate(`/dlq-history?namespace=${spot.namespace.id}`)
                }
                className="shrink-0 px-3 py-1 text-xs font-medium text-red-700 bg-red-100 hover:bg-red-200 border border-red-200 rounded-lg transition-colors"
              >
                View
              </button>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function HealthScoreBadge({ totalActive, totalDlq }: { totalActive: number; totalDlq: number }) {
  const { grade, bgClass, textClass, borderClass } = getHealthGrade(totalActive, totalDlq);
  const total = totalActive + totalDlq;
  const dlqPct = total > 0 ? ((totalDlq / total) * 100).toFixed(1) : '0.0';
  return (
    <div
      className={`flex flex-col items-center px-2 py-1 rounded-lg border ${bgClass} ${borderClass}`}
      title={`DLQ ratio: ${dlqPct}% | ${totalDlq} DLQ of ${total} total messages`}
      aria-label={`Health grade ${grade}. DLQ ratio: ${dlqPct}% — ${totalDlq} dead-lettered of ${total} total messages`}
    >
      <span className={`text-2xl font-bold leading-none ${textClass}`}>{grade}</span>
      <span className={`text-[10px] font-medium ${textClass}`}>Health</span>
    </div>
  );
}

// ============================================================================
// Stat Cell
// ============================================================================

function StatCell({
  label,
  value,
  colorClass,
}: {
  label: string;
  value: number | string;
  colorClass?: string;
}) {
  return (
    <div className="bg-gray-50 rounded-lg p-2 text-center">
      <p className="text-xs text-gray-500 mb-1">{label}</p>
      <p className={`text-lg font-semibold ${colorClass ?? 'text-gray-800'}`}>{value}</p>
    </div>
  );
}

// ============================================================================
// Skeleton Card (loading state)
// ============================================================================

function SkeletonCard() {
  return (
    <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-5 animate-pulse">
      <div className="flex items-center gap-2 mb-4">
        <div className="h-5 w-10 bg-gray-200 rounded-full" />
        <div className="h-5 w-32 bg-gray-200 rounded" />
      </div>
      <div className="h-3 w-48 bg-gray-100 rounded mb-4" />
      <div className="grid grid-cols-3 sm:grid-cols-6 gap-2 mb-4">
        {[0, 1, 2, 3, 4, 5].map((i) => (
          <div key={i} className="bg-gray-100 rounded-lg p-3">
            <div className="h-3 w-8 bg-gray-200 rounded mb-2 mx-auto" />
            <div className="h-6 w-6 bg-gray-200 rounded mx-auto" />
          </div>
        ))}
      </div>
      <div className="h-8 bg-gray-100 rounded-lg mb-3" />
      <div className="flex gap-2">
        <div className="h-9 flex-1 bg-gray-100 rounded-lg" />
        <div className="h-9 flex-1 bg-gray-100 rounded-lg" />
      </div>
    </div>
  );
}

// ============================================================================
// DLQ Trend Sparkline
// ============================================================================

interface TrendPoint {
  date: string;
  newCount: number;
  resolvedCount: number;
}

function DlqTrendSparkline({ namespaceId, sseConnected = false }: { namespaceId: string; sseConnected?: boolean }) {
  const { isDemoMode } = useDemoContext();
  const { data: trendData } = useQuery<TrendPoint[]>({
    queryKey: ['dlq-trend', namespaceId],
    queryFn: async () => {
      const res = await apiClient.get(`/dlq/trend`, {
        params: { namespaceId, days: 7 },
      });
      return (res.data as Array<{ date: string; newMessages: number; resolvedMessages: number }>).map(d => ({
        date: d.date,
        newCount: d.newMessages,
        resolvedCount: d.resolvedMessages,
      }));
    },
    enabled: !isDemoMode,
    refetchInterval: sseConnected ? TREND_POLL_MS.relaxed : TREND_POLL_MS.normal,
  });

  if (!trendData || trendData.length < 2) {
    return (
      <div className="px-5 pb-2">
        <p className="text-[10px] text-gray-400 text-center">No trend data yet</p>
      </div>
    );
  }

  return (
    <div className="px-5 pb-2">
      <ResponsiveContainer width="100%" height={60}>
        <LineChart data={trendData}>
          <Line type="monotone" dataKey="newCount" stroke="#ef4444" strokeWidth={1.5} dot={false} />
          <Line type="monotone" dataKey="resolvedCount" stroke="#22c55e" strokeWidth={1.5} dot={false} />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}

// ============================================================================
// NamespaceCard
// ============================================================================

export interface NamespaceCardProps {
  namespace: Namespace;
  dlqThreshold?: number;
  sseConnected?: boolean;
}

export function NamespaceCard({ namespace, dlqThreshold = DLQ_SPIKE_THRESHOLD, sseConnected = false }: NamespaceCardProps) {
  const navigate = useNavigate();
  const providerStyle = getProviderStyle(namespace.cloudProvider);
  const { data: queues, isLoading: queuesLoading, isError } = useQueues(
    namespace.id,
    true,
    sseConnected ? QUEUES_POLL_MS.relaxed : QUEUES_POLL_MS.normal,
  );
  const { data: topics } = useTopics(namespace.id, false);
  // GCP Pub/Sub has no message-count API (ProviderCapabilities.Gcp), so its active/DLQ/scheduled
  // totals are structurally 0 rather than measured. Rendering a bare "0" reads as "definitely
  // empty" when it really means "unknown" — same dash convention NamespacesPanel and MessageList
  // already use. Entity counts (queues/topics/subs) are real on every provider and stay numeric.
  const { data: capabilitiesMap } = useProviderCapabilities();
  const supportsCounts =
    getProviderCapabilities(capabilitiesMap, namespace.cloudProvider)?.supportsMessageCounts ?? true;

  // Land the user on a concrete entity instead of the "No entity selected"
  // empty state: first browsable queue (skipping AWS companion DLQ queues),
  // then an AWS topic's fan-out view, then the multi-cloud overview.
  const handleBrowseQueues = () => {
    setThemeProvider(namespace.cloudProvider);
    const dlqTargets = new Set(
      (queues ?? []).map((q) => q.deadLetterTargetQueue).filter(Boolean) as string[],
    );
    const firstQueue = (queues ?? []).find((q) => !dlqTargets.has(q.name)) ?? queues?.[0];
    if (firstQueue) {
      navigate(`/messages?namespace=${namespace.id}&queue=${encodeURIComponent(firstQueue.name)}&queueType=active`);
      return;
    }
    const firstTopic = topics?.[0];
    if (firstTopic && namespace.cloudProvider === 'aws') {
      navigate(`/messages?namespace=${namespace.id}&topic=${encodeURIComponent(firstTopic.name)}`);
      return;
    }
    navigate('/messages-overview?tab=active');
  };

  const handleViewDlq = () => {
    navigate(`/dlq-history?namespace=${namespace.id}`);
  };

  // Use the stats endpoint for accurate totals (includes subscription DLQs) — shared
  // useNamespaceStats hook also carries the Demo Mode guard.
  const [{ data: stats, isLoading: statsLoading }] = useNamespaceStats(
    [namespace.id],
    true,
    sseConnected ? STATS_POLL_MS.relaxed : STATS_POLL_MS.normal,
  );

  const isLoading = queuesLoading || statsLoading;
  const totalQueues = stats?.totalQueues ?? queues?.length ?? 0;
  const totalTopics = stats?.totalTopics ?? 0;
  const totalSubscriptions = stats?.totalSubscriptions ?? 0;
  const totalActive = stats?.totalActive ?? queues?.reduce((s, q) => s + q.activeMessageCount, 0) ?? 0;
  const totalDlq = stats?.totalDlq ?? queues?.reduce((s, q) => s + q.deadLetterMessageCount, 0) ?? 0;
  const totalScheduled = stats?.totalScheduled ?? queues?.reduce((s, q) => s + q.scheduledMessageCount, 0) ?? 0;
  const isDlqSpike = totalDlq > dlqThreshold;

  // Track previous DLQ count to detect sudden increases
  const prevDlqRef = useRef<number | null>(null);
  const dlqDelta =
    !isLoading && !isError && prevDlqRef.current !== null && totalDlq > prevDlqRef.current
      ? totalDlq - prevDlqRef.current
      : 0;
  // Update previous after computing delta (runs after render)
  if (!isLoading && !isError && prevDlqRef.current !== totalDlq) {
    prevDlqRef.current = totalDlq;
  }

  const displayName = namespace.displayName || namespace.name;

  if (isLoading) {
    return <SkeletonCard />;
  }

  return (
    <div
      className={`rounded-xl border shadow-sm overflow-hidden bg-white ${
        isDlqSpike ? 'border-red-300' : providerStyle.headerBorder
      }`}
    >
      {/* Card Header — tinted by cloud provider (Azure blue / AWS orange / GCP green) */}
      <div className={`px-5 pt-5 pb-3 ${providerStyle.headerBg}`}>
        <div className="flex items-start justify-between">
          <div className="flex items-center gap-2 mb-1">
            <ProviderBadge provider={namespace.cloudProvider} />
            <EnvironmentBadge env={namespace.environment} />
            <h3 className="text-base font-semibold text-gray-900 truncate">{displayName}</h3>
            {dlqDelta > 0 && (
              <span className="ml-auto shrink-0 flex items-center gap-1 px-2 py-0.5 bg-red-500 text-white text-xs font-bold rounded-full animate-pulse">
                <TrendingUp className="w-3 h-3" />
                +{dlqDelta} DLQ
              </span>
            )}
          </div>
          {!isError && supportsCounts && (
            <HealthScoreBadge totalActive={totalActive} totalDlq={totalDlq} />
          )}
        </div>
        <p className="text-xs text-gray-400 truncate">{namespace.name}</p>
      </div>

      {/* Stats Grid */}
      <div className="grid grid-cols-3 sm:grid-cols-6 gap-2 px-5 pb-3">
        <StatCell label="Queues" value={isError ? '—' : totalQueues} />
        <StatCell label="Topics" value={isError ? '—' : totalTopics} colorClass="text-indigo-700" />
        <StatCell label="Subs" value={isError ? '—' : totalSubscriptions} colorClass="text-indigo-700" />
        <StatCell
          label="Active"
          value={isError || !supportsCounts ? '—' : totalActive}
          colorClass="text-sky-700"
        />
        <StatCell
          label="DLQ"
          value={isError || !supportsCounts ? '—' : totalDlq}
          colorClass={supportsCounts && totalDlq > 0 ? 'text-red-700' : undefined}
        />
        <StatCell
          label="Sched"
          value={isError || !supportsCounts ? '—' : totalScheduled}
          colorClass="text-purple-700"
        />
      </div>

      {/* DLQ Trend Sparkline */}
      <DlqTrendSparkline namespaceId={namespace.id} sseConnected={sseConnected} />

      {/* Status Banner */}
      <div className="px-5 pb-4">
        {isError ? (
          <div className="flex items-center gap-2 px-3 py-2 bg-gray-100 rounded-lg text-red-600 text-sm">
            <AlertTriangle className="w-4 h-4 shrink-0" />
            <span>Unable to reach namespace</span>
          </div>
        ) : !supportsCounts ? (
          // "Healthy" here would be an assertion derived from counts this provider never
          // reports — the same misleading zero, one level of derivation removed.
          <div className="flex items-center gap-2 px-3 py-2 bg-gray-100 border border-gray-200 rounded-lg text-gray-600 text-sm">
            <Info className="w-4 h-4 shrink-0" />
            <span>Message counts unavailable for this provider</span>
          </div>
        ) : isDlqSpike ? (
          <div className="flex items-center gap-2 px-3 py-2 bg-red-100 border border-red-200 rounded-lg text-red-700 text-sm">
            <AlertTriangle className="w-4 h-4 shrink-0" />
            <span>DLQ: {totalDlq} messages need attention</span>
          </div>
        ) : (
          <div className="flex items-center gap-2 px-3 py-2 bg-emerald-50 border border-emerald-200 rounded-lg text-emerald-700 text-sm">
            <CheckCircle className="w-4 h-4 shrink-0" />
            <span>Healthy</span>
          </div>
        )}
      </div>

      {/* Action Buttons */}
      <div className="flex gap-2 px-5 pb-5">
        <button
          onClick={handleBrowseQueues}
          className="flex-1 px-3 py-2 text-sm font-medium text-sky-700 bg-sky-50 hover:bg-sky-100 border border-sky-200 rounded-lg transition-colors"
        >
          Browse Queues
        </button>
        <button
          onClick={handleViewDlq}
          className="flex-1 px-3 py-2 text-sm font-medium text-red-700 bg-red-50 hover:bg-red-100 border border-red-200 rounded-lg transition-colors"
        >
          View DLQ History
        </button>
      </div>
    </div>
  );
}

// ============================================================================
// DashboardPage
// ============================================================================

export function DashboardPage() {
  const navigate = useNavigate();
  const { data: namespaces, isLoading, isFetching, isError: namespacesError, refetch, dataUpdatedAt } = useNamespaces();

  // Push updates via SSE; while connected, polling relaxes to a safety net.
  const { connected: sseConnected } = useEventStream();

  // Track when queue stats last settled to drive the live badge
  const allStats: NamespaceQueueStats[] = useAllNamespacesQueues(
    namespaces?.map(ns => ns.id) ?? [],
    true,
    sseConnected
      ? { queuesMs: QUEUES_POLL_MS.relaxed, statsMs: STATS_POLL_MS.relaxed }
      : undefined,
  );
  const statsLoading = allStats.some(s => s.isLoading);

  // "Refresh" previously refetched only the namespace list, so every number on this page —
  // queue counts, the stats rollup behind each card's Active/DLQ/Sched cells, the DLQ trend
  // sparklines — kept whatever value it had while the badge reset to "Live · just now". The
  // button claimed a refresh it had not performed. Invalidating by key prefix covers the live
  // and demo variants of each key in one call, and TanStack Query dedupes the refetches across
  // every card sharing a cached query, so this issues one request per namespace, not one per
  // card. `refetchType: 'active'` keeps unmounted/background queries from being refetched too.
  const queryClient = useQueryClient();
  const handleRefresh = () => {
    refetch();
    for (const key of ['queues', 'namespace-stats', 'dlq-trend']) {
      queryClient.invalidateQueries({ queryKey: [key], refetchType: 'active' });
    }
  };

  const lastUpdatedAt = useRef<number | null>(null);
  if (!statsLoading && allStats.length > 0) {
    lastUpdatedAt.current = Date.now();
  } else if (dataUpdatedAt && lastUpdatedAt.current === null) {
    lastUpdatedAt.current = dataUpdatedAt;
  }

  // All of this is derived purely from `namespaces` + `allStats` — memoized so it doesn't
  // re-run on renders triggered by unrelated local state (search/filter toggles etc.).
  const {
    aggregateStats,
    hotspots,
    maxDlq,
    sortedNamespaces,
    prodNamespaces,
    nonProdNamespaces,
    hasBothEnvs,
    prodTotals,
    nonProdTotals,
  } = useMemo(() => {
    // Build a lookup: namespaceId → stats
    const statsById = new Map<string, NamespaceQueueStats>(
      allStats.map((s) => [s.namespaceId, s]),
    );

    // Aggregate totals across all namespaces
    const aggregateStats: AggregateStats = {
      totalNamespaces: namespaces?.length ?? 0,
      loadedNamespaces: allStats.filter((s) => !s.isLoading && !s.isError).length,
      totalActive: allStats.reduce((sum, s) => sum + s.totalActive, 0),
      totalDlq: allStats.reduce((sum, s) => sum + s.totalDlq, 0),
      totalScheduled: allStats.reduce((sum, s) => sum + s.totalScheduled, 0),
      spikeCount: allStats.filter((s) => s.totalDlq > DLQ_SPIKE_THRESHOLD).length,
      isLoading: allStats.some((s) => s.isLoading),
    };

    // DLQ hot spots: namespaces with spikes, ranked by DLQ count descending
    const hotspots: HotSpot[] = (namespaces ?? [])
      .map((ns) => {
        const s = statsById.get(ns.id);
        return { namespace: ns, totalDlq: s?.totalDlq ?? 0, isError: s?.isError ?? false };
      })
      .filter((h) => h.totalDlq > DLQ_SPIKE_THRESHOLD)
      .sort((a, b) => b.totalDlq - a.totalDlq);

    const maxDlq = hotspots[0]?.totalDlq ?? 1;

    // Sort namespace cards: highest DLQ first, then by name
    const sortedNamespaces = [...(namespaces ?? [])].sort((a, b) => {
      const dlqA = statsById.get(a.id)?.totalDlq ?? 0;
      const dlqB = statsById.get(b.id)?.totalDlq ?? 0;
      if (dlqB !== dlqA) return dlqB - dlqA;
      const nameA = a.displayName || a.name;
      const nameB = b.displayName || b.name;
      return nameA.localeCompare(nameB);
    });

    // Prod vs Non-Prod comparison (only shown when both exist)
    const prodNamespaces = (namespaces ?? []).filter((ns) => ns.environment === 'prod');
    const nonProdNamespaces = (namespaces ?? []).filter((ns) => ns.environment !== 'prod');
    const hasBothEnvs = prodNamespaces.length > 0 && nonProdNamespaces.length > 0;

    function envTotals(nsList: Namespace[]) {
      return nsList.reduce(
        (acc, ns) => {
          const s = statsById.get(ns.id);
          return {
            active: acc.active + (s?.totalActive ?? 0),
            dlq: acc.dlq + (s?.totalDlq ?? 0),
            scheduled: acc.scheduled + (s?.totalScheduled ?? 0),
          };
        },
        { active: 0, dlq: 0, scheduled: 0 },
      );
    }
    const prodTotals = hasBothEnvs ? envTotals(prodNamespaces) : null;
    const nonProdTotals = hasBothEnvs ? envTotals(nonProdNamespaces) : null;

    return {
      aggregateStats,
      hotspots,
      maxDlq,
      sortedNamespaces,
      prodNamespaces,
      nonProdNamespaces,
      hasBothEnvs,
      prodTotals,
      nonProdTotals,
    };
  }, [namespaces, allStats]);

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      {/* Header */}
      <div className="bg-gradient-to-r from-indigo-600 to-indigo-500 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <Globe className="w-6 h-6 text-white/80" />
            <div>
              <h1 className="text-xl font-semibold text-white">Namespace Overview</h1>
              <div className="flex items-center gap-3 mt-0.5">
                <p className="text-indigo-100 text-sm">
                  {namespaces && namespaces.length > 0
                    ? `${namespaces.length} namespace${namespaces.length !== 1 ? 's' : ''}`
                    : 'All connected namespaces at a glance'}
                </p>
                {lastUpdatedAt.current !== null && (
                  <LiveBadge triggerAt={lastUpdatedAt.current} />
                )}
              </div>
            </div>
          </div>
          <div className="flex items-center gap-3">
            {aggregateStats.spikeCount > 0 && (
              <span className="flex items-center gap-1.5 px-3 py-1.5 bg-red-500/90 text-white rounded-lg text-sm font-medium">
                <Flame className="w-4 h-4" />
                {aggregateStats.spikeCount} DLQ spike{aggregateStats.spikeCount !== 1 ? 's' : ''}
              </span>
            )}
            <button
              onClick={handleRefresh}
              disabled={isFetching}
              className="flex items-center gap-2 px-3 py-1.5 bg-white/20 hover:bg-white/30 disabled:opacity-50 text-white rounded-lg text-sm font-medium transition-colors"
            >
              <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
              Refresh
            </button>
          </div>
        </div>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto bg-gray-50 p-6">
        {isLoading ? (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-5">
            {[0, 1, 2].map((i) => (
              <SkeletonCard key={i} />
            ))}
          </div>
        ) : namespacesError && (!namespaces || namespaces.length === 0) ? (
          <div className="bg-red-50 border border-red-200 rounded-xl p-6 text-center">
            <p className="text-red-700 font-medium">Unable to reach the API server</p>
            <p className="text-red-500 text-sm mt-1">Ensure the backend is running and try again.</p>
          </div>
        ) : !namespaces || namespaces.length === 0 ? (
          <EmptyState
            icon={Globe}
            heading="No namespaces connected yet"
            subtext="Connect a Service Bus namespace to see it here."
            action={{ label: 'Connect a namespace', icon: Plus, onClick: () => navigate('/connect') }}
          />
        ) : (
          <>
            {namespacesError && (
              <div className="flex items-center gap-2 bg-amber-50 border border-amber-200 rounded-xl px-4 py-2.5 mb-4 text-sm text-amber-700">
                <AlertTriangle className="w-4 h-4 shrink-0" />
                Unable to refresh namespaces — showing last known data.
              </div>
            )}

            {/* Aggregate Stats Bar */}
            <AggregateSummaryBar stats={aggregateStats} />

            {/* Quick Actions Row */}
            <div className="flex flex-wrap items-center gap-2 mb-5">
              <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide mr-1">
                Quick Actions
              </span>
              <button
                onClick={() => navigate('/dlq-history')}
                className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-red-700 bg-red-50 hover:bg-red-100 border border-red-200 rounded-lg transition-colors"
              >
                <Flame className="w-3.5 h-3.5" />
                Browse All DLQs
              </button>
              <button
                onClick={() => navigate('/scheduled')}
                className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-sky-700 bg-sky-50 hover:bg-sky-100 border border-sky-200 rounded-lg transition-colors"
              >
                <Clock className="w-3.5 h-3.5" />
                All Scheduled
              </button>
              <button
                onClick={() => navigate('/cross-cloud-trace')}
                className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-violet-700 bg-violet-50 hover:bg-violet-100 border border-violet-200 rounded-lg transition-colors"
              >
                <GitMerge className="w-3.5 h-3.5" />
                Cross-Cloud Trace
              </button>
              <button
                onClick={() => navigate('/rules')}
                className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-amber-700 bg-amber-50 hover:bg-amber-100 border border-amber-200 rounded-lg transition-colors"
              >
                <Zap className="w-3.5 h-3.5" />
                Auto-Replay Rules
              </button>
              <button
                onClick={() => navigate('/fleet')}
                className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-indigo-700 bg-indigo-50 hover:bg-indigo-100 border border-indigo-200 rounded-lg transition-colors"
              >
                <Activity className="w-3.5 h-3.5" />
                Fleet Health
              </button>
            </div>

            {/* Prod vs Non-Prod Comparison — only when both exist */}
            {hasBothEnvs && prodTotals && nonProdTotals && (
              <div className="mb-5 bg-white border border-gray-200 rounded-xl shadow-sm overflow-hidden">
                <div className="flex items-center gap-2 px-5 py-3 bg-gray-50 border-b border-gray-200">
                  <BarChart2 className="w-4 h-4 text-gray-500" />
                  <span className="text-sm font-semibold text-gray-700">
                    Prod vs Non-Prod Comparison
                  </span>
                </div>
                <div className="grid grid-cols-2 divide-x divide-gray-100">
                  {[
                    { label: `Production (${prodNamespaces.length})`, totals: prodTotals, accent: 'text-red-700', bg: 'bg-red-50/40' },
                    { label: `Non-Prod (${nonProdNamespaces.length})`, totals: nonProdTotals, accent: 'text-emerald-700', bg: 'bg-emerald-50/40' },
                  ].map((side) => (
                    <div key={side.label} className={`px-6 py-4 ${side.bg}`}>
                      <p className={`text-xs font-bold uppercase tracking-wide mb-3 ${side.accent}`}>{side.label}</p>
                      <div className="flex gap-6">
                        <div>
                          <p className="text-xs text-gray-500">Active</p>
                          <p className="text-lg font-semibold text-sky-700">{side.totals.active.toLocaleString()}</p>
                        </div>
                        <div>
                          <p className="text-xs text-gray-500">Dead Letter</p>
                          <p className={`text-lg font-semibold ${side.totals.dlq > 0 ? 'text-red-700' : 'text-gray-400'}`}>
                            {side.totals.dlq.toLocaleString()}
                          </p>
                        </div>
                        <div>
                          <p className="text-xs text-gray-500">Scheduled</p>
                          <p className="text-lg font-semibold text-purple-700">{side.totals.scheduled.toLocaleString()}</p>
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {/* DLQ Hot Spots Panel — only shown when spikes exist */}
            {hotspots.length > 0 && (
              <DlqHotSpotsPanel hotspots={hotspots} maxDlq={maxDlq} />
            )}

            {/* Namespace Cards — sorted by DLQ severity */}
            <div className="flex items-center gap-2 mb-3">
              <BarChart2 className="w-4 h-4 text-gray-400" />
              <span className="text-xs text-gray-500 font-medium uppercase tracking-wide">
                Namespaces · sorted by DLQ severity
              </span>
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-5">
              {sortedNamespaces.map((ns) => (
                <NamespaceCard key={ns.id} namespace={ns} sseConnected={sseConnected} />
              ))}
            </div>
          </>
        )}
      </div>
    </div>
  );
}

export default DashboardPage;


