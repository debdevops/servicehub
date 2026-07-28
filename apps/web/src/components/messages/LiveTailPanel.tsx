import { useEffect } from 'react';
import { X, Radio, Trash2, Play, Pause } from 'lucide-react';
import { useLiveTail } from '@servicehub/ui-shared/hooks/useLiveTail';
import type { LiveTailStatus } from '@servicehub/ui-shared/hooks/useLiveTail';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

interface LiveTailPanelProps {
  isOpen: boolean;
  onClose: () => void;
  namespaceId: string;
  entityName: string;
  subscriptionName?: string;
  fromDeadLetter?: boolean;
}

const STATUS_LABEL: Record<LiveTailStatus, string> = {
  idle: 'Stopped',
  connecting: 'Connecting…',
  connected: 'Live',
  disconnected: 'Disconnected',
  unsupported: 'Not available',
};

const STATUS_DOT: Record<LiveTailStatus, string> = {
  idle: 'bg-gray-300',
  connecting: 'bg-amber-400 animate-pulse',
  connected: 'bg-emerald-500 animate-pulse',
  disconnected: 'bg-red-400',
  unsupported: 'bg-gray-300',
};

function formatRelativeTime(iso: string): string {
  const diffMs = Date.now() - new Date(iso).getTime();
  const seconds = Math.max(0, Math.floor(diffMs / 1000));
  if (seconds < 5) return 'just now';
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  return `${hours}h ago`;
}

export function LiveTailPanel({
  isOpen,
  onClose,
  namespaceId,
  entityName,
  subscriptionName,
  fromDeadLetter,
}: LiveTailPanelProps) {
  const { isDemoMode } = useDemoContext();
  const { status, messages, start, stop, clear } = useLiveTail({
    namespaceId,
    entityName,
    subscriptionName,
    fromDeadLetter,
  });

  // Start watching the moment the panel opens; always stop when it closes so a
  // background poll never keeps running against a panel the user can't see.
  useEffect(() => {
    if (isOpen) {
      start();
    } else {
      stop();
    }
    // start/stop are stable (useCallback with no changing deps) — only isOpen should retrigger this.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen]);

  if (!isOpen) return null;

  const isRunning = status === 'connected' || status === 'connecting';

  return (
    <>
      <div className="fixed inset-0 bg-black/30 z-40" onClick={onClose} />
      <div className="fixed right-0 top-0 h-full w-[440px] max-w-[90vw] bg-white shadow-2xl z-50 flex flex-col">
        <div className="flex items-center justify-between p-4 border-b border-gray-200 bg-gradient-to-r from-emerald-50 to-white">
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              <Radio className="w-4 h-4 text-emerald-600" />
              <h2 className="font-semibold text-gray-900">Live Tail</h2>
            </div>
            <p className="text-xs text-gray-500 mt-0.5 truncate max-w-[320px]">
              {subscriptionName ? `${entityName} / ${subscriptionName}` : entityName}
            </p>
          </div>
          <button
            onClick={onClose}
            className="p-1.5 hover:bg-gray-100 rounded-lg transition-colors"
            aria-label="Close Live Tail"
          >
            <X className="w-5 h-5 text-gray-500" />
          </button>
        </div>

        <div className="flex items-center justify-between px-4 py-2.5 border-b border-gray-100">
          <div className="flex items-center gap-2 text-sm">
            <span className={`w-2 h-2 rounded-full ${STATUS_DOT[status]}`} />
            <span className="text-gray-700 font-medium">{STATUS_LABEL[status]}</span>
            {messages.length > 0 && (
              <span className="text-gray-400">· {messages.length} message{messages.length === 1 ? '' : 's'}</span>
            )}
          </div>
          <div className="flex items-center gap-1.5">
            <button
              onClick={clear}
              disabled={messages.length === 0}
              className="p-1.5 hover:bg-gray-100 rounded-lg transition-colors disabled:opacity-40 disabled:hover:bg-transparent"
              aria-label="Clear messages"
              title="Clear"
            >
              <Trash2 className="w-4 h-4 text-gray-500" />
            </button>
            <button
              onClick={() => (isRunning ? stop() : start())}
              disabled={status === 'unsupported'}
              className="p-1.5 hover:bg-gray-100 rounded-lg transition-colors disabled:opacity-40 disabled:hover:bg-transparent"
              aria-label={isRunning ? 'Pause Live Tail' : 'Resume Live Tail'}
              title={isRunning ? 'Pause' : 'Resume'}
            >
              {isRunning ? <Pause className="w-4 h-4 text-gray-500" /> : <Play className="w-4 h-4 text-gray-500" />}
            </button>
          </div>
        </div>

        <div className="flex-1 overflow-y-auto">
          {status === 'unsupported' && (
            <div className="p-4 m-4 bg-amber-50 border border-amber-200 rounded-lg text-sm text-amber-800">
              {isDemoMode
                ? "Live Tail isn't available in Demo Mode — this is a read-only preview with pre-seeded data, not a live provider connection."
                : "Live Tail isn't available for this namespace's provider. AWS SQS has no " +
                  "non-destructive peek — repeated polling would risk pushing messages over the " +
                  "queue's redelivery limit and accidentally dead-lettering them."}
            </div>
          )}

          {status !== 'unsupported' && messages.length === 0 && (
            <div className="flex flex-col items-center justify-center h-40 text-gray-400 text-sm px-6 text-center">
              {status === 'connected'
                ? 'Watching for new messages…'
                : status === 'connecting'
                  ? 'Connecting…'
                  : 'No messages captured yet.'}
            </div>
          )}

          <ul className="divide-y divide-gray-100">
            {messages.map((message, index) => (
              <li key={`${message.messageId || message.sequenceNumber}-${index}`} className="p-3 hover:bg-gray-50">
                <div className="flex items-center justify-between gap-2 mb-1">
                  <span className="text-xs font-mono text-gray-500 truncate">
                    {message.messageId || `seq-${message.sequenceNumber}`}
                  </span>
                  <span className="text-xs text-gray-400 whitespace-nowrap">
                    {formatRelativeTime(message.enqueuedTime)}
                  </span>
                </div>
                {message.deliveryCount > 1 && (
                  <span className="inline-block px-1.5 py-0.5 mb-1 text-[10px] font-medium rounded bg-amber-50 text-amber-700 border border-amber-200">
                    Delivery #{message.deliveryCount}
                  </span>
                )}
                <p className="text-sm text-gray-700 truncate">
                  {message.body ? message.body.slice(0, 140) : '[No body]'}
                </p>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </>
  );
}
