import { useState, useMemo, useEffect } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  RefreshCw,
  Download,
  Filter,
  X,
  AlertCircle,
  Archive,
  BarChart3,
} from 'lucide-react';
import { DlqHistoryTable, DlqTimelineDrawer } from '@/components/dlq';
import { useDlqHistory, useDlqSummary } from '@/hooks/useDlqHistory';
import { useNamespaces } from '@/hooks/useNamespaces';
import { dlqHistoryApi } from '@/lib/api/dlqHistory';
import { HelpTooltip } from '@/components/help';
import { tooltips } from '@/lib/helpContent';
import type { ForensicBatchSummary } from '@/lib/api/dlqHistory';
import toast from 'react-hot-toast';
import { Zap, Shield } from 'lucide-react';

// ─── Inline Trend Chart (pure SVG, no chart library) ───────────────

interface TrendPoint {
  date: string;
  newMessages: number;
  resolvedMessages: number;
}

function TrendChart({ trend }: { trend: TrendPoint[] }) {
  if (!trend || trend.length === 0) return null;

  const maxVal = Math.max(...trend.map(t => Math.max(t.newMessages, t.resolvedMessages)), 1);
  const barWidth = 100 / trend.length;

  return (
    <div className="bg-white rounded-xl border border-gray-200 p-4 mb-3">
      <div className="flex items-center justify-between mb-3">
        <h3 className="text-sm font-semibold text-gray-700">
          30-Day DLQ Trend
          <HelpTooltip {...tooltips.dlqHistory.trendChart} position="right" className="ml-1" />
        </h3>
        <div className="flex items-center gap-3 text-xs text-gray-500">
          <span className="flex items-center gap-1">
            <span className="w-3 h-3 rounded bg-red-400 inline-block" />
            New
          </span>
          <span className="flex items-center gap-1">
            <span className="w-3 h-3 rounded bg-green-400 inline-block" />
            Resolved
          </span>
        </div>
      </div>
      <div className="relative h-24">
        <svg
          viewBox="0 0 100 100"
          preserveAspectRatio="none"
          className="w-full h-full"
        >
          {trend.map((point, i) => {
            const newH = (point.newMessages / maxVal) * 80;
            const resH = (point.resolvedMessages / maxVal) * 80;
            const x = i * barWidth;
            return (
              <g key={point.date}>
                <rect
                  x={x + barWidth * 0.05}
                  y={100 - newH}
                  width={barWidth * 0.42}
                  height={newH}
                  fill="#f87171"
                  rx="1"
                  opacity="0.85"
                />
                <rect
                  x={x + barWidth * 0.52}
                  y={100 - resH}
                  width={barWidth * 0.42}
                  height={resH}
                  fill="#4ade80"
                  rx="1"
                  opacity="0.85"
                />
              </g>
            );
          })}
        </svg>
      </div>
      <div className="flex justify-between mt-1">
        {trend.map((point, i) => {
          if (i !== 0 && i !== Math.floor(trend.length / 2) && i !== trend.length - 1) return null;
          const date = new Date(point.date);
          return (
            <span key={point.date} className="text-xs text-gray-400">
              {date.toLocaleDateString('en', { month: 'short', day: 'numeric' })}
            </span>
          );
        })}
      </div>
    </div>
  );
}

// ─── Constants ──────────────────────────────────────────────────────

const STATUS_OPTIONS = ['Active', 'Replayed', 'Archived', 'Discarded', 'ReplayFailed', 'Resolved'] as const;
const CATEGORY_OPTIONS = [
  'Unknown', 'Transient', 'MaxDelivery', 'Expired', 'DataQuality',
  'Authorization', 'ProcessingError', 'ResourceNotFound', 'QuotaExceeded',
] as const;

export function DlqHistoryPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const urlNamespaceId = searchParams.get('namespace') || undefined;

  const { data: namespaces } = useNamespaces();

  // Auto-select active namespace when URL param is missing or stale
  const activeNamespace = namespaces?.find(ns => ns.isActive);
  const namespaceId = urlNamespaceId || activeNamespace?.id;
  const currentNamespace = namespaces?.find(ns => ns.id === namespaceId);

  // Sync URL with resolved namespace ID so bookmarks / refreshes work
  useEffect(() => {
    if (namespaceId && namespaceId !== urlNamespaceId && namespaces) {
      setSearchParams({ namespace: namespaceId }, { replace: true });
    }
  }, [namespaceId, urlNamespaceId, namespaces, setSearchParams]);

  // Filters
  const [statusFilter, setStatusFilter] = useState<string | undefined>();
  const [categoryFilter, setCategoryFilter] = useState<string | undefined>();
  const [entityFilter, setEntityFilter] = useState('');
  const [page, setPage] = useState(1);
  const [showFilters, setShowFilters] = useState(false);
  const [selectedTimelineId, setSelectedTimelineId] = useState<number | null>(null);
  const [isScanning, setIsScanning] = useState(false);
  const [isAnalysing, setIsAnalysing] = useState(false);
  const [batchSummary, setBatchSummary] = useState<ForensicBatchSummary | null>(null);

  const pageSize = 50;

  const params = useMemo(() => ({
    namespaceId,
    entityName: entityFilter || undefined,
    status: statusFilter,
    category: categoryFilter,
    page,
    pageSize,
  }), [namespaceId, entityFilter, statusFilter, categoryFilter, page]);

  const { data, isLoading, refetch, isFetching } = useDlqHistory(params, !!namespaceId);
  const { data: summary } = useDlqSummary(namespaceId);

  const activeFilters = [statusFilter, categoryFilter, entityFilter].filter(Boolean).length;

  const handleClearFilters = () => {
    setStatusFilter(undefined);
    setCategoryFilter(undefined);
    setEntityFilter('');
    setPage(1);
  };

  const handleExport = (format: 'json' | 'csv') => {
    const url = dlqHistoryApi.getExportUrl(format, params);
    window.open(url, '_blank');
    toast.success(`Exporting DLQ messages as ${format.toUpperCase()}`);
  };

  const handleRefresh = () => {
    refetch();
    toast.success('DLQ history refreshed');
  };

  const handleScanNow = async () => {
    if (!namespaceId || isScanning) return;
    setIsScanning(true);
    try {
      const newCount = await dlqHistoryApi.triggerScan(namespaceId);
      // Brief delay to let the DB commit settle, then refetch
      setTimeout(() => {
        refetch();
        setIsScanning(false);
        toast.success(newCount > 0 ? `Found ${newCount} new DLQ message(s)` : 'No new DLQ messages');
      }, 1500);
    } catch (error) {
      if (import.meta.env.DEV) console.error('Scan failed:', error);
      toast.error('DLQ scan failed');
      setIsScanning(false);
    }
  };

  const handleAnalyseAll = async () => {
    if (!namespaceId || isAnalysing) return;
    setIsAnalysing(true);
    setBatchSummary(null);
    try {
      const result = await dlqHistoryApi.analyseBatch(namespaceId);
      setBatchSummary(result);
      refetch();
      toast.success(`Analysed ${result.analysed} messages, updated ${result.updated}`);
    } catch (error) {
      if (import.meta.env.DEV) console.error('Batch analysis failed:', error);
      toast.error('Batch forensic analysis failed');
    } finally {
      setIsAnalysing(false);
    }
  };

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      {/* Header */}
      <div className="bg-white border-b border-gray-200 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between mb-3">
          <div>
            <h1 className="text-xl font-bold text-gray-900">DLQ Intelligence</h1>
            <p className="text-sm text-gray-500 mt-0.5">
              Dead-letter queue message history and monitoring
              <HelpTooltip {...tooltips.dlqHistory.trendChart} position="right" className="ml-1" />
              {currentNamespace && (
                <span className="text-primary-600 ml-1">
                  — {currentNamespace.displayName || currentNamespace.name}
                </span>
              )}
            </p>
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={() => handleExport('csv')}
              className="flex items-center gap-1.5 px-3 py-2 border border-gray-200 rounded-lg text-sm text-gray-700 hover:bg-gray-50 transition-colors"
              title="Export as CSV"
            >
              <Download className="w-4 h-4" />
              CSV
            </button>
            <button
              onClick={() => handleExport('json')}
              className="flex items-center gap-1.5 px-3 py-2 border border-gray-200 rounded-lg text-sm text-gray-700 hover:bg-gray-50 transition-colors"
              title="Export as JSON"
            >
              <Download className="w-4 h-4" />
              JSON
            </button>
            <button
              onClick={handleScanNow}
              disabled={isScanning}
              className="flex items-center gap-1.5 px-3 py-2 border border-amber-300 bg-amber-50 hover:bg-amber-100 text-amber-700 rounded-lg text-sm font-medium transition-colors disabled:opacity-60"
              title="Instantly scan DLQs for new messages"
            >
              <Zap className={`w-4 h-4 ${isScanning ? 'animate-pulse' : ''}`} />
              {isScanning ? 'Scanning...' : 'Scan Now'}
            </button>
            <button
              onClick={handleAnalyseAll}
              disabled={isAnalysing}
              className="flex items-center gap-1.5 px-3 py-2 border border-purple-300 bg-purple-50 hover:bg-purple-100 text-purple-700 rounded-lg text-sm font-medium transition-colors disabled:opacity-60"
              title="Run forensic analysis on all active DLQ messages"
            >
              <Shield className={`w-4 h-4 ${isAnalysing ? 'animate-pulse' : ''}`} />
              {isAnalysing ? 'Analysing...' : 'Analyse All'}
            </button>
            <button
              onClick={handleRefresh}
              disabled={isFetching}
              className="flex items-center gap-1.5 px-3 py-2 bg-primary-500 hover:bg-primary-600 text-white rounded-lg text-sm font-medium transition-colors disabled:opacity-60"
            >
              <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
              Refresh
            </button>
          </div>
        </div>

        {/* Summary Cards */}
        {summary && (
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 mb-3">
            <SummaryCard
              icon={<AlertCircle className="w-5 h-5 text-red-500" />}
              label="Active"
              value={summary.activeMessages}
              bg="bg-red-50"
            />
            <SummaryCard
              icon={<RefreshCw className="w-5 h-5 text-green-500" />}
              label="Replayed"
              value={summary.replayedMessages}
              bg="bg-green-50"
            />
            <SummaryCard
              icon={<Archive className="w-5 h-5 text-gray-500" />}
              label="Archived"
              value={summary.archivedMessages}
              bg="bg-gray-50"
            />
            <SummaryCard
              icon={<BarChart3 className="w-5 h-5 text-primary-500" />}
              label="Total"
              value={summary.totalMessages}
              bg="bg-primary-50"
            />
          </div>
        )}

        {/* DLQ Trend Chart */}
        {summary?.dailyTrend && summary.dailyTrend.length > 0 && (
          <TrendChart trend={summary.dailyTrend} />
        )}

        {/* Batch Forensic Summary */}
        {batchSummary && (
          <div className="bg-purple-50 border border-purple-200 rounded-xl p-3 mb-3">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Shield className="w-4 h-4 text-purple-600" />
                <span className="text-sm font-medium text-purple-800">
                  Forensic Analysis: {batchSummary.analysed} analysed, {batchSummary.updated} updated
                </span>
              </div>
              <div className="flex items-center gap-2 text-xs text-purple-600">
                {Object.entries(batchSummary.byCategory).map(([cat, count]) => (
                  <span key={cat} className="bg-purple-100 px-2 py-0.5 rounded-full">
                    {cat}: {count}
                  </span>
                ))}
              </div>
            </div>
          </div>
        )}

        {/* Filter Bar */}
        <div className="flex items-center gap-2 flex-wrap">
          <button
            onClick={() => setShowFilters(!showFilters)}
            className={`flex items-center gap-1.5 px-3 py-2 border rounded-lg text-sm transition-colors ${
              showFilters || activeFilters > 0
                ? 'border-primary-300 bg-primary-50 text-primary-700'
                : 'border-gray-200 hover:bg-gray-50 text-gray-700'
            }`}
          >
            <Filter className="w-4 h-4" />
            Filters
            {activeFilters > 0 && (
              <span className="bg-primary-500 text-white text-xs px-1.5 py-0.5 rounded-full font-medium">
                {activeFilters}
              </span>
            )}
          </button>

          {activeFilters > 0 && (
            <button
              onClick={handleClearFilters}
              className="flex items-center gap-1 text-sm text-gray-500 hover:text-gray-700"
            >
              <X className="w-3.5 h-3.5" />
              Clear all
            </button>
          )}

          {statusFilter && (
            <FilterChip label={`Status: ${statusFilter}`} onRemove={() => { setStatusFilter(undefined); setPage(1); }} />
          )}
          {categoryFilter && (
            <FilterChip label={`Category: ${categoryFilter}`} onRemove={() => { setCategoryFilter(undefined); setPage(1); }} />
          )}
          {entityFilter && (
            <FilterChip label={`Entity: ${entityFilter}`} onRemove={() => { setEntityFilter(''); setPage(1); }} />
          )}
        </div>

        {/* Filter Panel */}
        {showFilters && (
          <div className="mt-3 p-4 bg-gray-50 rounded-xl border border-gray-200 grid grid-cols-1 sm:grid-cols-3 gap-4">
            <div>
              <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">Status</label>
              <select
                value={statusFilter || ''}
                onChange={(e) => { setStatusFilter(e.target.value || undefined); setPage(1); }}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm bg-white focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
              >
                <option value="">All</option>
                {STATUS_OPTIONS.map(s => <option key={s} value={s}>{s}</option>)}
              </select>
            </div>
            <div>
              <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">Category</label>
              <select
                value={categoryFilter || ''}
                onChange={(e) => { setCategoryFilter(e.target.value || undefined); setPage(1); }}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm bg-white focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
              >
                <option value="">All</option>
                {CATEGORY_OPTIONS.map(c => <option key={c} value={c}>{c}</option>)}
              </select>
            </div>
            <div>
              <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">Entity Name</label>
              <input
                type="text"
                value={entityFilter}
                onChange={(e) => { setEntityFilter(e.target.value); setPage(1); }}
                placeholder="Filter by entity name..."
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm bg-white focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
              />
            </div>
          </div>
        )}
      </div>

      {/* Table */}
      <div className="flex-1 overflow-y-auto p-6">
        <DlqHistoryTable
          items={data?.items ?? []}
          totalCount={data?.totalCount ?? 0}
          page={page}
          pageSize={pageSize}
          hasNextPage={data?.hasNextPage ?? false}
          hasPreviousPage={data?.hasPreviousPage ?? false}
          isLoading={isLoading}
          onPageChange={setPage}
          onViewTimeline={setSelectedTimelineId}
        />
      </div>

      {/* Timeline Drawer */}
      <DlqTimelineDrawer
        messageId={selectedTimelineId}
        onClose={() => setSelectedTimelineId(null)}
      />
    </div>
  );
}

// ─── Helper Components ─────────────────────────────────────────────

function SummaryCard({
  icon,
  label,
  value,
  bg,
}: {
  icon: React.ReactNode;
  label: string;
  value: number;
  bg: string;
}) {
  return (
    <div className={`${bg} rounded-xl p-3 flex items-center gap-3`}>
      {icon}
      <div>
        <div className="text-2xl font-bold text-gray-900">{value.toLocaleString()}</div>
        <div className="text-xs text-gray-500 font-medium">{label}</div>
      </div>
    </div>
  );
}

function FilterChip({ label, onRemove }: { label: string; onRemove: () => void }) {
  return (
    <span className="inline-flex items-center gap-1 px-2.5 py-1 bg-primary-100 text-primary-700 rounded-full text-xs font-medium">
      {label}
      <button onClick={onRemove} className="hover:text-primary-900">
        <X className="w-3 h-3" />
      </button>
    </span>
  );
}
