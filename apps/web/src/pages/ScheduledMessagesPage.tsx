import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Clock, RefreshCw, XCircle, Calendar, AlertCircle, Inbox } from 'lucide-react';
import { useNamespaces } from '@/hooks/useNamespaces';
import { useQueues } from '@/hooks/useQueues';
import { useScheduledMessages, useCancelScheduledMessage } from '@/hooks/useScheduledMessages';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import { Message } from '@/lib/api/types';

// ============================================================================
// ScheduledMessagesPage - Dashboard for viewing and cancelling scheduled messages
// ============================================================================

function formatScheduledFor(isoString: string): string {
  const date = new Date(isoString);
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date);
}

function deliversIn(isoString: string): string {
  const now = Date.now();
  const target = new Date(isoString).getTime();
  const diffMs = target - now;
  if (diffMs <= 0) return 'Now';
  const diffSec = Math.floor(diffMs / 1000);
  const diffMin = Math.floor(diffSec / 60);
  const diffHr = Math.floor(diffMin / 60);
  const diffDay = Math.floor(diffHr / 24);
  if (diffDay > 0) return `in ${diffDay}d ${diffHr % 24}h`;
  if (diffHr > 0) return `in ${diffHr}h ${diffMin % 60}m`;
  if (diffMin > 0) return `in ${diffMin}m`;
  return `in ${diffSec}s`;
}

function formatBytes(bytes?: number | null): string {
  if (bytes == null) return '—';
  if (bytes < 1024) return `${bytes} B`;
  return `${(bytes / 1024).toFixed(1)} KB`;
}

interface ScheduledMessageRowProps {
  message: Message;
  namespaceId: string;
  queueName: string;
}

function ScheduledMessageRow({ message, namespaceId, queueName }: ScheduledMessageRowProps) {
  const cancel = useCancelScheduledMessage();
  const [confirmOpen, setConfirmOpen] = useState(false);

  const handleCancelClick = () => setConfirmOpen(true);

  const handleConfirm = async () => {
    setConfirmOpen(false);
    await cancel.mutateAsync({
      namespaceId,
      queueName,
      sequenceNumber: message.sequenceNumber,
    });
  };

  const scheduledTime = message.scheduledEnqueueTime;
  const shortId = message.messageId
    ? `${message.messageId.substring(0, 12)}…`
    : `#${message.sequenceNumber}`;

  return (
    <>
      <tr className="border-b border-gray-100 hover:bg-sky-50/30 transition-colors">
        <td className="px-4 py-3 text-sm font-mono text-gray-700">
          <span title={message.messageId ?? undefined} className="cursor-default">
            {shortId}
          </span>
        </td>
        <td className="px-4 py-3 text-sm text-gray-500 max-w-[220px]">
          <span className="font-mono text-xs bg-gray-50 border border-gray-200 rounded px-1.5 py-0.5 block truncate">
            {message.body ? message.body.substring(0, 80) : <span className="italic text-gray-300">empty</span>}
          </span>
        </td>
        <td className="px-4 py-3 text-sm text-gray-600 whitespace-nowrap">
          {scheduledTime ? (
            <div className="flex items-center gap-1.5 text-sky-700 font-medium">
              <Clock className="w-3.5 h-3.5 shrink-0" />
              {formatScheduledFor(scheduledTime)}
            </div>
          ) : (
            <span className="text-gray-400 italic text-xs">—</span>
          )}
        </td>
        <td className="px-4 py-3 text-sm text-gray-500 whitespace-nowrap">
          {scheduledTime ? (
            <span className="text-xs text-sky-600 font-medium">{deliversIn(scheduledTime)}</span>
          ) : (
            <span className="text-gray-400 italic text-xs">—</span>
          )}
        </td>
        <td className="px-4 py-3 text-sm text-gray-500 whitespace-nowrap">
          {formatBytes(message.body ? new TextEncoder().encode(message.body).length : null)}
        </td>
        <td className="px-4 py-3 text-right">
          <button
            onClick={handleCancelClick}
            disabled={cancel.isPending}
            className="flex items-center gap-1.5 px-2.5 py-1.5 text-xs font-medium text-red-600 hover:text-red-700 hover:bg-red-50 border border-red-200 hover:border-red-300 rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <XCircle className="w-3.5 h-3.5" />
            Cancel
          </button>
        </td>
      </tr>

      <ConfirmDialog
        isOpen={confirmOpen}
        title="Cancel Scheduled Message"
        message={`Cancel the scheduled message ${shortId}?\n\nThis action cannot be undone.`}
        confirmLabel="Yes, Cancel Message"
        cancelLabel="Keep It"
        variant="danger"
        onConfirm={handleConfirm}
        onCancel={() => setConfirmOpen(false)}
      />
    </>
  );
}

export function ScheduledMessagesPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const selectedNamespaceId = searchParams.get('namespace') ?? '';
  const selectedQueue = searchParams.get('queue') ?? '';

  const setNamespace = (id: string) => {
    setSearchParams(id ? { namespace: id } : {});
  };

  const setQueue = (name: string) => {
    if (!name) {
      const next = new URLSearchParams(searchParams);
      next.delete('queue');
      setSearchParams(next);
    } else {
      setSearchParams({ namespace: selectedNamespaceId, queue: name });
    }
  };

  const { data: namespaces } = useNamespaces();
  const { data: queues } = useQueues(selectedNamespaceId);

  const {
    data: paginatedMessages,
    isLoading,
    isError,
    refetch,
    isFetching,
  } = useScheduledMessages(selectedNamespaceId, selectedQueue);

  const messages = paginatedMessages?.items ?? [];
  const scheduledCount = paginatedMessages?.totalCount ?? messages.length;

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      {/* Header */}
      <div className="bg-gradient-to-r from-sky-600 to-sky-500 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <Calendar className="w-6 h-6 text-white/80" />
            <div>
              <h1 className="text-xl font-semibold text-white">Scheduled Messages</h1>
              <p className="text-sky-100 text-sm">
                View and cancel messages queued for future delivery
              </p>
            </div>
          </div>
          <button
            onClick={() => refetch()}
            disabled={isFetching || !selectedQueue}
            className="flex items-center gap-2 px-3 py-1.5 bg-white/20 hover:bg-white/30 disabled:opacity-50 text-white rounded-lg text-sm font-medium transition-colors"
            title="Refresh"
          >
            <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
            Refresh
          </button>
        </div>
      </div>

      {/* Filters */}
      <div className="bg-white border-b border-gray-200 px-6 py-3 flex items-center gap-4 shrink-0">
        <div className="flex items-center gap-2">
          <label className="text-sm font-medium text-gray-600">Namespace</label>
          <select
            value={selectedNamespaceId}
            onChange={(e) => setNamespace(e.target.value)}
            className="px-3 py-1.5 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-sky-400 min-w-[180px]"
          >
            <option value="">Select namespace…</option>
            {namespaces?.map((ns) => (
              <option key={ns.id} value={ns.id}>
                {ns.displayName || ns.name}
              </option>
            ))}
          </select>
        </div>

        <div className="flex items-center gap-2">
          <label className="text-sm font-medium text-gray-600">Queue</label>
          <select
            value={selectedQueue}
            onChange={(e) => setQueue(e.target.value)}
            disabled={!selectedNamespaceId}
            className="px-3 py-1.5 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-sky-400 min-w-[180px] disabled:bg-gray-50 disabled:cursor-not-allowed"
          >
            <option value="">Select queue…</option>
            {queues?.map((q) => (
              <option key={q.name} value={q.name}>
                {q.name} ({q.scheduledMessageCount} scheduled)
              </option>
            ))}
          </select>
        </div>

        {selectedQueue && (
          <div className="ml-auto flex items-center gap-2">
            <span className="px-2.5 py-1 bg-sky-100 text-sky-700 text-sm font-semibold rounded-full">
              {scheduledCount} message{scheduledCount !== 1 ? 's' : ''}
            </span>
          </div>
        )}
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto bg-gray-50">
        {!selectedNamespaceId || !selectedQueue ? (
          <div className="flex flex-col items-center justify-center h-full text-center px-6">
            <Calendar className="w-12 h-12 text-gray-300 mb-3" />
            <p className="text-gray-500 font-medium">Select a namespace and queue</p>
            <p className="text-gray-400 text-sm mt-1">
              Choose a namespace and queue above to view scheduled messages
            </p>
          </div>
        ) : isLoading ? (
          <div className="flex flex-col items-center justify-center h-full gap-3">
            <RefreshCw className="w-8 h-8 text-sky-400 animate-spin" />
            <p className="text-gray-500 text-sm">Loading scheduled messages…</p>
          </div>
        ) : isError ? (
          <div className="flex flex-col items-center justify-center h-full gap-3">
            <AlertCircle className="w-10 h-10 text-red-400" />
            <p className="text-gray-600 font-medium">Failed to load scheduled messages</p>
            <button
              onClick={() => refetch()}
              className="px-4 py-2 text-sm text-sky-600 hover:text-sky-700 border border-sky-300 rounded-lg hover:bg-sky-50 transition-colors"
            >
              Try Again
            </button>
          </div>
        ) : messages.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-full gap-3">
            <Inbox className="w-10 h-10 text-gray-300" />
            <p className="text-gray-500 font-medium">No scheduled messages</p>
            <p className="text-gray-400 text-sm">
              This queue has no messages pending future delivery
            </p>
          </div>
        ) : (
          <div className="p-6">
            <div className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden">
              <table className="w-full text-left">
                <thead>
                  <tr className="border-b border-gray-200 bg-gray-50">
                    <th className="px-4 py-3 text-xs font-semibold text-gray-500 uppercase tracking-wider">
                      Message ID
                    </th>
                    <th className="px-4 py-3 text-xs font-semibold text-gray-500 uppercase tracking-wider">
                      Body Preview
                    </th>
                    <th className="px-4 py-3 text-xs font-semibold text-gray-500 uppercase tracking-wider">
                      Scheduled For
                    </th>
                    <th className="px-4 py-3 text-xs font-semibold text-gray-500 uppercase tracking-wider">
                      Delivers In
                    </th>
                    <th className="px-4 py-3 text-xs font-semibold text-gray-500 uppercase tracking-wider">
                      Size
                    </th>
                    <th className="px-4 py-3 text-right text-xs font-semibold text-gray-500 uppercase tracking-wider">
                      Action
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {messages.map((msg) => (
                    <ScheduledMessageRow
                      key={msg.sequenceNumber}
                      message={msg}
                      namespaceId={selectedNamespaceId}
                      queueName={selectedQueue}
                    />
                  ))}
                </tbody>
              </table>
            </div>
            <p className="text-xs text-gray-400 mt-3 text-center">
              Showing {messages.length} of {scheduledCount} scheduled message{scheduledCount !== 1 ? 's' : ''}.
              Auto-refreshes every 10 seconds.
            </p>
          </div>
        )}
      </div>
    </div>
  );
}
