import { useRef, useMemo, useEffect, useCallback, memo } from 'react';
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
// Azure Service Bus provides: delivery count, queue location (active/DLQ)
// ServiceHub derives status badge from these facts for visual clarity
// ============================================================================

const STATUS_CONFIG = {
  success: {
    icon: CheckCircle,
    label: 'Normal',
    tooltip: 'First delivery attempt (delivery count = 1)',
    bgColor: 'bg-green-100',
    textColor: 'text-green-700',
    iconColor: 'text-green-600',
  },
  warning: {
    icon: AlertTriangle,
    label: 'Retried',
    tooltip: 'Message has been retried (delivery count > 1)',
    bgColor: 'bg-amber-100',
    textColor: 'text-amber-700',
    iconColor: 'text-amber-600',
  },
  error: {
    icon: XCircle,
    label: 'Dead-Letter',
    tooltip: 'Message is in the dead-letter queue',
    bgColor: 'bg-red-100',
    textColor: 'text-red-700',
    iconColor: 'text-red-600',
  },
} as const;

function StatusBadge({ status, deliveryCount }: { status: Message['status']; deliveryCount?: number }) {
  const config = STATUS_CONFIG[status];
  const Icon = config.icon;
  
  // Build detailed tooltip
  const tooltip = deliveryCount !== undefined && status !== 'success'
    ? `ServiceHub Assessment: ${config.tooltip} — Delivery count: ${deliveryCount}`
    : config.tooltip;

  return (
    <span
      className={`inline-flex items-center gap-1 px-2 py-0.5 rounded text-xs font-medium ${config.bgColor} ${config.textColor} cursor-pointer transition-opacity hover:opacity-80`}
      title={tooltip}
    >
      <Icon size={12} className={config.iconColor} />
      {config.label}
    </span>
  );
}

// ============================================================================
// Message Card Component - Memoized for performance
// Displays event type (human-readable), status, AI insight badge, and preview
// ============================================================================

interface MessageCardProps {
  message: Message;
  isSelected: boolean;
  onClick: () => void;
}

function humanizeTitle(value: string): string {
  return value
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_.-]+/g, ' ')
    .trim();
}

const MessageCard = memo(function MessageCard({ message, isSelected, onClick }: MessageCardProps) {
  // Determine display title - prefer eventType, then displayTitle, then short ID
  const displayTitle = useMemo(() => {
    if (message.eventType) return humanizeTitle(message.eventType);
    if (message.displayTitle) return humanizeTitle(message.displayTitle);
    return 'Message';
  }, [message.eventType, message.displayTitle]);

  return (
    <div
      onClick={onClick}
      className={`
        px-4 py-4 cursor-pointer transition-colors border-b border-gray-100
        ${isSelected
          ? 'bg-primary-50 border-l-4 border-l-primary-500'
          : 'bg-transparent hover:bg-gray-50 border-l-4 border-l-transparent'
        }
      `}
    >
      {/* Row 1: Event Type and Timestamp - Large, Clear */}
      <div className="flex items-center justify-between mb-3">
        <span className="font-bold text-base text-gray-900 truncate flex-1 mr-2">
          {displayTitle}
        </span>
        <span 
          className="text-xs text-gray-400 cursor-help whitespace-nowrap"
          title={message.enqueuedTime.toISOString()}
        >
          {formatRelativeTime(message.enqueuedTime)}
        </span>
      </div>

      {/* Row 2: Status and AI Badge - With breathing room */}
      <div className="flex items-center gap-2 mb-3">
        <StatusBadge status={message.status} deliveryCount={message.deliveryCount} />
        {message.hasAIInsight && (
          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-xs font-medium bg-primary-100 text-primary-700">
            <Bot size={12} />
            AI
          </span>
        )}
      </div>

      {/* Visual Separator */}
      <div className="h-px bg-gray-200 my-2" />

      {/* Row 3: Preview - Two-line JSON payload preview */}
      <p
        className="text-xs text-gray-600 leading-relaxed overflow-hidden"
        style={{
          display: '-webkit-box',
          WebkitLineClamp: 2,
          WebkitBoxOrient: 'vertical',
        }}
      >
        {message.preview}
      </p>
    </div>
  );
});

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

  // Filter messages by queue type - memoized for performance
  const filteredMessages = useMemo(
    () => messages.filter((m) => m.queueType === queueTab),
    [messages, queueTab]
  );

  // Memoized callback for selecting messages by index
  const selectMessageByIndex = useCallback((index: number) => {
    if (index >= 0 && index < filteredMessages.length) {
      onSelectMessage(filteredMessages[index].id);
    }
  }, [filteredMessages, onSelectMessage]);

  // Keyboard navigation: j/k or arrow keys to navigate messages
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Don't interfere with input fields
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) {
        return;
      }

      if (!filteredMessages.length) return;
      
      const selectedIndex = filteredMessages.findIndex(m => m.id === selectedId);
      let nextIndex = -1;

      if (e.key === 'j' || e.key === 'ArrowDown') {
        e.preventDefault();
        nextIndex = selectedIndex < filteredMessages.length - 1 ? selectedIndex + 1 : selectedIndex;
      } else if (e.key === 'k' || e.key === 'ArrowUp') {
        e.preventDefault();
        nextIndex = selectedIndex > 0 ? selectedIndex - 1 : selectedIndex;
      }

      if (nextIndex !== -1 && nextIndex !== selectedIndex) {
        selectMessageByIndex(nextIndex);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [filteredMessages, selectedId, selectMessageByIndex]);

  // Virtual list setup
  const virtualizer = useVirtualizer({
    count: filteredMessages.length,
    getScrollElement: () => parentRef.current,
    estimateSize: () => 148,
    measureElement: (element) => element.getBoundingClientRect().height,
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
                ref={virtualizer.measureElement}
                style={{
                  position: 'absolute',
                  top: 0,
                  left: 0,
                  width: '100%',
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
            <p className="text-sm text-gray-400 text-center max-w-xs">
              {queueTab === 'deadletter'
                ? 'Dead-letter queue is empty — no messages have been dead-lettered'
                : 'Active queue is empty — no messages are currently pending'}
            </p>
          </div>
        )}
      </div>
    </div>
  );
}
