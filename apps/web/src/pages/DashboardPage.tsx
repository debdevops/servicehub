import { useNavigate } from 'react-router-dom';
import { Globe, RefreshCw, AlertTriangle, CheckCircle, Plus } from 'lucide-react';
import { useNamespaces } from '@/hooks/useNamespaces';
import { useQueues } from '@/hooks/useQueues';
import { Namespace, EnvironmentType } from '@/lib/api/types';

// ============================================================================
// Environment Badge
// ============================================================================

function EnvironmentBadge({ env }: { env?: EnvironmentType }) {
  if (env === 'Prod') {
    return (
      <span className="px-2 py-0.5 text-xs font-bold rounded-full bg-red-100 text-red-700 border border-red-200">
        PROD
      </span>
    );
  }
  if (env === 'Uat') {
    return (
      <span className="px-2 py-0.5 text-xs font-bold rounded-full bg-amber-100 text-amber-700 border border-amber-200">
        UAT
      </span>
    );
  }
  if (env === 'Dev') {
    return (
      <span className="px-2 py-0.5 text-xs font-bold rounded-full bg-emerald-100 text-emerald-700 border border-emerald-200">
        DEV
      </span>
    );
  }
  return (
    <span className="px-2 py-0.5 text-xs font-bold rounded-full bg-gray-100 text-gray-500 border border-gray-200">
      —
    </span>
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
      <div className="grid grid-cols-4 gap-2 mb-4">
        {[0, 1, 2, 3].map((i) => (
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
// NamespaceCard
// ============================================================================

export interface NamespaceCardProps {
  namespace: Namespace;
  dlqThreshold?: number;
}

export function NamespaceCard({ namespace, dlqThreshold = 10 }: NamespaceCardProps) {
  const navigate = useNavigate();
  const { data: queues, isLoading, isError } = useQueues(namespace.id, true);

  const totalQueues = queues?.length ?? 0;
  const totalActive = queues?.reduce((s, q) => s + q.activeMessageCount, 0) ?? 0;
  const totalDlq = queues?.reduce((s, q) => s + q.deadLetterMessageCount, 0) ?? 0;
  const totalScheduled = queues?.reduce((s, q) => s + q.scheduledMessageCount, 0) ?? 0;
  const isDlqSpike = totalDlq > dlqThreshold;

  const displayName = namespace.displayName || namespace.name;

  if (isLoading) {
    return <SkeletonCard />;
  }

  return (
    <div
      className={`rounded-xl border shadow-sm overflow-hidden bg-white ${
        isDlqSpike ? 'border-red-300' : 'border-emerald-200'
      }`}
    >
      {/* Card Header */}
      <div className="px-5 pt-5 pb-3">
        <div className="flex items-center gap-2 mb-1">
          <EnvironmentBadge env={namespace.environment} />
          <h3 className="text-base font-semibold text-gray-900 truncate">{displayName}</h3>
        </div>
        <p className="text-xs text-gray-400 truncate">{namespace.name}</p>
      </div>

      {/* Stats Grid */}
      <div className="grid grid-cols-4 gap-2 px-5 pb-3">
        <StatCell label="Queues" value={isError ? '—' : totalQueues} />
        <StatCell
          label="Active"
          value={isError ? '—' : totalActive}
          colorClass="text-sky-700"
        />
        <StatCell
          label="DLQ"
          value={isError ? '—' : totalDlq}
          colorClass={totalDlq > 0 ? 'text-red-700' : undefined}
        />
        <StatCell
          label="Sched"
          value={isError ? '—' : totalScheduled}
          colorClass="text-purple-700"
        />
      </div>

      {/* Status Banner */}
      <div className="px-5 pb-4">
        {isError ? (
          <div className="flex items-center gap-2 px-3 py-2 bg-gray-100 rounded-lg text-red-600 text-sm">
            <AlertTriangle className="w-4 h-4 shrink-0" />
            <span>Unable to reach namespace</span>
          </div>
        ) : isDlqSpike ? (
          <div className="flex items-center gap-2 px-3 py-2 bg-red-100 border border-red-200 rounded-lg text-red-700 text-sm">
            <AlertTriangle className="w-4 h-4 shrink-0" />
            <span>⚠️ DLQ: {totalDlq} messages need attention</span>
          </div>
        ) : (
          <div className="flex items-center gap-2 px-3 py-2 bg-emerald-50 border border-emerald-200 rounded-lg text-emerald-700 text-sm">
            <CheckCircle className="w-4 h-4 shrink-0" />
            <span>✅ Healthy</span>
          </div>
        )}
      </div>

      {/* Action Buttons */}
      <div className="flex gap-2 px-5 pb-5">
        <button
          onClick={() => navigate(`/messages?namespace=${namespace.id}`)}
          className="flex-1 px-3 py-2 text-sm font-medium text-sky-700 bg-sky-50 hover:bg-sky-100 border border-sky-200 rounded-lg transition-colors"
        >
          Browse Queues
        </button>
        <button
          onClick={() => navigate(`/dlq-history?namespace=${namespace.id}`)}
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
  const { data: namespaces, isLoading, isFetching, refetch } = useNamespaces();

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      {/* Header */}
      <div className="bg-gradient-to-r from-indigo-600 to-indigo-500 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <Globe className="w-6 h-6 text-white/80" />
            <div>
              <h1 className="text-xl font-semibold text-white">Namespace Overview</h1>
              <p className="text-indigo-100 text-sm">All connected namespaces at a glance</p>
            </div>
          </div>
          <button
            onClick={() => refetch()}
            disabled={isFetching}
            className="flex items-center gap-2 px-3 py-1.5 bg-white/20 hover:bg-white/30 disabled:opacity-50 text-white rounded-lg text-sm font-medium transition-colors"
          >
            <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
            Refresh
          </button>
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
        ) : !namespaces || namespaces.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-full text-center px-6">
            <Globe className="w-14 h-14 text-gray-300 mb-4" />
            <p className="text-gray-600 font-semibold text-lg mb-1">No namespaces connected yet</p>
            <p className="text-gray-400 text-sm mb-5">
              Connect a Service Bus namespace to see it here.
            </p>
            <button
              onClick={() => navigate('/connect')}
              className="flex items-center gap-2 px-5 py-2.5 bg-indigo-600 hover:bg-indigo-700 text-white rounded-lg text-sm font-medium transition-colors"
            >
              <Plus className="w-4 h-4" />
              Connect a namespace
            </button>
          </div>
        ) : (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-5">
            {namespaces.map((ns) => (
              <NamespaceCard key={ns.id} namespace={ns} />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
