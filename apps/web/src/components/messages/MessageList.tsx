import { useRef, useMemo } from 'react';
import { useVirtualizer } from '@tanstack/react-virtual';
import { Bot, AlertTriangle, CheckCircle, XCircle } from 'lucide-react';
import { formatRelativeTime } from '@/lib/utils';
import type { Message } from '@/lib/mockData';

// ============================================================================
// Types
// ============================================================================

export type QueueTab = 'active' | 'deadletter';

interface MessageListProps {
  messages: Message[];
  selectedId: string | null;
  onSelectMessage: (id: string) => void;
  queueTab: QueueTab;
  onQueueTabChange: (tab: QueueTab) => void;
  activeCounts: { active: number; deadletter: number };
}

// ============================================================================
// Status Badge Component
// ============================================================================

const STATUS_CONFIG = {
  success: {
    icon: CheckCircle,
    label: 'Success',
    bgColor: 'bg-green-100',
    textColor: 'text-green-700',
    iconColor: 'text-green-600',
  },
  warning: {
    icon: AlertTriangle,
    label: 'Warning',
    bgColor: 'bg-amber-100',
    textColor: 'text-amber-700',
    iconColor: 'text-amber-600',
  },
  error: {
    icon: XCircle,
    label: 'Error',
    bgColor: 'bg-red-100',
    textColor: 'text-red-700',
    iconColor: 'text-red-600',
  },
} as const;

function StatusBadge({ status }: { status: Message['status'] }) {
  const config = STATUS_CONFIG[status];
  const Icon = config.icon;

  return (
    <span
      className={`inline-flex items-center gap-1 px-2 py-0.5 rounded text-xs font-medium ${config.bgColor} ${config.textColor}`}
    >
      <Icon size={12} className={config.iconColor} />
      {config.label}
    </span>
  );
}

// ============================================================================
// Message Card Component
// ============================================================================

interface MessageCardProps {
  message: Message;
  isSelected: boolean;
  onClick: () => void;
}

function MessageCard({ message, isSelected, onClick }: MessageCardProps) {
  // Safely handle message ID - it may not be a hyphenated UUID
  const shortId = message.id 
    ? (message.id.includes('-') 
        ? message.id.split('-').slice(0, 2).join('-') 
        : message.id.substring(0, 16))
    : `#${message.sequenceNumber}`;

  return (
    <div
      onClick={onClick}
      className={`
        px-4 py-3 cursor-pointer transition-colors border-b border-gray-100
        ${isSelected
          ? 'bg-primary-50 border-l-4 border-l-primary-500'
          : 'bg-transparent hover:bg-gray-50 border-l-4 border-l-transparent'
        }
      `}
    >
      {/* Row 1: ID and Timestamp */}
      <div className="flex items-center justify-between mb-2">
        <span className="font-mono text-sm font-medium text-gray-900">
          {shortId}
        </span>
        <span 
          className="text-xs text-gray-500 cursor-help"
          title={message.enqueuedTime.toISOString()}
        >
          {formatRelativeTime(message.enqueuedTime)}
        </span>
      </div>

      {/* Row 2: Status and AI Badge */}
      <div className="flex items-center gap-2 mb-2">
        <StatusBadge status={message.status} />
        {message.hasAIInsight && (
          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-xs font-medium bg-primary-100 text-primary-700">
            <Bot size={12} />
            AI
          </span>
        )}
      </div>

      {/* Row 3: Preview */}
      <p className="text-sm text-gray-600 truncate">
        {message.preview}
      </p>
    </div>
  );
}

// ============================================================================
// Main MessageList Component
// ============================================================================

export function MessageList({
  messages,
  selectedId,
  onSelectMessage,
  queueTab,
  onQueueTabChange,
  activeCounts,
}: MessageListProps) {
  const parentRef = useRef<HTMLDivElement>(null);

  // Filter messages by queue type
  const filteredMessages = useMemo(
    () => messages.filter((m) => m.queueType === queueTab),
    [messages, queueTab]
  );

  // Virtual list setup
  const virtualizer = useVirtualizer({
    count: filteredMessages.length,
    getScrollElement: () => parentRef.current,
    estimateSize: () => 88, // Estimated height of each card
    overscan: 10,
  });

  const virtualItems = virtualizer.getVirtualItems();

  return (
    <div className="flex flex-col h-full border-r border-gray-200 bg-white" style={{ width: 420 }}>
      {/* Tabs Header */}
      <div className="flex border-b border-gray-200 bg-white shrink-0">
        <button
          onClick={() => onQueueTabChange('active')}
          className={`
            flex-1 px-4 py-3 text-sm font-medium transition-colors relative
            ${queueTab === 'active'
              ? 'text-primary-600 border-b-2 border-primary-500'
              : 'text-gray-500 hover:text-gray-700'
            }
          `}
        >
          Active ({activeCounts.active.toLocaleString()})
        </button>
        <button
          onClick={() => onQueueTabChange('deadletter')}
          className={`
            flex-1 px-4 py-3 text-sm font-medium transition-colors relative
            ${queueTab === 'deadletter'
              ? 'text-primary-600 border-b-2 border-primary-500'
              : 'text-gray-500 hover:text-gray-700'
            }
          `}
        >
          <span className="flex items-center justify-center gap-2">
            Dead-Letter ({activeCounts.deadletter.toLocaleString()})
            {activeCounts.deadletter > 0 && (
              <span className="w-2 h-2 rounded-full bg-red-500" />
            )}
          </span>
        </button>
      </div>

      {/* Virtualized List */}
      <div ref={parentRef} className="flex-1 overflow-auto">
        <div
          style={{
            height: virtualizer.getTotalSize(),
            width: '100%',
            position: 'relative',
          }}
        >
          {virtualItems.map((virtualItem) => {
            const message = filteredMessages[virtualItem.index];
            return (
              <div
                key={virtualItem.key}
                style={{
                  position: 'absolute',
                  top: 0,
                  left: 0,
                  width: '100%',
                  height: virtualItem.size,
                  transform: `translateY(${virtualItem.start}px)`,
                }}
              >
                <MessageCard
                  message={message}
                  isSelected={message.id === selectedId}
                  onClick={() => onSelectMessage(message.id)}
                />
              </div>
            );
          })}
        </div>

        {/* Empty State */}
        {filteredMessages.length === 0 && (
          <div className="flex flex-col items-center justify-center h-full text-gray-500 p-8">
            <CheckCircle size={48} className="text-gray-300 mb-4" />
            <p className="text-lg font-medium">No messages</p>
            <p className="text-sm text-gray-400">
              {queueTab === 'deadletter'
                ? 'No dead-letter messages in this queue'
                : 'No active messages in this queue'}
            </p>
          </div>
        )}
      </div>
    </div>
  );
}
