import { useMemo, useState, useEffect, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  AlertTriangle,
  CheckCircle2,
  Ban,
  Archive,
  AlertCircle,
  RefreshCw,
  Download,
  Search,
  X,
  ChevronLeft,
  ChevronRight,
  ScanSearch,
  Eye,
  BookOpen,
  History,
  MoreVertical,
  Zap,
  SlidersHorizontal,
  Layers,
} from 'lucide-react';
import { LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer, Legend } from 'recharts';
import { useIncidentsList, type IncidentListItem } from '@servicehub/ui-shared/hooks/useIncidentsList';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { formatRelativeTime } from '@servicehub/ui-shared/lib/utils';
import { ProviderBadge } from '@servicehub/ui-shared/lib/providerStyles';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';
import { StatusBadge, CategoryBadge } from '@/components/dlq';
import { IncidentDetailPanel, SeverityBadge } from '@/components/incidents/IncidentDetailPanel';

const DAYS_OPTIONS = [
  { label: 'Last 24 hours', days: 1 },
  { label: 'Last 7 days', days: 7 },
  { label: 'Last 30 days', days: 30 },
];

const STATUS_OPTIONS = ['Active', 'Reopened', 'Resolved', 'Suppressed', 'Archived'];
const SEVERITY_OPTIONS = ['Critical', 'High', 'Medium', 'Low'];
const CATEGORY_BAR_HEX = ['#ef4444', '#f59e0b', '#8b5cf6', '#0ea5e9', '#10b981', '#9ca3af'];
const PAGE_SIZE = 10;

type StatusTab = 'active' | 'needsAction' | 'resolved' | 'suppressed' | 'archived';

const STATUS_TABS: { id: StatusTab; label: string }[] = [
  { id: 'active', label: 'Active' },
  { id: 'needsAction', label: 'Needs Action' },
  { id: 'resolved', label: 'Resolved' },
  { id: 'suppressed', label: 'Suppressed' },
  { id: 'archived', label: 'Archived' },
];

function matchesTab(item: IncidentListItem, tab: StatusTab): boolean {
  switch (tab) {
    case 'active':
      return item.status === 'Active';
    case 'needsAction':
      return item.status === 'Active' || item.status === 'Reopened';
    case 'resolved':
      return item.status === 'Resolved';
    case 'suppressed':
      return item.status === 'Suppressed';
    case 'archived':
      return item.status === 'Archived';
  }
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

function StatTile({ icon, label, value, tone }: { icon: ReactNode; label: string; value: number; tone: string }) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-4 flex items-center gap-3">
      <div className={`w-10 h-10 rounded-lg flex items-center justify-center shrink-0 ${tone}`}>{icon}</div>
      <div className="min-w-0">
        <div className="text-2xl font-semibold text-gray-900 leading-none">{value}</div>
        <div className="text-xs text-gray-500 mt-1 truncate" title={label}>{label}</div>
      </div>
    </div>
  );
}

function IncidentTrendChart({
  trend,
  days,
  onDaysChange,
}: {
  trend: { bucketStart: string; active: number; resolved: number; new: number }[];
  days: number;
  onDaysChange: (days: number) => void;
}) {
  const hourly = days <= 1;
  return (
    <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-4">
      <div className="flex items-center justify-between mb-2">
        <h3 className="text-sm font-semibold text-gray-700">Incident Trend</h3>
        <div className="flex items-center gap-1 bg-gray-100 rounded-lg p-0.5">
          {DAYS_OPTIONS.map((opt) => (
            <button
              key={opt.days}
              onClick={() => onDaysChange(opt.days)}
              className={`px-2.5 py-1 text-xs font-medium rounded-md transition-colors ${
                days === opt.days ? 'bg-white text-primary-700 shadow-sm' : 'text-gray-500 hover:text-gray-700'
              }`}
            >
              {opt.days === 1 ? '24h' : `${opt.days}d`}
            </button>
          ))}
        </div>
      </div>
      {trend.length > 0 ? (
        <div style={{ width: '100%', height: 220 }}>
          <ResponsiveContainer>
            <LineChart data={trend} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
              <XAxis
                dataKey="bucketStart"
                tickFormatter={(d: string) =>
                  hourly
                    ? new Date(d).toLocaleTimeString(undefined, { hour: 'numeric' })
                    : new Date(d).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
                }
                tick={{ fontSize: 10, fill: '#9ca3af' }}
              />
              <YAxis allowDecimals={false} tick={{ fontSize: 10, fill: '#9ca3af' }} width={28} />
              <Tooltip
                labelFormatter={(d) => new Date(d as string).toLocaleString()}
              />
              <Legend wrapperStyle={{ fontSize: 11 }} />
              <Line type="monotone" dataKey="active" name="Active" stroke="#dc2626" strokeWidth={2} dot={false} />
              <Line type="monotone" dataKey="resolved" name="Resolved" stroke="#16a34a" strokeWidth={2} dot={false} />
              <Line type="monotone" dataKey="new" name="New" stroke="#2563eb" strokeWidth={2} dot={false} />
            </LineChart>
          </ResponsiveContainer>
        </div>
      ) : (
        <p className="text-xs text-gray-400 italic py-16 text-center">No trend data for this window</p>
      )}
    </div>
  );
}

function TopCategoriesPanel({ categories }: { categories: { category: string; count: number; percent: number }[] }) {
  const max = categories[0]?.count ?? 0;
  return (
    <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-4">
      <h3 className="text-sm font-semibold text-gray-700 mb-3">Top Incident Categories</h3>
      {categories.length === 0 ? (
        <p className="text-xs text-gray-400 italic py-16 text-center">No signatures yet</p>
      ) : (
        <div className="space-y-2.5">
          {categories.map((c, i) => (
            <div key={c.category} className="flex items-center gap-2 text-xs">
              <span className="w-28 shrink-0 text-gray-600 truncate" title={c.category}>{c.category}</span>
              <div className="flex-1 h-2.5 bg-gray-100 rounded-full overflow-hidden">
                <div
                  className="h-full rounded-full"
                  style={{ width: max > 0 ? `${(c.count / max) * 100}%` : '0%', backgroundColor: CATEGORY_BAR_HEX[i % CATEGORY_BAR_HEX.length] }}
                />
              </div>
              <span className="w-12 shrink-0 text-right font-semibold text-gray-700">{c.count.toLocaleString()}</span>
              <span className="w-10 shrink-0 text-right text-gray-400">{c.percent}%</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function IncidentRow({
  item,
  selected,
  onSelect,
  onKnowledge,
  onReplay,
}: {
  item: IncidentListItem;
  selected: boolean;
  onSelect: () => void;
  onKnowledge: () => void;
  onReplay: () => void;
}) {
  const navigate = useNavigate();
  const [menuOpen, setMenuOpen] = useState(false);
  const { isDemoMode, cloudProvider } = useDemoContext();
  const navPrefix = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';

  return (
    <tr className={`border-t border-gray-100 hover:bg-gray-50 align-top cursor-pointer ${selected ? 'bg-primary-50/40' : ''}`} onClick={onSelect}>
      <td className="px-3 py-3" onClick={(e) => e.stopPropagation()}>
        <input type="checkbox" checked={selected} onChange={onSelect} aria-label={`Select ${item.displayName}`} />
      </td>
      <td className="px-3 py-3 max-w-xs">
        <div className="flex items-center gap-1.5 flex-wrap mb-1">
          <SeverityBadge severity={item.severity} />
          <StatusBadge status={item.status} />
        </div>
        <p className="font-medium text-gray-900 text-sm truncate" title={item.displayName}>{item.displayName}</p>
      </td>
      <td className="px-3 py-3">
        {item.cloudProvider && <ProviderBadge provider={item.cloudProvider} />}
      </td>
      <td className="px-3 py-3 text-gray-600 max-w-[160px] truncate" title={item.namespaceName ?? undefined}>
        {item.namespaceName ?? '—'}
      </td>
      <td className="px-3 py-3 text-right font-semibold text-gray-900">{item.messageCount}</td>
      <td className="px-3 py-3 text-gray-500 whitespace-nowrap">{formatRelativeTime(new Date(item.lastSeenAt))}</td>
      <td className="px-3 py-3">
        <CategoryBadge category={item.category} />
      </td>
      <td className="px-3 py-3">
        <StatusBadge status={item.status} />
      </td>
      <td className="px-3 py-3" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center gap-1.5 justify-end">
          <button
            onClick={() => navigate(`${navPrefix}/incidents/${item.signatureHash}?namespace=${item.namespaceId}`)}
            aria-label={`Investigate ${item.displayName}`}
            className="flex items-center gap-1 px-2.5 py-1 text-xs font-medium text-primary-700 bg-primary-50 hover:bg-primary-100 border border-primary-200 rounded-md transition-colors whitespace-nowrap"
          >
            <Eye className="w-3 h-3" />
            Investigate
          </button>
          <div className="relative">
            <button
              onClick={() => setMenuOpen((v) => !v)}
              aria-label={`More actions for ${item.displayName}`}
              className="p-1.5 rounded-md text-gray-400 hover:bg-gray-100 hover:text-gray-600"
            >
              <MoreVertical className="w-3.5 h-3.5" />
            </button>
            {menuOpen && (
              <>
                <div className="fixed inset-0 z-10" onClick={() => setMenuOpen(false)} />
                <div className="absolute right-0 mt-1 w-52 bg-white border border-gray-200 rounded-lg shadow-lg z-20 py-1">
                  <button
                    onClick={() => { setMenuOpen(false); onKnowledge(); }}
                    aria-label={`${item.hasKnowledge ? 'Update' : 'Add'} knowledge for ${item.displayName}`}
                    className="w-full text-left px-3 py-1.5 text-xs text-gray-700 hover:bg-gray-50 flex items-center gap-2"
                  >
                    <BookOpen className="w-3.5 h-3.5" />
                    {item.hasKnowledge ? 'Update Knowledge' : 'Add Knowledge'}
                  </button>
                  <button
                    onClick={() => { setMenuOpen(false); onReplay(); }}
                    aria-label={`Replay preview for ${item.displayName}`}
                    className="w-full text-left px-3 py-1.5 text-xs text-gray-700 hover:bg-gray-50 flex items-center gap-2"
                  >
                    <History className="w-3.5 h-3.5" />
                    Replay Preview
                  </button>
                  <button
                    onClick={() => { setMenuOpen(false); navigate(`${navPrefix}/rules`); }}
                    className="w-full text-left px-3 py-1.5 text-xs text-gray-700 hover:bg-gray-50 flex items-center gap-2"
                  >
                    <Zap className="w-3.5 h-3.5" />
                    Create Auto-Replay Rule
                  </button>
                </div>
              </>
            )}
          </div>
        </div>
      </td>
    </tr>
  );
}

export function FailureIntelligenceCenterPage() {
  const navigate = useNavigate();
  const { isDemoMode, cloudProvider } = useDemoContext();
  const navPrefix = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';
  const { data: namespaces } = useNamespaces();

  const [days, setDays] = useState(7);
  const { data, isLoading, isFetching, isError, refetch } = useIncidentsList(days);

  const [statusTab, setStatusTab] = useState<StatusTab>('active');
  const [search, setSearch] = useState('');
  const [provider, setProvider] = useState<CloudProviderType | 'all'>('all');
  const [namespaceId, setNamespaceId] = useState('');
  const [status, setStatus] = useState<string>('all');
  const [category, setCategory] = useState<string>('all');
  const [severity, setSeverity] = useState<string>('all');
  const [moreFiltersOpen, setMoreFiltersOpen] = useState(false);
  const [escalatingOnly, setEscalatingOnly] = useState(false);
  const [missingKnowledgeOnly, setMissingKnowledgeOnly] = useState(false);
  const [page, setPage] = useState(1);
  const [selectedHash, setSelectedHash] = useState<string | null>(null);

  const items = useMemo(() => data?.items ?? [], [data]);

  const categoryOptions = useMemo(
    () => [...new Set(items.map((i) => i.category))].sort(),
    [items],
  );

  const namespaceOptions = useMemo(
    () => (namespaces ?? []).filter((ns) => provider === 'all' || ns.cloudProvider === provider),
    [namespaces, provider],
  );

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return items.filter((i) => {
      if (!matchesTab(i, statusTab)) return false;
      if (provider !== 'all' && i.cloudProvider !== provider) return false;
      if (namespaceId && i.namespaceId !== namespaceId) return false;
      if (status !== 'all' && i.status !== status) return false;
      if (category !== 'all' && i.category !== category) return false;
      if (severity !== 'all' && i.severity !== severity) return false;
      if (escalatingOnly && !i.isEscalating) return false;
      if (missingKnowledgeOnly && i.hasKnowledge) return false;
      if (q) {
        const haystack = `${i.displayName} ${i.signatureHash} ${i.category} ${i.namespaceName ?? ''}`.toLowerCase();
        if (!haystack.includes(q)) return false;
      }
      return true;
    });
  }, [items, statusTab, search, provider, namespaceId, status, category, severity, escalatingOnly, missingKnowledgeOnly]);

  const tabCounts = useMemo(
    () => Object.fromEntries(STATUS_TABS.map((t) => [t.id, items.filter((i) => matchesTab(i, t.id)).length])) as Record<StatusTab, number>,
    [items],
  );

  const filtersActive =
    provider !== 'all' || namespaceId !== '' || status !== 'all' || category !== 'all' || severity !== 'all' ||
    escalatingOnly || missingKnowledgeOnly || search !== '';

  const clearFilters = () => {
    setProvider('all');
    setNamespaceId('');
    setStatus('all');
    setCategory('all');
    setSeverity('all');
    setEscalatingOnly(false);
    setMissingKnowledgeOnly(false);
    setSearch('');
  };

  // Reset to page 1 whenever the visible set changes shape.
  useEffect(() => setPage(1), [statusTab, search, provider, namespaceId, status, category, severity, escalatingOnly, missingKnowledgeOnly]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const pageItems = filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE);

  // Keep a selection: default to the first visible row, and clear it if it scrolls out of the
  // current filtered/paged set.
  useEffect(() => {
    if (pageItems.length === 0) {
      setSelectedHash(null);
      return;
    }
    if (!pageItems.some((i) => i.signatureHash === selectedHash)) {
      setSelectedHash(pageItems[0].signatureHash);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pageItems.map((i) => i.signatureHash).join(',')]);

  const selectedItem = items.find((i) => i.signatureHash === selectedHash) ?? null;

  const goKnowledge = (item: IncidentListItem) => navigate(`${navPrefix}/signatures/${item.signatureHash}?namespace=${item.namespaceId}`);
  const goReplay = (item: IncidentListItem) =>
    navigate(`${navPrefix}/incidents/${item.signatureHash}?namespace=${item.namespaceId}&tab=recovery`);

  const exportCsv = () => {
    const header = ['Severity', 'Status', 'Signature', 'Provider', 'Namespace', 'Messages', 'Category', 'First Seen', 'Last Seen'];
    const rows = filtered.map((i) => [
      i.severity, i.status, i.displayName, i.cloudProvider ?? '', i.namespaceName ?? '', i.messageCount, i.category, i.firstSeenAt, i.lastSeenAt,
    ]);
    downloadCsv(`incident-center-${new Date().toISOString().slice(0, 10)}.csv`, [header, ...rows]);
  };

  return (
    <div className="flex-1 overflow-y-auto min-w-0">
      <div className="p-6 max-w-[1600px] mx-auto space-y-6">
        {/* Header */}
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-lg bg-red-50 flex items-center justify-center">
              <AlertTriangle className="w-5 h-5 text-red-600" />
            </div>
            <div>
              <h1 className="text-xl font-bold text-gray-900">Incident Center</h1>
              <p className="text-sm text-gray-500">Operational command center for failure investigation and remediation.</p>
            </div>
          </div>
          <div className="flex items-center gap-2 flex-wrap">
            <select
              value={days}
              onChange={(e) => setDays(Number(e.target.value))}
              aria-label="Time range"
              className="px-3 py-2 rounded-lg text-sm border border-gray-300 text-gray-700 bg-white focus:outline-none focus:ring-2 focus:ring-primary-500"
            >
              {DAYS_OPTIONS.map((opt) => (
                <option key={opt.days} value={opt.days}>{opt.label}</option>
              ))}
            </select>
            <button
              onClick={() => refetch()}
              className="inline-flex items-center gap-1.5 px-3 py-2 text-sm font-medium bg-white border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors"
            >
              <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
              Refresh
            </button>
            <button
              onClick={exportCsv}
              className="inline-flex items-center gap-1.5 px-3 py-2 text-sm font-medium bg-white border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors"
            >
              <Download className="w-4 h-4" />
              Export
            </button>
            <button
              onClick={() => refetch()}
              title="Re-scan the current data — refreshes every signature, metric, and trend on this page"
              className="inline-flex items-center gap-1.5 px-3 py-2 text-sm font-semibold bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors"
            >
              <ScanSearch className="w-4 h-4" />
              Scan Now
            </button>
          </div>
        </div>

        {isError && (
          <div className="flex items-center justify-between gap-3 bg-red-50 border border-red-200 text-red-700 rounded-lg p-4 text-sm">
            <span>Unable to load the Incident Center.</span>
            <button onClick={() => refetch()} className="shrink-0 px-3 py-1.5 text-xs font-medium text-red-700 bg-white border border-red-300 rounded-lg hover:bg-red-100">
              Try Again
            </button>
          </div>
        )}

        {isLoading && !data && (
          <div className="flex flex-col items-center justify-center gap-3 py-20 text-gray-400">
            <RefreshCw className="w-6 h-6 animate-spin text-red-400" />
            <p className="text-sm text-gray-500">Loading Incident Center…</p>
          </div>
        )}

        {data && (
          <>
            {/* KPI tiles */}
            <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-4">
              <StatTile icon={<Layers className="w-5 h-5 text-purple-600" />} label="Total Signatures" value={data.metrics.totalSignatures} tone="bg-purple-50" />
              <StatTile icon={<AlertTriangle className="w-5 h-5 text-red-600" />} label="Active Incidents" value={data.metrics.activeSignatures} tone="bg-red-50" />
              <StatTile icon={<CheckCircle2 className="w-5 h-5 text-green-600" />} label="Resolved" value={data.metrics.resolvedSignatures} tone="bg-green-50" />
              <StatTile icon={<Ban className="w-5 h-5 text-gray-500" />} label="Suppressed" value={data.metrics.suppressedSignatures} tone="bg-gray-100" />
              <StatTile icon={<Archive className="w-5 h-5 text-gray-500" />} label="Archived" value={data.metrics.archivedSignatures} tone="bg-gray-100" />
              <StatTile icon={<AlertCircle className="w-5 h-5 text-orange-600" />} label="Requires Action" value={data.metrics.requiresAction} tone="bg-orange-50" />
            </div>

            {/* Trend + categories */}
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
              <IncidentTrendChart trend={data.trend} days={days} onDaysChange={setDays} />
              <TopCategoriesPanel categories={data.topCategories} />
            </div>

            {/* Filters */}
            <div className="bg-white border border-gray-200 rounded-xl shadow-sm p-3 space-y-2">
              <div className="flex flex-wrap items-center gap-2">
                <div className="relative flex-1 min-w-[220px]">
                  <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
                  <input
                    type="text"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="Search incidents, patterns, error text, IDs…"
                    className="w-full pl-9 pr-8 py-2 rounded-lg text-sm bg-white border border-gray-300 text-gray-700 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-red-500"
                  />
                  {search && (
                    <button onClick={() => setSearch('')} aria-label="Clear search" className="absolute right-2.5 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600">
                      <X className="w-4 h-4" />
                    </button>
                  )}
                </div>

                <select value={provider} onChange={(e) => setProvider(e.target.value as CloudProviderType | 'all')} aria-label="Filter by provider" className="px-3 py-2 rounded-lg text-sm border border-gray-300 bg-white">
                  <option value="all">All Providers</option>
                  <option value="azure">Azure</option>
                  <option value="aws">AWS</option>
                  <option value="gcp">GCP</option>
                </select>

                <select value={namespaceId} onChange={(e) => setNamespaceId(e.target.value)} aria-label="Filter by namespace" className="px-3 py-2 rounded-lg text-sm border border-gray-300 bg-white max-w-[180px]">
                  <option value="">All Namespaces</option>
                  {namespaceOptions.map((ns) => (
                    <option key={ns.id} value={ns.id}>{ns.displayName || ns.name}</option>
                  ))}
                </select>

                <select value={status} onChange={(e) => setStatus(e.target.value)} aria-label="Filter by status" className="px-3 py-2 rounded-lg text-sm border border-gray-300 bg-white">
                  <option value="all">All Status</option>
                  {STATUS_OPTIONS.map((s) => <option key={s} value={s}>{s}</option>)}
                </select>

                <select value={category} onChange={(e) => setCategory(e.target.value)} aria-label="Filter by category" className="px-3 py-2 rounded-lg text-sm border border-gray-300 bg-white max-w-[160px]">
                  <option value="all">All Categories</option>
                  {categoryOptions.map((c) => <option key={c} value={c}>{c}</option>)}
                </select>

                <select value={severity} onChange={(e) => setSeverity(e.target.value)} aria-label="Filter by severity" className="px-3 py-2 rounded-lg text-sm border border-gray-300 bg-white">
                  <option value="all">All Severities</option>
                  {SEVERITY_OPTIONS.map((s) => <option key={s} value={s}>{s}</option>)}
                </select>

                {filtersActive && (
                  <button onClick={clearFilters} className="flex items-center gap-1 text-xs font-medium text-gray-500 hover:text-gray-700">
                    <X className="w-3.5 h-3.5" />
                    Clear Filters
                  </button>
                )}

                <button
                  onClick={() => setMoreFiltersOpen((v) => !v)}
                  className={`ml-auto flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-lg border transition-colors ${
                    moreFiltersOpen ? 'text-primary-700 bg-primary-50 border-primary-200' : 'text-gray-600 hover:text-gray-900 border-gray-200 hover:bg-gray-50'
                  }`}
                >
                  <SlidersHorizontal className="w-3.5 h-3.5" />
                  More Filters
                </button>
              </div>

              {moreFiltersOpen && (
                <div className="flex flex-wrap items-center gap-4 pt-2 border-t border-gray-100 text-sm">
                  <label className="flex items-center gap-1.5 text-gray-600">
                    <input type="checkbox" checked={escalatingOnly} onChange={(e) => setEscalatingOnly(e.target.checked)} />
                    Escalating only
                  </label>
                  <label className="flex items-center gap-1.5 text-gray-600">
                    <input type="checkbox" checked={missingKnowledgeOnly} onChange={(e) => setMissingKnowledgeOnly(e.target.checked)} />
                    Missing knowledge only
                  </label>
                </div>
              )}
            </div>

            {/* Status tabs */}
            <div className="flex items-center gap-1 border-b border-gray-200">
              {STATUS_TABS.map((tab) => (
                <button
                  key={tab.id}
                  onClick={() => setStatusTab(tab.id)}
                  className={`inline-flex items-center gap-1.5 px-3 py-2 text-sm font-medium border-b-2 whitespace-nowrap ${
                    statusTab === tab.id ? 'border-primary-600 text-primary-700' : 'border-transparent text-gray-500 hover:text-gray-700'
                  }`}
                >
                  {tab.label}
                  <span className="px-1.5 py-0.5 rounded-full text-xs bg-gray-100 text-gray-600">{tabCounts[tab.id]}</span>
                </button>
              ))}
            </div>

            {/* List + detail panel */}
            <div className="flex flex-col lg:flex-row gap-4 items-start">
              <div className="flex-1 min-w-0 bg-white border border-gray-200 rounded-xl shadow-sm overflow-hidden">
                {pageItems.length === 0 ? (
                  <div className="p-10 text-center text-sm text-gray-400">No incidents match the current filters.</div>
                ) : (
                  <div className="overflow-x-auto">
                    <table className="w-full text-sm">
                      <thead className="bg-gray-50 text-[11px] text-gray-500">
                        <tr>
                          <th className="px-3 py-2"></th>
                          <th className="px-3 py-2 text-left font-medium">Incident</th>
                          <th className="px-3 py-2 text-left font-medium">Provider</th>
                          <th className="px-3 py-2 text-left font-medium">Context</th>
                          <th className="px-3 py-2 text-right font-medium">Messages</th>
                          <th className="px-3 py-2 text-left font-medium">Last Seen</th>
                          <th className="px-3 py-2 text-left font-medium">Category</th>
                          <th className="px-3 py-2 text-left font-medium">Status</th>
                          <th className="px-3 py-2"></th>
                        </tr>
                      </thead>
                      <tbody>
                        {pageItems.map((item) => (
                          <IncidentRow
                            key={item.signatureHash}
                            item={item}
                            selected={item.signatureHash === selectedHash}
                            onSelect={() => setSelectedHash(item.signatureHash)}
                            onKnowledge={() => goKnowledge(item)}
                            onReplay={() => goReplay(item)}
                          />
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}

                {filtered.length > 0 && (
                  <div className="flex items-center justify-between gap-3 px-4 py-3 border-t border-gray-100 text-xs text-gray-500">
                    <span>
                      Showing {(page - 1) * PAGE_SIZE + 1}-{Math.min(page * PAGE_SIZE, filtered.length)} of {filtered.length} incidents
                    </span>
                    <div className="flex items-center gap-1">
                      <button
                        onClick={() => setPage((p) => Math.max(1, p - 1))}
                        disabled={page === 1}
                        className="p-1.5 rounded-md border border-gray-200 disabled:opacity-40 hover:bg-gray-50"
                        aria-label="Previous page"
                      >
                        <ChevronLeft className="w-3.5 h-3.5" />
                      </button>
                      {Array.from({ length: totalPages }, (_, i) => i + 1)
                        .filter((p) => Math.abs(p - page) <= 2 || p === 1 || p === totalPages)
                        .map((p, idx, arr) => (
                          <span key={p} className="flex items-center">
                            {idx > 0 && arr[idx - 1] !== p - 1 && <span className="px-1 text-gray-300">…</span>}
                            <button
                              onClick={() => setPage(p)}
                              className={`w-7 h-7 rounded-md text-xs font-medium ${p === page ? 'bg-primary-600 text-white' : 'hover:bg-gray-100 text-gray-600'}`}
                            >
                              {p}
                            </button>
                          </span>
                        ))}
                      <button
                        onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                        disabled={page === totalPages}
                        className="p-1.5 rounded-md border border-gray-200 disabled:opacity-40 hover:bg-gray-50"
                        aria-label="Next page"
                      >
                        <ChevronRight className="w-3.5 h-3.5" />
                      </button>
                    </div>
                  </div>
                )}
              </div>

              {selectedItem && (
                <IncidentDetailPanel item={selectedItem} namespaces={namespaces} onClose={() => setSelectedHash(null)} />
              )}
            </div>
          </>
        )}
      </div>
    </div>
  );
}

export default FailureIntelligenceCenterPage;
