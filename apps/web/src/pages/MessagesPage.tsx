import { useState, useMemo, useEffect, useDeferredValue } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { Search, Filter, RefreshCw, Sparkles, X, AlertCircle, Play, Pause } from 'lucide-react';
import { MessageList, MessageDetailPanel, type QueueTab } from '@/components/messages';
import { AIFindingsDropdown } from '@/components/ai';
import { MessageListSkeleton } from '@/components/messages/MessageListSkeleton';
import { useMessages } from '@/hooks/useMessages';
import { useClientSideInsights, useInsightsSummary } from '@/hooks/useInsights';
import { useQueues } from '@/hooks/useQueues';
import { useSubscriptions } from '@/hooks/useSubscriptions';
import { useNamespaces } from '@/hooks/useNamespaces';
import type { Message } from '@/lib/mockData';
import type { Message as APIMessage } from '@/lib/api/types';
import toast from 'react-hot-toast';

// Transform API message to UI message format
function transformMessage(
  apiMessage: APIMessage, 
  insightMessageIds: string[] = [],
  queueType: 'active' | 'deadletter' = 'active'
): Message {
  // Use messageId as the primary identifier
  const id = apiMessage.messageId || `seq-${apiMessage.sequenceNumber}`;
  const body = apiMessage.body;
  
  // Derive status from state
  let status: 'success' | 'warning' | 'error' = 'success';
  if (apiMessage.isFromDeadLetter || apiMessage.deadLetterReason) {
    status = 'error';
  } else if ((apiMessage.deliveryCount || 0) > 1) {
    status = 'warning';
  }
  
  return {
    id,
    enqueuedTime: new Date(apiMessage.enqueuedTime),
    status,
    preview: body ? body.substring(0, 100) : '[Body unavailable - may exceed size limit or API throttled]',
    contentType: (apiMessage.contentType || 'application/json') as any,
    deliveryCount: apiMessage.deliveryCount || 0,
    hasAIInsight: insightMessageIds.includes(id),
    sequenceNumber: apiMessage.sequenceNumber || 0,
    properties: apiMessage.applicationProperties || {},
    queueType,
    body: body || '', // Keep for compatibility but show unavailable in UI
    headers: {
      'Content-Type': apiMessage.contentType || 'application/json',
      ...(apiMessage.correlationId ? { 'Correlation-Id': apiMessage.correlationId } : {}),
      ...(apiMessage.sessionId ? { 'Session-Id': apiMessage.sessionId } : {}),
    },
    timeToLive: apiMessage.timeToLive || '',
    lockToken: '',
    deadLetterReason: apiMessage.deadLetterReason || undefined,
    deadLetterSource: apiMessage.deadLetterSource || undefined,
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
  const [searchParams, setSearchParams] = useSearchParams();
  const queryClient = useQueryClient();
  const namespaceId = searchParams.get('namespace');
  const queueName = searchParams.get('queue');
  const topicName = searchParams.get('topic');
  const subscriptionName = searchParams.get('subscription');
  const queueTypeParam = searchParams.get('queueType'); // Get queueType from URL

  // Determine entity type and name
  const entityType: 'queue' | 'topic' = topicName ? 'topic' : 'queue';
  const entityName = queueName || (topicName && subscriptionName ? `${topicName}/subscriptions/${subscriptionName}` : topicName) || '';

  // Fetch available namespaces to validate the current namespace ID
  const { data: namespaces } = useNamespaces();

  // Auto-fix invalid namespace ID by redirecting to the first available namespace
  useEffect(() => {
    if (namespaces && namespaces.length > 0 && namespaceId) {
      const namespaceExists = namespaces.some(ns => ns.id === namespaceId);
      if (!namespaceExists) {
        // Namespace ID in URL is invalid (likely from previous API session with in-memory storage)
        const firstNamespace = namespaces[0];
        console.warn(`[MessagesPage] Invalid namespace ID "${namespaceId}" - redirecting to "${firstNamespace.id}"`);
        
        // Update URL with valid namespace ID while preserving other parameters
        const newParams = new URLSearchParams(searchParams);
        newParams.set('namespace', firstNamespace.id);
        setSearchParams(newParams, { replace: true });
        
        toast.success(`Reconnected to ${firstNamespace.displayName || firstNamespace.name}`, {
          duration: 3000,
        });
      }
    }
  }, [namespaces, namespaceId, searchParams, setSearchParams]);

  // Selected message for detail panel
  const [selectedMessageId, setSelectedMessageId] = useState<string | null>(null);
  
  // Queue tab: active or deadletter (sync with URL parameter)
  const [queueTab, setQueueTab] = useState<QueueTab>('active');

  // Auto-refresh control
  const [autoRefreshEnabled, setAutoRefreshEnabled] = useState(true);

  // Pagination constant
  const BATCH_SIZE = 1000;

  // Sync queueTab with URL parameter on mount and when it changes
  useEffect(() => {
    if (queueTypeParam === 'deadletter') {
      setQueueTab('deadletter');
    } else if (queueTypeParam === 'active') {
      setQueueTab('active');
    }
  }, [queueTypeParam]);

  // Clear selection when switching queues/topics to prevent stale detail panel
  useEffect(() => {
    setSelectedMessageId(null);
  }, [namespaceId, queueName, topicName, subscriptionName]);

  // Fetch messages from API for current tab
  const { data: messagesData, isLoading, error, refetch, isFetching, dataUpdatedAt } = useMessages({
    namespaceId: namespaceId || '',
    queueOrTopicName: entityName,
    entityType,
    queueType: queueTab,
    skip: 0,
    take: 1000,
    autoRefresh: autoRefreshEnabled,
  });

  // Fetch authoritative counts from queue/subscription metadata
  const { data: queuesData, refetch: refetchQueues } = useQueues(namespaceId || '', autoRefreshEnabled);
  const { data: subscriptionsData, refetch: refetchSubscriptions } = useSubscriptions(namespaceId || '', topicName || '', autoRefreshEnabled);

  // Force refresh metadata counts when entity changes
  useEffect(() => {
    if (entityType === 'queue' && namespaceId && queueName) {
      refetchQueues();
    } else if (entityType === 'topic' && namespaceId && topicName && subscriptionName) {
      refetchSubscriptions();
    }
  }, [namespaceId, queueName, topicName, subscriptionName, entityType, refetchQueues, refetchSubscriptions]);

  // Extract counts from authoritative metadata
  const getMessageCounts = () => {
    if (entityType === 'queue' && queueName) {
      const queue = queuesData?.find(q => q.name === queueName);
      return {
        active: queue?.activeMessageCount || 0,
        deadletter: queue?.deadLetterMessageCount || 0,
      };
    } else if (entityType === 'topic' && subscriptionName && topicName) {
      const subscription = subscriptionsData?.find(s => s.name === subscriptionName);
      return {
        active: subscription?.activeMessageCount || 0,
        deadletter: subscription?.deadLetterMessageCount || 0,
      };
    }
    return { active: 0, deadletter: 0 };
  };

  const messageCounts = getMessageCounts();

  // Perform client-side AI analysis on loaded messages
  // This provides AI insights even when backend AI service is unavailable
  const { data: insights } = useClientSideInsights(
    messagesData?.items,
    {
      namespaceId: namespaceId || '',
      entityName: entityName,
      subscriptionName: subscriptionName || undefined,
      entityType,
    },
    !!namespaceId && !!entityName && !isLoading
  );

  // Fetch AI insights summary for badge (from backend if available)
  const { data: insightsSummary } = useInsightsSummary(
    namespaceId || '',
    entityName || ''
  );

  // AI dropdown visibility
  const [showAIDropdown, setShowAIDropdown] = useState(false);

  // Search functionality with debouncing
  const [searchInput, setSearchInput] = useState('');
  const searchQuery = useDeferredValue(searchInput); // Debounce search
  const [showFilterPanel, setShowFilterPanel] = useState(false);
  const [statusFilter, setStatusFilter] = useState<'all' | 'success' | 'warning' | 'error'>('all');

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

  // Get messages from API or empty array - sort by enqueued time descending (newest first)
  const messages: Message[] = (messagesData?.items || [])
    .map(msg => transformMessage(msg, insightMessageIds, queueTab))
    .sort((a, b) => b.enqueuedTime.getTime() - a.enqueuedTime.getTime());

  // Find selected message
  const selectedMessage = useMemo(
    () => messages.find(m => m.id === selectedMessageId) ?? null,
    [messages, selectedMessageId]
  );

  // Filter messages by evidence filter, search query, and status
  const filteredMessages = useMemo(() => {
    let result = messages;
    
    // Apply evidence filter first
    if (evidenceFilter) {
      result = result.filter(m => evidenceFilter.includes(m.id));
    }
    
    // Apply search filter
    if (searchQuery.trim()) {
      const query = searchQuery.toLowerCase();
      result = result.filter(m => 
        m.id.toLowerCase().includes(query) ||
        m.preview.toLowerCase().includes(query) ||
        (typeof m.body === 'string' && m.body.toLowerCase().includes(query)) ||
        JSON.stringify(m.properties).toLowerCase().includes(query)
      );
    }
    
    // Apply status filter
    if (statusFilter !== 'all') {
      result = result.filter(m => m.status === statusFilter);
    }
    
    return result;
  }, [messages, evidenceFilter, searchQuery, statusFilter]);

  // Active AI insights count - prefer client-side analysis, fallback to backend summary
  const activeInsightsCount = insights?.length || insightsSummary?.activeCount || 0;

  // Check if we're showing a partial view due to batch limit
  const totalMessagesInQueue = messagesData?.totalCount || 0;
  const isPartialView = totalMessagesInQueue > messages.length && messages.length >= BATCH_SIZE;

  // Handle message selection
  const handleSelectMessage = (id: string) => {
    setSelectedMessageId(id);
  };

  // Handle queue tab change
  const handleQueueTabChange = (tab: QueueTab) => {
    setQueueTab(tab);
    setSelectedMessageId(null); // Clear selection when switching tabs
    
    // Update URL to keep it in sync with tab state
    const newParams = new URLSearchParams(searchParams);
    newParams.set('queueType', tab);
    setSearchParams(newParams, { replace: true });
    
    // Refresh counts when switching between active/dlq tabs
    // This ensures the counts stay synchronized with actual queue state
    if (entityType === 'queue') {
      refetchQueues();
    } else if (entityType === 'topic') {
      refetchSubscriptions();
    }
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

  // Handle refresh button - refresh messages and queue/topic counts
  const handleRefresh = () => {
    refetch();
    // Also refresh queue/topic counts in sidebar
    queryClient.invalidateQueries({ queryKey: ['queues'] });
    queryClient.invalidateQueries({ queryKey: ['topics'] });
    queryClient.invalidateQueries({ queryKey: ['subscriptions'] });
    toast.success('Messages refreshed');
  };

  // Toggle auto-refresh
  const handleToggleAutoRefresh = () => {
    setAutoRefreshEnabled(prev => {
      const newState = !prev;
      toast.success(newState ? '🔄 Auto-refresh enabled (7s)' : '⏸️ Auto-refresh paused', {
        duration: 2000,
      });
      return newState;
    });
  };

  // Format last updated time
  const getLastUpdatedText = () => {
    if (!dataUpdatedAt) return '';
    const seconds = Math.floor((Date.now() - dataUpdatedAt) / 1000);
    if (seconds < 5) return 'just now';
    if (seconds < 60) return `${seconds}s ago`;
    return `${Math.floor(seconds / 60)}m ago`;
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
    const errorMessage = error instanceof Error ? error.message : 'An error occurred';
    const isConnectionError = errorMessage.toLowerCase().includes('network') || 
                              errorMessage.toLowerCase().includes('connection') ||
                              errorMessage.toLowerCase().includes('timeout');
    
    return (
      <div className="flex-1 flex items-center justify-center bg-gray-50">
        <div className="text-center max-w-md p-8">
          <div className="text-6xl mb-4">⚠️</div>
          <h2 className="text-xl font-semibold text-gray-900 mb-2">Failed to load messages</h2>
          <p className="text-gray-600 mb-2">{errorMessage}</p>
          {isConnectionError && (
            <p className="text-sm text-gray-500 mb-4">
              Check if the API server is running and Azure Service Bus is accessible.
            </p>
          )}
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
  if (!namespaceId || !entityName) {
    return (
      <div className="flex-1 flex items-center justify-center bg-gray-50">
        <div className="text-center max-w-md p-8">
          <div className="text-6xl mb-4">📬</div>
          <h2 className="text-xl font-semibold text-gray-900 mb-2">No entity selected</h2>
          <p className="text-gray-600">
            Select a queue or topic subscription from the sidebar to view messages
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex-1 flex flex-col overflow-hidden relative">
      {/* Toolbar */}
      <div className="bg-white border-b border-gray-200 px-4 py-3 flex items-center gap-3 shrink-0">
        {/* Search */}
        <div className="flex-1 max-w-md relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input
            type="text"
            placeholder="Search messages by ID, properties, or content..."
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            className="w-full pl-10 pr-4 py-2.5 rounded-lg text-sm bg-white border border-gray-300 text-gray-700 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-sky-500 focus:border-sky-500 transition-all"
          />
          {searchInput && (
            <button
              onClick={() => setSearchInput('')}
              className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
            >
              <X className="w-4 h-4" />
            </button>
          )}
        </div>

        {/* Filter Button */}
        <div className="relative">
          <button 
            onClick={() => setShowFilterPanel(!showFilterPanel)}
            className={`flex items-center gap-2 px-3 py-2 border rounded-lg text-sm transition-colors ${
              statusFilter !== 'all' || showFilterPanel
                ? 'border-sky-300 bg-sky-50 text-sky-700'
                : 'border-gray-200 hover:bg-gray-50 text-gray-700'
            }`}
          >
            <Filter className="w-4 h-4" />
            Filter
            {statusFilter !== 'all' && (
              <span className="w-2 h-2 bg-sky-500 rounded-full" />
            )}
          </button>
          
          {/* Filter Dropdown */}
          {showFilterPanel && (
            <div className="absolute top-full right-0 mt-1 w-48 bg-white border border-gray-200 rounded-lg shadow-lg z-50 py-1">
              <div className="px-3 py-2 text-xs font-semibold text-gray-500 uppercase">Status</div>
              {(['all', 'success', 'warning', 'error'] as const).map((status) => (
                <button
                  key={status}
                  onClick={() => {
                    setStatusFilter(status);
                    setShowFilterPanel(false);
                  }}
                  className={`w-full px-3 py-2 text-left text-sm hover:bg-gray-50 flex items-center gap-2 ${
                    statusFilter === status ? 'bg-sky-50 text-sky-700' : 'text-gray-700'
                  }`}
                >
                  <span className={`w-2 h-2 rounded-full ${
                    status === 'all' ? 'bg-gray-400' :
                    status === 'success' ? 'bg-green-500' :
                    status === 'warning' ? 'bg-amber-500' : 'bg-red-500'
                  }`} />
                  {status === 'all' ? 'All Messages' : status.charAt(0).toUpperCase() + status.slice(1)}
                </button>
              ))}
            </div>
          )}
        </div>

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

        {/* Auto-refresh Toggle */}
        <button 
          onClick={handleToggleAutoRefresh}
          className={`flex items-center gap-2 px-3 py-2 border rounded-lg text-sm font-medium transition-colors ${
            autoRefreshEnabled
              ? 'border-green-300 bg-green-50 text-green-700 hover:bg-green-100'
              : 'border-gray-300 bg-gray-50 text-gray-600 hover:bg-gray-100'
          }`}
          aria-label={autoRefreshEnabled ? 'Pause auto-refresh' : 'Resume auto-refresh'}
          title={autoRefreshEnabled ? 'Auto-refresh every 7 seconds' : 'Auto-refresh paused'}
        >
          {autoRefreshEnabled ? (
            <>
              <Pause className="w-4 h-4" />
              <span className="hidden sm:inline">Auto: ON</span>
            </>
          ) : (
            <>
              <Play className="w-4 h-4" />
              <span className="hidden sm:inline">Auto: OFF</span>
            </>
          )}
        </button>

        {/* Refresh */}
        <button 
          onClick={handleRefresh}
          className="flex items-center gap-2 px-3 py-2 bg-primary-500 hover:bg-primary-600 text-white rounded-lg text-sm font-medium transition-colors relative"
          aria-label="Refresh message list"
          disabled={isFetching && !isLoading}
        >
          <RefreshCw className={`w-4 h-4 ${isFetching && !isLoading ? 'animate-spin' : ''}`} />
          <span className="hidden sm:inline">Refresh</span>
          {dataUpdatedAt && (
            <span className="hidden md:inline text-xs opacity-75 ml-1">
              ({getLastUpdatedText()})
            </span>
          )}
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

      {/* Partial View Warning */}
      {isPartialView && !evidenceFilter && (
        <div className="bg-amber-50 border-b border-amber-200 px-4 py-2.5 flex items-center gap-2">
          <AlertCircle className="w-4 h-4 text-amber-600 shrink-0" />
          <span className="text-xs text-amber-800">
            <span className="font-semibold">Partial View:</span> Showing first {messages.length.toLocaleString()} of {totalMessagesInQueue.toLocaleString()} messages.
            Older messages not loaded. Use search or refresh to view different messages.
          </span>
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
            active: messageCounts.active,
            deadletter: messageCounts.deadletter,
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
