import { useState, useEffect, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';
import { GitMerge, Search, AlertTriangle, Clock, CheckCircle } from 'lucide-react';
import { useCorrelationSearch } from '@/hooks/useCorrelation';
import { useNamespaces } from '@/hooks/useNamespaces';
import type { CorrelationTimelineEntry } from '@/lib/api/types';

// ============================================================================
// State badge helpers
// ============================================================================

type StateColor = {
  bg: string;
  text: string;
  dot: string;
};

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

// ============================================================================
// Timeline Entry Card
// ============================================================================

function TimelineEntryCard({ entry, isLast }: { entry: CorrelationTimelineEntry; isLast: boolean }) {
  const stateColor = getStateColor(entry.state);

  return (
    <div className="flex gap-4">
      {/* Left column: dot + connector line */}
      <div className="flex flex-col items-center">
        <div className={`w-3 h-3 rounded-full shrink-0 mt-3 ${stateColor.dot}`} />
        {!isLast && <div className="w-0.5 flex-1 bg-gray-200 mt-1" />}
      </div>

      {/* Right column: entry card */}
      <div className="flex-1 mb-4">
        <div className="bg-white border border-gray-200 rounded-xl shadow-sm p-4">
          {/* Header row */}
          <div className="flex items-center justify-between mb-2 gap-2">
            <div className="flex items-center gap-2 min-w-0">
              <span className={`text-xs font-semibold px-2 py-0.5 rounded-full shrink-0 ${stateColor.bg} ${stateColor.text}`}>
                {entry.state}
              </span>
              <span className="text-sm font-medium text-gray-900 truncate">{entry.entityName}</span>
            </div>
            <span
              className={`text-xs px-2 py-0.5 rounded-full shrink-0 font-medium ${
                entry.source === 'Live'
                  ? 'bg-sky-100 text-sky-700'
                  : 'bg-gray-100 text-gray-600'
              }`}
            >
              {entry.source}
            </span>
          </div>

          {/* Timestamp + Seq no */}
          <div className="flex items-center gap-4 text-xs text-gray-500 mb-2">
            <span className="flex items-center gap-1">
              <Clock className="w-3 h-3" />
              {formatTimestamp(entry.timestamp)}
            </span>
            <span>SeqNo: {entry.sequenceNumber.toLocaleString()}</span>
          </div>

          {/* Namespace */}
          <p className="text-xs text-gray-500 mb-1">
            Namespace: <span className="font-medium text-gray-700">{entry.namespaceDisplayName}</span>
            {entry.entityPath && entry.entityPath !== entry.entityName && (
              <span className="ml-1 text-gray-400">({entry.entityPath})</span>
            )}
          </p>

          {/* Body preview */}
          {entry.bodyPreview && (
            <p className="text-xs text-gray-600 font-mono bg-gray-50 rounded px-2 py-1 truncate mt-1">
              {entry.bodyPreview.length > 80
                ? `${entry.bodyPreview.slice(0, 80)}…`
                : entry.bodyPreview}
            </p>
          )}

          {/* DLQ reason */}
          {entry.deadLetterReason && (
            <p className="text-xs text-red-600 mt-1 flex items-center gap-1">
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

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      {/* Header */}
      <div className="bg-gradient-to-r from-violet-600 to-violet-500 px-6 py-4 shrink-0">
        <div className="flex items-center gap-3">
          <GitMerge className="w-6 h-6 text-white/80" />
          <div>
            <h1 className="text-xl font-semibold text-white">Correlation Explorer</h1>
            <p className="text-violet-100 text-sm">
              Find every message sharing a CorrelationId across all queues and namespaces
            </p>
          </div>
        </div>
      </div>

      {/* Search bar */}
      <div className="bg-white border-b border-gray-200 px-6 py-3 shrink-0">
        <div className="flex items-center gap-3">
          {/* Correlation ID input */}
          <div className="flex-1 flex items-center gap-2 bg-gray-50 border border-gray-300 rounded-lg px-3 py-2 focus-within:border-violet-400 focus-within:ring-1 focus-within:ring-violet-400 transition-all">
            <Search className="w-4 h-4 text-gray-400 shrink-0" />
            <input
              type="text"
              value={correlationIdInput}
              onChange={e => setCorrelationIdInput(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Enter Correlation ID…"
              className="flex-1 bg-transparent text-sm text-gray-900 placeholder-gray-400 outline-none"
              aria-label="Correlation ID"
            />
          </div>

          {/* Namespace filter */}
          <select
            value={selectedNamespaceId}
            onChange={e => setSelectedNamespaceId(e.target.value)}
            className="text-sm border border-gray-300 rounded-lg px-3 py-2 bg-white text-gray-700 focus:border-violet-400 focus:outline-none focus:ring-1 focus:ring-violet-400"
            aria-label="Namespace filter"
          >
            <option value="">All Namespaces</option>
            {namespaces?.map(ns => (
              <option key={ns.id} value={ns.id}>
                {ns.displayName ?? ns.name}
              </option>
            ))}
          </select>

          {/* Search button */}
          <button
            onClick={handleSearch}
            disabled={!correlationIdInput.trim() || isLoading}
            className="flex items-center gap-2 px-4 py-2 bg-violet-600 hover:bg-violet-700 disabled:bg-violet-300 text-white rounded-lg text-sm font-medium transition-colors"
          >
            <Search className="w-4 h-4" />
            {isLoading ? 'Searching…' : 'Search'}
          </button>
        </div>
      </div>

      {/* Content area */}
      <div className="flex-1 overflow-auto bg-gray-50">
        {isLoading ? (
          // Loading state
          <div className="flex flex-col items-center justify-center h-full gap-3 text-gray-500">
            <div className="animate-spin rounded-full border-4 border-violet-200 border-t-violet-600 w-10 h-10" />
            <p className="text-sm">
              Searching across {namespaces?.length ?? '…'} namespace(s)…
            </p>
          </div>
        ) : !hasSearched ? (
          <PlaceholderState />
        ) : result && result.totalCount === 0 ? (
          // Empty results
          <div className="flex flex-col items-center justify-center h-full text-center px-8 py-16">
            <CheckCircle className="w-12 h-12 text-gray-300 mb-4" />
            <p className="text-gray-600 font-semibold text-lg mb-1">No messages found</p>
            <p className="text-gray-400 text-sm">
              No messages found for correlation ID:{' '}
              <span className="font-mono text-gray-600">{result.correlationId}</span>
            </p>
          </div>
        ) : result ? (
          <div className="px-6 py-5 max-w-3xl mx-auto">
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
            <div className="mb-5">
              <p className="text-gray-700 text-sm">
                Found{' '}
                <span className="font-semibold text-gray-900">{result.totalCount}</span>{' '}
                message(s) across{' '}
                <span className="font-semibold">{result.entitiesSearched}</span> queue(s) in{' '}
                <span className="font-semibold">{result.namespacesSearched}</span> namespace(s)
              </p>
              <p className="text-xs text-gray-400 mt-0.5">
                Search completed in {result.searchDurationMs.toLocaleString()}ms
              </p>
            </div>

            {/* Timeline */}
            <div className="ml-4">
              {result.entries.map((entry, idx) => (
                <TimelineEntryCard
                  key={`${entry.messageId}-${idx}`}
                  entry={entry}
                  isLast={idx === result.entries.length - 1}
                />
              ))}
            </div>
          </div>
        ) : null}
      </div>
    </div>
  );
}
