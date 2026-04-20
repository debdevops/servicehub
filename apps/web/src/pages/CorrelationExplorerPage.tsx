import { useState, useEffect, useRef, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  GitMerge,
  Search,
  AlertTriangle,
  Clock,
  CheckCircle,
  Download,
  Filter,
  ChevronDown,
  ChevronUp,
  Database,
  Radio,
} from 'lucide-react';
import { useCorrelationSearch } from '@/hooks/useCorrelation';
import { useNamespaces } from '@/hooks/useNamespaces';
import { CopyButton } from '@/components/CopyButton';
import type { CorrelationTimelineEntry, CorrelationTimelineResponse } from '@/lib/api/types';

// ============================================================================
// State badge helpers
// ============================================================================

type StateColor = { bg: string; text: string; dot: string };

function getStateColor(state: string): StateColor {
  switch (state) {
    case 'Active':
      return { bg: 'bg-emerald-100', text: 'text-emerald-700', dot: 'bg-emerald-500' };
    case 'Scheduled':
      return { bg: 'bg-sky-100', text: 'text-sky-700', dot: 'bg-sky-500' };
    case 'DeadLettered':
      return { bg: 'bg-red-100', text: 'text-red-700', dot: 'bg-red-500' };
    case 'Replayed':
      return { bg: 'bg-amber-100', text: 'text-amber-700', dot: 'bg-amber-500' };
    case 'Resolved':
      return { bg: 'bg-gray-100', text: 'text-gray-600', dot: 'bg-gray-400' };
    case 'Deferred':
      return { bg: 'bg-purple-100', text: 'text-purple-700', dot: 'bg-purple-500' };
    default:
      return { bg: 'bg-gray-100', text: 'text-gray-600', dot: 'bg-gray-400' };
  }
}

function formatTimestamp(ts: string): string {
  try {
    return new Date(ts).toLocaleString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  } catch {
    return ts;
  }
}

/** Detect if entity path looks like a topic subscription */
function getEntityType(entry: CorrelationTimelineEntry): 'Queue' | 'Topic/Sub' {
  return entry.entityPath?.includes('/subscriptions/') ? 'Topic/Sub' : 'Queue';
}

// ============================================================================
// Time range options
// ============================================================================

type TimeRange = '1h' | '6h' | '24h' | '7d' | 'all';
const TIME_RANGE_OPTIONS: { label: string; value: TimeRange }[] = [
  { label: 'Last 1 hour', value: '1h' },
  { label: 'Last 6 hours', value: '6h' },
  { label: 'Last 24 hours', value: '24h' },
  { label: 'Last 7 days', value: '7d' },
  { label: 'All time', value: 'all' },
];

function filterByTimeRange(entries: CorrelationTimelineEntry[], range: TimeRange): CorrelationTimelineEntry[] {
  if (range === 'all') return entries;
  const now = Date.now();
  const msMap: Record<TimeRange, number> = {
    '1h': 60 * 60 * 1000,
    '6h': 6 * 60 * 60 * 1000,
    '24h': 24 * 60 * 60 * 1000,
    '7d': 7 * 24 * 60 * 60 * 1000,
    all: Infinity,
  };
  const cutoff = now - msMap[range];
  return entries.filter((e) => new Date(e.timestamp).getTime() >= cutoff);
}

// ============================================================================
// Export helpers
// ============================================================================

function exportAsJson(result: CorrelationTimelineResponse) {
  const blob = new Blob([JSON.stringify(result, null, 2)], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `correlation-${result.correlationId}-${new Date().toISOString().slice(0, 19).replace(/:/g, '-')}.json`;
  a.click();
  URL.revokeObjectURL(url);
}

// ============================================================================
// Duration between events
// ============================================================================

function formatDuration(ms: number): string {
  if (ms < 1_000) return `${ms}ms`;
  const sec = Math.floor(ms / 1_000);
  if (sec < 60) return `${sec}s`;
  const min = Math.floor(sec / 60);
  if (min < 60) return `${min}m ${sec % 60}s`;
  const hr = Math.floor(min / 60);
  if (hr < 24) return `${hr}h ${min % 60}m`;
  return `${Math.floor(hr / 24)}d ${hr % 24}h`;
}

function DurationConnector({ fromTs, toTs }: { fromTs: string; toTs: string }) {
  const ms = new Date(toTs).getTime() - new Date(fromTs).getTime();
  if (ms <= 0) return null;
  return (
    <div className="flex items-center gap-2 ml-8 my-1 text-xs text-gray-400 select-none">
      <div className="h-px flex-1 border-l-0 border-t border-dashed border-gray-300" />
      <span className="shrink-0 px-2 py-0.5 rounded-full bg-gray-100 text-gray-500 font-mono">
        +{formatDuration(ms)}
      </span>
      <div className="h-px flex-1 border-r-0 border-t border-dashed border-gray-300" />
    </div>
  );
}

// ============================================================================
// Horizontal timeline minimap
// ============================================================================

function TimelineMinimap({ entries }: { entries: CorrelationTimelineEntry[] }) {
  if (entries.length < 2) return null;

  const timestamps = entries.map(e => new Date(e.timestamp).getTime());
  const minT = Math.min(...timestamps);
  const maxT = Math.max(...timestamps);
  const span = maxT - minT || 1;

  // SVG dimensions
  const W = 100; // viewBox units (percent)
  const H = 28;
  const RAIL_Y = 14;
  const DOT_R = 3.5;

  return (
    <div className="mb-5 bg-white border border-gray-200 rounded-xl px-5 py-3 shadow-sm">
      <div className="flex items-center justify-between mb-1.5">
        <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide">
          Timeline Minimap · {entries.length} event{entries.length !== 1 ? 's' : ''}
        </span>
        <span className="text-xs text-gray-400 font-mono">
          {formatDuration(span)} total span
        </span>
      </div>
      <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" className="w-full" style={{ height: H }}>
        {/* Rail */}
        <line x1="2" y1={RAIL_Y} x2="98" y2={RAIL_Y} stroke="#e5e7eb" strokeWidth="1.5" />
        {/* Events */}
        {entries.map((entry, i) => {
          const t = new Date(entry.timestamp).getTime();
          const pct = 2 + ((t - minT) / span) * 96;
          const { dot } = getStateColor(entry.state);
          // Convert tailwind dot class to fill color approximation
          const fillMap: Record<string, string> = {
            'bg-emerald-500': '#10b981',
            'bg-sky-500': '#0ea5e9',
            'bg-red-500': '#ef4444',
            'bg-amber-500': '#f59e0b',
            'bg-gray-400': '#9ca3af',
            'bg-purple-500': '#a855f7',
          };
          const fill = fillMap[dot] ?? '#9ca3af';
          return (
            <g key={i}>
              <circle cx={pct} cy={RAIL_Y} r={DOT_R} fill={fill} opacity="0.9" />
            </g>
          );
        })}
      </svg>
      <div className="flex justify-between text-[10px] text-gray-400 font-mono mt-0.5">
        <span>{formatTimestamp(entries[0].timestamp).split(',')[0]}</span>
        <span>{formatTimestamp(entries[entries.length - 1].timestamp).split(',')[0]}</span>
      </div>
    </div>
  );
}

// ============================================================================
// Timeline Entry Card with body expand
// ============================================================================

function TimelineEntryCard({
  entry,
  isLast,
  index,
}: {
  entry: CorrelationTimelineEntry;
  isLast: boolean;
  index: number;
}) {
  const stateColor = getStateColor(entry.state);
  const [expanded, setExpanded] = useState(false);
  const entityType = getEntityType(entry);

  return (
    <div className="flex gap-4">
      {/* Left column: index + dot + connector line */}
      <div className="flex flex-col items-center shrink-0 w-8">
        <span className="text-xs font-bold text-gray-400 text-center leading-none mb-1 pt-3">
          {index + 1}
        </span>
        <div className={`w-3 h-3 rounded-full shrink-0 ${stateColor.dot}`} />
        {!isLast && <div className="w-0.5 flex-1 bg-gray-200 mt-1" />}
      </div>

      {/* Right column: entry card */}
      <div className="flex-1 min-w-0 mb-4">
        <div className="bg-white border border-gray-200 rounded-xl shadow-sm p-4 overflow-hidden">
          {/* Header row */}
          <div className="flex items-center justify-between mb-2 gap-2 flex-wrap">
            <div className="flex items-center gap-2 min-w-0 flex-wrap">
              <span
                className={`text-xs font-semibold px-2 py-0.5 rounded-full shrink-0 ${stateColor.bg} ${stateColor.text}`}
              >
                {entry.state}
              </span>
              <span className="text-sm font-medium text-gray-900 truncate">{entry.entityName}</span>
              <span
                className={`text-xs px-2 py-0.5 rounded-full shrink-0 font-medium ${
                  entityType === 'Queue'
                    ? 'bg-sky-50 text-sky-600 border border-sky-100'
                    : 'bg-indigo-50 text-indigo-600 border border-indigo-100'
                }`}
              >
                {entityType}
              </span>
            </div>
            <span
              className={`text-xs px-2 py-0.5 rounded-full shrink-0 font-medium ${
                entry.source === 'Live' ? 'bg-sky-100 text-sky-700' : 'bg-gray-100 text-gray-600'
              }`}
            >
              {entry.source === 'Live' ? (
                <span className="flex items-center gap-1"><Radio className="w-3 h-3" />Live</span>
              ) : (
                <span className="flex items-center gap-1"><Database className="w-3 h-3" />History</span>
              )}
            </span>
          </div>

          {/* Timestamp + Seq no */}
          <div className="flex items-center gap-4 text-xs text-gray-500 mb-2 flex-wrap">
            <span className="flex items-center gap-1">
              <Clock className="w-3 h-3" />
              {formatTimestamp(entry.timestamp)}
            </span>
            <span>SeqNo: {entry.sequenceNumber.toLocaleString()}</span>
            <span>Size: {entry.sizeInBytes > 0 ? `${(entry.sizeInBytes / 1024).toFixed(1)} KB` : '—'}</span>            {entry.messageId && (
              <span className="flex items-center gap-1">
                <span className="font-mono text-gray-500">ID: {entry.messageId.slice(0, 8)}…</span>
                <CopyButton text={entry.messageId} label="message ID" iconSize="w-3 h-3" />
              </span>
            )}          </div>

          {/* Namespace */}
          <p className="text-xs text-gray-500 mb-2">
            Namespace:{' '}
            <span className="font-medium text-gray-700">{entry.namespaceDisplayName}</span>
            {entry.entityPath && entry.entityPath !== entry.entityName && (
              <span className="ml-1 text-gray-400">({entry.entityPath})</span>
            )}
          </p>

          {/* Body preview — expandable */}
          {entry.bodyPreview && (
            <div className="mt-1 min-w-0 max-w-full overflow-hidden">
              <div
                className={`text-xs text-gray-600 font-mono bg-gray-50 border border-gray-100 rounded px-2 py-1.5 overflow-hidden ${
                  expanded ? 'whitespace-pre-wrap break-all' : 'truncate'
                }`}
              >
                {expanded ? entry.bodyPreview : entry.bodyPreview.slice(0, 200) + (entry.bodyPreview.length > 200 ? '…' : '')}
              </div>
              {entry.bodyPreview.length > 200 && (
                <button
                  onClick={() => setExpanded((v) => !v)}
                  className="mt-1 text-xs text-violet-600 hover:text-violet-800 flex items-center gap-1"
                >
                  {expanded ? (
                    <><ChevronUp className="w-3 h-3" /> Show less</>
                  ) : (
                    <><ChevronDown className="w-3 h-3" /> Show full body</>
                  )}
                </button>
              )}
            </div>
          )}

          {/* DLQ reason */}
          {entry.deadLetterReason && (
            <p className="text-xs text-red-600 mt-2 flex items-center gap-1">
              <AlertTriangle className="w-3 h-3 shrink-0" />
              DLQ Reason: {entry.deadLetterReason}
            </p>
          )}
        </div>
      </div>
    </div>
  );
}

// ============================================================================
// Placeholder (initial state)
// ============================================================================

function PlaceholderState() {
  return (
    <div className="flex flex-col items-center justify-center h-full text-center px-8 py-16">
      <GitMerge className="w-14 h-14 text-gray-300 mb-4" />
      <p className="text-gray-600 font-semibold text-lg mb-1">Enter a Correlation ID</p>
      <p className="text-gray-400 text-sm max-w-sm">
        Trace a message journey across all your queues and namespaces by entering a Correlation ID above.
      </p>
      <div className="mt-6 grid grid-cols-3 gap-3 text-left w-full max-w-sm">
        {[
          { icon: '🔍', title: 'Cross-namespace', body: 'Searches all connected namespaces in parallel' },
          { icon: '📜', title: 'Live + History', body: 'Merges live queue data with DLQ history' },
          { icon: '📦', title: 'Full journey', body: 'Shows message state at every hop' },
        ].map((tip) => (
          <div key={tip.title} className="bg-white border border-gray-100 rounded-xl p-3">
            <div className="text-lg mb-1">{tip.icon}</div>
            <p className="text-xs font-semibold text-gray-700 mb-0.5">{tip.title}</p>
            <p className="text-xs text-gray-400">{tip.body}</p>
          </div>
        ))}
      </div>
    </div>
  );
}

// ============================================================================
// CorrelationExplorerPage
// ============================================================================

export function CorrelationExplorerPage() {
  const [searchParams, setSearchParams] = useSearchParams();

  const initialCorrelationId = searchParams.get('correlationId') ?? '';
  const initialNamespaceId = searchParams.get('namespaceId') ?? '';

  const [correlationIdInput, setCorrelationIdInput] = useState(initialCorrelationId);
  const [selectedNamespaceId, setSelectedNamespaceId] = useState(initialNamespaceId);
  const [timeRange, setTimeRange] = useState<TimeRange>('all');
  const [entityTypeFilter, setEntityTypeFilter] = useState<'all' | 'queue' | 'topic'>('all');
  const [showFilters, setShowFilters] = useState(false);

  const { data: namespaces } = useNamespaces();
  const search = useCorrelationSearch();
  const hasAutoSearched = useRef(false);

  // Auto-trigger search from URL params on first mount
  useEffect(() => {
    if (initialCorrelationId && !hasAutoSearched.current) {
      hasAutoSearched.current = true;
      search.mutate({
        correlationId: initialCorrelationId,
        namespaceId: initialNamespaceId || undefined,
      });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function handleSearch() {
    if (!correlationIdInput.trim()) return;
    const params: Record<string, string> = { correlationId: correlationIdInput.trim() };
    if (selectedNamespaceId) params.namespaceId = selectedNamespaceId;
    setSearchParams(params);
    search.mutate({
      correlationId: correlationIdInput.trim(),
      namespaceId: selectedNamespaceId || undefined,
    });
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'Enter') handleSearch();
  }

  const result = search.data;
  const isLoading = search.isPending;
  const hasSearched = search.isSuccess || search.isError;

  // Client-side filtering of timeline entries
  const filteredEntries = useMemo(() => {
    if (!result?.entries) return [];
    let entries = filterByTimeRange(result.entries, timeRange);
    if (entityTypeFilter === 'queue') {
      entries = entries.filter((e) => !e.entityPath?.includes('/subscriptions/'));
    } else if (entityTypeFilter === 'topic') {
      entries = entries.filter((e) => e.entityPath?.includes('/subscriptions/'));
    }
    return entries;
  }, [result?.entries, timeRange, entityTypeFilter]);

  const hasActiveFilters = timeRange !== 'all' || entityTypeFilter !== 'all';

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      {/* Header */}
      <div className="bg-gradient-to-r from-violet-600 to-violet-500 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <GitMerge className="w-6 h-6 text-white/80" />
            <div>
              <h1 className="text-xl font-semibold text-white">Correlation Explorer</h1>
              <p className="text-violet-100 text-sm">
                Trace any message's full journey across all queues and namespaces
              </p>
            </div>
          </div>
          {result && result.totalCount > 0 && (
            <button
              onClick={() => exportAsJson(result)}
              className="flex items-center gap-2 px-3 py-1.5 bg-white/20 hover:bg-white/30 text-white rounded-lg text-sm font-medium transition-colors"
              title="Export timeline as JSON"
            >
              <Download className="w-4 h-4" />
              Export JSON
            </button>
          )}
        </div>
      </div>

      {/* Search bar */}
      <div className="bg-white border-b border-gray-200 px-3 sm:px-4 lg:px-6 py-3 shrink-0 overflow-x-auto">
        <div className="flex items-center gap-2 sm:gap-3 flex-wrap">
          {/* Correlation ID input */}
          <div className="flex-1 flex items-center gap-2 bg-gray-50 border border-gray-300 rounded-lg px-3 py-2 focus-within:border-violet-400 focus-within:ring-1 focus-within:ring-violet-400 transition-all">
            <Search className="w-4 h-4 text-gray-400 shrink-0" />
            <input
              type="text"
              value={correlationIdInput}
              onChange={(e) => setCorrelationIdInput(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Enter Correlation ID…"
              className="flex-1 bg-transparent text-sm text-gray-900 placeholder-gray-400 outline-none"
              aria-label="Correlation ID"
            />
          </div>

          {/* Namespace filter */}
          <select
            value={selectedNamespaceId}
            onChange={(e) => setSelectedNamespaceId(e.target.value)}
            className="text-sm border border-gray-300 rounded-lg px-3 py-2 bg-white text-gray-700 focus:border-violet-400 focus:outline-none focus:ring-1 focus:ring-violet-400"
            aria-label="Namespace filter"
          >
            <option value="">All Namespaces</option>
            {namespaces?.map((ns) => (
              <option key={ns.id} value={ns.id}>
                {ns.displayName ?? ns.name}
              </option>
            ))}
          </select>

          {/* Filters toggle */}
          <button
            onClick={() => setShowFilters((v) => !v)}
            className={`flex items-center gap-1.5 px-2 sm:px-3 py-2 border rounded-lg text-xs sm:text-sm font-medium transition-colors ${
              hasActiveFilters
                ? 'border-violet-400 bg-violet-50 text-violet-700'
                : 'border-gray-300 bg-white text-gray-700 hover:bg-gray-50'
            }`}
            aria-label="Toggle result filters"
          >
            <Filter className="w-4 h-4" />
            <span className="hidden sm:inline">Filters</span>
            {hasActiveFilters && (
              <span className="w-2 h-2 rounded-full bg-violet-500 ml-0.5" />
            )}
          </button>

          {/* Search button */}
          <button
            onClick={handleSearch}
            disabled={!correlationIdInput.trim() || isLoading}
            className="flex items-center gap-1.5 px-3 sm:px-4 py-2 bg-violet-600 hover:bg-violet-700 disabled:bg-violet-300 text-white rounded-lg text-xs sm:text-sm font-medium transition-colors whitespace-nowrap"
          >
            <Search className="w-4 h-4" />
            <span className="hidden sm:inline">{isLoading ? 'Searching…' : 'Search'}</span>
            <span className="sm:hidden">{isLoading ? '...' : '→'}</span>
          </button>
        </div>

        {/* Expandable filter panel */}
        {showFilters && (
          <div className="flex items-center gap-2 sm:gap-4 mt-3 pt-3 border-t border-gray-100 flex-wrap">
            <div className="flex items-center gap-2">
              <label className="text-xs font-medium text-gray-600">Time range</label>
              <select
                value={timeRange}
                onChange={(e) => setTimeRange(e.target.value as TimeRange)}
                className="text-sm border border-gray-200 rounded-lg px-2.5 py-1 bg-white focus:border-violet-400 focus:outline-none focus:ring-1 focus:ring-violet-400"
              >
                {TIME_RANGE_OPTIONS.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
            </div>
            <div className="flex items-center gap-2">
              <label className="text-xs font-medium text-gray-600">Entity type</label>
              <div className="flex rounded-lg border border-gray-200 overflow-hidden">
                {(['all', 'queue', 'topic'] as const).map((opt) => (
                  <button
                    key={opt}
                    onClick={() => setEntityTypeFilter(opt)}
                    className={`px-3 py-1 text-xs font-medium transition-colors ${
                      entityTypeFilter === opt
                        ? 'bg-violet-600 text-white'
                        : 'bg-white text-gray-600 hover:bg-gray-50'
                    }`}
                  >
                    {opt === 'all' ? 'All' : opt === 'queue' ? 'Queues' : 'Topics'}
                  </button>
                ))}
              </div>
            </div>
            {hasActiveFilters && (
              <button
                onClick={() => { setTimeRange('all'); setEntityTypeFilter('all'); }}
                className="text-xs text-gray-400 hover:text-gray-600 underline"
              >
                Clear filters
              </button>
            )}
          </div>
        )}
      </div>

      {/* Content area */}
      <div className="flex-1 overflow-y-auto overflow-x-hidden bg-gray-50">
        {isLoading ? (
          <div className="flex flex-col items-center justify-center h-full gap-3 text-gray-500">
            <div className="animate-spin rounded-full border-4 border-violet-200 border-t-violet-600 w-10 h-10" />
            <p className="text-sm">
              Searching across {namespaces?.length ?? '…'} namespace(s)…
            </p>
          </div>
        ) : !hasSearched ? (
          <PlaceholderState />
        ) : result && result.totalCount === 0 ? (
          <div className="flex flex-col items-center justify-center h-full text-center px-8 py-16">
            <CheckCircle className="w-12 h-12 text-gray-300 mb-4" />
            <p className="text-gray-600 font-semibold text-lg mb-1">No messages found</p>
            <p className="text-gray-400 text-sm">
              No messages for correlation ID:{' '}
              <span className="font-mono text-gray-600">{result.correlationId}</span>
            </p>
          </div>
        ) : result ? (
          <div className="px-3 sm:px-4 lg:px-6 py-4 sm:py-5 w-full max-w-5xl mx-auto">
            {/* Partial result banner */}
            {result.isPartialResult && (
              <div className="flex items-center gap-2 bg-amber-50 border border-amber-200 rounded-lg px-4 py-2.5 mb-4 text-amber-800 text-sm">
                <AlertTriangle className="w-4 h-4 shrink-0 text-amber-600" />
                <span>
                  Search timed out — showing partial results ({result.totalCount} entries found)
                </span>
              </div>
            )}

            {/* Results header */}
            <div className="flex items-center justify-between mb-4 flex-wrap gap-2">
              <div>
                <p className="text-gray-700 text-sm">
                  Found{' '}
                  <span className="font-semibold text-gray-900">{result.totalCount}</span>{' '}
                  message(s) across{' '}
                  <span className="font-semibold">{result.entitiesSearched}</span> entity/ies in{' '}
                  <span className="font-semibold">{result.namespacesSearched}</span> namespace(s)
                </p>
                <p className="text-xs text-gray-400 mt-0.5 flex items-center gap-1.5">
                  Search completed in {result.searchDurationMs.toLocaleString()}ms
                  {hasActiveFilters && filteredEntries.length !== result.entries.length && (
                    <span className="ml-2 text-violet-600 font-medium">
                      &middot; Showing {filteredEntries.length} of {result.totalCount} after filters
                    </span>
                  )}
                  <span className="flex items-center gap-1 ml-2 font-mono text-gray-500">
                    CorrelationID: {result.correlationId.slice(0, 12)}…
                    <CopyButton text={result.correlationId} label="correlation ID" iconSize="w-3 h-3" />
                  </span>
                </p>
              </div>
              <button
                onClick={() => exportAsJson(result)}
                className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-violet-700 bg-violet-50 hover:bg-violet-100 border border-violet-200 rounded-lg transition-colors"
              >
                <Download className="w-3.5 h-3.5" />
                Export JSON
              </button>
            </div>

            {/* Source legend */}
            <div className="flex items-center gap-4 mb-4 text-xs text-gray-500">
              <span className="flex items-center gap-1">
                <Radio className="w-3 h-3 text-sky-500" />
                Live = currently in queue
              </span>
              <span className="flex items-center gap-1">
                <Database className="w-3 h-3 text-gray-400" />
                History = from DLQ history database
              </span>
            </div>

            {/* Filtered empty state */}
            {filteredEntries.length === 0 ? (
              <div className="text-center py-12 text-gray-400">
                <Filter className="w-10 h-10 mx-auto mb-3 opacity-40" />
                <p className="font-medium">No entries match the current filters</p>
                <button
                  onClick={() => { setTimeRange('all'); setEntityTypeFilter('all'); }}
                  className="mt-2 text-sm text-violet-600 underline"
                >
                  Clear filters
                </button>
              </div>
            ) : (
              /* Timeline */
              <>
                <TimelineMinimap entries={filteredEntries} />
                <div className="ml-1 sm:ml-2 -mr-4">
                  {filteredEntries.map((entry, idx) => (
                    <div key={`${entry.messageId}-${idx}`}>
                      {idx > 0 && (
                        <DurationConnector
                          fromTs={filteredEntries[idx - 1].timestamp}
                          toTs={entry.timestamp}
                        />
                      )}
                      <TimelineEntryCard
                        entry={entry}
                        isLast={idx === filteredEntries.length - 1}
                        index={idx}
                      />
                    </div>
                  ))}
                </div>
              </>
            )}
          </div>
        ) : null}
      </div>
    </div>
  );
}

export default CorrelationExplorerPage;
