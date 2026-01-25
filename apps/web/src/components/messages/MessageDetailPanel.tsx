import { useState } from 'react';
import { FileText, Code, Bot, List, Inbox } from 'lucide-react';
import { Play, Clipboard, Trash2 } from 'lucide-react';
import { useSearchParams } from 'react-router-dom';
import { useTabPersistence, type DetailTab } from '@/hooks/useTabPersistence';
import { PropertiesTab, BodyTab, AIInsightsTab, HeadersTab } from './tabs';
import { useReplayMessage, usePurgeMessage } from '@/hooks/useMessages';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import type { Message } from '@/lib/mockData';
import toast from 'react-hot-toast';

// ============================================================================
// Types
// ============================================================================

interface MessageDetailPanelProps {
  message: Message | null;
  onViewPattern?: (messageIds: string[]) => void;
}

// ============================================================================
// Tab Configuration
// ============================================================================

const TABS: { id: DetailTab; label: string; icon: typeof FileText }[] = [
  { id: 'properties', label: 'Properties', icon: FileText },
  { id: 'body', label: 'Body', icon: Code },
  { id: 'ai', label: 'AI Insights', icon: Bot },
  { id: 'headers', label: 'Headers', icon: List },
];

// ============================================================================
// Empty State
// ============================================================================

function EmptyState() {
  return (
    <div className="flex-1 flex flex-col items-center justify-center text-gray-500 bg-gray-50">
      <div className="bg-white rounded-xl border border-gray-200 shadow-sm px-10 py-12 text-center" style={{ maxWidth: 520 }}>
        <Inbox size={64} className="text-gray-300 mx-auto mb-4" />
        <h3 className="text-xl font-semibold text-gray-700">No Message Selected</h3>
        <p className="text-sm text-gray-500 mt-2">
          Select a message from the list to view its details, body content, and AI insights.
        </p>
      </div>
    </div>
  );
}

// ============================================================================
// Tab Content Renderer
// ============================================================================

function TabContent({ tab, message, onViewPattern }: { tab: DetailTab; message: Message; onViewPattern?: (messageIds: string[]) => void }) {
  switch (tab) {
    case 'properties':
      return <PropertiesTab message={message} />;
    case 'body':
      return <BodyTab body={message.body} contentType={message.contentType} />;
    case 'ai':
      return (
        <AIInsightsTab 
          message={message}
          onViewPattern={onViewPattern}
        />
      );
    case 'headers':
      return <HeadersTab headers={message.headers} />;
    default:
      return null;
  }
}

// ============================================================================
// Action Buttons
// ============================================================================

interface ActionButtonsProps {
  message: Message;
  namespaceId: string | null;
}

interface ConfirmState {
  isOpen: boolean;
  title: string;
  message: string;
  variant: 'default' | 'danger';
  action: 'replay' | 'purge' | null;
}

function ActionButtons({ message, namespaceId }: ActionButtonsProps) {
  const replayMessage = useReplayMessage();
  const purgeMessage = usePurgeMessage();
  const [searchParams] = useSearchParams();
  
  const [confirmState, setConfirmState] = useState<ConfirmState>({
    isOpen: false,
    title: '',
    message: '',
    variant: 'default',
    action: null,
  });

  // Get entity information from URL params
  const queueName = searchParams.get('queue');
  const topicName = searchParams.get('topic');
  const subscriptionName = searchParams.get('subscription');
  
  // Determine entity name and type
  const entityName = topicName || queueName || '';
  const isFromDeadLetter = message.queueType === 'deadletter' || !!message.deadLetterReason;

  const openConfirm = (action: 'replay' | 'purge') => {
    const shortId = message.id?.split('-').slice(0, 2).join('-') || `#${message.sequenceNumber}`;
    
    if (action === 'replay') {
      setConfirmState({
        isOpen: true,
        title: 'Replay Message',
        message: `Are you sure you want to replay message ${shortId}?\n\nThis will re-send the message to the queue for processing.`,
        variant: 'default',
        action: 'replay',
      });
    } else if (action === 'purge') {
      setConfirmState({
        isOpen: true,
        title: 'Permanently Delete Message',
        message: `Are you sure you want to permanently delete message ${shortId}?\n\nThis action cannot be undone.`,
        variant: 'danger',
        action: 'purge',
      });
    }
  };

  const handleConfirm = async () => {
    if (!namespaceId) {
      toast.error('Namespace context missing');
      return;
    }

    if (!entityName) {
      toast.error('Queue or topic name is missing');
      return;
    }

    try {
      if (confirmState.action === 'replay') {
        await replayMessage.mutateAsync({ 
          namespaceId, 
          sequenceNumber: message.sequenceNumber,
          entityName,
          subscriptionName: subscriptionName || undefined
        });
      } else if (confirmState.action === 'purge') {
        await purgeMessage.mutateAsync({ 
          namespaceId, 
          sequenceNumber: message.sequenceNumber,
          entityName,
          subscriptionName: subscriptionName || undefined,
          fromDeadLetter: isFromDeadLetter
        });
      }
    } catch (error) {
      // Error handled by mutation hook
    } finally {
      setConfirmState(prev => ({ ...prev, isOpen: false, action: null }));
    }
  };

  const handleCancel = () => {
    setConfirmState(prev => ({ ...prev, isOpen: false, action: null }));
  };

  const handleCopyId = async () => {
    try {
      await navigator.clipboard.writeText(message.id);
      toast.success('Message ID copied to clipboard');
    } catch {
      toast.error('Failed to copy ID');
    }
  };

  return (
    <>
      <div className="flex items-center gap-3 p-4 border-t border-gray-200 bg-white">
        <button
          className="inline-flex items-center gap-2 px-4 py-2 bg-primary-500 hover:bg-primary-600 text-white rounded-lg font-medium transition-colors disabled:bg-primary-300 disabled:cursor-not-allowed"
          onClick={() => openConfirm('replay')}
          disabled={replayMessage.isPending || !namespaceId || !isFromDeadLetter}
          title={!isFromDeadLetter ? 'Replay is only available for dead-letter messages' : 'Replay message to main queue'}
          aria-label="Replay message"
        >
          <Play size={16} />
          {replayMessage.isPending ? 'Replaying...' : 'Replay'}
        </button>
        <button
          className="inline-flex items-center gap-2 px-4 py-2 bg-white hover:bg-gray-50 text-gray-700 border border-gray-200 rounded-lg font-medium transition-colors"
          onClick={handleCopyId}
          aria-label="Copy message ID to clipboard"
        >
          <Clipboard size={16} />
          Copy ID
        </button>
        <button
          className="inline-flex items-center gap-2 px-4 py-2 bg-white hover:bg-red-50 text-gray-700 hover:text-red-600 border border-gray-200 hover:border-red-200 rounded-lg font-medium transition-colors disabled:bg-gray-100 disabled:cursor-not-allowed"
          onClick={() => openConfirm('purge')}
          disabled={purgeMessage.isPending || !namespaceId}
          aria-label="Permanently delete message"
        >
          <Trash2 size={16} />
          {purgeMessage.isPending ? 'Purging...' : 'Purge'}
        </button>
      </div>

      <ConfirmDialog
        isOpen={confirmState.isOpen}
        title={confirmState.title}
        message={confirmState.message}
        variant={confirmState.variant}
        confirmLabel={confirmState.action === 'purge' ? 'Delete' : 'Confirm'}
        onConfirm={handleConfirm}
        onCancel={handleCancel}
      />
    </>
  );
}

// ============================================================================
// Main Component
// ============================================================================

// Helper to extract meaningful title from message
function extractMessageTitle(message: Message): { title: string; subtitle: string } {
  // Try to parse body as JSON to extract meaningful info
  try {
    const body = typeof message.body === 'string' ? JSON.parse(message.body) : message.body;
    
    // Common patterns for extracting meaningful titles
    const eventType = body?.eventType || body?.type || body?.event;
    const orderId = body?.data?.orderId || body?.orderId;
    const transactionId = body?.data?.transactionId || body?.transactionId;
    const notificationId = body?.data?.notificationId || body?.notificationId;
    const userId = body?.data?.userId || body?.userId;
    const errorCode = body?.data?.error?.code || body?.error?.code || body?.errorCode;
    const status = body?.data?.status || body?.status;
    
    // Build meaningful title based on available data
    if (eventType) {
      const formattedEvent = eventType.replace(/([A-Z])/g, ' $1').trim();
      let subtitle = '';
      
      if (orderId) subtitle = `Order: ${orderId}`;
      else if (transactionId) subtitle = `Transaction: ${transactionId}`;
      else if (notificationId) subtitle = `Notification: ${notificationId}`;
      else if (errorCode) subtitle = `Error: ${errorCode}`;
      else if (status) subtitle = `Status: ${status}`;
      
      return { title: formattedEvent, subtitle };
    }
    
    // Fallback to any identifiable field
    if (orderId) return { title: 'Order Message', subtitle: orderId };
    if (transactionId) return { title: 'Payment Transaction', subtitle: transactionId };
    if (notificationId) return { title: 'Notification', subtitle: notificationId };
    if (errorCode) return { title: 'Error Event', subtitle: errorCode };
    if (userId) return { title: 'User Activity', subtitle: `User: ${userId}` };
  } catch {
    // Body is not valid JSON
  }
  
  // Use message ID or sequence number
  const shortId = message.id 
    ? (message.id.includes('-') 
        ? message.id.split('-').slice(0, 2).join('-') 
        : message.id.substring(0, 12))
    : `#${message.sequenceNumber}`;
  return { title: 'Message', subtitle: shortId };
}

export function MessageDetailPanel({ message, onViewPattern }: MessageDetailPanelProps) {
  const [activeTab, setActiveTab] = useTabPersistence();
  const [searchParams] = useSearchParams();
  const namespaceId = searchParams.get('namespace');

  if (!message) {
    return <EmptyState />;
  }

  const { title, subtitle } = extractMessageTitle(message);
  const isDLQ = message.queueType === 'deadletter' || !!message.deadLetterReason;

  return (
    <div className="flex-1 flex flex-col bg-gray-50 overflow-hidden">
      {/* Header */}
      <div className={`shrink-0 px-6 py-4 border-b border-gray-200 ${isDLQ ? 'bg-red-50' : 'bg-white'}`}>
        <h2 className="text-xl font-semibold text-gray-900">
          {title}
        </h2>
        {subtitle && (
          <p className="text-sm text-gray-500 mt-1 font-mono">{subtitle}</p>
        )}
        {isDLQ && (
          <div className="mt-2 flex items-center gap-2 text-red-600">
            <span className="inline-flex items-center gap-1 px-2 py-1 bg-red-100 rounded text-xs font-medium">
              ⚠️ Dead-Letter Queue
            </span>
            {message.deadLetterReason && (
              <span className="text-sm">{message.deadLetterReason}</span>
            )}
          </div>
        )}
      </div>

      {/* Tab Bar */}
      <div className="shrink-0 flex border-b border-gray-200 bg-white">
        {TABS.map(({ id, label, icon: Icon }) => (
          <button
            key={id}
            onClick={() => setActiveTab(id)}
            className={`
              flex items-center gap-2 px-4 py-3 text-sm font-medium transition-colors relative
              ${activeTab === id
                ? 'text-primary-600 border-b-2 border-primary-500 -mb-px'
                : 'text-gray-500 hover:text-gray-700 hover:bg-gray-50'
              }
            `}
          >
            <Icon size={16} />
            {label}
          </button>
        ))}
      </div>

      {/* Tab Content */}
      <div className="flex-1 overflow-auto bg-gray-50">
        <TabContent tab={activeTab} message={message} onViewPattern={onViewPattern} />
      </div>

      {/* Action Buttons */}
      <ActionButtons message={message} namespaceId={namespaceId} />
    </div>
  );
}
