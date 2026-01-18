import { FileText, Code, Bot, List, Inbox } from 'lucide-react';
import { Play, Clipboard, Zap, Trash2 } from 'lucide-react';
import { useSearchParams } from 'react-router-dom';
import { useTabPersistence, type DetailTab } from '@/hooks/useTabPersistence';
import { PropertiesTab, BodyTab, AIInsightsTab, HeadersTab } from './tabs';
import { useReplayMessage, usePurgeMessage } from '@/hooks/useMessages';
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

function ActionButtons({ message, namespaceId }: ActionButtonsProps) {
  const replayMessage = useReplayMessage();
  const purgeMessage = usePurgeMessage();

  const handleReplay = async () => {
    if (!namespaceId) {
      toast.error('Namespace context missing');
      return;
    }

    if (confirm(`Replay message ${message.id}?`)) {
      try {
        await replayMessage.mutateAsync({ namespaceId, messageId: message.id });
      } catch (error) {
        // Error handled by mutation hook
      }
    }
  };

  const handleCopyId = async () => {
    try {
      await navigator.clipboard.writeText(message.id);
      toast.success('Message ID copied to clipboard');
    } catch {
      toast.error('Failed to copy ID');
    }
  };

  const handleResubmit = async () => {
    if (!namespaceId) {
      toast.error('Namespace context missing');
      return;
    }

    if (confirm(`Resubmit message ${message.id}?`)) {
      try {
        await replayMessage.mutateAsync({ namespaceId, messageId: message.id });
        toast.success(`Message ${message.id} resubmitted successfully`);
      } catch (error) {
        // Error handled by mutation hook
      }
    }
  };

  const handlePurge = async () => {
    if (!namespaceId) {
      toast.error('Namespace context missing');
      return;
    }

    if (confirm(`⚠️ Permanently delete message ${message.id}?\n\nThis action cannot be undone.`)) {
      try {
        await purgeMessage.mutateAsync({ namespaceId, messageId: message.id });
      } catch (error) {
        // Error handled by mutation hook
      }
    }
  };

  return (
    <div className="flex items-center gap-3 p-4 border-t border-gray-200 bg-white">
      <button
        className="inline-flex items-center gap-2 px-4 py-2 bg-primary-500 hover:bg-primary-600 text-white rounded-lg font-medium transition-colors disabled:bg-primary-300 disabled:cursor-not-allowed"
        onClick={handleReplay}
        disabled={replayMessage.isPending || !namespaceId}
      >
        <Play size={16} />
        {replayMessage.isPending ? 'Replaying...' : 'Replay'}
      </button>
      <button
        className="inline-flex items-center gap-2 px-4 py-2 bg-white hover:bg-gray-50 text-gray-700 border border-gray-200 rounded-lg font-medium transition-colors"
        onClick={handleCopyId}
      >
        <Clipboard size={16} />
        Copy ID
      </button>
      <button
        className="inline-flex items-center gap-2 px-4 py-2 bg-white hover:bg-gray-50 text-gray-700 border border-gray-200 rounded-lg font-medium transition-colors disabled:bg-gray-100 disabled:cursor-not-allowed"
        onClick={handleResubmit}
        disabled={replayMessage.isPending || !namespaceId}
      >
        <Zap size={16} />
        Resubmit
      </button>
      <button
        className="inline-flex items-center gap-2 px-4 py-2 bg-white hover:bg-red-50 text-gray-700 hover:text-red-600 border border-gray-200 hover:border-red-200 rounded-lg font-medium transition-colors disabled:bg-gray-100 disabled:cursor-not-allowed"
        onClick={handlePurge}
        disabled={purgeMessage.isPending || !namespaceId}
      >
        <Trash2 size={16} />
        {purgeMessage.isPending ? 'Purging...' : 'Purge'}
      </button>
    </div>
  );
}

// ============================================================================
// Main Component
// ============================================================================

export function MessageDetailPanel({ message, onViewPattern }: MessageDetailPanelProps) {
  const [activeTab, setActiveTab] = useTabPersistence();
  const [searchParams] = useSearchParams();
  const namespaceId = searchParams.get('namespace');

  if (!message) {
    return <EmptyState />;
  }

  const shortId = message.id.split('-').slice(0, 2).join('-');

  return (
    <div className="flex-1 flex flex-col bg-gray-50 overflow-hidden">
      {/* Header */}
      <div className="shrink-0 px-6 py-4 border-b border-gray-200 bg-white">
        <h2 className="text-xl font-semibold text-gray-900">
          Message: {shortId}
        </h2>
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
            {id === 'ai' && message.hasAIInsight && (
              <span className="ml-1 w-2 h-2 rounded-full bg-primary-500" />
            )}
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
