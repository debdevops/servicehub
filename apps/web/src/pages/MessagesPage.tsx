import { useState, useMemo, useEffect, useRef, useDeferredValue } from 'react';
import { useSearchParams, useNavigate } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { Search, Filter, RefreshCw, Sparkles, X, AlertCircle, Play, Pause, Radio, Inbox, Clock, Archive } from 'lucide-react';
import { MessageList, MessageDetailPanel, LiveTailPanel, type QueueTab } from '@/components/messages';
import { EmptyState } from '@/components/EmptyState';
import { AwsTopicFanout } from '@/components/aws/AwsTopicFanout';
import { AIFindingsDropdown } from '@/components/ai';
import { MessageListSkeleton } from '@/components/messages/MessageListSkeleton';
import { HelpTooltip } from '@/components/help';
import { tooltips } from '@servicehub/ui-shared/lib/helpContent';
import { useMessages } from '@servicehub/ui-shared/hooks/useMessages';
import { useClientSideInsights, useInsightsSummary } from '@servicehub/ui-shared/hooks/useInsights';
import { useQueues } from '@servicehub/ui-shared/hooks/useQueues';
import { useSubscriptions } from '@servicehub/ui-shared/hooks/useSubscriptions';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { getProviderCapabilities } from '@servicehub/ui-shared/lib/api/cloudBridge';
import type { Message, ContentType } from '@servicehub/ui-shared/lib/mockData';
import type { Message as APIMessage, CloudProviderType, ApiError } from '@servicehub/ui-shared/lib/api/types';
import toast from 'react-hot-toast';

const PROVIDER_SERVICE_LABELS: Record<CloudProviderType, string> = {
  azure: 'Azure Service Bus',
  aws: 'AWS SQS / SNS',
  gcp: 'GCP Pub/Sub',
};

// Transform API message to UI message format
function transformMessage(
  apiMessage: APIMessage, 
  insightMessageIds: string[] = [],
  queueType: 'active' | 'deadletter' = 'active'
): Message {
  // Use messageId as the primary identifier
  const id = apiMessage.messageId || `seq-${apiMessage.sequenceNumber}`;
  const body = apiMessage.body;
  
  // Extract event type from message body (if JSON)
  let eventType: string | undefined;
  let displayTitle: string | undefined;
  try {
    if (body && typeof body === 'string') {
      const parsed = JSON.parse(body);
      eventType = parsed.eventType || parsed.event || parsed.type;
      // If no eventType, try to construct a display title from other fields
      if (!eventType) {
        if (parsed.messageType) displayTitle = parsed.messageType;
        else if (parsed.name) displayTitle = parsed.name;
        else if (parsed.action) displayTitle = parsed.action;
      }
    }
  } catch {
    // Body is not JSON or parsing failed - use as-is for preview
  }
  
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
    contentType: (apiMessage.contentType || 'application/json') as ContentType,
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
    eventType,
    displayTitle: displayTitle || eventType,
    deadLetterReason: apiMessage.deadLetterReason || undefined,
    deadLetterSource: apiMessage.deadLetterSource || undefined,
    scheduledEnqueueTime: apiMessage.scheduledEnqueueTime ?? undefined,
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

  const demoParam = searchParams.get('demo');
  const navigate = useNavigate();

  // Redirect legacy demo query parameter URLs to the new structured routes
  useEffect(() => {
    if (demoParam === 'azure' || demoParam === 'aws' || demoParam === 'gcp') {
      navigate(`/demo/${demoParam}`, { replace: true });
    }
  }, [demoParam, navigate]);

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
        if (import.meta.env.DEV) console.warn(`[MessagesPage] Invalid namespace ID "${namespaceId}" - redirecting to "${firstNamespace.id}"`);
        
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

  // Selected message for detail panel — initialized from the URL so message
  // links ("?message=<id>") can be shared and restore the selection on load
  const [selectedMessageId, setSelectedMessageId] = useState<string | null>(
    () => searchParams.get('message')
  );

  const syncMessageParam = (id: string | null) => {
    const newParams = new URLSearchParams(searchParams);
    if (id) {
      newParams.set('message', id);
    } else {
      newParams.delete('message');
    }
    setSearchParams(newParams, { replace: true });
  };
  
  // Queue tab: active or deadletter (sync with URL parameter)
  const [queueTab, setQueueTab] = useState<QueueTab>('active');

  // Auto-refresh control
  const [autoRefreshEnabled, setAutoRefreshEnabled] = useState(true);

  // SQS has no true peek: every browse is a receive that increments the message's
  // ReceiveCount, so background polling would push messages over the queue's
  // maxReceiveCount and silently dead-letter them. Disable auto-refresh for AWS —
  // the user can still refresh manually or re-enable the toggle deliberately.
  const isAwsNamespace = namespaces?.find(ns => ns.id === namespaceId)?.cloudProvider === 'aws';

  // AWS SNS topics render the fan-out dashboard, so their (non-existent)
  // message list must never be fetched.
  const isAwsTopicFanout = isAwsNamespace && !!topicName && !subscriptionName;
  useEffect(() => {
    if (isAwsNamespace) {
      setAutoRefreshEnabled(false);
    }
  }, [isAwsNamespace, namespaceId]);

  // Live Tail
  const [liveTailOpen, setLiveTailOpen] = useState(false);
  const currentProvider = namespaces?.find(ns => ns.id === namespaceId)?.cloudProvider;
  const { data: capabilitiesMap } = useProviderCapabilities();
  const supportsRepeatablePeek = getProviderCapabilities(capabilitiesMap, currentProvider)?.supportsRepeatablePeek ?? true;
  const canLiveTail = supportsRepeatablePeek && !isAwsTopicFanout && !!entityName;
  const supportsScheduledMessages = getProviderCapabilities(capabilitiesMap, currentProvider)?.supportsScheduledMessages ?? true;
  const canViewScheduled = entityType === 'queue' && !!namespaceId && !!queueName && supportsScheduledMessages;
  const canViewDlqHistory = queueTab === 'deadletter' && !!namespaceId && !!entityName;

  // Pagination constants and state
  const BATCH_SIZE = 50; // Load 50 messages per batch for optimal performance
  const [paginationState, setPaginationState] = useState({ skip: 0, allMessages: [] as APIMessage[] });
  const [isLoadingMore, setIsLoadingMore] = useState(false);

  // Sync queueTab with URL parameter on mount and when it changes
  useEffect(() => {
    if (queueTypeParam === 'deadletter') {
      setQueueTab('deadletter');
    } else if (queueTypeParam === 'active') {
      setQueueTab('active');
    }
  }, [queueTypeParam]);

  // Clear selection when switching queues/topics to prevent stale detail panel,
  // and drop the ?message= param so the URL never carries a deep link into an
  // entity the message doesn't belong to. Skipped on first render so a
  // deep-linked "?message=<id>" selection survives page load.
  const isFirstEntityRender = useRef(true);
  useEffect(() => {
    if (isFirstEntityRender.current) {
      isFirstEntityRender.current = false;
      return;
    }
    setSelectedMessageId(null);
    setSearchParams(prev => {
      const next = new URLSearchParams(prev);
      next.delete('message');
      return next;
    }, { replace: true });
    // setSearchParams is deliberately omitted from deps: react-router recreates it on
    // every location change, so including it re-runs this effect after each URL update
    // and wipes a selection the instant it is made.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [namespaceId, queueName, topicName, subscriptionName]);

  // Reset pagination when queue/topic/tab changes
  useEffect(() => {
    setPaginationState({ skip: 0, allMessages: [] });
  }, [namespaceId, queueName, topicName, subscriptionName, queueTab]);

  // Fetch messages from API for current tab
  const { data: messagesData, isLoading, error, refetch, isFetching, dataUpdatedAt } = useMessages({
    namespaceId: namespaceId || '',
    queueOrTopicName: isAwsTopicFanout ? '' : entityName,
    entityType,
    queueType: queueTab,
    skip: paginationState.skip,
    take: 50, // Initial fetch: 50 messages for fast load times
    autoRefresh: autoRefreshEnabled && paginationState.skip === 0, // Only auto-refresh on first fetch
  });

  // Fetch authoritative counts from queue/subscription metadata
  const { data: queuesData, refetch: refetchQueues } = useQueues(namespaceId || '', autoRefreshEnabled);
  const { data: subscriptionsData, refetch: refetchSubscriptions } = useSubscriptions(namespaceId || '', topicName || '', autoRefreshEnabled);

  // Track Active ⇄ Dead-Letter switches so the list can show a sync indicator
  // while the new tab's messages are fetched. Without this, a cached (stale)
  // list renders instantly with no hint that fresh data is still loading.
  const [isTabSyncing, setIsTabSyncing] = useState(false);
  const prevQueueTabRef = useRef(queueTab);
  useEffect(() => {
    if (prevQueueTabRef.current !== queueTab) {
      prevQueueTabRef.current = queueTab;
      setIsTabSyncing(true);
    }
  }, [queueTab]);
  useEffect(() => {
    if (isTabSyncing && !isFetching) {
      setIsTabSyncing(false);
    }
  }, [isTabSyncing, isFetching]);

  // Accumulate messages when new data arrives
  useEffect(() => {
    if (messagesData?.items) {
      setPaginationState(prev => {
        // If skip is 0, replace all messages; otherwise append to existing ones
        if (prev.skip === 0) {
          return { ...prev, allMessages: messagesData.items };
        } else {
          // Avoid duplicates when appending
          const existingIds = new Set(prev.allMessages.map(m => m.messageId));
          const newMessages = messagesData.items.filter(m => !existingIds.has(m.messageId));
          return { ...prev, allMessages: [...prev.allMessages, ...newMessages] };
        }
      });
      setIsLoadingMore(false);
    }
  }, [messagesData?.items, paginationState.skip]);

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

  // Build insights from demo messages (they already have AI analysis embedded)
  // or fetch from API for real data
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
  
  // Memoize displayInsights to provide stable reference for dependents
  const displayInsights = useMemo(
    () => insights || [],
    [insights]
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
    if (!displayInsights) return [];
    const ids = new Set<string>();
    displayInsights.forEach(insight => {
      insight.evidence.affectedMessageIds.forEach(msgId => ids.add(msgId));
    });
    return Array.from(ids);
  }, [displayInsights]);

  // Get messages from API - sort by enqueued time descending (newest first)
  const messages: Message[] = useMemo(
    () => paginationState.allMessages
      .map(msg => transformMessage(msg, insightMessageIds, queueTab))
      .sort((a, b) => b.enqueuedTime.getTime() - a.enqueuedTime.getTime()),
    [paginationState.allMessages, insightMessageIds, queueTab]
  );

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
  const activeInsightsCount = displayInsights?.length || insightsSummary?.activeCount || 0;

  // Check if we're showing a partial view due to batch limit
  const totalMessagesInQueue = messagesData?.totalCount || 0;
  const hasMoreMessages = paginationState.skip + BATCH_SIZE < totalMessagesInQueue && messages.length < totalMessagesInQueue;

  // Load more messages handler
  const handleLoadMore = () => {
    setIsLoadingMore(true);
    setPaginationState(prev => ({ ...prev, skip: prev.skip + BATCH_SIZE }));
  };

  // Handle message selection — mirrored into the URL so the link is shareable
  const handleSelectMessage = (id: string) => {
    setSelectedMessageId(id);
    syncMessageParam(id);
  };

  // Handle queue tab change
  const handleQueueTabChange = (tab: QueueTab) => {
    setQueueTab(tab);
    setSelectedMessageId(null); // Clear selection when switching tabs

    // Update URL to keep it in sync with tab state (and drop the stale message link)
    const newParams = new URLSearchParams(searchParams);
    newParams.set('queueType', tab);
    newParams.delete('message');
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

  // AWS SNS topics store no messages — show the fan-out dashboard instead of a
  // message list (checked before the loading/error returns so the topic's
  // pointless message fetch can't hijack the view). Azure topics keep the
  // existing subscription-based flow.
  if (isAwsNamespace && namespaceId && topicName && !subscriptionName) {
    return <AwsTopicFanout namespaceId={namespaceId} topicName={topicName} />;
  }

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
    // Prefer the backend's ProblemDetails "detail" (e.g. the specific reason a GCP Pub/Sub
    // subscription can't be browsed) over axios's generic "Request failed with status code..."
    const errorMessage =
      (error as ApiError)?.response?.data?.detail ||
      (error as ApiError)?.response?.data?.message ||
      (error instanceof Error ? error.message : 'An error occurred');
    const isConnectionError = errorMessage.toLowerCase().includes('network') ||
                              errorMessage.toLowerCase().includes('connection') ||
                              errorMessage.toLowerCase().includes('timeout');
    const providerLabel =
      PROVIDER_SERVICE_LABELS[namespaces?.find(ns => ns.id === namespaceId)?.cloudProvider ?? 'azure'];

    return (
      <div className="flex-1 flex items-center justify-center bg-gray-50">
        <div className="text-center max-w-md p-8">
          <AlertCircle className="w-14 h-14 text-red-400 mx-auto mb-4" />
          <h2 className="text-xl font-semibold text-gray-900 mb-2">Failed to load messages</h2>
          <p className="text-gray-600 mb-2">{errorMessage}</p>
          {isConnectionError && (
            <p className="text-sm text-gray-500 mb-4">
              Check if the API server is running and {providerLabel} is accessible.
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
        <EmptyState
          icon={Inbox}
          heading="No entity selected"
          subtext="Select a queue or topic subscription from the sidebar to view messages."
          fillHeight={false}
        />
      </div>
    );
  }

  return (
    <div className="flex-1 flex flex-col overflow-hidden relative" data-tour="messages-area">
      {/* Toolbar */}
      <div className="bg-white border-b border-gray-200 px-4 py-3 flex items-center gap-3 shrink-0">
        {/* Search */}
        <div className="flex-1 max-w-md relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input
            type="text"
            placeholder="Search messages by ID, properties, or content..."
            aria-label="Search messages"
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            className="w-full pl-10 pr-4 py-2.5 rounded-lg text-sm bg-white border border-gray-300 text-gray-700 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition-all"
          />
          {searchInput && (
            <button
              onClick={() => setSearchInput('')}
              aria-label="Clear search"
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
            aria-label="Filter messages by status"
            aria-expanded={showFilterPanel}
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
                  {status === 'all' ? 'All Messages' :
                    status === 'error' ? 'Dead-Letter' :
                    status.charAt(0).toUpperCase() + status.slice(1)}
                </button>
              ))}
            </div>
          )}
        </div>

        {/* AI Findings Button */}
        <div className="relative">
          <button
            onClick={() => setShowAIDropdown(!showAIDropdown)}
            aria-label="AI Findings"
            aria-expanded={showAIDropdown}
            className={`flex items-center gap-2 px-3 py-2 border rounded-lg text-sm font-medium transition-colors ${
              showAIDropdown
                ? 'border-primary-300 bg-primary-50 text-primary-700'
                : 'border-gray-200 hover:bg-gray-50 text-gray-700'
            }`}
          >
            <Sparkles className="w-4 h-4 text-primary-500" />
            AI Findings: {activeInsightsCount}
            <HelpTooltip {...tooltips.messages.aiFindings} position="bottom" className="ml-0.5" />
          </button>

          {showAIDropdown && (
            <AIFindingsDropdown
              insights={displayInsights || []}
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
              <HelpTooltip {...tooltips.messages.autoRefresh} position="bottom" className="ml-0.5" />
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

        {/* Live Tail */}
        {canLiveTail && (
          <button
            onClick={() => setLiveTailOpen(true)}
            className="flex items-center gap-2 px-3 py-2 border border-gray-300 bg-gray-50 text-gray-600 hover:bg-gray-100 rounded-lg text-sm font-medium transition-colors"
            aria-label="Open Live Tail"
            title="Watch new messages arrive in real time"
          >
            <Radio className="w-4 h-4" />
            <span className="hidden sm:inline">Live Tail</span>
          </button>
        )}

        {/* Scheduled Messages */}
        {canViewScheduled && (
          <button
            onClick={() => navigate(`/scheduled?namespace=${namespaceId}&queue=${encodeURIComponent(queueName)}`)}
            className="flex items-center gap-2 px-3 py-2 border border-gray-300 bg-gray-50 text-gray-600 hover:bg-gray-100 rounded-lg text-sm font-medium transition-colors"
            aria-label="View scheduled messages for this queue"
            title="View messages scheduled for future delivery in this queue"
          >
            <Clock className="w-4 h-4" />
            <span className="hidden sm:inline">Scheduled</span>
          </button>
        )}

        {/* DLQ History */}
        {canViewDlqHistory && (
          <button
            onClick={() => navigate(`/dlq-history?namespace=${namespaceId}&entity=${encodeURIComponent(entityName)}`)}
            className="flex items-center gap-2 px-3 py-2 border border-gray-300 bg-gray-50 text-gray-600 hover:bg-gray-100 rounded-lg text-sm font-medium transition-colors"
            aria-label="View DLQ history for this entity"
            title="View dead-letter history and replay tools for this entity"
          >
            <Archive className="w-4 h-4" />
            <span className="hidden sm:inline">DLQ History</span>
          </button>
        )}
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

      {/* More Messages Available Banner */}
      {hasMoreMessages && !evidenceFilter && (
        <div className="bg-blue-50 border-b border-blue-200 px-4 py-2.5 flex items-center gap-2">
          <AlertCircle className="w-4 h-4 text-blue-600 shrink-0" />
          <span className="text-xs text-blue-800 flex-1">
            <span className="font-semibold">More messages available:</span> Showing {messages.length.toLocaleString()} of {totalMessagesInQueue.toLocaleString()} messages.
            Scroll down or click "Load More" to view additional messages.
          </span>
        </div>
      )}

      {/* AWS SQS semantics notice — browsing counts as deliveries */}
      {isAwsNamespace && (
        <div className="px-4 py-1.5 bg-amber-50 border-b border-amber-100 text-amber-700 text-xs shrink-0">
          AWS SQS counts every view as a delivery — repeated refreshes can move messages to the DLQ
          once the queue's redrive policy limit is reached. Auto-refresh is off by default here.
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
          hasMoreMessages={hasMoreMessages}
          isLoadingMore={isLoadingMore}
          onLoadMore={handleLoadMore}
          isSyncing={isTabSyncing}
          tabLabels={isAwsNamespace ? {
            active: 'Queue',
            deadletter: 'DLQ',
            deadletterTitle: (() => {
              const dlqName = queuesData?.find(q => q.name === queueName)?.deadLetterTargetQueue;
              return dlqName
                ? `Separate SQS queue: ${dlqName} (redrive target)`
                : 'Messages in the redrive dead-letter queue';
            })(),
          } : undefined}
        />

        {/* Right: Detail Panel */}
        <MessageDetailPanel
          message={selectedMessage}
          onViewPattern={handleViewEvidence}
          insights={displayInsights}
        />
      </div>

      <LiveTailPanel
        isOpen={liveTailOpen}
        onClose={() => setLiveTailOpen(false)}
        namespaceId={namespaceId || ''}
        entityName={entityType === 'topic' ? (topicName || '') : (queueName || '')}
        subscriptionName={entityType === 'topic' ? subscriptionName || undefined : undefined}
        fromDeadLetter={queueTab === 'deadletter'}
      />
    </div>
  );
}
