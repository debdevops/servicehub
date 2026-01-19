import { useState, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Search, Filter, RefreshCw, Sparkles, X } from 'lucide-react';
import { MessageList, MessageDetailPanel, type QueueTab } from '@/components/messages';
import { AIFindingsDropdown } from '@/components/ai';
import { MessageListSkeleton } from '@/components/messages/MessageListSkeleton';
import { useMessages } from '@/hooks/useMessages';
import { useInsights, useInsightsSummary } from '@/hooks/useInsights';
import type { Message } from '@/lib/mockData';
import type { Message as APIMessage } from '@/lib/api/types';
import toast from 'react-hot-toast';

// Transform API message to UI message format
function transformMessage(apiMessage: APIMessage, insightMessageIds: string[] = []): Message {
  return {
    ...apiMessage,
    enqueuedTime: new Date(apiMessage.enqueuedTime),
    status: apiMessage.state.toLowerCase() as any,
    preview: apiMessage.body.substring(0, 100),
    hasAIInsight: insightMessageIds.includes(apiMessage.id),
    contentType: (apiMessage.contentType || 'application/json') as any,
    sequenceNumber: apiMessage.sequenceNumber || 0,
    timeToLive: apiMessage.timeToLive || '',
    lockToken: apiMessage.lockToken || '',
  };
}

/**
 * Message Inspector Page - Split View Layout
 * 
 * Features:
 * - Left: Virtualized message card list (420px)
 * - Right: Detail panel with persistent tabs
 * - Queue tabs: Active / Dead-Letter filtering
 * - Real API integration with Service Bus
 * - FAB for sending messages
 * - AI pattern detection with evidence filtering
 */
export function MessagesPage() {
  const [searchParams] = useSearchParams();
  const namespaceId = searchParams.get('namespace');
  const queueName = searchParams.get('queue');

  // Selected message for detail panel
  const [selectedMessageId, setSelectedMessageId] = useState<string | null>(null);
  
  // Queue tab: active or deadletter
  const [queueTab, setQueueTab] = useState<QueueTab>('active');

  // Fetch messages from API
  const { data: messagesData, isLoading, error, refetch } = useMessages({
    namespaceId: namespaceId || '',
    queueOrTopicName: queueName || '',
    queueType: queueTab,
    skip: 0,
    take: 1000,
  });

  // Fetch AI insights for this queue
  const { data: insights } = useInsights({
    namespaceId: namespaceId || '',
    queueOrTopicName: queueName || undefined,
    status: 'active',
  });

  // Fetch AI insights summary for badge
  const { data: insightsSummary } = useInsightsSummary(
    namespaceId || '',
    queueName || ''
  );

  // AI dropdown visibility
  const [showAIDropdown, setShowAIDropdown] = useState(false);

  // Evidence filter (when viewing AI pattern affected messages)
  const [evidenceFilter, setEvidenceFilter] = useState<string[] | null>(null);

  // Get all message IDs that have AI insights
  const insightMessageIds = useMemo(() => {
    if (!insights) return [];
    const ids = new Set<string>();
    insights.forEach(insight => {
      insight.evidence.affectedMessageIds.forEach(msgId => ids.add(msgId));
    });
    return Array.from(ids);
  }, [insights]);

  // Get messages from API or empty array
  const messages: Message[] = (messagesData?.items || []).map(msg => 
    transformMessage(msg, insightMessageIds)
  );

  // Find selected message
  const selectedMessage = useMemo(
    () => messages.find(m => m.id === selectedMessageId) ?? null,
    [messages, selectedMessageId]
  );

  // Filter messages by evidence filter
  const filteredMessages = useMemo(() => {
    if (evidenceFilter) {
      return messages.filter(m => evidenceFilter.includes(m.id));
    }
    return messages;
  }, [messages, evidenceFilter]);

  // Active AI insights count from summary
  const activeInsightsCount = insightsSummary?.activeCount || 0;

  // Handle message selection
  const handleSelectMessage = (id: string) => {
    setSelectedMessageId(id);
  };

  // Handle queue tab change
  const handleQueueTabChange = (tab: QueueTab) => {
    setQueueTab(tab);
    setSelectedMessageId(null); // Clear selection when switching tabs
  };

  // Handle viewing evidence from AI pattern
  const handleViewEvidence = (messageIds: string[]) => {
    setEvidenceFilter(messageIds);
    setShowAIDropdown(false);
    toast.success(`Showing ${messageIds.length} affected messages`);
  };

  // Clear evidence filter
  const clearEvidenceFilter = () => {
    setEvidenceFilter(null);
    toast.success('Filter cleared');
  };

  // Handle refresh button
  const handleRefresh = () => {
    refetch();
    toast.success('Messages refreshed');
  };

  // Loading state
  if (isLoading) {
    return (
      <div className="flex-1 flex flex-col overflow-hidden">
        <div className="bg-white border-b border-gray-200 px-4 py-3 flex items-center gap-3 shrink-0">
          <div className="flex-1 text-sm text-gray-500">Loading messages...</div>
        </div>
        <MessageListSkeleton />
      </div>
    );
  }

  // Error state
  if (error) {
    return (
      <div className="flex-1 flex items-center justify-center bg-gray-50">
        <div className="text-center max-w-md p-8">
          <div className="text-6xl mb-4">⚠️</div>
          <h2 className="text-xl font-semibold text-gray-900 mb-2">Failed to load messages</h2>
          <p className="text-gray-600 mb-4">
            {error instanceof Error ? error.message : 'An error occurred'}
          </p>
          <button
            onClick={() => refetch()}
            className="px-4 py-2 bg-primary-500 text-white rounded-lg hover:bg-primary-600"
          >
            Try Again
          </button>
        </div>
      </div>
    );
  }

  // Empty state (no namespace/queue selected)
  if (!namespaceId || !queueName) {
    return (
      <div className="flex-1 flex items-center justify-center bg-gray-50">
        <div className="text-center max-w-md p-8">
          <div className="text-6xl mb-4">📬</div>
          <h2 className="text-xl font-semibold text-gray-900 mb-2">No queue selected</h2>
          <p className="text-gray-600">
            Select a queue from the sidebar to view its messages
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex-1 flex flex-col overflow-hidden relative">
      {/* Toolbar */}
      <div className="bg-white border-b border-gray-200 px-4 py-3 flex items-center gap-3 shrink-0">
        {/* Search - Placeholder only (API-driven later) */}
        <div className="flex-1 max-w-md relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input
            type="text"
            placeholder="Search messages by ID, properties, or content..."
            disabled
            className="w-full pl-10 pr-4 py-2.5 rounded-lg text-sm bg-gray-50 border border-gray-200 text-gray-400 cursor-not-allowed"
          />
        </div>

        {/* Filter Button - Disabled for now */}
        <button 
          disabled
          className="flex items-center gap-2 px-3 py-2 border border-gray-200 rounded-lg text-sm text-gray-400 bg-gray-50 cursor-not-allowed"
        >
          <Filter className="w-4 h-4" />
          Filter
        </button>

        {/* AI Findings Button */}
        <div className="relative">
          <button 
            onClick={() => setShowAIDropdown(!showAIDropdown)}
            className={`flex items-center gap-2 px-3 py-2 border rounded-lg text-sm font-medium transition-colors ${
              showAIDropdown 
                ? 'border-primary-300 bg-primary-50 text-primary-700'
                : 'border-gray-200 hover:bg-gray-50 text-gray-700'
            }`}
          >
            <Sparkles className="w-4 h-4 text-primary-500" />
            AI Findings: {activeInsightsCount}
          </button>

          {showAIDropdown && (
            <AIFindingsDropdown
              insights={insights || []}
              onClose={() => setShowAIDropdown(false)}
              onViewEvidence={handleViewEvidence}
            />
          )}
        </div>

        {/* Refresh */}
        <button 
          onClick={handleRefresh}
          className="flex items-center gap-2 px-3 py-2 bg-primary-500 hover:bg-primary-600 text-white rounded-lg text-sm font-medium transition-colors"
        >
          <RefreshCw className="w-4 h-4" />
          Refresh
        </button>
      </div>

      {/* Evidence Filter Banner */}
      {evidenceFilter && (
        <div className="bg-primary-50 border-b border-primary-200 px-4 py-3 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Sparkles className="w-4 h-4 text-primary-500" />
            <span className="text-sm text-primary-700">
              Showing <strong>{evidenceFilter.length}</strong> of {messages.length.toLocaleString()} messages
              <span className="text-primary-500 ml-1">(AI pattern filter active)</span>
            </span>
          </div>
          <button 
            onClick={clearEvidenceFilter}
            className="flex items-center gap-1 text-sm text-primary-600 hover:text-primary-700 font-medium"
          >
            Clear filter
            <X className="w-4 h-4" />
          </button>
        </div>
      )}

      {/* Split View: Message List + Detail Panel */}
      <div className="flex-1 flex overflow-hidden">
        {/* Left: Message List */}
        <MessageList
          messages={filteredMessages}
          selectedId={selectedMessageId}
          onSelectMessage={handleSelectMessage}
          queueTab={queueTab}
          onQueueTabChange={handleQueueTabChange}
          activeCounts={{
            active: queueTab === 'active' ? messagesData?.totalCount || 0 : 0,
            deadletter: queueTab === 'deadletter' ? messagesData?.totalCount || 0 : 0,
          }}
        />

        {/* Right: Detail Panel */}
        <MessageDetailPanel 
          message={selectedMessage} 
          onViewPattern={handleViewEvidence}
        />
      </div>
    </div>
  );
}
