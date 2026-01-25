import { NavLink, useNavigate } from 'react-router-dom';
import {
  ChevronDown,
  ChevronRight,
  Inbox,
  Plus,
  AlertCircle,
  Clock,
  Database,
  Newspaper,
  RefreshCw,
} from 'lucide-react';
import toast from 'react-hot-toast';
import { useState } from 'react';
import { useNamespaces } from '@/hooks/useNamespaces';
import { useQueues } from '@/hooks/useQueues';
import { useTopics } from '@/hooks/useTopics';
import { useSubscriptions } from '@/hooks/useSubscriptions';
import { useInsightsSummary } from '@/hooks/useInsights';

interface NamespaceItemProps {
  namespace: {
    id: string;
    name: string;
    displayName?: string;
    isActive: boolean;
  };
}

interface QueueItemProps {
  queue: {
    name: string;
    activeMessageCount: number;
    deadLetterMessageCount: number;
  };
  namespaceId: string;
}

interface TopicItemProps {
  topic: {
    name: string;
    subscriptionCount: number;
  };
  namespaceId: string;
}

interface SubscriptionItemProps {
  subscription: {
    name: string;
    activeMessageCount: number;
    deadLetterMessageCount: number;
  };
  namespaceId: string;
  topicName: string;
}

function QueueItem({ queue, namespaceId }: QueueItemProps) {
  // Fetch AI insights summary for this queue
  const { data: insightsSummary } = useInsightsSummary(namespaceId, queue.name);
  const hasAIInsight = (insightsSummary?.activeCount || 0) > 0;

  return (
    <NavLink
      key={queue.name}
      to={`/messages?namespace=${namespaceId}&queue=${queue.name}`}
      className={({ isActive }) => {
        // Only show selected state if this exact queue is in the route (not just namespace)
        const searchParams = new URLSearchParams(window.location.search);
        const queueParam = searchParams.get('queue');
        const topicParam = searchParams.get('topic');
        const isExactMatch = isActive && queueParam === queue.name && !topicParam;
        
        return `flex items-center justify-between px-3 py-2.5 rounded-lg text-sm transition-all duration-200 ${
          isExactMatch
            ? 'bg-sky-600 text-white shadow-xl border-2 border-sky-400 font-bold transform scale-[1.02] -ml-1 mr-1'
            : 'bg-white text-gray-700 hover:bg-sky-50 hover:text-sky-700 border border-gray-200 hover:border-sky-300'
        }`;
      }}
    >
      {() => {
        const searchParams = new URLSearchParams(window.location.search);
        const queueParam = searchParams.get('queue');
        const topicParam = searchParams.get('topic');
        const isExactMatch = queueParam === queue.name && !topicParam;
        
        return (
        <>
          <span className="truncate flex items-center gap-1.5">
            {isExactMatch && <span className="w-1.5 h-1.5 bg-white rounded-full animate-pulse" />}
            {queue.name}
            {hasAIInsight && (
              <span 
                className={`w-2 h-2 rounded-full animate-pulse ${
                  isExactMatch ? 'bg-yellow-300' : 'bg-primary-500'
                }`}
                title="AI patterns detected"
              />
            )}
          </span>
          <div className="flex items-center gap-1 shrink-0">
            <span className={`px-2 py-0.5 text-xs font-bold rounded-full ${
              isExactMatch 
                ? 'bg-white text-sky-700' 
                : 'bg-green-100 text-green-700'
            }`}>
              {queue.activeMessageCount}
            </span>
            {queue.deadLetterMessageCount > 0 && (
              <span className={`px-2 py-0.5 text-xs font-bold rounded-full ${
                isExactMatch 
                  ? 'bg-red-200 text-red-800' 
                  : 'bg-red-100 text-red-700'
              }`}>
                {queue.deadLetterMessageCount}
              </span>
            )}
          </div>
        </>
        );
      }}
    </NavLink>
  );
}

function TopicItem({ topic, namespaceId }: TopicItemProps) {
  const [showSubscriptions, setShowSubscriptions] = useState(false);
  const { data: subscriptions, isLoading: subsLoading } = useSubscriptions(
    namespaceId,
    topic.name
  );

  return (
    <div>
      <button
        onClick={() => setShowSubscriptions(!showSubscriptions)}
        className="w-full flex items-center justify-between px-3 py-1.5 rounded text-sm text-gray-700 hover:bg-gray-100 transition-colors"
      >
        <span className="truncate flex items-center gap-1.5">
          {showSubscriptions ? (
            <ChevronDown className="w-3 h-3 shrink-0" />
          ) : (
            <ChevronRight className="w-3 h-3 shrink-0" />
          )}
          {topic.name}
        </span>
        <span className="px-1.5 py-0.5 bg-primary-100 text-primary-700 text-xs font-medium rounded shrink-0">
          {topic.subscriptionCount}
        </span>
      </button>

      {showSubscriptions && (
        <div className="ml-4 mt-0.5 space-y-0.5">
          {subsLoading ? (
            <div className="px-3 py-1 text-xs text-gray-500">Loading...</div>
          ) : subscriptions && subscriptions.length > 0 ? (
            subscriptions.map((sub) => (
              <SubscriptionItem
                key={sub.name}
                subscription={sub}
                namespaceId={namespaceId}
                topicName={topic.name}
              />
            ))
          ) : (
            <div className="px-3 py-1 text-xs text-gray-500">No subscriptions</div>
          )}
        </div>
      )}
    </div>
  );
}

function SubscriptionItem({ subscription, namespaceId, topicName }: SubscriptionItemProps) {
  return (
    <NavLink
      to={`/messages?namespace=${namespaceId}&topic=${topicName}&subscription=${subscription.name}`}
      className={({ isActive }) => {
        // Only show selected state if this exact subscription is in the route
        const searchParams = new URLSearchParams(window.location.search);
        const subscriptionParam = searchParams.get('subscription');
        const topicParam = searchParams.get('topic');
        const isExactMatch = isActive && subscriptionParam === subscription.name && topicParam === topicName;
        
        return `flex items-center justify-between px-3 py-2.5 rounded-lg text-sm transition-all duration-200 ${
          isExactMatch
            ? 'bg-sky-600 text-white shadow-xl border-2 border-sky-400 font-bold transform scale-[1.02] -ml-1 mr-1'
            : 'bg-white text-gray-600 hover:bg-sky-50 hover:text-sky-700 border border-gray-200 hover:border-sky-300'
        }`;
      }}
    >
      {() => {
        const searchParams = new URLSearchParams(window.location.search);
        const subscriptionParam = searchParams.get('subscription');
        const topicParam = searchParams.get('topic');
        const isExactMatch = subscriptionParam === subscription.name && topicParam === topicName;
        
        return (
          <>
            <span className="truncate flex items-center gap-1.5">
              {isExactMatch && <span className="w-1.5 h-1.5 bg-white rounded-full animate-pulse" />}
              {subscription.name}
            </span>
            <div className="flex items-center gap-1 shrink-0">
              <span className={`px-2 py-0.5 text-xs font-bold rounded-full ${
                isExactMatch 
                  ? 'bg-white text-sky-700' 
                  : 'bg-green-100 text-green-700'
              }`}>
                {subscription.activeMessageCount}
              </span>
              {subscription.deadLetterMessageCount > 0 && (
                <span className={`px-2 py-0.5 text-xs font-bold rounded-full ${
                  isExactMatch 
                    ? 'bg-red-200 text-red-800' 
                    : 'bg-red-100 text-red-700'
                }`}>
                  {subscription.deadLetterMessageCount}
                </span>
              )}
            </div>
          </>
        );
      }}
    </NavLink>
  );
}

function NamespaceSection({ namespace }: NamespaceItemProps) {
  const { data: queues, isLoading: queuesLoading } = useQueues(namespace.id);
  const { data: topics, isLoading: topicsLoading } = useTopics(namespace.id);
  const [isExpanded, setIsExpanded] = useState(namespace.isActive);
  const [showQueues, setShowQueues] = useState(true);
  const [showTopics, setShowTopics] = useState(true);

  return (
    <div className="mb-2">
      {/* Namespace Header */}
      <button
        onClick={() => setIsExpanded(!isExpanded)}
        className={`w-full flex items-center gap-2 px-3 py-2 rounded-lg text-left transition-all ${
          namespace.isActive
            ? 'bg-gradient-to-r from-primary-50 to-blue-50 border border-primary-200 hover:from-primary-100 hover:to-blue-100 shadow-sm'
            : 'hover:bg-gray-50 border border-transparent'
        }`}
      >
        {isExpanded ? (
          <ChevronDown className="w-4 h-4 text-gray-500 shrink-0" />
        ) : (
          <ChevronRight className="w-4 h-4 text-gray-500 shrink-0" />
        )}
        <div
          className={`w-2 h-2 rounded-full shrink-0 ${
            namespace.isActive ? 'bg-green-500' : 'bg-gray-300'
          }`}
        />
        <div className="flex-1 min-w-0">
          <div className="font-medium text-sm text-gray-900 truncate">
            {namespace.displayName || namespace.name}
          </div>
          <div className="text-xs text-gray-500 truncate">{namespace.name}</div>
        </div>
      </button>

      {/* Expanded Content */}
      {isExpanded && namespace.isActive && (
        <div className="mt-1 ml-4 space-y-1">
          {/* Queues Section */}
          <button
            onClick={() => setShowQueues(!showQueues)}
            className="w-full flex items-center gap-2 px-2 py-1 text-xs font-semibold text-gray-500 uppercase tracking-wider hover:text-gray-700"
          >
            {showQueues ? (
              <ChevronDown className="w-3 h-3" />
            ) : (
              <ChevronRight className="w-3 h-3" />
            )}
            <Inbox className="w-3 h-3" />
            Queues ({queues?.length || 0})
          </button>

          {showQueues && (
            <div className="space-y-0.5">
              {queuesLoading ? (
                <div className="px-3 py-2 text-xs text-gray-500">Loading...</div>
              ) : queues && queues.length > 0 ? (
                queues.map((queue) => (
                  <QueueItem 
                    key={queue.name} 
                    queue={queue} 
                    namespaceId={namespace.id} 
                  />
                ))
              ) : (
                <div className="px-3 py-2 text-xs text-gray-500">No queues found</div>
              )}
            </div>
          )}

          {/* Topics Section */}
          <button
            onClick={() => setShowTopics(!showTopics)}
            className="w-full flex items-center gap-2 px-2 py-1 text-xs font-semibold text-gray-500 uppercase tracking-wider hover:text-gray-700"
          >
            {showTopics ? (
              <ChevronDown className="w-3 h-3" />
            ) : (
              <ChevronRight className="w-3 h-3" />
            )}
            <Newspaper className="w-3 h-3" />
            Topics ({topics?.length || 0})
          </button>

          {showTopics && (
            <div className="space-y-0.5">
              {topicsLoading ? (
                <div className="px-3 py-2 text-xs text-gray-500">Loading...</div>
              ) : topics && topics.length > 0 ? (
                topics.map((topic) => (
                  <TopicItem 
                    key={topic.name} 
                    topic={topic} 
                    namespaceId={namespace.id} 
                  />
                ))
              ) : (
                <div className="px-3 py-2 text-xs text-gray-500">No topics found</div>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export function Sidebar() {
  const navigate = useNavigate();
  const { data: namespaces, isLoading, refetch } = useNamespaces();
  
  // Get active namespace for Quick Access
  const activeNamespace = namespaces?.find(ns => ns.isActive);
  
  // Fetch queues and topics for Quick Access buttons
  const { data: queues } = useQueues(activeNamespace?.id || '');
  const { data: topics } = useTopics(activeNamespace?.id || '');

  return (
    <aside className="w-[260px] bg-white border-r border-gray-200 flex flex-col overflow-hidden">
      {/* Namespaces Section */}
      <div className="flex-1 overflow-y-auto p-3">
        <div className="flex items-center justify-between mb-3">
          <h2 className="text-xs font-semibold text-gray-500 uppercase tracking-wider">
            Namespaces
          </h2>
          <div className="flex items-center gap-1">
            <button
              onClick={() => refetch()}
              className="p-1 hover:bg-primary-50 rounded transition-colors group"
              title="Refresh Namespaces"
              aria-label="Refresh namespaces list"
            >
              <RefreshCw className="w-4 h-4 text-primary-500 group-hover:rotate-180 transition-transform duration-300" />
            </button>
            <NavLink
              to="/connect"
              className="p-1 hover:bg-gray-100 rounded transition-colors"
              title="Add Connection"
              aria-label="Add new connection"
            >
              <Plus className="w-4 h-4 text-gray-500" />
            </NavLink>
          </div>
        </div>

        {isLoading ? (
          <div className="px-3 py-4 text-sm text-gray-500 text-center">
            Loading namespaces...
          </div>
        ) : namespaces && namespaces.length > 0 ? (
          namespaces.map((ns) => (
            <NamespaceSection key={ns.id} namespace={ns} />
          ))
        ) : (
          <div className="px-3 py-4 text-sm text-gray-500 text-center">
            <p className="mb-2">No connections yet</p>
            <NavLink
              to="/connect"
              className="text-primary-600 hover:text-primary-700 font-medium"
            >
              Add your first connection
            </NavLink>
          </div>
        )}
      </div>

      {/* Quick Filters */}
      <div className="border-t border-gray-200 p-3 bg-gradient-to-b from-sky-50 to-white">
        <h2 className="text-xs font-semibold text-sky-700 uppercase tracking-wider mb-2">
          Quick Access
        </h2>
        <nav className="space-y-1">
          <button
            onClick={() => {
              const activeNamespace = namespaces?.find(ns => ns.isActive);
              if (!activeNamespace) {
                toast.error('No active namespace selected');
                return;
              }
              
              // Navigate to first queue if available
              const firstQueue = queues?.[0];
              if (firstQueue) {
                navigate(`/messages?namespace=${activeNamespace.id}&queue=${firstQueue.name}&queueType=active`);
                return;
              }
              
              // If no queues, check for topics (user will need to select subscription)
              const firstTopic = topics?.[0];
              if (firstTopic) {
                toast('Select a subscription from the topic to view messages', { icon: 'ℹ️' });
                return;
              }
              
              toast('No queues or topics available', { icon: 'ℹ️' });
            }}
            className="w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all bg-white hover:bg-sky-50 text-gray-700 hover:text-sky-700 border border-gray-200 hover:border-sky-300 shadow-sm"
          >
            <Database className="w-4 h-4 text-sky-500" />
            <span className="flex-1 text-left">Active Messages</span>
            <span className="text-xs text-sky-600 font-medium">View All</span>
          </button>
          <button
            onClick={() => {
              const activeNamespace = namespaces?.find(ns => ns.isActive);
              if (!activeNamespace) {
                toast.error('No active namespace selected');
                return;
              }
              
              // Navigate to first queue's DLQ if available
              const firstQueue = queues?.[0];
              if (firstQueue) {
                navigate(`/messages?namespace=${activeNamespace.id}&queue=${firstQueue.name}&queueType=deadletter`);
                return;
              }
              
              // If no queues, check for topics
              const firstTopic = topics?.[0];
              if (firstTopic) {
                toast('Select a subscription from the topic to view DLQ', { icon: '⚠️' });
                return;
              }
              
              toast('No queues or topics available for DLQ view', { icon: '⚠️' });
            }}
            className="w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all bg-white hover:bg-red-50 text-gray-700 hover:text-red-700 border border-gray-200 hover:border-red-300 shadow-sm"
          >
            <AlertCircle className="w-4 h-4 text-red-500" />
            <span className="flex-1 text-left">Dead-Letter</span>
            <span className="text-xs text-red-600 font-medium">DLQ</span>
          </button>
          <button
            onClick={() => {
              toast('Scheduled messages feature coming soon!', {
                icon: '⏰',
                duration: 2000
              });
            }}
            className="w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all bg-white hover:bg-sky-50 text-gray-700 hover:text-sky-700 border border-gray-200 hover:border-sky-300 shadow-sm opacity-75"
          >
            <Clock className="w-4 h-4 text-sky-500" />
            <span className="flex-1 text-left">Scheduled</span>
            <span className="text-xs text-gray-400 font-medium">Soon</span>
          </button>
        </nav>
      </div>

      {/* Add Connection CTA */}
      <div className="border-t border-gray-200 p-3 bg-white">
        <NavLink
          to="/connect"
          className="flex items-center justify-center gap-2 w-full px-4 py-2.5 bg-sky-500 hover:bg-sky-600 text-white rounded-lg text-sm font-medium transition-all shadow-md hover:shadow-lg"
        >
          <Plus className="w-4 h-4" />
          Add Connection
        </NavLink>
      </div>
    </aside>
  );
}
