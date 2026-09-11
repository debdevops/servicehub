import { Fragment, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import {
  Layers,
  AlertTriangle,
  Inbox,
  RefreshCw,
  Clock,
  Search,
  ExternalLink,
  Flame,
  GitMerge,
  Zap,
  Siren,
  ChevronDown,
  ChevronRight,
  MoreVertical,
  Lightbulb,
  X,
} from 'lucide-react';
import { LineChart, Line, XAxis, Tooltip, ResponsiveContainer, CartesianGrid, PieChart, Pie, Cell } from 'recharts';
import { useFleetOverview } from '@servicehub/ui-shared/hooks/useFleet';
import { useHealthReport } from '@servicehub/ui-shared/hooks/useHealth';
import { useAllNamespacesQueues, type NamespaceQueueStats } from '@servicehub/ui-shared/hooks/useQueues';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { ProviderIcon } from '@servicehub/ui-shared/components/ProviderIcon';
import { ProviderBadge, PROVIDER_STYLES, PROVIDER_STATE_STYLES } from '@servicehub/ui-shared/lib/providerStyles';
import { EnvironmentBadge } from '@/components/EnvironmentBadge';
import type { ProviderInstallState } from '@servicehub/ui-shared/lib/providerConnectionState';
import type { FleetHealthSeverity, FleetNamespaceHealth } from '@servicehub/ui-shared/lib/api/fleet';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';

const WINDOW_OPTIONS = [
  { label: '24h', hours: 24 },
  { label: '7d', hours: 168 },
  { label: '30d', hours: 720 },
];

// Mirrors DashboardPage's DLQ_SPIKE_THRESHOLD — the two pages must agree on what counts as a
// spike, since an operator bouncing between Namespace Overview and Fleet Overview would
// otherwise see a different namespace count flagged for the same underlying data.
const DLQ_SPIKE_THRESHOLD = 10;

const severityStyles: Record<FleetHealthSeverity, { dot: string; text: string; label: string; badge: string; hex: string }> = {
  critical: { dot: 'bg-red-500', text: 'text-red-700', label: 'Critical', badge: 'bg-red-50 text-red-700 border-red-200', hex: '#ef4444' },
  warning: { dot: 'bg-amber-500', text: 'text-amber-700', label: 'Needs Attention', badge: 'bg-amber-50 text-amber-700 border-amber-200', hex: '#f59e0b' },
  healthy: { dot: 'bg-emerald-500', text: 'text-emerald-700', label: 'Healthy', badge: 'bg-emerald-50 text-emerald-700 border-emerald-200', hex: '#10b981' },
  // Zero known dead-letters, but the namespace was never scanned — must read distinctly from
  // "Healthy" (blueprint Gap 1: an unmonitored namespace must never render green).
  unknown: { dot: 'bg-slate-400', text: 'text-slate-600', label: 'Not monitored', badge: 'bg-slate-50 text-slate-600 border-slate-200', hex: '#94a3b8' },
};

// Maps a provider connectivity health-check entry name (registered in Program.cs /
// AwsDependencyInjection / GcpDependencyInjection) to the provider it reports on.
const CONNECTIVITY_CHECKS: { name: string; provider: CloudProviderType }[] = [
  { name: 'servicebus', provider: 'azure' },
  { name: 'aws-connectivity', provider: 'aws' },
  { name: 'gcp-connectivity', provider: 'gcp' },
];

function relativeAge(iso: string | null): string {
  if (!iso) return '—';
  return relativeMs(new Date(iso).getTime());
}

function relativeMs(ms: number | undefined): string {
  if (!ms) return '—';
  const delta = Date.now() - ms;
  if (delta < 0) return 'just now';
  const mins = Math.floor(delta / 60000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  return `${Math.floor(hours / 24)}d ago`;
}

function csvCell(value: string | number): string {
  const s = String(value);
  return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
}

function downloadCsv(filename: string, rows: (string | number)[][]) {
  const content = rows.map((row) => row.map(csvCell).join(',')).join('\n');
  const blob = new Blob([content], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

function StatTile({
  icon,
  label,
  value,
  tone,
  sub,
}: {
  icon: ReactNode;
  label: string;
  value: number | string;
  tone: string;
  sub?: ReactNode;
}) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-4 flex items-center gap-3">
      <div className={`w-10 h-10 rounded-lg flex items-center justify-center shrink-0 ${tone}`}>{icon}</div>
      <div className="min-w-0">
        <div className="flex items-baseline gap-1.5">
          <span className="text-2xl font-semibold text-gray-900 leading-none">{value}</span>
          {sub}
        </div>
        <div className="text-xs text-gray-500 mt-1 truncate">{label}</div>
      </div>
    </div>
  );
}

// ─── Provider connectivity strip ──────────────────────────────────────────

// Provider connectivity, cross-referenced against real namespace counts — a health check
// reports "Healthy" for a flag-enabled provider with zero namespaces configured (see
// AwsHealthCheck's "No AWS namespaces configured" case), so trusting `entry.status` alone
// makes "never connected" look identical to "verified and connected." Namespace count is
// the tie-breaker, since useNamespaces() is the authoritative per-provider count everywhere
// else in the app. Every provider always renders here (never silently dropped when its
// flag is off) so "not part of this installation" reads as an explicit state, not an
// absence.
function ConnectivityStrip() {
  const { data: report } = useHealthReport();
  const { data: namespaces } = useNamespaces();
  if (!report || namespaces === undefined) return null;

  const checks = CONNECTIVITY_CHECKS.map(({ name, provider }) => {
    const entry = report.entries[name];
    const nsCount = namespaces.filter((ns) => ns.cloudProvider === provider).length;

    let state: ProviderInstallState;
    if (!entry) {
      state = 'unavailable';
    } else if (nsCount === 0) {
      state = 'available-unconfigured';
    } else if (entry.status === 'Degraded' || entry.status === 'Unhealthy') {
      state = 'connection-issue';
    } else {
      state = 'connected';
    }

    return { provider, entry, state };
  });

  return (
    <div className="flex flex-wrap items-center gap-2">
      <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide">Provider connectivity</span>
      {checks.map(({ provider, entry, state }) => {
        const stateStyle = PROVIDER_STATE_STYLES[state];
        return (
          <span
            key={provider}
            title={entry?.description ?? stateStyle.label}
            className={`inline-flex items-center gap-1.5 pl-1.5 pr-2 py-1 rounded-full text-xs font-medium border ${
              state === 'connected'
                ? 'bg-emerald-50 text-emerald-700 border-emerald-200'
                : state === 'connection-issue'
                  ? 'bg-amber-50 text-amber-700 border-amber-200'
                  : 'bg-gray-50 text-gray-500 border-gray-200'
            }`}
          >
            <ProviderIcon provider={provider} className={`w-3.5 h-3.5 ${stateStyle.iconClass}`} />
            {PROVIDER_STYLES[provider].label}
            {stateStyle.dotClass && <span className={`w-1.5 h-1.5 rounded-full ${stateStyle.dotClass}`} />}
          </span>
        );
      })}
    </div>
  );
}

// ─── Namespace Health donut ────────────────────────────────────────────────

function NamespaceHealthDonut({ namespaces }: { namespaces: FleetNamespaceHealth[] }) {
  const counts: Record<FleetHealthSeverity, number> = { healthy: 0, warning: 0, critical: 0, unknown: 0 };
  for (const n of namespaces) counts[n.severity]++;
  const order: FleetHealthSeverity[] = ['healthy', 'warning', 'critical', 'unknown'];
  const slices = order.map((sev) => ({ sev, count: counts[sev] })).filter((s) => s.count > 0);

  return (
    <div className="flex items-center gap-5">
      <div className="relative w-24 h-24 shrink-0">
        {slices.length > 0 ? (
          <ResponsiveContainer width="100%" height="100%">
            <PieChart>
              <Pie
                data={slices}
                dataKey="count"
                nameKey="sev"
                innerRadius={28}
                outerRadius={44}
                paddingAngle={slices.length > 1 ? 3 : 0}
                stroke="none"
                isAnimationActive={false}
              >
                {slices.map((s) => (
                  <Cell key={s.sev} fill={severityStyles[s.sev].hex} />
                ))}
              </Pie>
            </PieChart>
          </ResponsiveContainer>
        ) : (
          <div className="w-full h-full rounded-full border-8 border-gray-100" />
        )}
        <div className="absolute inset-0 flex flex-col items-center justify-center pointer-events-none">
          <span className="text-lg font-bold text-gray-900 leading-none">{namespaces.length}</span>
          <span className="text-[9px] text-gray-400">Namespaces</span>
        </div>
      </div>
      <ul className="flex-1 space-y-1.5 min-w-0">
        {order.map((sev) => (
          <li key={sev} className="flex items-center gap-2 text-sm">
            <span className={`w-2.5 h-2.5 rounded-full shrink-0 ${severityStyles[sev].dot}`} />
            <span className="text-gray-700">{severityStyles[sev].label}</span>
            <span className="ml-auto text-gray-500 font-medium">{counts[sev]}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

// ─── Row action menu (kebab) ───────────────────────────────────────────────

interface RowMenuAction {
  label: string;
  onClick: () => void;
  disabled?: boolean;
  title?: string;
}

function RowMenu({ actions, onClose, anchorRect }: { actions: RowMenuAction[]; onClose: () => void; anchorRect: DOMRect }) {
  const top = anchorRect.bottom + 4;
  const right = window.innerWidth - anchorRect.right;

  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', handleEscape);
    return () => window.removeEventListener('keydown', handleEscape);
  }, [onClose]);

  return (
    <>
      <div className="fixed inset-0 z-40" onClick={onClose} />
      <div
        role="menu"
        style={{ position: 'fixed', top, right }}
        className="z-50 w-56 bg-white border border-gray-200 rounded-lg shadow-lg py-1"
      >
        {actions.map((action) => (
          <button
            key={action.label}
            role="menuitem"
            disabled={action.disabled}
            title={action.title}
            onClick={() => {
              if (action.disabled) return;
              action.onClick();
              onClose();
            }}
            className="w-full flex items-center px-3 py-2 text-sm text-left text-gray-700 hover:bg-gray-50 disabled:text-gray-300 disabled:hover:bg-transparent disabled:cursor-not-allowed"
          >
            {action.label}
          </button>
        ))}
      </div>
    </>
  );
}

export default function FleetPage() {
  const navigate = useNavigate();
  const [windowHours, setWindowHours] = useState(24);
  const [providerFilter, setProviderFilter] = useState<CloudProviderType | 'all'>('all');
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [menuFor, setMenuFor] = useState<{ id: string; rect: DOMRect } | null>(null);
  const [bulkMenuOpen, setBulkMenuOpen] = useState(false);
  const bulkMenuButtonRef = useRef<HTMLButtonElement>(null);
  const { data, isLoading, isError, refetch, isFetching } = useFleetOverview(windowHours);

  // Fleet's DLQ overview intentionally includes orphaned records for deleted namespaces
  // (so historical DLQ activity isn't silently dropped) — skip live queue/topic lookups
  // for those, since the namespace no longer exists and every such call would just 404.
  const namespaceIds = useMemo(
    () => (data?.namespaces ?? [])
      .filter((n) => n.namespaceName !== '(deleted namespace)')
      .map((n) => n.namespaceId),
    [data]
  );
  const liveStats = useAllNamespacesQueues(namespaceIds, false);
  const liveStatsByNamespace = useMemo(
    () => new Map<string, NamespaceQueueStats>(liveStats.map((s) => [s.namespaceId, s])),
    [liveStats]
  );

  const providerCounts = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const n of data?.namespaces ?? []) {
      const p = n.provider.toLowerCase();
      counts[p] = (counts[p] ?? 0) + 1;
    }
    return counts;
  }, [data]);

  const filteredNamespaces = useMemo(() => {
    const all = data?.namespaces ?? [];
    return all.filter((n) => {
      if (providerFilter !== 'all' && n.provider.toLowerCase() !== providerFilter) return false;
      if (search && !n.namespaceName.toLowerCase().includes(search.toLowerCase())) return false;
      return true;
    });
  }, [data, providerFilter, search]);

  const atRisk = useMemo(
    () => (data?.namespaces ?? []).filter((n) => n.severity !== 'healthy').length,
    [data]
  );

  const topCategories = useMemo(
    () => Object.entries(data?.topCategories ?? {}).sort((a, b) => b[1] - a[1]).slice(0, 5),
    [data]
  );

  const liveTotals = useMemo(() => {
    let totalActive = 0;
    let totalScheduled = 0;
    let spikeCount = 0;
    for (const s of liveStats) {
      totalActive += s.totalActive;
      totalScheduled += s.totalScheduled;
      if (s.totalDlq > DLQ_SPIKE_THRESHOLD) spikeCount++;
    }
    return { totalActive, totalScheduled, spikeCount };
  }, [liveStats]);

  const goToNamespace = (n: FleetNamespaceHealth) =>
    navigate(`/dlq-history?namespace=${n.namespaceId}`);

  const goToBulkActions = (n: FleetNamespaceHealth) =>
    navigate(`/dlq-history?namespace=${n.namespaceId}&openBulk=true`);

  const toggleSelect = (id: string) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const allFilteredSelected = filteredNamespaces.length > 0 && filteredNamespaces.every((n) => selected.has(n.namespaceId));
  const toggleSelectAll = () => {
    setSelected((prev) => {
      if (allFilteredSelected) {
        const next = new Set(prev);
        for (const n of filteredNamespaces) next.delete(n.namespaceId);
        return next;
      }
      const next = new Set(prev);
      for (const n of filteredNamespaces) next.add(n.namespaceId);
      return next;
    });
  };

  const selectedNamespaces = (data?.namespaces ?? []).filter((n) => selected.has(n.namespaceId));

  const exportSelectedCsv = () => {
    const header = ['Namespace', 'Provider', 'Environment', 'Health', 'Active', 'Dead Letter', 'Scheduled', 'Queues', 'Topics', 'Subscriptions'];
    const rows = selectedNamespaces.map((n) => {
      const live = liveStatsByNamespace.get(n.namespaceId);
      return [
        n.namespaceName,
        n.provider,
        n.environment,
        severityStyles[n.severity].label,
        live?.totalActive ?? '',
        n.activeCount,
        live?.totalScheduled ?? '',
        live?.totalQueues ?? '',
        live?.totalTopics ?? '',
        live?.totalSubscriptions ?? '',
      ];
    });
    downloadCsv(`fleet-namespaces-${new Date().toISOString().slice(0, 10)}.csv`, [header, ...rows]);
  };

  const singleSelectedForBulk =
    selectedNamespaces.length === 1 && selectedNamespaces[0].activeCount > 0 && selectedNamespaces[0].environment.toLowerCase() !== 'prod'
      ? selectedNamespaces[0]
      : null;

  return (
    <div className="flex-1 overflow-y-auto min-w-0">
    <div className="p-6 max-w-7xl mx-auto space-y-6">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-lg bg-indigo-50 flex items-center justify-center">
            <Layers className="w-5 h-5 text-indigo-600" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-gray-900">Fleet Overview</h1>
            <p className="text-sm text-gray-500">
              Dead-letter health across every namespace — what died overnight, at a glance.
            </p>
          </div>
        </div>
        <div className="flex items-center gap-2 flex-wrap">
          {data && (
            <span className="text-xs text-gray-400 hidden sm:inline">
              Last updated: {new Date(data.generatedAt).toLocaleString(undefined, {
                day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit',
              })}
            </span>
          )}
          <Link
            to="/dashboard"
            className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-gray-600 hover:text-gray-900 hover:bg-gray-50 border border-gray-200 rounded-lg transition-colors focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
          >
            Namespace Overview
            <ExternalLink className="w-3.5 h-3.5" />
          </Link>
          <div className="inline-flex rounded-lg border border-gray-200 bg-white p-0.5">
            {WINDOW_OPTIONS.map((opt) => (
              <button
                key={opt.hours}
                onClick={() => setWindowHours(opt.hours)}
                className={`px-3 py-1.5 text-sm rounded-md transition-colors ${
                  windowHours === opt.hours
                    ? 'bg-indigo-600 text-white'
                    : 'text-gray-600 hover:bg-gray-50'
                }`}
                aria-pressed={windowHours === opt.hours}
              >
                {opt.label}
              </button>
            ))}
          </div>
          <button
            onClick={() => refetch()}
            className="p-2 rounded-lg border border-gray-200 bg-white text-gray-600 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
            aria-label="Refresh fleet overview"
          >
            <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
          </button>
        </div>
      </div>

      <ConnectivityStrip />

      {isError && (
        <div className="flex items-center justify-between gap-3 bg-red-50 border border-red-200 text-red-700 rounded-lg p-4 text-sm">
          <span>Failed to load the fleet overview.</span>
          <button
            onClick={() => refetch()}
            className="shrink-0 px-3 py-1.5 text-xs font-medium text-red-700 bg-white border border-red-300 rounded-lg hover:bg-red-100 transition-colors focus:outline-none focus:ring-2 focus:ring-red-400 focus:ring-offset-1"
          >
            Try Again
          </button>
        </div>
      )}

      {isLoading && !data && (
        <div className="flex flex-col items-center justify-center gap-3 py-20 text-gray-400">
          <RefreshCw className="w-6 h-6 animate-spin text-indigo-400" />
          <p className="text-sm text-gray-500">Loading fleet overview…</p>
        </div>
      )}

      {data && (
        <>
          {/* Key metrics */}
          <div className="grid grid-cols-2 md:grid-cols-5 gap-4">
            <StatTile
              icon={<Layers className="w-5 h-5 text-indigo-600" />}
              label={`Namespaces (${atRisk} at risk)`}
              value={data.namespaceCount}
              tone="bg-indigo-50"
            />
            <StatTile
              icon={<Inbox className="w-5 h-5 text-sky-600" />}
              label="Active messages"
              value={liveTotals.totalActive.toLocaleString()}
              tone="bg-sky-50"
            />
            <StatTile
              icon={<AlertTriangle className="w-5 h-5 text-red-600" />}
              label="Dead letter"
              value={data.totalActive.toLocaleString()}
              tone="bg-red-50"
              sub={
                data.totalNewInWindow > 0 ? (
                  <span className="text-xs font-semibold text-red-600">+{data.totalNewInWindow}</span>
                ) : undefined
              }
            />
            <StatTile
              icon={<Clock className="w-5 h-5 text-purple-600" />}
              label="Scheduled"
              value={liveTotals.totalScheduled.toLocaleString()}
              tone="bg-purple-50"
            />
            <StatTile
              icon={<Flame className="w-5 h-5 text-orange-600" />}
              label="DLQ spikes"
              value={liveTotals.spikeCount}
              tone="bg-orange-50"
            />
          </div>

          {/* Quick Actions */}
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide mr-1">Quick Actions</span>
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
              onClick={() => navigate('/incidents')}
              className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-rose-700 bg-rose-50 hover:bg-rose-100 border border-rose-200 rounded-lg transition-colors"
            >
              <Siren className="w-3.5 h-3.5" />
              View Incidents
            </button>
          </div>

          {/* Trend + Health */}
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
            <div className="lg:col-span-2 bg-white rounded-xl border border-gray-200 shadow-sm p-4">
              <h2 className="text-sm font-medium text-gray-700 mb-3">Dead-letter trend (all namespaces)</h2>
              <div style={{ width: '100%', height: 180 }}>
                <ResponsiveContainer>
                  <LineChart data={data.dailyTrend}>
                    <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
                    <XAxis
                      dataKey="date"
                      tickFormatter={(d: string) => new Date(d).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}
                      tick={{ fontSize: 11, fill: '#9ca3af' }}
                    />
                    <Tooltip
                      labelFormatter={(d) => new Date(d as string).toLocaleDateString()}
                      formatter={(value, name) => [value as number, name === 'newMessages' ? 'New' : 'Resolved']}
                    />
                    <Line type="monotone" dataKey="newMessages" stroke="#ef4444" strokeWidth={2} dot={false} name="newMessages" />
                    <Line type="monotone" dataKey="resolvedMessages" stroke="#10b981" strokeWidth={2} dot={false} name="resolvedMessages" />
                  </LineChart>
                </ResponsiveContainer>
              </div>
              <div className="flex items-center gap-4 mt-1 text-xs text-gray-500">
                <span className="flex items-center gap-1.5"><span className="w-2.5 h-0.5 bg-red-500 inline-block" /> New dead-letters</span>
                <span className="flex items-center gap-1.5"><span className="w-2.5 h-0.5 bg-emerald-500 inline-block" /> Resolved</span>
              </div>
            </div>
            <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-4 flex flex-col gap-4">
              <div>
                <h2 className="text-sm font-medium text-gray-700 mb-3">Namespace health</h2>
                <NamespaceHealthDonut namespaces={data.namespaces} />
              </div>
              {topCategories.length > 0 && (
                <div className="pt-3 border-t border-gray-100">
                  <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">Top failure categories</h3>
                  <ul className="space-y-1.5">
                    {topCategories.map(([cat, count]) => (
                      <li key={cat} className="flex items-center justify-between text-sm">
                        <span className="text-gray-700 truncate">{cat}</span>
                        <span className="text-xs font-medium bg-gray-100 text-gray-700 px-2 py-0.5 rounded-full shrink-0 ml-2">
                          {count}
                        </span>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          </div>

          {/* Filters */}
          <div className="flex flex-wrap items-center gap-2">
            <div className="inline-flex rounded-lg border border-gray-200 bg-white p-0.5">
              {(['all', 'azure', 'aws', 'gcp'] as const).map((p) => (
                <button
                  key={p}
                  onClick={() => setProviderFilter(p)}
                  className={`flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-md transition-colors ${
                    providerFilter === p ? 'bg-indigo-600 text-white' : 'text-gray-600 hover:bg-gray-50'
                  }`}
                  aria-pressed={providerFilter === p}
                >
                  {p !== 'all' && <ProviderIcon provider={p} className="w-3.5 h-3.5" />}
                  {p === 'all'
                    ? `All Providers (${data.namespaces.length})`
                    : `${PROVIDER_STYLES[p].label} (${providerCounts[p] ?? 0})`}
                </button>
              ))}
            </div>
            <div className="relative flex-1 min-w-[200px] max-w-xs">
              <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-400" />
              <input
                type="text"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Search namespaces…"
                className="w-full pl-8 pr-3 py-1.5 text-xs border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                aria-label="Search namespaces"
              />
            </div>
            {(providerFilter !== 'all' || search) && (
              <button
                onClick={() => { setProviderFilter('all'); setSearch(''); }}
                className="flex items-center gap-1 text-xs font-medium text-gray-500 hover:text-gray-700"
              >
                <X className="w-3.5 h-3.5" />
                Clear filters
              </button>
            )}

            {/* Bulk operations */}
            {selected.size > 0 && (
              <div className="ml-auto flex items-center gap-2">
                <span className="text-xs font-medium text-gray-600">{selected.size} selected</span>
                <div className="relative">
                  <button
                    ref={bulkMenuButtonRef}
                    onClick={() => setBulkMenuOpen((v) => !v)}
                    className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-indigo-700 bg-indigo-50 hover:bg-indigo-100 border border-indigo-200 rounded-lg transition-colors"
                  >
                    Bulk actions
                    <ChevronDown className="w-3.5 h-3.5" />
                  </button>
                  {bulkMenuOpen && (
                    <RowMenu
                      anchorRect={bulkMenuButtonRef.current?.getBoundingClientRect() ?? new DOMRect(0, 0, 0, 0)}
                      onClose={() => setBulkMenuOpen(false)}
                      actions={[
                        {
                          label: 'Export selected (CSV)',
                          onClick: exportSelectedCsv,
                        },
                        {
                          label: 'Open bulk replay/purge',
                          disabled: !singleSelectedForBulk,
                          title: singleSelectedForBulk
                            ? undefined
                            : 'Select exactly one non-prod namespace with active dead-letters to open bulk replay/purge',
                          onClick: () => singleSelectedForBulk && goToBulkActions(singleSelectedForBulk),
                        },
                      ]}
                    />
                  )}
                </div>
              </div>
            )}
          </div>

          {/* Per-namespace health */}
          <div className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden">
            <div className="px-4 py-3 border-b border-gray-100">
              <h2 className="text-sm font-medium text-gray-700">
                Namespaces (worst first) — {filteredNamespaces.length} of {data.namespaces.length}
              </h2>
            </div>
            {data.namespaces.length === 0 ? (
              <div className="flex flex-col items-center gap-2 py-16 text-center">
                <Layers className="w-8 h-8 text-gray-300" />
                <h3 className="text-sm font-medium text-gray-700">No namespaces to report</h3>
                <p className="text-xs text-gray-400 max-w-xs">
                  Connect a namespace to start tracking dead-letter health across your fleet.
                </p>
              </div>
            ) : filteredNamespaces.length === 0 ? (
              <div className="p-8 text-center text-gray-400 text-sm">No namespaces match the current filters.</div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="sticky top-0 z-10 bg-gray-50">
                    <tr className="text-left text-xs text-gray-500 border-b border-gray-100">
                      <th className="px-3 py-2 w-8">
                        <input
                          type="checkbox"
                          aria-label="Select all namespaces"
                          checked={allFilteredSelected}
                          onChange={toggleSelectAll}
                          className="rounded border-gray-300"
                        />
                      </th>
                      <th className="px-2 py-2 w-6"></th>
                      <th className="px-2 py-2 font-medium">Namespace</th>
                      <th className="px-4 py-2 font-medium">Env</th>
                      <th className="px-4 py-2 font-medium text-right">Queues</th>
                      <th className="px-4 py-2 font-medium text-right">Topics</th>
                      <th className="px-4 py-2 font-medium text-right">Subs</th>
                      <th className="px-4 py-2 font-medium text-right">Active</th>
                      <th className="px-4 py-2 font-medium text-right">DLQ</th>
                      <th className="px-4 py-2 font-medium text-right">Scheduled</th>
                      <th className="px-4 py-2 font-medium">Health</th>
                      <th className="px-4 py-2 font-medium">Last Updated</th>
                      <th className="px-4 py-2"></th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredNamespaces.map((n) => {
                      const sev = severityStyles[n.severity];
                      const live = liveStatsByNamespace.get(n.namespaceId);
                      const isProd = n.environment.toLowerCase() === 'prod';
                      const isExpanded = expandedId === n.namespaceId;
                      return (
                        <Fragment key={n.namespaceId}>
                          <tr className="border-b border-gray-50 hover:bg-indigo-50/40">
                            <td className="px-3 py-2.5">
                              <input
                                type="checkbox"
                                aria-label={`Select ${n.namespaceName}`}
                                checked={selected.has(n.namespaceId)}
                                onChange={() => toggleSelect(n.namespaceId)}
                                className="rounded border-gray-300"
                              />
                            </td>
                            <td className="px-2 py-2.5">
                              <button
                                onClick={() => setExpandedId(isExpanded ? null : n.namespaceId)}
                                aria-label={isExpanded ? 'Collapse details' : 'Expand details'}
                                aria-expanded={isExpanded}
                                className="text-gray-400 hover:text-gray-700"
                              >
                                {isExpanded ? <ChevronDown className="w-4 h-4" /> : <ChevronRight className="w-4 h-4" />}
                              </button>
                            </td>
                            <td className="px-2 py-2.5">
                              <div className="flex items-center gap-2">
                                <span
                                  className={`w-2 h-2 rounded-full ${sev.dot}`}
                                  title={n.severity === 'unknown' ? (n.coverageNote ?? sev.label) : sev.label}
                                />
                                <span className="font-medium text-gray-800">{n.namespaceName}</span>
                                <ProviderBadge provider={n.provider.toLowerCase() as CloudProviderType} />
                              </div>
                            </td>
                            <td className="px-4 py-2.5"><EnvironmentBadge env={n.environment} /></td>
                            <td className="px-4 py-2.5 text-right text-gray-600">
                              {live && !live.isLoading ? live.totalQueues : '—'}
                            </td>
                            <td className="px-4 py-2.5 text-right text-gray-600">
                              {live && !live.isLoading ? live.totalTopics : '—'}
                            </td>
                            <td className="px-4 py-2.5 text-right text-gray-600">
                              {live && !live.isLoading ? live.totalSubscriptions : '—'}
                            </td>
                            <td className="px-4 py-2.5 text-right text-gray-600">
                              {live && !live.isLoading ? live.totalActive : '—'}
                            </td>
                            <td className="px-4 py-2.5 text-right font-medium text-gray-800">{n.activeCount}</td>
                            <td className="px-4 py-2.5 text-right text-gray-600">
                              {live && !live.isLoading ? live.totalScheduled : '—'}
                            </td>
                            <td className="px-4 py-2.5">
                              <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-[11px] font-medium border ${sev.badge}`}>
                                {sev.label}
                              </span>
                            </td>
                            <td className="px-4 py-2.5 text-gray-500">{relativeMs(live?.dataUpdatedAt)}</td>
                            <td className="px-4 py-2.5">
                              <div className="flex items-center justify-end gap-1">
                                <button
                                  onClick={() => goToNamespace(n)}
                                  className="px-2.5 py-1 text-xs font-medium text-indigo-600 hover:text-white hover:bg-indigo-600 border border-indigo-200 hover:border-indigo-600 rounded-lg transition-colors"
                                >
                                  View
                                </button>
                                <button
                                  onClick={(e) => setMenuFor({ id: n.namespaceId, rect: (e.currentTarget as HTMLElement).getBoundingClientRect() })}
                                  aria-label={`More actions for ${n.namespaceName}`}
                                  className="p-1 text-gray-400 hover:text-gray-700 rounded"
                                >
                                  <MoreVertical className="w-4 h-4" />
                                </button>
                                {menuFor?.id === n.namespaceId && (
                                  <RowMenu
                                    anchorRect={menuFor.rect}
                                    onClose={() => setMenuFor(null)}
                                    actions={[
                                      {
                                        label: 'Browse active messages',
                                        onClick: () => navigate(`/messages-overview?tab=active`),
                                      },
                                      {
                                        label: 'View DLQ history',
                                        onClick: () => goToNamespace(n),
                                      },
                                      {
                                        label: 'Open bulk replay/purge',
                                        disabled: !(n.activeCount > 0 && !isProd),
                                        title: n.activeCount > 0 && !isProd ? undefined : 'Only available for non-prod namespaces with active dead-letters',
                                        onClick: () => goToBulkActions(n),
                                      },
                                      {
                                        label: 'Open in Auto-Replay Rules',
                                        onClick: () => navigate('/rules'),
                                      },
                                    ]}
                                  />
                                )}
                              </div>
                            </td>
                          </tr>
                          {isExpanded && (
                            <tr className="bg-gray-50/60 border-b border-gray-100">
                              <td></td>
                              <td></td>
                              <td colSpan={10} className="px-2 py-3">
                                <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 text-xs">
                                  <div>
                                    <p className="text-gray-400">New in window</p>
                                    <p className={`font-semibold ${n.newInWindow > 0 ? 'text-red-600' : 'text-gray-700'}`}>
                                      {n.newInWindow > 0 ? `+${n.newInWindow}` : '0'}
                                    </p>
                                  </div>
                                  <div>
                                    <p className="text-gray-400">Resolved in window</p>
                                    <p className="font-semibold text-gray-700">{n.resolvedInWindow > 0 ? n.resolvedInWindow : '—'}</p>
                                  </div>
                                  <div>
                                    <p className="text-gray-400">Total (all-time)</p>
                                    <p className="font-semibold text-gray-700">{n.totalCount}</p>
                                  </div>
                                  <div>
                                    <p className="text-gray-400">Oldest active</p>
                                    <p className="font-semibold text-gray-700 flex items-center gap-1">
                                      <Clock className="w-3 h-3" />
                                      {relativeAge(n.oldestActiveDetectedAt)}
                                    </p>
                                  </div>
                                  <div>
                                    <p className="text-gray-400">Top category</p>
                                    <p className="font-semibold text-gray-700">{n.topCategory ?? '—'}</p>
                                  </div>
                                  <div>
                                    <p className="text-gray-400">Top entity</p>
                                    <p className="font-semibold text-gray-700">
                                      {n.topEntity ? `${n.topEntity} (${n.topEntityCount})` : '—'}
                                    </p>
                                  </div>
                                  {n.coverage !== 'scanned' && (
                                    <div className="col-span-2">
                                      <p className="text-gray-400">Coverage</p>
                                      <p className="font-semibold text-amber-700">
                                        {n.coverageNote ?? 'The background monitor does not scan this namespace automatically.'}
                                      </p>
                                    </div>
                                  )}
                                </div>
                              </td>
                            </tr>
                          )}
                        </Fragment>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          {/* Guidance footer */}
          <div className="flex flex-wrap items-center justify-between gap-4 bg-indigo-50 border border-indigo-100 rounded-xl px-5 py-4">
            <div className="flex items-center gap-3">
              <Lightbulb className="w-5 h-5 text-indigo-400 shrink-0" />
              <div>
                <p className="text-sm font-semibold text-indigo-900">Keep your fleet healthy</p>
                <p className="text-xs text-indigo-700">
                  Monitor DLQ spikes, track namespace health, and set up auto-replay rules to reduce recovery time.
                </p>
              </div>
            </div>
            <button
              onClick={() => navigate('/incidents')}
              className="shrink-0 flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg transition-colors"
            >
              Go to Incident Center
              <ExternalLink className="w-3.5 h-3.5" />
            </button>
          </div>
        </>
      )}
    </div>
    </div>
  );
}
