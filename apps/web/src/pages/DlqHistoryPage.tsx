import { useState, useMemo } from 'react';
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
import toast from 'react-hot-toast';

const STATUS_OPTIONS = ['Active', 'Replayed', 'Archived', 'Discarded', 'ReplayFailed'] as const;
const CATEGORY_OPTIONS = [
  'Unknown', 'Transient', 'MaxDelivery', 'Expired', 'DataQuality',
  'Authorization', 'ProcessingError', 'ResourceNotFound', 'QuotaExceeded',
] as const;

export function DlqHistoryPage() {
  const [searchParams] = useSearchParams();
  const namespaceId = searchParams.get('namespace') || undefined;

  const { data: namespaces } = useNamespaces();
  const currentNamespace = namespaces?.find(ns => ns.id === namespaceId);

  // Filters
  const [statusFilter, setStatusFilter] = useState<string | undefined>();
  const [categoryFilter, setCategoryFilter] = useState<string | undefined>();
  const [entityFilter, setEntityFilter] = useState('');
  const [page, setPage] = useState(1);
  const [showFilters, setShowFilters] = useState(false);
  const [selectedTimelineId, setSelectedTimelineId] = useState<number | null>(null);

  const pageSize = 50;

  const params = useMemo(() => ({
    namespaceId,
    entityName: entityFilter || undefined,
    status: statusFilter,
    category: categoryFilter,
    page,
    pageSize,
  }), [namespaceId, entityFilter, statusFilter, categoryFilter, page]);

  const { data, isLoading, refetch, isFetching } = useDlqHistory(params);
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

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      {/* Header */}
      <div className="bg-white border-b border-gray-200 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between mb-3">
          <div>
            <h1 className="text-xl font-bold text-gray-900">DLQ Intelligence</h1>
            <p className="text-sm text-gray-500 mt-0.5">
              Dead-letter queue message history and monitoring
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
