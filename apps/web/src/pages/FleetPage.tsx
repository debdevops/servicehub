import { useMemo, useState, type ReactNode } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import {
  Layers,
  AlertTriangle,
  Inbox,
  CheckCircle,
  RefreshCw,
  Clock,
  ArrowRight,
  Search,
  ExternalLink,
  PlayCircle,
} from 'lucide-react';
import { LineChart, Line, XAxis, Tooltip, ResponsiveContainer, CartesianGrid } from 'recharts';
import { useFleetOverview } from '@servicehub/ui-shared/hooks/useFleet';
import { useHealthReport } from '@servicehub/ui-shared/hooks/useHealth';
import { useAllNamespacesQueues } from '@servicehub/ui-shared/hooks/useQueues';
import { ProviderIcon } from '@servicehub/ui-shared/components/ProviderIcon';
import { ProviderBadge, PROVIDER_STYLES } from '@servicehub/ui-shared/lib/providerStyles';
import type { FleetHealthSeverity, FleetNamespaceHealth } from '@servicehub/ui-shared/lib/api/fleet';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';

const WINDOW_OPTIONS = [
  { label: '24h', hours: 24 },
  { label: '3d', hours: 72 },
  { label: '7d', hours: 168 },
];

const severityStyles: Record<FleetHealthSeverity, { dot: string; text: string; label: string }> = {
  critical: { dot: 'bg-red-500', text: 'text-red-700', label: 'Critical' },
  warning: { dot: 'bg-amber-500', text: 'text-amber-700', label: 'Warning' },
  healthy: { dot: 'bg-emerald-500', text: 'text-emerald-700', label: 'Healthy' },
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
  const ms = Date.now() - new Date(iso).getTime();
  if (ms < 0) return 'just now';
  const mins = Math.floor(ms / 60000);
  if (mins < 60) return `${mins}m`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours}h`;
  return `${Math.floor(hours / 24)}d`;
}

function StatTile({
  icon,
  label,
  value,
  tone,
}: {
  icon: ReactNode;
  label: string;
  value: number | string;
  tone: string;
}) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-4 flex items-center gap-3">
      <div className={`w-10 h-10 rounded-lg flex items-center justify-center ${tone}`}>{icon}</div>
      <div>
        <div className="text-2xl font-semibold text-gray-900 leading-none">{value}</div>
        <div className="text-xs text-gray-500 mt-1">{label}</div>
      </div>
    </div>
  );
}

function ConnectivityStrip() {
  const { data: report } = useHealthReport();
  if (!report) return null;

  const checks = CONNECTIVITY_CHECKS
    .map(({ name, provider }) => ({ provider, entry: report.entries[name] }))
    .filter((c): c is { provider: CloudProviderType; entry: NonNullable<typeof c.entry> } => !!c.entry);

  if (checks.length === 0) return null;

  return (
    <div className="flex flex-wrap items-center gap-2">
      <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide">Provider connectivity</span>
      {checks.map(({ provider, entry }) => {
        const healthy = entry.status === 'Healthy';
        const degraded = entry.status === 'Degraded';
        return (
          <span
            key={provider}
            title={entry.description ?? entry.status}
            className={`inline-flex items-center gap-1.5 pl-1.5 pr-2 py-1 rounded-full text-xs font-medium border ${
              healthy
                ? 'bg-emerald-50 text-emerald-700 border-emerald-200'
                : degraded
                  ? 'bg-amber-50 text-amber-700 border-amber-200'
                  : 'bg-red-50 text-red-700 border-red-200'
            }`}
          >
            <ProviderIcon provider={provider} className="w-3.5 h-3.5" />
            {PROVIDER_STYLES[provider].label}
            <span className={`w-1.5 h-1.5 rounded-full ${healthy ? 'bg-emerald-500' : degraded ? 'bg-amber-500' : 'bg-red-500'}`} />
          </span>
        );
      })}
    </div>
  );
}

export default function FleetPage() {
  const navigate = useNavigate();
  const [windowHours, setWindowHours] = useState(24);
  const [providerFilter, setProviderFilter] = useState<CloudProviderType | 'all'>('all');
  const [search, setSearch] = useState('');
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
    () => new Map(liveStats.map((s) => [s.namespaceId, s])),
    [liveStats]
  );

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
    () => Object.entries(data?.topCategories ?? {}).sort((a, b) => b[1] - a[1]).slice(0, 6),
    [data]
  );

  const goToNamespace = (n: FleetNamespaceHealth) =>
    navigate(`/dlq-history?namespace=${n.namespaceId}`);

  const goToBulkActions = (n: FleetNamespaceHealth, e: React.MouseEvent) => {
    e.stopPropagation();
    navigate(`/dlq-history?namespace=${n.namespaceId}&openBulk=true`);
  };

  return (
    <div className="p-6 max-w-7xl mx-auto space-y-6">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-lg bg-indigo-50 flex items-center justify-center">
            <Layers className="w-5 h-5 text-indigo-600" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-gray-900">Fleet Operations</h1>
            <p className="text-sm text-gray-500">
              Dead-letter health across every namespace — what died overnight, at a glance.
            </p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <Link
            to="/dashboard"
            className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-gray-600 hover:text-gray-900 hover:bg-gray-50 border border-gray-200 rounded-lg transition-colors focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
          >
            Per-namespace details
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
        <div className="bg-red-50 border border-red-200 text-red-700 rounded-lg p-4 text-sm">
          Failed to load the fleet overview. Please try again.
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
          {/* Summary tiles */}
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            <StatTile
              icon={<Inbox className="w-5 h-5 text-purple-600" />}
              label="Active dead-letters"
              value={data.totalActive}
              tone="bg-purple-50"
            />
            <StatTile
              icon={<AlertTriangle className="w-5 h-5 text-red-600" />}
              label={`New in last ${data.windowHours}h`}
              value={data.totalNewInWindow}
              tone="bg-red-50"
            />
            <StatTile
              icon={<CheckCircle className="w-5 h-5 text-emerald-600" />}
              label={`Resolved in ${data.windowHours}h`}
              value={data.totalResolvedInWindow}
              tone="bg-emerald-50"
            />
            <StatTile
              icon={<Layers className="w-5 h-5 text-indigo-600" />}
              label={`Namespaces at risk (of ${data.namespaceCount})`}
              value={atRisk}
              tone="bg-indigo-50"
            />
          </div>

          {/* Trend + categories */}
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
            <div className="lg:col-span-2 bg-white rounded-xl border border-gray-200 shadow-sm p-4">
              <h2 className="text-sm font-medium text-gray-700 mb-3">7-day fleet trend</h2>
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
                    <Line type="monotone" dataKey="newMessages" stroke="#ef4444" strokeWidth={2} dot={false} />
                    <Line type="monotone" dataKey="resolvedMessages" stroke="#10b981" strokeWidth={2} dot={false} />
                  </LineChart>
                </ResponsiveContainer>
              </div>
            </div>
            <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-4">
              <h2 className="text-sm font-medium text-gray-700 mb-3">Top failure categories</h2>
              {topCategories.length === 0 ? (
                <p className="text-sm text-gray-400">No active failures 🎉</p>
              ) : (
                <ul className="space-y-2">
                  {topCategories.map(([cat, count]) => (
                    <li key={cat} className="flex items-center justify-between text-sm">
                      <span className="text-gray-700">{cat}</span>
                      <span className="text-xs font-medium bg-gray-100 text-gray-700 px-2 py-0.5 rounded-full">
                        {count}
                      </span>
                    </li>
                  ))}
                </ul>
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
                  {p === 'all' ? 'All providers' : PROVIDER_STYLES[p].label}
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
                      <th className="px-4 py-2 font-medium">Namespace</th>
                      <th className="px-4 py-2 font-medium">Env</th>
                      <th className="px-4 py-2 font-medium text-right">Queues</th>
                      <th className="px-4 py-2 font-medium text-right">Topics</th>
                      <th className="px-4 py-2 font-medium text-right">Active</th>
                      <th className="px-4 py-2 font-medium text-right">DLQ Active</th>
                      <th className="px-4 py-2 font-medium text-right">New</th>
                      <th className="px-4 py-2 font-medium text-right">Resolved</th>
                      <th className="px-4 py-2 font-medium text-right">Total</th>
                      <th className="px-4 py-2 font-medium">Top category</th>
                      <th className="px-4 py-2 font-medium">Top entity</th>
                      <th className="px-4 py-2 font-medium">Oldest</th>
                      <th className="px-4 py-2"></th>
                      <th className="px-4 py-2"></th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredNamespaces.map((n) => {
                      const sev = severityStyles[n.severity];
                      const stats = liveStatsByNamespace.get(n.namespaceId);
                      const isProd = n.environment.toLowerCase() === 'prod';
                      return (
                        <tr
                          key={n.namespaceId}
                          onClick={() => goToNamespace(n)}
                          onKeyDown={(e) => {
                            if (e.key === 'Enter' || e.key === ' ') {
                              e.preventDefault();
                              goToNamespace(n);
                            }
                          }}
                          role="button"
                          tabIndex={0}
                          className="border-b border-gray-50 hover:bg-indigo-50/40 cursor-pointer focus:outline-none focus:ring-2 focus:ring-inset focus:ring-indigo-500"
                        >
                          <td className="px-4 py-2.5">
                            <div className="flex items-center gap-2">
                              <span className={`w-2 h-2 rounded-full ${sev.dot}`} title={sev.label} />
                              <span className="font-medium text-gray-800">{n.namespaceName}</span>
                              <ProviderBadge provider={n.provider.toLowerCase() as CloudProviderType} />
                            </div>
                          </td>
                          <td className="px-4 py-2.5 text-gray-500">{n.environment}</td>
                          <td className="px-4 py-2.5 text-right text-gray-600">
                            {stats && !stats.isLoading ? stats.totalQueues : '—'}
                          </td>
                          <td className="px-4 py-2.5 text-right text-gray-600">
                            {stats && !stats.isLoading ? stats.totalTopics : '—'}
                          </td>
                          <td className="px-4 py-2.5 text-right text-gray-600">
                            {stats && !stats.isLoading ? stats.totalActive : '—'}
                          </td>
                          <td className="px-4 py-2.5 text-right font-medium text-gray-800">{n.activeCount}</td>
                          <td className={`px-4 py-2.5 text-right ${n.newInWindow > 0 ? 'text-red-600 font-medium' : 'text-gray-400'}`}>
                            {n.newInWindow > 0 ? `+${n.newInWindow}` : '0'}
                          </td>
                          <td className="px-4 py-2.5 text-right text-gray-600">
                            {n.resolvedInWindow > 0 ? n.resolvedInWindow : '—'}
                          </td>
                          <td className="px-4 py-2.5 text-right text-gray-500">{n.totalCount}</td>
                          <td className="px-4 py-2.5 text-gray-600">{n.topCategory ?? '—'}</td>
                          <td className="px-4 py-2.5 text-gray-600">
                            {n.topEntity ? `${n.topEntity} (${n.topEntityCount})` : '—'}
                          </td>
                          <td className="px-4 py-2.5 text-gray-500">
                            <span className="inline-flex items-center gap-1">
                              <Clock className="w-3 h-3" />
                              {relativeAge(n.oldestActiveDetectedAt)}
                            </span>
                          </td>
                          <td className="px-4 py-2.5">
                            {n.activeCount > 0 && !isProd && (
                              <button
                                onClick={(e) => goToBulkActions(n, e)}
                                className="flex items-center gap-1 text-xs font-medium text-indigo-600 hover:text-indigo-800"
                                title="Open bulk replay/purge for this namespace"
                              >
                                <PlayCircle className="w-3.5 h-3.5" />
                                Bulk actions
                              </button>
                            )}
                          </td>
                          <td className="px-4 py-2.5 text-right">
                            <ArrowRight className="w-4 h-4 text-gray-300" />
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </>
      )}
    </div>
  );
}
