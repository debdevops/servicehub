import { useState, useEffect } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Clock, RefreshCw, XCircle, Calendar, AlertCircle, Inbox, CalendarClock } from 'lucide-react';
import { useNamespaces } from '@/hooks/useNamespaces';
import { useQueues } from '@/hooks/useQueues';
import { useScheduledMessages, useCancelScheduledMessage } from '@/hooks/useScheduledMessages';
import { useSendMessage } from '@/hooks/useMessages';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import { CopyButton } from '@/components/CopyButton';
import { Message } from '@/lib/api/types';
import toast from 'react-hot-toast';

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
  if (diffSec < 60) return `in ${diffSec}s`;
  const diffMin = Math.floor(diffSec / 60);
  if (diffMin < 60) return `in ${diffMin}m ${diffSec % 60}s`;
  const diffHr = Math.floor(diffMin / 60);
  const diffDay = Math.floor(diffHr / 24);
  if (diffDay > 0) return `in ${diffDay}d ${diffHr % 24}h`;
  return `in ${diffHr}h ${diffMin % 60}m`;
}

function formatBytes(bytes?: number | null): string {
  if (bytes == null) return '—';
  if (bytes < 1024) return `${bytes} B`;
  return `${(bytes / 1024).toFixed(1)} KB`;
}

// ============================================================================
// RescheduleModal
// ============================================================================

interface RescheduleModalProps {
  message: Message;
  namespaceId: string;
  queueName: string;
  onClose: () => void;
}

interface ScheduledMessageRowProps {
  message: Message;
  namespaceId: string;
  queueName: string;
}

function RescheduleModal({ message, namespaceId, queueName, onClose }: RescheduleModalProps) {
  const cancel = useCancelScheduledMessage();
  const send = useSendMessage();

  // Pre-fill with the existing scheduled time (or 1 hour from now)
  const defaultValue = message.scheduledEnqueueTime
    ? new Date(message.scheduledEnqueueTime).toISOString().slice(0, 16)
    : new Date(Date.now() + 60 * 60_000).toISOString().slice(0, 16);

  const [newTime, setNewTime] = useState(defaultValue);
  const [busy, setBusy] = useState(false);

  const minValue = new Date(Date.now() + 30_000).toISOString().slice(0, 16);

  const handleReschedule = async () => {
    if (!newTime) return;
    setBusy(true);
    try {
      // 1. Cancel the existing scheduled message
      await cancel.mutateAsync({ namespaceId, queueName, sequenceNumber: message.sequenceNumber });
      // 2. Resend with updated scheduled time
      await send.mutateAsync({
        namespaceId,
        queueOrTopicName: queueName,
        entityType: 'queue',
        message: {
          body: message.body ?? '',
          contentType: message.contentType ?? 'application/json',
          correlationId: message.correlationId ?? undefined,
          sessionId: message.sessionId ?? undefined,
          scheduledEnqueueTime: new Date(newTime).toISOString(),
        },
      });
      toast.success('Message rescheduled successfully');
      onClose();
    } catch {
      // errors are handled by the mutation hooks
      setBusy(false);
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center"
      role="dialog"
      aria-modal="true"
      aria-label="Reschedule message"
    >
      <div className="absolute inset-0 bg-black/40" onClick={onClose} />
      <div className="relative bg-white rounded-2xl shadow-2xl w-full max-w-md mx-4 p-6">
        <div className="flex items-center gap-2 mb-5">
          <CalendarClock className="w-5 h-5 text-sky-600" />
          <h2 className="text-base font-semibold text-gray-900">Reschedule Message</h2>
        </div>

        <p className="text-sm text-gray-500 mb-4">
          The original scheduled message will be cancelled and re-enqueued with the new delivery time.
        </p>

        <label className="block text-sm font-medium text-gray-700 mb-1.5" htmlFor="new-schedule-time">
          New delivery time
        </label>
        <input
          id="new-schedule-time"
          type="datetime-local"
          value={newTime}
          min={minValue}
          onChange={e => setNewTime(e.target.value)}
          className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-sky-400"
        />

        <div className="flex items-center justify-end gap-2 mt-6">
          <button
            onClick={onClose}
            disabled={busy}
            className="px-4 py-2 text-sm font-medium text-gray-600 hover:text-gray-800 hover:bg-gray-50 rounded-lg transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={handleReschedule}
            disabled={busy || !newTime}
            className="flex items-center gap-1.5 px-4 py-2 text-sm font-medium bg-sky-600 text-white hover:bg-sky-700 rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <CalendarClock className="w-3.5 h-3.5" />
            {busy ? 'Rescheduling…' : 'Confirm Reschedule'}
          </button>
        </div>
      </div>
    </div>
  );
}



function ScheduledMessageRow({ message, namespaceId, queueName }: ScheduledMessageRowProps) {
  const cancel = useCancelScheduledMessage();
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [rescheduleOpen, setRescheduleOpen] = useState(false);
  // Adaptive tick: 1s when delivery is <60s away, 10s when <1hr, otherwise 30s
  const [, setTick] = useState(0);
  const scheduledTime = message.scheduledEnqueueTime;
  useEffect(() => {
    let id: ReturnType<typeof setTimeout>;
    function schedule() {
      const ms = scheduledTime ? new Date(scheduledTime).getTime() - Date.now() : Infinity;
      const interval = ms < 60_000 ? 1_000 : ms < 3_600_000 ? 10_000 : 30_000;
      id = setTimeout(() => { setTick(t => t + 1); schedule(); }, interval);
    }
    schedule();
    return () => clearTimeout(id);
  }, [scheduledTime]);

  const handleCancelClick = () => setConfirmOpen(true);

  const handleConfirm = async () => {
    setConfirmOpen(false);
    await cancel.mutateAsync({
      namespaceId,
      queueName,
      sequenceNumber: message.sequenceNumber,
    });
  };

  const shortId = message.messageId
    ? `${message.messageId.substring(0, 12)}…`
    : `#${message.sequenceNumber}`;

  return (
    <>
      <tr className="border-b border-gray-100 hover:bg-sky-50/30 transition-colors">
        <td className="px-4 py-3 text-sm font-mono text-gray-700">
          <span className="flex items-center gap-1">
            <span title={message.messageId ?? undefined} className="cursor-default">
              {shortId}
            </span>
            {message.messageId && <CopyButton text={message.messageId} label="message ID" iconSize="w-3 h-3" />}
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
          <div className="flex items-center justify-end gap-2">
            <button
              onClick={() => setRescheduleOpen(true)}
              disabled={cancel.isPending}
              className="flex items-center gap-1.5 px-2.5 py-1.5 text-xs font-medium text-sky-600 hover:text-sky-700 hover:bg-sky-50 border border-sky-200 hover:border-sky-300 rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <CalendarClock className="w-3.5 h-3.5" />
              Reschedule
            </button>
            <button
              onClick={handleCancelClick}
              disabled={cancel.isPending}
              className="flex items-center gap-1.5 px-2.5 py-1.5 text-xs font-medium text-red-600 hover:text-red-700 hover:bg-red-50 border border-red-200 hover:border-red-300 rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <XCircle className="w-3.5 h-3.5" />
              Cancel
            </button>
          </div>
        </td>
      </tr>

      {rescheduleOpen && (
        <RescheduleModal
          message={message}
          namespaceId={namespaceId}
          queueName={queueName}
          onClose={() => setRescheduleOpen(false)}
        />
      )}

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
