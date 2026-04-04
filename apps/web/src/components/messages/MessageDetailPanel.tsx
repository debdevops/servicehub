import { useState, useEffect } from 'react';
import { FileText, Code, Bot, List, Inbox, AlertTriangle, Shield } from 'lucide-react';
import { Play, Clipboard } from 'lucide-react'; // Trash2 removed - purge feature disabled
import { useSearchParams } from 'react-router-dom';
import { useTabPersistence, type DetailTab } from '@/hooks/useTabPersistence';
import { PropertiesTab, BodyTab, AIInsightsTab, HeadersTab, ForensicTab } from './tabs';
import { useReplayMessage } from '@/hooks/useMessages';
import { useNamespaces } from '@/hooks/useNamespaces';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import { CopyButton } from '@/components/CopyButton';
import type { Message } from '@/lib/mockData';
import type { AIInsight } from '@/lib/api/types';
import toast from 'react-hot-toast';

// ============================================================================
// Types
// ============================================================================

interface MessageDetailPanelProps {
  message: Message | null;
  onViewPattern?: (messageIds: string[]) => void;
  insights?: AIInsight[];
}

// ============================================================================
// Tab Configuration
// ============================================================================

const TABS: { id: DetailTab; label: string; icon: typeof FileText }[] = [
  { id: 'properties', label: 'Properties', icon: FileText },
  { id: 'body', label: 'Body', icon: Code },
  { id: 'ai', label: 'AI Insights', icon: Bot },
  { id: 'forensic', label: 'Forensic', icon: Shield },
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

function TabContent({ tab, message, onViewPattern, onForensicResult, insights }: { tab: DetailTab; message: Message; onViewPattern?: (messageIds: string[]) => void; onForensicResult?: (replaySafety: string | null) => void; insights?: AIInsight[] }) {
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
          insights={insights}
        />
      );
    case 'forensic':
      return (
        <ForensicTab
          message={message}
          onForensicResult={onForensicResult}
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
  forensicSafety: string | null;
}

interface ConfirmState {
  isOpen: boolean;
  title: string;
  message: string;
  variant: 'default' | 'danger';
  action: 'replay' | null; // 'purge' removed - Azure Service Bus limitation
}

function ActionButtons({ message, namespaceId, forensicSafety }: ActionButtonsProps) {
  const replayMessage = useReplayMessage();
  const { data: namespaces } = useNamespaces();
  const currentNs = namespaces?.find(ns => ns.id === namespaceId);
  const isProd = currentNs?.environment === 'Prod';
  const hasSendPermission = currentNs?.hasSendPermission !== false;
  // const purgeMessage = usePurgeMessage(); // Removed - Azure Service Bus limitation
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

  const openConfirm = (action: 'replay') => {
    const shortId = message.id?.split('-').slice(0, 2).join('-') || `#${message.sequenceNumber}`;
    
    if (action === 'replay') {
      setConfirmState({
        isOpen: true,
        title: 'Replay Message',
        message: `Are you sure you want to replay message ${shortId}?\n\nThis will re-send the message to the queue for processing.`,
        variant: 'default',
        action: 'replay',
      });
    }
    /* PURGE REMOVED - Azure Service Bus limitation
    else if (action === 'purge') {
      setConfirmState({
        isOpen: true,
        title: 'Permanently Delete Message',
        message: `Are you sure you want to permanently delete message ${shortId}?\n\nThis action cannot be undone.`,
        variant: 'danger',
        action: 'purge',
      });
    }
    */
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
      }
      /* PURGE REMOVED - Azure Service Bus limitation
      else if (confirmState.action === 'purge') {
        await purgeMessage.mutateAsync({ 
          namespaceId, 
          sequenceNumber: message.sequenceNumber,
          entityName,
          subscriptionName: subscriptionName || undefined,
          fromDeadLetter: isFromDeadLetter
        });
      }
      */
    } catch {
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
        {/* Replay Button — gated by forensic safety verdict */}
        <div className="flex items-center gap-2">
          {(() => {
            const isUnsafe = forensicSafety === 'Unsafe';
            const requiresReview = forensicSafety === 'RequiresReview';
            const isSafe = forensicSafety === 'Safe';
            const hasVerdict = forensicSafety !== null;

            // Production environment guard — block all replay actions
            if (isProd) {
              return (
                <button
                  disabled
                  title="Replay is disabled for production namespaces. Use your CI/CD pipeline for production message operations."
                  className="inline-flex items-center gap-2 px-4 py-2 bg-red-100 text-red-400 rounded-lg font-medium cursor-not-allowed border border-red-200"
                  aria-label="Replay blocked in production"
                >
                  <Play size={16} />
                  PROD — Replay Blocked
                </button>
              );
            }

            // Send permission guard — block replay without Manage SAS policy
            if (!hasSendPermission) {
              return (
                <button
                  disabled
                  title="Replay requires a SAS policy with Manage permission. Update your connection string to enable replay."
                  className="inline-flex items-center gap-2 px-4 py-2 bg-amber-100 text-amber-400 rounded-lg font-medium cursor-not-allowed border border-amber-200"
                  aria-label="Replay blocked — insufficient permissions"
                >
                  <Play size={16} />
                  Replay — Manage Required
                </button>
              );
            }

            if (!isFromDeadLetter) {
              return (
                <>
                  <button
                    disabled
                    title="Replay is only available for dead-letter messages — active messages are already being processed"
                    className="inline-flex items-center gap-2 px-4 py-2 bg-gray-300 text-gray-500 rounded-lg font-medium cursor-not-allowed"
                    aria-label="Replay message"
                  >
                    <Play size={16} />
                    Replay
                  </button>
                  <span className="text-xs text-gray-500 italic max-w-[160px]" title="Active messages are already queued for processing. Replay is for returning dead-letter messages to the main queue.">
                    Active messages cannot be replayed
                  </span>
                </>
              );
            }

            if (hasVerdict && isUnsafe) {
              return (
                <button
                  disabled
                  title="Forensic analysis classified this message as Unsafe to replay. Fix the root cause first."
                  className="inline-flex items-center gap-2 px-4 py-2 bg-red-100 text-red-400 rounded-lg font-medium cursor-not-allowed border border-red-200"
                  aria-label="Replay blocked"
                >
                  <Play size={16} />
                  \uD83D\uDD12 Replay Blocked
                </button>
              );
            }

            if (hasVerdict && requiresReview) {
              return (
                <button
                  onClick={() => {
                    if (window.confirm(
                      '\u26A0\uFE0F Forensic analysis marked this message as "Requires Review" before replaying.\n\n' +
                      'Are you sure you want to replay this message?'
                    )) {
                      openConfirm('replay');
                    }
                  }}
                  disabled={replayMessage.isPending || !namespaceId}
                  className="inline-flex items-center gap-2 px-4 py-2 bg-amber-100 text-amber-700 border border-amber-300 hover:bg-amber-200 rounded-lg font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                  title="Forensic analysis recommends reviewing before replay"
                  aria-label="Replay with review"
                >
                  <Play size={16} />
                  {replayMessage.isPending ? 'Replaying...' : '\u26A0\uFE0F Replay (Review First)'}
                </button>
              );
            }

            return (
              <button
                className="inline-flex items-center gap-2 px-4 py-2 bg-primary-500 hover:bg-primary-600 text-white rounded-lg font-medium transition-colors disabled:bg-gray-300 disabled:text-gray-500 disabled:cursor-not-allowed"
                onClick={() => openConfirm('replay')}
                disabled={replayMessage.isPending || !namespaceId}
                title={isSafe ? '\u2713 Forensic analysis: Safe to replay' : 'Re-send this message from DLQ back to the main queue for reprocessing'}
                aria-label="Replay message"
              >
                <Play size={16} />
                {replayMessage.isPending ? 'Replaying...' : (isSafe ? '\u2713 Replay' : 'Replay')}
              </button>
            );
          })()}
        </div>
        <button
          className="inline-flex items-center gap-2 px-4 py-2 bg-white hover:bg-gray-50 text-gray-700 border border-gray-200 rounded-lg font-medium transition-colors"
          onClick={handleCopyId}
          aria-label="Copy message ID to clipboard"
        >
          <Clipboard size={16} />
          Copy ID
        </button>
        {/* PURGE BUTTON DISABLED - Azure Service Bus limitation prevents reliable individual message deletion
           The Service Bus SDK doesn't support direct access to messages by sequence number for active queues.
           Scanning through messages is too slow and times out for large queues.
           This feature can be re-enabled if Microsoft adds support for targeted message deletion.
        <button
          className="inline-flex items-center gap-2 px-4 py-2 bg-white hover:bg-red-50 text-gray-700 hover:text-red-600 border border-gray-200 hover:border-red-200 rounded-lg font-medium transition-colors disabled:bg-gray-100 disabled:cursor-not-allowed"
          onClick={() => openConfirm('purge')}
          disabled={purgeMessage.isPending || !namespaceId}
          aria-label="Permanently delete message"
        >
          <Trash2 size={16} />
          {purgeMessage.isPending ? 'Purging...' : 'Purge'}
        </button>
        */}
      </div>

      <ConfirmDialog
        isOpen={confirmState.isOpen}
        title={confirmState.title}
        message={confirmState.message}
        variant={confirmState.variant}
        confirmLabel={'Confirm'} // Was: confirmState.action === 'purge' ? 'Delete' : 'Confirm'
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

export function MessageDetailPanel({ message, onViewPattern, insights }: MessageDetailPanelProps) {
  const [activeTab, setActiveTab] = useTabPersistence();
  const [searchParams] = useSearchParams();
  const namespaceId = searchParams.get('namespace');
  const [forensicSafety, setForensicSafety] = useState<string | null>(
    message?.replaySafety ?? null
  );

  // Reset forensic verdict when the selected message changes
  useEffect(() => {
    setForensicSafety(message?.replaySafety ?? null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [message?.id]);

  if (!message) {
    return <EmptyState />;
  }

  const { title, subtitle } = extractMessageTitle(message);
  const isDLQ = message.queueType === 'deadletter' || !!message.deadLetterReason;
  
  // Get DLQ severity for appropriate styling
  const getDLQSeverity = (msg: Message): 'test' | 'warning' | 'critical' => {
    const reason = (msg.deadLetterReason || '').toLowerCase();
    const description = (msg.deadLetterSource || '').toLowerCase();
    if (reason.includes('test') || reason.includes('demo') || reason.includes('manual') ||
        description.includes('servicehub') || description.includes('testing')) {
      return 'test';
    }
    if (msg.deliveryCount > 5) {
      return 'critical';
    }
    return 'warning';
  };
  
  const dlqSeverity = isDLQ ? getDLQSeverity(message) : null;

  return (
    <div className="flex-1 flex flex-col bg-gray-50 overflow-hidden">
      {/* Header */}
      <div className={`shrink-0 px-6 py-4 border-b border-gray-200 ${
        isDLQ && dlqSeverity === 'critical' ? 'bg-red-50' : 'bg-white'
      }`}>
        <div className="flex items-start justify-between">
          <div>
            <h2 className="text-xl font-semibold text-gray-900">
              {title}
            </h2>
            {subtitle && (
              <p className="text-sm text-gray-500 mt-1 font-mono">{subtitle}</p>
            )}
          </div>
          <CopyButton
            text={(() => {
              const base = `${window.location.origin}/messages?namespace=${namespaceId || ''}`;
              const queueParam = searchParams.get('queue');
              const topicParam = searchParams.get('topic');
              const subParam = searchParams.get('subscription');
              const entityPart = queueParam
                ? `&queue=${queueParam}`
                : topicParam
                  ? `&topic=${topicParam}${subParam ? `&subscription=${subParam}` : ''}`
                  : '';
              return `${base}${entityPart}&messageId=${message.id}`;
            })()}
            label="Copy Link"
            className="shrink-0 ml-3 px-2 py-1 border border-gray-200 rounded-lg"
            iconSize="w-4 h-4"
          />
        </div>
        {isDLQ && (
          <div className="mt-2 flex items-center gap-2">
            <span className={`inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium ${
              dlqSeverity === 'critical'
                ? 'bg-red-100 text-red-800'
                : 'bg-amber-100 text-amber-800'
            }`}>
              <AlertTriangle size={12} className={dlqSeverity === 'critical' ? 'text-red-600' : 'text-amber-600'} />
              ServiceHub Assessment: {dlqSeverity === 'critical' ? 'Critical' : 'Warning'}
            </span>
            <span className="text-sm text-gray-600" title="The reason provided by Azure Service Bus.">
              {message.deadLetterReason}
            </span>
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
        <TabContent tab={activeTab} message={message} onViewPattern={onViewPattern} onForensicResult={setForensicSafety} insights={insights} />
      </div>

      {/* Action Buttons */}
      <ActionButtons message={message} namespaceId={namespaceId} forensicSafety={forensicSafety} />
    </div>
  );
}
