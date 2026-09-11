import { useMemo, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  AlertTriangle,
  Layers,
  Boxes,
  Radio,
  Clock,
  RefreshCw,
  Search,
  X,
  ChevronDown,
  Download,
  ExternalLink,
  Lightbulb,
  Zap,
  ArrowUpRight,
  ArrowDownRight,
  Mail,
} from 'lucide-react';
import { LineChart, Line, XAxis, Tooltip, ResponsiveContainer } from 'recharts';
import { useDlqOverview } from '@servicehub/ui-shared/hooks/useDlqOverview';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { ProviderBadge, getProviderStyle } from '@servicehub/ui-shared/lib/providerStyles';
import { EnvironmentBadge } from '@/components/EnvironmentBadge';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';
import type {
  DlqFailureCategory,
  DlqOverviewEnvironment,
  DlqOverviewNamespace,
  DlqProviderOverview,
} from '@servicehub/ui-shared/lib/api/dlqOverview';

const DAYS_OPTIONS = [
  { label: 'Last 24 hours', days: 1 },
  { label: 'Last 7 days', days: 7 },
  { label: 'Last 30 days', days: 30 },
];

// Human-readable labels for the backend's FailureCategory enum (camelCase wire values).
const REASON_LABELS: Record<DlqFailureCategory, string> = {
  unknown: 'Unknown',
  transient: 'Transient',
  maxDelivery: 'Max Delivery',
  expired: 'Expired',
  dataQuality: 'Data Quality',
  authorization: 'Authorization',
  processingError: 'Processing Error',
  resourceNotFound: 'Resource Not Found',
  quotaExceeded: 'Quota Exceeded',
};

const REASON_ORDER: DlqFailureCategory[] = [
  'processingError',
  'maxDelivery',
  'dataQuality',
  'transient',
  'authorization',
  'resourceNotFound',
  'quotaExceeded',
  'expired',
  'unknown',
];

// Matches PROVIDER_STYLES' dot accent hexes (providerStyles.tsx) — Recharts needs a literal
// color, not a Tailwind class, so this stays in sync with that file's brand accents by hand.
const PROVIDER_HEX: Record<CloudProviderType, string> = {
  azure: '#0078D4',
  aws: '#FF9900',
  gcp: '#34A853',
};

const REASON_BAR_HEX = ['#ef4444', '#f59e0b', '#8b5cf6', '#0ea5e9', '#10b981'];

function formatAge(iso: string | null): string {
  if (!iso) return '—';
  const ms = Date.now() - new Date(iso).getTime();
  if (ms < 0) return 'just now';
  const totalMinutes = Math.floor(ms / 60000);
  const days = Math.floor(totalMinutes / 1440);
  const hours = Math.floor((totalMinutes % 1440) / 60);
  if (days > 0) return `${days}d ${hours}h`;
  const minutes = totalMinutes % 60;
  if (hours > 0) return `${hours}h ${minutes}m`;
  return totalMinutes <= 0 ? 'just now' : `${totalMinutes}m`;
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

function ChangeBadge({ percent }: { percent: number | null }) {
  if (percent === null) {
    return <span className="text-[11px] font-semibold text-gray-400">New</span>;
  }
  if (percent === 0) {
    return <span className="text-[11px] font-semibold text-gray-400">flat</span>;
  }
  const up = percent > 0;
  return (
    <span className={`inline-flex items-center gap-0.5 text-[11px] font-semibold ${up ? 'text-red-600' : 'text-emerald-600'}`}>
      {up ? <ArrowUpRight className="w-3 h-3" /> : <ArrowDownRight className="w-3 h-3" />}
      {Math.abs(percent).toFixed(percent % 1 === 0 ? 0 : 1)}%
    </span>
  );
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

function ProviderSection({
  section,
  search,
  onNavigateNamespace,
}: {
  section: DlqProviderOverview;
  search: string;
  onNavigateNamespace: (namespace: DlqOverviewNamespace) => void;
}) {
  const [collapsed, setCollapsed] = useState(false);
  const style = getProviderStyle(section.provider);
  const isOpen = search ? true : !collapsed;

  const searchLower = search.trim().toLowerCase();
  const visibleNamespaces = searchLower
    ? section.namespaces.filter((n) => n.namespaceName.toLowerCase().includes(searchLower))
    : section.namespaces;

  const topReasons = [...section.topReasons].sort((a, b) => b.count - a.count);
  const maxReasonCount = topReasons[0]?.count ?? 0;

  return (
    <section className={`bg-white rounded-xl border ${style.headerBorder} shadow-sm overflow-hidden`}>
      <button
        onClick={() => setCollapsed((v) => !v)}
        aria-expanded={isOpen}
        className={`w-full flex flex-wrap items-center gap-3 px-5 py-3 ${style.headerBg} border-b ${style.headerBorder} text-left`}
      >
        <ChevronDown className={`w-4 h-4 text-gray-400 transition-transform ${isOpen ? '' : '-rotate-90'}`} />
        <ProviderBadge provider={section.provider} />
        <span className="px-1.5 py-0.5 rounded text-[10px] font-semibold bg-white/70 text-gray-500 border border-gray-200">
          {section.namespacesWithDlq} namespace{section.namespacesWithDlq === 1 ? '' : 's'} with DLQ
        </span>
        <div className="ml-auto flex items-center gap-4">
          <div className="text-right">
            <div className={`text-lg font-bold ${section.totalDeadLettered > 0 ? 'text-red-700' : 'text-gray-400'}`}>
              {section.totalDeadLettered.toLocaleString()}
            </div>
            <div className="text-[10px] text-gray-500 -mt-0.5">Dead-Lettered</div>
          </div>
          <span
            onClick={(e) => {
              e.stopPropagation();
            }}
            className="hidden sm:block"
          >
            <ChangeBadge percent={section.changePercent} />
          </span>
        </div>
      </button>

      {isOpen && (
        <div className="p-4 space-y-4">
          <div className="flex flex-wrap items-center gap-4 text-xs text-gray-500">
            <span className="flex items-center gap-1.5">
              <Boxes className="w-3.5 h-3.5" /> {section.affectedQueues} queue{section.affectedQueues === 1 ? '' : 's'}
            </span>
            <span className="flex items-center gap-1.5">
              <Radio className="w-3.5 h-3.5" /> {section.affectedTopics} topic{section.affectedTopics === 1 ? '' : 's'}
            </span>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
            <div className="border border-gray-100 rounded-lg p-3">
              <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">
                DLQ Trend ({style.label})
              </h3>
              {section.dailyTrend.length > 0 ? (
                <div style={{ width: '100%', height: 140 }}>
                  <ResponsiveContainer>
                    <LineChart data={section.dailyTrend} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
                      <XAxis
                        dataKey="date"
                        tickFormatter={(d: string) => new Date(d).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}
                        tick={{ fontSize: 10, fill: '#9ca3af' }}
                      />
                      <Tooltip
                        labelFormatter={(d) => new Date(d as string).toLocaleDateString()}
                        formatter={(value) => [value as number, 'Active backlog']}
                      />
                      <Line type="monotone" dataKey="count" stroke={PROVIDER_HEX[section.provider]} strokeWidth={2} dot={false} />
                    </LineChart>
                  </ResponsiveContainer>
                </div>
              ) : (
                <p className="text-xs text-gray-400 italic py-8 text-center">No trend data for this window</p>
              )}
            </div>

            <div className="border border-gray-100 rounded-lg p-3">
              <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">Top DLQ Reasons</h3>
              {topReasons.length > 0 ? (
                <div className="space-y-2">
                  {topReasons.map((reason, i) => (
                    <div key={reason.category} className="flex items-center gap-2 text-xs">
                      <span className="w-28 shrink-0 text-gray-600 truncate">{REASON_LABELS[reason.category]}</span>
                      <div className="flex-1 h-2.5 bg-gray-100 rounded-full overflow-hidden">
                        <div
                          className="h-full rounded-full"
                          style={{
                            width: maxReasonCount > 0 ? `${(reason.count / maxReasonCount) * 100}%` : '0%',
                            backgroundColor: REASON_BAR_HEX[i % REASON_BAR_HEX.length],
                          }}
                        />
                      </div>
                      <span className="w-10 shrink-0 text-right font-semibold text-gray-700">{reason.count}</span>
                      <span className="w-10 shrink-0 text-right text-gray-400">{reason.percent}%</span>
                    </div>
                  ))}
                </div>
              ) : (
                <p className="text-xs text-gray-400 italic py-8 text-center">No dead-lettered messages</p>
              )}
            </div>
          </div>

          <div>
            <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">
              Namespaces ({style.label})
            </h3>
            {visibleNamespaces.length === 0 ? (
              <p className="text-sm text-gray-400 italic py-4 text-center border border-gray-100 rounded-lg">
                {search ? 'No namespaces match your search' : 'No namespaces with dead-lettered messages'}
              </p>
            ) : (
              <div className="border border-gray-100 rounded-lg overflow-hidden">
                <table className="w-full text-sm">
                  <thead className="bg-gray-50 text-[11px] text-gray-500">
                    <tr>
                      <th className="px-3 py-1.5 text-left font-medium">Namespace</th>
                      <th className="px-3 py-1.5 text-left font-medium">Environment</th>
                      <th className="px-3 py-1.5 text-left font-medium">Queues (DLQ)</th>
                      <th className="px-3 py-1.5 text-left font-medium">Topics (DLQ)</th>
                      <th className="px-3 py-1.5 text-left font-medium">DLQ Count</th>
                      <th className="px-3 py-1.5 text-left font-medium">Oldest</th>
                      <th className="px-3 py-1.5"></th>
                    </tr>
                  </thead>
                  <tbody>
                    {visibleNamespaces.map((ns) => (
                      <tr key={ns.namespaceId} className="border-t border-gray-100 hover:bg-gray-50">
                        <td className="px-3 py-2 font-medium text-gray-900">{ns.namespaceName}</td>
                        <td className="px-3 py-2">
                          <EnvironmentBadge env={ns.environment} />
                        </td>
                        <td className="px-3 py-2 text-gray-600">{ns.queuesWithDlq}</td>
                        <td className="px-3 py-2 text-gray-600">{ns.topicsWithDlq}</td>
                        <td className="px-3 py-2 font-semibold text-red-700">{ns.dlqCount.toLocaleString()}</td>
                        <td className="px-3 py-2 text-gray-500">{formatAge(ns.oldestDetectedAt)}</td>
                        <td className="px-3 py-2 text-right">
                          <button
                            onClick={() => onNavigateNamespace(ns)}
                            className="px-2.5 py-1 text-xs font-medium text-indigo-700 bg-indigo-50 hover:bg-indigo-100 border border-indigo-200 rounded-md transition-colors"
                          >
                            View
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      )}
    </section>
  );
}

export default function DlqOverviewPage() {
  const navigate = useNavigate();
  const { isDemoMode, cloudProvider } = useDemoContext();
  const navPrefix = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';
  const [days, setDays] = useState(7);
  const [cloud, setCloud] = useState<CloudProviderType | 'all'>('all');
  const [environment, setEnvironment] = useState<DlqOverviewEnvironment | 'all'>('all');
  const [namespaceId, setNamespaceId] = useState('');
  const [reason, setReason] = useState<DlqFailureCategory | 'all'>('all');
  const [search, setSearch] = useState('');

  const { data: namespaces } = useNamespaces();

  const { data, isLoading, isError, refetch, isFetching } = useDlqOverview({
    days,
    cloud: cloud === 'all' ? undefined : cloud,
    environment: environment === 'all' ? undefined : environment,
    namespaceId: namespaceId || undefined,
    reason: reason === 'all' ? undefined : reason,
  });

  const filtersActive = cloud !== 'all' || environment !== 'all' || namespaceId !== '' || reason !== 'all' || search !== '';

  const clearFilters = () => {
    setCloud('all');
    setEnvironment('all');
    setNamespaceId('');
    setReason('all');
    setSearch('');
  };

  const namespaceOptions = useMemo(
    () => (namespaces ?? []).filter((ns) => cloud === 'all' || ns.cloudProvider === cloud),
    [namespaces, cloud],
  );

  const goToNamespace = (ns: DlqOverviewNamespace) => navigate(`${navPrefix}/dlq-history?namespace=${ns.namespaceId}`);

  const exportCsv = () => {
    const header = ['Provider', 'Namespace', 'Environment', 'Queues (DLQ)', 'Topics (DLQ)', 'DLQ Count', 'Oldest Detected'];
    const rows = (data?.providers ?? []).flatMap((p) =>
      p.namespaces
        .filter((n) => !search || n.namespaceName.toLowerCase().includes(search.toLowerCase()))
        .map((n) => [
          getProviderStyle(p.provider).label,
          n.namespaceName,
          n.environment,
          n.queuesWithDlq,
          n.topicsWithDlq,
          n.dlqCount,
          n.oldestDetectedAt ?? '',
        ]),
    );
    downloadCsv(`dlq-overview-${new Date().toISOString().slice(0, 10)}.csv`, [header, ...rows]);
  };

  return (
    <div className="flex-1 overflow-y-auto min-w-0">
      <div className="p-6 max-w-7xl mx-auto space-y-6">
        {/* Header */}
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-lg bg-red-50 flex items-center justify-center">
              <AlertTriangle className="w-5 h-5 text-red-600" />
            </div>
            <div>
              <h1 className="text-xl font-bold text-gray-900">Dead-Letter Overview</h1>
              <p className="text-sm text-gray-500">
                Investigate and resolve dead-lettered messages (DLQ) across all clouds. Grouped by provider and namespace.
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
            <button
              onClick={() => refetch()}
              className="p-2 rounded-lg border border-gray-200 bg-white text-gray-600 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
              aria-label="Refresh DLQ overview"
            >
              <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
            </button>
            <select
              value={days}
              onChange={(e) => setDays(Number(e.target.value))}
              aria-label="Time range"
              className="px-3 py-2 rounded-lg text-sm border border-gray-300 text-gray-700 bg-white focus:outline-none focus:ring-2 focus:ring-primary-500"
            >
              {DAYS_OPTIONS.map((opt) => (
                <option key={opt.days} value={opt.days}>
                  {opt.label}
                </option>
              ))}
            </select>
          </div>
        </div>

        {isError && (
          <div className="flex items-center justify-between gap-3 bg-red-50 border border-red-200 text-red-700 rounded-lg p-4 text-sm">
            <span>Failed to load the DLQ overview.</span>
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
            <RefreshCw className="w-6 h-6 animate-spin text-red-400" />
            <p className="text-sm text-gray-500">Loading DLQ overview…</p>
          </div>
        )}

        {data && (
          <>
            {/* Key metrics */}
            <div className="grid grid-cols-2 md:grid-cols-5 gap-4">
              <StatTile
                icon={<Mail className="w-5 h-5 text-red-600" />}
                label="Total Dead-Lettered"
                value={data.totals.totalDeadLettered.toLocaleString()}
                tone="bg-red-50"
                sub={<ChangeBadge percent={data.totals.changePercent} />}
              />
              <StatTile
                icon={<Layers className="w-5 h-5 text-indigo-600" />}
                label="Namespaces with DLQ"
                value={`${data.totals.namespacesWithDlq} / ${data.totals.namespacesTotal}`}
                tone="bg-indigo-50"
              />
              <StatTile
                icon={<Boxes className="w-5 h-5 text-amber-600" />}
                label="Affected Queues"
                value={data.totals.affectedQueues}
                tone="bg-amber-50"
              />
              <StatTile
                icon={<Radio className="w-5 h-5 text-purple-600" />}
                label="Affected Topics"
                value={data.totals.affectedTopics}
                tone="bg-purple-50"
              />
              <StatTile
                icon={<Clock className="w-5 h-5 text-gray-600" />}
                label="Oldest Message"
                value={formatAge(data.totals.oldestMessageDetectedAt)}
                tone="bg-gray-100"
              />
            </div>

            {/* Search + filters */}
            <div className="bg-white border border-gray-200 rounded-xl shadow-sm p-3 flex flex-wrap items-center gap-2">
              <div className="relative flex-1 min-w-[220px]">
                <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
                <input
                  type="text"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Search namespaces, queues, topics…"
                  aria-label="Search namespaces"
                  className="w-full pl-9 pr-8 py-2 rounded-lg text-sm bg-white border border-gray-300 text-gray-700 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-red-500 focus:border-red-500"
                />
                {search && (
                  <button
                    onClick={() => setSearch('')}
                    aria-label="Clear search"
                    className="absolute right-2.5 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
                  >
                    <X className="w-4 h-4" />
                  </button>
                )}
              </div>

              <select
                value={cloud}
                onChange={(e) => {
                  const next = e.target.value as CloudProviderType | 'all';
                  setCloud(next);
                  const selectedNs = namespaces?.find((ns) => ns.id === namespaceId);
                  if (selectedNs && next !== 'all' && selectedNs.cloudProvider !== next) {
                    setNamespaceId('');
                  }
                }}
                aria-label="Filter by cloud"
                className="px-3 py-2 rounded-lg text-sm border border-gray-300 text-gray-700 bg-white focus:outline-none focus:ring-2 focus:ring-red-500"
              >
                <option value="all">All Clouds</option>
                <option value="azure">Azure</option>
                <option value="aws">AWS</option>
                <option value="gcp">GCP</option>
              </select>

              <select
                value={environment}
                onChange={(e) => setEnvironment(e.target.value as DlqOverviewEnvironment | 'all')}
                aria-label="Filter by environment"
                className="px-3 py-2 rounded-lg text-sm border border-gray-300 text-gray-700 bg-white focus:outline-none focus:ring-2 focus:ring-red-500"
              >
                <option value="all">All Environments</option>
                <option value="dev">Dev</option>
                <option value="uat">UAT</option>
                <option value="prod">Prod</option>
              </select>

              <select
                value={namespaceId}
                onChange={(e) => setNamespaceId(e.target.value)}
                aria-label="Filter by namespace"
                className="px-3 py-2 rounded-lg text-sm border border-gray-300 text-gray-700 bg-white focus:outline-none focus:ring-2 focus:ring-red-500 max-w-[200px]"
              >
                <option value="">All Namespaces</option>
                {namespaceOptions.map((ns) => (
                  <option key={ns.id} value={ns.id}>
                    {ns.displayName || ns.name}
                  </option>
                ))}
              </select>

              <select
                value={reason}
                onChange={(e) => setReason(e.target.value as DlqFailureCategory | 'all')}
                aria-label="Filter by DLQ reason"
                className="px-3 py-2 rounded-lg text-sm border border-gray-300 text-gray-700 bg-white focus:outline-none focus:ring-2 focus:ring-red-500"
              >
                <option value="all">All DLQ Reasons</option>
                {REASON_ORDER.map((r) => (
                  <option key={r} value={r}>
                    {REASON_LABELS[r]}
                  </option>
                ))}
              </select>

              {filtersActive && (
                <button
                  onClick={clearFilters}
                  className="flex items-center gap-1 text-xs font-medium text-gray-500 hover:text-gray-700"
                >
                  <X className="w-3.5 h-3.5" />
                  Clear filters
                </button>
              )}

              <button
                onClick={exportCsv}
                className="ml-auto flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-gray-600 hover:text-gray-900 hover:bg-gray-50 border border-gray-200 rounded-lg transition-colors"
              >
                <Download className="w-3.5 h-3.5" />
                Export
              </button>
            </div>

            {/* Provider sections */}
            {data.providers.length === 0 ? (
              <div className="bg-white border border-gray-200 rounded-xl p-8 text-center text-sm text-gray-400">
                No namespaces match the current filters.
              </div>
            ) : (
              <div className="space-y-4">
                {data.providers.map((section) => (
                  <ProviderSection
                    key={section.provider}
                    section={section}
                    search={search}
                    onNavigateNamespace={goToNamespace}
                  />
                ))}
              </div>
            )}

            {/* Guidance banner */}
            <div className="flex flex-wrap items-center gap-4 bg-amber-50 border border-amber-200 rounded-xl p-4">
              <div className="w-10 h-10 rounded-lg bg-amber-100 flex items-center justify-center shrink-0">
                <Lightbulb className="w-5 h-5 text-amber-600" />
              </div>
              <div className="flex-1 min-w-[220px]">
                <h3 className="text-sm font-semibold text-gray-900">Take action to reduce dead-lettered messages</h3>
                <p className="text-xs text-gray-600 mt-0.5">
                  Use the insights above to identify root causes, replay messages, or configure auto-replay rules.
                </p>
              </div>
              <div className="flex items-center gap-2">
                <button
                  onClick={() => navigate(`${navPrefix}/rules`)}
                  className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-amber-700 bg-white hover:bg-amber-100 border border-amber-300 rounded-lg transition-colors"
                >
                  <Zap className="w-3.5 h-3.5" />
                  Go to Auto-Replay Rules
                </button>
                <button
                  onClick={() => navigate(`${navPrefix}/dlq-history`)}
                  className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-white bg-red-600 hover:bg-red-700 rounded-lg transition-colors"
                >
                  Investigate Now
                  <ExternalLink className="w-3.5 h-3.5" />
                </button>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
