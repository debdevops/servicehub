import { useState, useMemo, useCallback, useEffect, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  Shield,
  RefreshCw,
  Download,
  Filter,
  X,
  CheckCircle,
  XCircle,
  AlertCircle,
  Users,
  Activity,
  ChevronLeft,
  ChevronRight,
  Search,
  Clock,
} from 'lucide-react';
import { useAuditLogs, useAuditSummary } from '@servicehub/ui-shared/hooks/useAudit';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { auditApi, type AuditLogItem, type AuditParams } from '@servicehub/ui-shared/lib/api/audit';
import { ProviderBadge } from '@servicehub/ui-shared/lib/providerStyles';
import { EnvironmentBadge } from '@/components/EnvironmentBadge';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';
import toast from 'react-hot-toast';
import { useFocusTrap } from '@servicehub/ui-shared/hooks/useFocusTrap';

// ─── Constants ────────────────────────────────────────────────────────────────

const OUTCOME_OPTIONS = ['Success', 'Failure', 'Partial'] as const;
const ACTION_TYPE_OPTIONS = [
  { label: 'All Actions', value: '' },
  { label: 'Messages', value: 'Messages' },
  { label: 'Rules', value: 'Rule' },
  { label: 'Namespaces', value: 'Namespace' },
  { label: 'DLQ', value: 'Dlq' },
];

const DATE_PRESETS = [
  { label: 'Last Hour', hours: 1 },
  { label: 'Last 24h', hours: 24 },
  { label: 'Last 7 Days', hours: 168 },
  { label: 'Last 30 Days', hours: 720 },
] as const;

// ─── Cloud / Environment Badges ──────────────────────────────────────────────

const KNOWN_PROVIDERS: readonly CloudProviderType[] = ['azure', 'aws', 'gcp'];

function CloudBadge({ provider }: { provider: string | null }) {
  if (!provider) return null;
  const normalized = provider.toLowerCase() as CloudProviderType;
  if (!KNOWN_PROVIDERS.includes(normalized)) return null;
  return <ProviderBadge provider={normalized} />;
}

function OutcomeBadge({ outcome }: { outcome: string }) {
  if (outcome === 'Success')
    return <span className="inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium rounded-full bg-green-100 text-green-700"><CheckCircle className="w-3 h-3" />Success</span>;
  if (outcome === 'Failure')
    return <span className="inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium rounded-full bg-red-100 text-red-700"><XCircle className="w-3 h-3" />Failure</span>;
  return <span className="inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium rounded-full bg-amber-100 text-amber-700"><AlertCircle className="w-3 h-3" />{outcome}</span>;
}

// ─── Stat Card ────────────────────────────────────────────────────────────────

function StatCard({
  label,
  value,
  sub,
  icon: Icon,
  colorClass,
}: {
  label: string;
  value: string | number;
  sub?: string;
  icon: React.ElementType;
  colorClass: string;
}) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 p-4 shadow-sm flex items-start gap-3">
      <div className={`p-2.5 rounded-lg ${colorClass}`}>
        <Icon className="w-5 h-5" />
      </div>
      <div>
        <p className="text-xs text-gray-500 font-medium">{label}</p>
        <p className="text-2xl font-bold text-gray-900 leading-tight">{value}</p>
        {sub && <p className="text-xs text-gray-400 mt-0.5">{sub}</p>}
      </div>
    </div>
  );
}

// ─── Detail Drawer ────────────────────────────────────────────────────────────

function AuditDetailDrawer({
  entry,
  onClose,
}: {
  entry: AuditLogItem | null;
  onClose: () => void;
}) {
  useEffect(() => {
    if (!entry) return;
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', handleEscape);
    return () => window.removeEventListener('keydown', handleEscape);
  }, [entry, onClose]);

  // Declared before the early return — hooks must run on every render.
  const dialogRef = useFocusTrap<HTMLDivElement>(!!entry);

  if (!entry) return null;

  const detailsObj = (() => {
    if (!entry.detailsJson) return null;
    try { return JSON.parse(entry.detailsJson); } catch { return null; }
  })();

  return (
    <>
      {/* Overlay */}
      <div
        className="fixed inset-0 bg-black/20 z-40"
        onClick={onClose}
        aria-hidden="true"
      />
      {/* Drawer */}
      <div
        className="fixed right-0 top-0 h-full w-full max-w-md bg-white z-50 shadow-2xl flex flex-col overflow-hidden"
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="audit-detail-drawer-title"
      >
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-gray-200 shrink-0">
          <div className="flex items-center gap-2">
            <Shield className="w-4 h-4 text-violet-600" />
            <h2 id="audit-detail-drawer-title" className="font-semibold text-gray-900 text-sm">Audit Entry Detail</h2>
          </div>
          <button
            onClick={onClose}
            className="p-1.5 rounded-lg hover:bg-gray-100 text-gray-500"
            aria-label="Close details"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto p-5 space-y-5">
          {/* Summary */}
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <OutcomeBadge outcome={entry.outcome} />
              <span className="text-xs text-gray-400">
                {new Date(entry.timestamp).toLocaleString()}
              </span>
            </div>
            <div>
              <p className="text-lg font-semibold text-gray-900">{entry.action}</p>
              <p className="text-sm text-gray-500">{entry.userIdentity}</p>
            </div>
            <div className="flex items-center gap-2 flex-wrap">
              <CloudBadge provider={entry.cloudProvider} />
              <EnvironmentBadge env={entry.environment} />
              {entry.namespaceName && (
                <span className="text-xs text-gray-500 bg-gray-100 px-1.5 py-0.5 rounded">
                  {entry.namespaceName}
                </span>
              )}
            </div>
          </div>

          {/* Resource info */}
          {(entry.entityName || entry.resourceName || entry.sequenceNumber) && (
            <section>
              <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">Resource</h3>
              <dl className="space-y-1">
                {entry.entityName && <Row label="Entity" value={entry.entityName} />}
                {entry.resourceName && <Row label="Resource" value={entry.resourceName} />}
                {entry.sequenceNumber != null && (
                  <Row label="Sequence #" value={String(entry.sequenceNumber)} />
                )}
              </dl>
            </section>
          )}

          {/* HTTP context */}
          <section>
            <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">HTTP Context</h3>
            <dl className="space-y-1">
              {entry.httpMethod && entry.httpPath && (
                <Row label="Request" value={`${entry.httpMethod} ${entry.httpPath}`} mono />
              )}
              {entry.correlationId && <Row label="Correlation ID" value={entry.correlationId} mono />}
              {entry.clientIp && <Row label="Client IP" value={entry.clientIp} mono />}
              {entry.userAgent && (
                <div className="py-1">
                  <dt className="text-xs text-gray-400">User Agent</dt>
                  <dd className="text-xs text-gray-700 font-mono break-all leading-relaxed mt-0.5">
                    {entry.userAgent}
                  </dd>
                </div>
              )}
            </dl>
          </section>

          {/* Details */}
          {detailsObj && (
            <section>
              <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">Details</h3>
              <pre className="bg-gray-50 border border-gray-200 rounded-lg p-3 text-xs font-mono overflow-x-auto text-gray-700 leading-relaxed">
                {JSON.stringify(detailsObj, null, 2)}
              </pre>
            </section>
          )}

          {/* Error details */}
          {entry.errorDetails && (
            <section>
              <h3 className="text-xs font-semibold text-red-500 uppercase tracking-wide mb-2">Error Details</h3>
              <pre className="bg-red-50 border border-red-200 rounded-lg p-3 text-xs font-mono overflow-x-auto text-red-700 leading-relaxed whitespace-pre-wrap">
                {entry.errorDetails}
              </pre>
            </section>
          )}
        </div>
      </div>
    </>
  );
}

function Row({
  label,
  value,
  mono = false,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="flex items-start gap-2 py-0.5">
      <dt className="text-xs text-gray-400 w-24 shrink-0">{label}</dt>
      <dd className={`text-xs text-gray-700 break-all ${mono ? 'font-mono' : ''}`}>{value}</dd>
    </div>
  );
}

// ─── Main Page ────────────────────────────────────────────────────────────────

export function AuditPage() {
  const [searchParams] = useSearchParams();
  const urlNamespaceId = searchParams.get('namespace') || undefined;

  const { data: namespaces } = useNamespaces();
  const activeNamespace = namespaces?.find(ns => ns.isActive);
  const namespaceId = urlNamespaceId || activeNamespace?.id;

  // ─── Filters ────────────────────────────────────────────────────────
  const [search, setSearch] = useState('');
  const [actionType, setActionType] = useState('');
  const [outcome, setOutcome] = useState('');
  const [selectedPreset, setSelectedPreset] = useState<number | null>(null);
  const [page, setPage] = useState(1);
  const [showFilters, setShowFilters] = useState(false);
  const [selectedEntry, setSelectedEntry] = useState<AuditLogItem | null>(null);
  const [showExportMenu, setShowExportMenu] = useState(false);
  const exportMenuRef = useRef<HTMLDivElement>(null);

  const pageSize = 50;

  // Close the export menu on outside click or Escape — it used to be CSS
  // hover-only, which made it unreachable for keyboard/touch users.
  useEffect(() => {
    if (!showExportMenu) return;
    const handleClickOutside = (e: MouseEvent) => {
      if (exportMenuRef.current && !exportMenuRef.current.contains(e.target as Node)) {
        setShowExportMenu(false);
      }
    };
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setShowExportMenu(false);
    };
    document.addEventListener('mousedown', handleClickOutside);
    window.addEventListener('keydown', handleEscape);
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      window.removeEventListener('keydown', handleEscape);
    };
  }, [showExportMenu]);

  // ─── Derive time range from preset ──────────────────────────────────
  const { from, to } = useMemo(() => {
    if (selectedPreset === null) return { from: undefined, to: undefined };
    const now = new Date();
    const f = new Date(now.getTime() - selectedPreset * 3_600_000);
    return { from: f.toISOString(), to: now.toISOString() };
  }, [selectedPreset]);

  // ─── Debounced search ────────────────────────────────────────────────
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const debounceRef = useState<ReturnType<typeof setTimeout> | null>(null);

  const handleSearchChange = useCallback((val: string) => {
    setSearch(val);
    if (debounceRef[0]) clearTimeout(debounceRef[0]);
    debounceRef[1](setTimeout(() => {
      setDebouncedSearch(val);
      setPage(1);
    }, 400));
  }, [debounceRef]);

  const params: AuditParams = useMemo(() => ({
    namespaceId,
    search: debouncedSearch || undefined,
    actionType: actionType || undefined,
    outcome: outcome || undefined,
    from,
    to,
    page,
    pageSize,
  }), [namespaceId, debouncedSearch, actionType, outcome, from, to, page]);

  const { data, isLoading, isError, refetch, isFetching } = useAuditLogs(params, true);
  const { data: summary } = useAuditSummary(namespaceId);

  const activeFilters = [actionType, outcome, selectedPreset !== null ? 'date' : ''].filter(Boolean).length;

  const handleClearFilters = () => {
    setActionType('');
    setOutcome('');
    setSelectedPreset(null);
    setSearch('');
    setDebouncedSearch('');
    setPage(1);
  };

  const handleExport = async (format: 'csv' | 'json') => {
    try {
      await auditApi.downloadExport(format, {
        namespaceId,
        actionType: actionType || undefined,
        outcome: outcome || undefined,
        from,
        to,
      });
      toast.success(`Audit logs exported as ${format.toUpperCase()}`);
    } catch {
      toast.error('Export failed. Please try again.');
    }
  };

  const handleRefresh = () => {
    refetch();
    toast.success('Audit logs refreshed');
  };

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      {/* ── Header ── */}
      <div className="bg-white border-b border-gray-200 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between mb-3">
          <div>
            <h1 className="text-xl font-bold text-gray-900 flex items-center gap-2">
              <Shield className="w-5 h-5 text-violet-600" />
              Audit Trail
            </h1>
            <p className="text-sm text-gray-500 mt-0.5">
              Persistent record of all critical operations and access events
            </p>
          </div>
          <div className="flex items-center gap-2">
            {/* Export dropdown */}
            <div className="relative" ref={exportMenuRef}>
              <button
                onClick={() => setShowExportMenu(v => !v)}
                aria-haspopup="menu"
                aria-expanded={showExportMenu}
                className="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 shadow-sm transition-all focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
              >
                <Download className="w-4 h-4" />
                Export
              </button>
              {showExportMenu && (
                <div role="menu" className="absolute right-0 top-full mt-1 w-36 bg-white border border-gray-200 rounded-lg shadow-lg z-10">
                  <button
                    role="menuitem"
                    onClick={() => { handleExport('csv'); setShowExportMenu(false); }}
                    className="w-full text-left px-3 py-2 text-sm text-gray-700 hover:bg-gray-50 rounded-t-lg"
                  >
                    Export as CSV
                  </button>
                  <button
                    role="menuitem"
                    onClick={() => { handleExport('json'); setShowExportMenu(false); }}
                    className="w-full text-left px-3 py-2 text-sm text-gray-700 hover:bg-gray-50 rounded-b-lg"
                  >
                    Export as JSON
                  </button>
                </div>
              )}
            </div>

            {/* Filter toggle */}
            <button
              onClick={() => setShowFilters(v => !v)}
              className={`flex items-center gap-1.5 px-3 py-2 text-sm font-medium rounded-lg border shadow-sm transition-all ${
                showFilters || activeFilters > 0
                  ? 'bg-violet-50 text-violet-700 border-violet-300'
                  : 'bg-white text-gray-700 border-gray-200 hover:bg-gray-50'
              }`}
            >
              <Filter className="w-4 h-4" />
              Filters
              {activeFilters > 0 && (
                <span className="ml-1 text-xs bg-violet-600 text-white rounded-full w-4 h-4 flex items-center justify-center">
                  {activeFilters}
                </span>
              )}
            </button>

            {/* Refresh */}
            <button
              onClick={handleRefresh}
              disabled={isFetching}
              className="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 shadow-sm transition-all disabled:opacity-50 focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
            >
              <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
              Refresh
            </button>
          </div>
        </div>

        {/* ── Summary Stats ── */}
        {summary && (
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 mt-3">
            <StatCard
              label="Total Events"
              value={summary.totalEvents.toLocaleString()}
              icon={Activity}
              colorClass="bg-violet-50 text-violet-600"
            />
            <StatCard
              label="Success Rate"
              value={`${summary.successRate}%`}
              sub={`${summary.successCount.toLocaleString()} successful`}
              icon={CheckCircle}
              colorClass="bg-green-50 text-green-600"
            />
            <StatCard
              label="Failures"
              value={summary.failureCount.toLocaleString()}
              icon={XCircle}
              colorClass="bg-red-50 text-red-600"
            />
            <StatCard
              label="Active Users"
              value={summary.activeUsers.toLocaleString()}
              icon={Users}
              colorClass="bg-sky-50 text-sky-600"
            />
          </div>
        )}
      </div>

      {/* ── Filter Bar ── */}
      {showFilters && (
        <div className="bg-gray-50 border-b border-gray-200 px-6 py-3 shrink-0">
          <div className="flex flex-wrap items-center gap-3">
            {/* Action type */}
            <select
              value={actionType}
              onChange={e => { setActionType(e.target.value); setPage(1); }}
              className="text-sm border border-gray-200 rounded-lg px-3 py-1.5 bg-white text-gray-700 focus:outline-none focus:ring-2 focus:ring-violet-500"
            >
              {ACTION_TYPE_OPTIONS.map(o => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </select>

            {/* Outcome */}
            <select
              value={outcome}
              onChange={e => { setOutcome(e.target.value); setPage(1); }}
              className="text-sm border border-gray-200 rounded-lg px-3 py-1.5 bg-white text-gray-700 focus:outline-none focus:ring-2 focus:ring-violet-500"
            >
              <option value="">All Outcomes</option>
              {OUTCOME_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
            </select>

            {/* Date presets */}
            <div className="flex items-center gap-1">
              <Clock className="w-4 h-4 text-gray-400" />
              {DATE_PRESETS.map(p => (
                <button
                  key={p.hours}
                  onClick={() => { setSelectedPreset(prev => prev === p.hours ? null : p.hours); setPage(1); }}
                  className={`text-xs px-2.5 py-1 rounded-md border transition-all ${
                    selectedPreset === p.hours
                      ? 'bg-violet-600 text-white border-violet-600'
                      : 'bg-white text-gray-600 border-gray-200 hover:border-violet-300'
                  }`}
                >
                  {p.label}
                </button>
              ))}
            </div>

            {/* Clear */}
            {activeFilters > 0 && (
              <button
                onClick={handleClearFilters}
                className="flex items-center gap-1 text-xs text-gray-500 hover:text-gray-700 ml-auto"
              >
                <X className="w-3 h-3" />
                Clear filters
              </button>
            )}
          </div>
        </div>
      )}

      {/* ── Search ── */}
      <div className="bg-white border-b border-gray-100 px-6 py-2.5 shrink-0">
        <div className="relative max-w-md">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input
            type="text"
            value={search}
            onChange={e => handleSearchChange(e.target.value)}
            placeholder="Search by user, action, or resource…"
            className="w-full pl-9 pr-4 py-2 text-sm border border-gray-200 rounded-lg bg-gray-50 text-gray-700 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-primary-500 focus:bg-white transition-all"
          />
          {search && (
            <button
              onClick={() => handleSearchChange('')}
              aria-label="Clear search"
              className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
            >
              <X className="w-3.5 h-3.5" />
            </button>
          )}
        </div>
      </div>

      {/* ── Table ── */}
      <div className="flex-1 overflow-auto">
        {isLoading ? (
          <div className="flex items-center justify-center h-64">
            <div className="flex flex-col items-center gap-3">
              <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600" />
              <p className="text-sm text-gray-500">Loading audit logs…</p>
            </div>
          </div>
        ) : isError ? (
          <div className="flex items-center justify-center h-64">
            <div className="text-center">
              <AlertCircle className="w-10 h-10 text-red-400 mx-auto mb-3" />
              <p className="text-gray-600 font-medium">Failed to load audit logs</p>
              <button
                onClick={() => refetch()}
                className="mt-3 px-4 py-2 text-sm text-primary-600 hover:text-primary-700 border border-primary-300 rounded-lg hover:bg-primary-50 transition-colors focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
              >
                Try Again
              </button>
            </div>
          </div>
        ) : !data || data.items.length === 0 ? (
          <div className="flex items-center justify-center h-64">
            <div className="text-center">
              <Shield className="w-10 h-10 text-gray-300 mx-auto mb-3" />
              <p className="text-gray-500 font-medium">No audit logs found</p>
              <p className="text-sm text-gray-400 mt-1">
                {activeFilters > 0 ? 'Try adjusting your filters' : 'Audit events will appear here as operations are performed'}
              </p>
            </div>
          </div>
        ) : (
          <table className="w-full text-sm" aria-label="Audit trail events">
            <thead className="bg-gray-50 border-b border-gray-200 sticky top-0 z-10">
              <tr>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500 w-40">Timestamp</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500">User</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500">Cloud / Env</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500">Action</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500">Resource</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500 w-24">Outcome</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {data.items.map(entry => (
                <tr
                  key={entry.id}
                  onClick={() => setSelectedEntry(entry)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      setSelectedEntry(entry);
                    }
                  }}
                  role="button"
                  tabIndex={0}
                  className="hover:bg-violet-50/40 cursor-pointer transition-colors group focus:outline-none focus:ring-2 focus:ring-inset focus:ring-violet-500"
                >
                  <td className="px-4 py-3 text-gray-500 whitespace-nowrap font-mono text-xs">
                    {new Date(entry.timestamp).toLocaleString(undefined, {
                      month: 'short', day: '2-digit',
                      hour: '2-digit', minute: '2-digit', second: '2-digit',
                    })}
                  </td>
                  <td className="px-4 py-3">
                    <span className="text-gray-800 font-medium text-xs">{entry.userIdentity}</span>
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-1 flex-wrap">
                      <CloudBadge provider={entry.cloudProvider} />
                      <EnvironmentBadge env={entry.environment} />
                    </div>
                  </td>
                  <td className="px-4 py-3">
                    <span className="font-medium text-gray-800">{entry.action}</span>
                    {entry.entityName && (
                      <span className="text-gray-400 text-xs ml-1.5 font-mono">{entry.entityName}</span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-gray-500 font-mono text-xs truncate max-w-[200px]">
                    {entry.resourceName || entry.correlationId || '—'}
                  </td>
                  <td className="px-4 py-3">
                    <OutcomeBadge outcome={entry.outcome} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* ── Pagination ── */}
      {data && data.totalCount > 0 && (
        <div className="bg-white border-t border-gray-200 px-6 py-3 shrink-0 flex items-center justify-between">
          <p className="text-xs text-gray-500">
            Showing <span className="font-medium">{(page - 1) * pageSize + 1}–{Math.min(page * pageSize, data.totalCount)}</span> of{' '}
            <span className="font-medium">{data.totalCount.toLocaleString()}</span> events
          </p>
          <div className="flex items-center gap-1">
            <button
              onClick={() => setPage(p => Math.max(p - 1, 1))}
              disabled={!data.hasPreviousPage}
              className="p-1.5 rounded-lg border border-gray-200 hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
            >
              <ChevronLeft className="w-4 h-4 text-gray-600" />
            </button>
            <span className="text-xs text-gray-600 px-2">
              Page {page} of {Math.max(1, Math.ceil(data.totalCount / data.pageSize))}
            </span>
            <button
              onClick={() => setPage(p => p + 1)}
              disabled={!data.hasNextPage}
              className="p-1.5 rounded-lg border border-gray-200 hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
            >
              <ChevronRight className="w-4 h-4 text-gray-600" />
            </button>
          </div>
        </div>
      )}

      {/* ── Detail Drawer ── */}
      {selectedEntry && (
        <AuditDetailDrawer
          entry={selectedEntry}
          onClose={() => setSelectedEntry(null)}
        />
      )}
    </div>
  );
}
