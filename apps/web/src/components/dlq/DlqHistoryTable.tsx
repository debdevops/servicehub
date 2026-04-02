import { useState, useMemo } from 'react';
import {
  ChevronLeft,
  ChevronRight,
  Clock,
  Eye,
  Shield,
} from 'lucide-react';
import type { DlqHistoryItem } from '@/lib/api/dlqHistory';
import { StatusBadge, CategoryBadge } from './StatusBadge';

interface DlqHistoryTableProps {
  items: DlqHistoryItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
  isLoading: boolean;
  onPageChange: (page: number) => void;
  onViewTimeline: (id: number) => void;
}

function formatTime(ts: string | null): string {
  if (!ts) return '—';
  const d = new Date(ts);
  return d.toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function formatRelative(ts: string): string {
  const diff = Date.now() - new Date(ts).getTime();
  const mins = Math.floor(diff / 60000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hrs = Math.floor(mins / 60);
  if (hrs < 24) return `${hrs}h ago`;
  const days = Math.floor(hrs / 24);
  return `${days}d ago`;
}

function truncate(text: string | null, max = 60): string {
  if (!text) return '—';
  return text.length > max ? text.slice(0, max) + '…' : text;
}

export function DlqHistoryTable({
  items,
  totalCount,
  page,
  pageSize,
  hasNextPage,
  hasPreviousPage,
  isLoading,
  onPageChange,
  onViewTimeline,
}: DlqHistoryTableProps) {
  const [hoveredRow, setHoveredRow] = useState<number | null>(null);

  const totalPages = useMemo(() => Math.ceil(totalCount / pageSize), [totalCount, pageSize]);
  const startItem = (page - 1) * pageSize + 1;
  const endItem = Math.min(page * pageSize, totalCount);

  if (isLoading) {
    return (
      <div className="bg-white rounded-xl border border-gray-200 overflow-hidden animate-pulse">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="bg-gray-50 border-b border-gray-200">
                {['w-10', 'flex-1', 'w-24', 'w-32', 'w-24', 'w-16', 'flex-1', 'w-20'].map((w, i) => (
                  <th key={i} className="px-4 py-3">
                    <div className={`h-3 bg-gray-200 rounded ${w}`} />
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {Array.from({ length: 8 }).map((_, i) => (
                <tr key={i} className="border-b border-gray-100">
                  <td className="px-4 py-3"><div className="h-3 w-6 bg-gray-100 rounded" /></td>
                  <td className="px-4 py-3"><div className="h-3 w-32 bg-gray-100 rounded" /></td>
                  <td className="px-4 py-3"><div className="h-5 w-20 bg-gray-100 rounded-full" /></td>
                  <td className="px-4 py-3"><div className="h-5 w-24 bg-gray-100 rounded-full" /></td>
                  <td className="px-4 py-3"><div className="h-5 w-20 bg-gray-100 rounded-full" /></td>
                  <td className="px-4 py-3"><div className="h-3 w-10 bg-gray-100 rounded" /></td>
                  <td className="px-4 py-3"><div className="h-3 w-40 bg-gray-100 rounded" /></td>
                  <td className="px-4 py-3"><div className="h-7 w-16 bg-gray-100 rounded-lg" /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    );
  }

  if (items.length === 0) {
    return (
      <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        <div className="p-12 text-center">
          <div className="text-5xl mb-3">📭</div>
          <h3 className="text-lg font-semibold text-gray-900">No DLQ messages found</h3>
          <p className="text-gray-500 mt-1">
            No dead-letter queue messages have been detected yet.
            The background monitor scans for new DLQ messages automatically.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="bg-gray-50 border-b border-gray-200">
              <th className="text-left px-4 py-3 font-semibold text-gray-600 w-16">#</th>
              <th className="text-left px-4 py-3 font-semibold text-gray-600">Entity</th>
              <th className="text-left px-4 py-3 font-semibold text-gray-600 w-28">Status</th>
              <th className="text-left px-4 py-3 font-semibold text-gray-600 w-36">Category</th>
              <th className="text-left px-4 py-3 font-semibold text-gray-600 w-28">Replay Safety</th>
              <th className="text-left px-4 py-3 font-semibold text-gray-600 w-20">
                <span title="Forensic Confidence">Conf.</span>
              </th>
              <th className="text-left px-4 py-3 font-semibold text-gray-600">DLQ Reason</th>
              <th className="text-left px-4 py-3 font-semibold text-gray-600 w-20">
                <span title="Delivery Count">Del.</span>
              </th>
              <th className="text-left px-4 py-3 font-semibold text-gray-600 w-28">Detected</th>
              <th className="text-left px-4 py-3 font-semibold text-gray-600 w-16">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {items.map((item) => (
              <tr
                key={item.id}
                className={`transition-colors cursor-pointer ${
                  hoveredRow === item.id ? 'bg-sky-50' : 'hover:bg-gray-50'
                }`}
                onClick={() => onViewTimeline(item.id)}
                onMouseEnter={() => setHoveredRow(item.id)}
                onMouseLeave={() => setHoveredRow(null)}
              >
                <td className="px-4 py-3 text-gray-500 font-mono text-xs">
                  {item.id}
                </td>
                <td className="px-4 py-3">
                  <div className="flex flex-col">
                    <span className="font-medium text-gray-900 truncate max-w-[200px]">
                      {item.entityName}
                    </span>
                    <span className="text-xs text-gray-400 font-mono truncate max-w-[200px]">
                      {item.messageId}
                    </span>
                  </div>
                </td>
                <td className="px-4 py-3">
                  <StatusBadge status={item.status} />
                </td>
                <td className="px-4 py-3">
                  <CategoryBadge
                    category={item.failureCategory}
                    confidence={item.categoryConfidence}
                  />
                </td>
                <td className="px-4 py-3">
                  {item.replaySafety ? (
                    <span className={`inline-flex items-center gap-1 text-xs font-medium px-2 py-0.5 rounded-full ${
                      item.replaySafety === 'Safe' ? 'bg-green-50 text-green-700' :
                      item.replaySafety === 'Unsafe' ? 'bg-red-50 text-red-700' :
                      'bg-amber-50 text-amber-700'
                    }`}>
                      <Shield className="w-3 h-3" />
                      {item.replaySafety}
                    </span>
                  ) : (
                    <span className="text-gray-300">—</span>
                  )}
                </td>
                <td className="px-4 py-3 text-gray-700 text-xs font-mono">
                  {item.forensicConfidence > 0
                    ? `${Math.round(item.forensicConfidence * 100)}%`
                    : '—'}
                </td>
                <td className="px-4 py-3 text-gray-700 max-w-[200px]">
                  <span className="truncate block" title={item.deadLetterReason || undefined}>
                    {truncate(item.deadLetterReason)}
                  </span>
                </td>
                <td className="px-4 py-3">
                  <span className={`font-medium ${
                    item.deliveryCount >= 10 ? 'text-red-600' :
                    item.deliveryCount >= 5 ? 'text-amber-600' : 'text-gray-700'
                  }`}>
                    {item.deliveryCount}
                  </span>
                </td>
                <td className="px-4 py-3">
                  <div className="flex items-center gap-1 text-gray-500" title={formatTime(item.detectedAtUtc)}>
                    <Clock className="w-3.5 h-3.5" />
                    <span className="text-xs">{formatRelative(item.detectedAtUtc)}</span>
                  </div>
                </td>
                <td className="px-4 py-3">
                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      onViewTimeline(item.id);
                    }}
                    className="p-1.5 hover:bg-primary-100 rounded-lg transition-colors text-primary-600"
                    title="View timeline"
                    aria-label={`View timeline for message ${item.messageId}`}
                  >
                    <Eye className="w-4 h-4" />
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      <div className="flex items-center justify-between px-4 py-3 border-t border-gray-200 bg-gray-50">
        <span className="text-sm text-gray-600">
          Showing <span className="font-medium">{startItem}</span> to{' '}
          <span className="font-medium">{endItem}</span> of{' '}
          <span className="font-medium">{totalCount.toLocaleString()}</span> messages
        </span>

        <div className="flex items-center gap-2">
          <button
            onClick={() => onPageChange(page - 1)}
            disabled={!hasPreviousPage}
            className="p-2 rounded-lg border border-gray-200 text-gray-600 hover:bg-white disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
            aria-label="Previous page"
          >
            <ChevronLeft className="w-4 h-4" />
          </button>
          <span className="text-sm text-gray-600 min-w-[80px] text-center">
            Page {page} of {totalPages}
          </span>
          <button
            onClick={() => onPageChange(page + 1)}
            disabled={!hasNextPage}
            className="p-2 rounded-lg border border-gray-200 text-gray-600 hover:bg-white disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
            aria-label="Next page"
          >
            <ChevronRight className="w-4 h-4" />
          </button>
        </div>
      </div>
    </div>
  );
}
