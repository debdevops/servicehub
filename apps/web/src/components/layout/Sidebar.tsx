import { NavLink } from 'react-router-dom';
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
      className={({ isActive }) =>
        `flex items-center justify-between px-3 py-1.5 rounded text-sm transition-colors ${
          isActive
            ? 'bg-primary-600 text-white shadow-sm'
            : 'bg-gray-50 text-gray-700 hover:bg-primary-50 hover:text-primary-700'
        }`
      }
    >
      {({ isActive }) => (
        <>
          <span className="truncate flex items-center gap-1.5">
            {queue.name}
            {hasAIInsight && (
              <span 
                className={`w-2 h-2 rounded-full animate-pulse ${
                  isActive ? 'bg-white' : 'bg-primary-500'
                }`}
                title="AI patterns detected"
              />
            )}
          </span>
          <div className="flex items-center gap-1 shrink-0">
            <span className={`px-1.5 py-0.5 text-xs font-medium rounded ${
              isActive 
                ? 'bg-green-500 text-white' 
                : 'bg-green-100 text-green-700'
            }`}>
              {queue.activeMessageCount}
            </span>
            {queue.deadLetterMessageCount > 0 && (
              <span className={`px-1.5 py-0.5 text-xs font-medium rounded ${
                isActive 
                  ? 'bg-red-500 text-white' 
                  : 'bg-red-100 text-red-700'
              }`}>
                {queue.deadLetterMessageCount}
              </span>
            )}
          </div>
        </>
      )}
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
        <span className="px-1.5 py-0.5 bg-blue-100 text-blue-700 text-xs font-medium rounded shrink-0">
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
      className={({ isActive }) =>
        `flex items-center justify-between px-3 py-1.5 rounded text-sm transition-colors ${
          isActive
            ? 'bg-primary-600 text-white shadow-sm'
            : 'bg-gray-50 text-gray-600 hover:bg-primary-50 hover:text-primary-700'
        }`
      }
    >
      {({ isActive }) => (
        <>
          <span className="truncate">{subscription.name}</span>
          <div className="flex items-center gap-1 shrink-0">
            <span className={`px-1.5 py-0.5 text-xs font-medium rounded ${
              isActive 
                ? 'bg-green-500 text-white' 
                : 'bg-green-100 text-green-700'
            }`}>
              {subscription.activeMessageCount}
            </span>
            {subscription.deadLetterMessageCount > 0 && (
              <span className={`px-1.5 py-0.5 text-xs font-medium rounded ${
                isActive 
                  ? 'bg-red-500 text-white' 
                  : 'bg-red-100 text-red-700'
              }`}>
                {subscription.deadLetterMessageCount}
              </span>
            )}
          </div>
        </>
      )}
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
  const { data: namespaces, isLoading, refetch } = useNamespaces();

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
            >
              <RefreshCw className="w-4 h-4 text-primary-500 group-hover:rotate-180 transition-transform duration-300" />
            </button>
            <NavLink
              to="/connect"
              className="p-1 hover:bg-gray-100 rounded transition-colors"
              title="Add Connection"
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
      <div className="border-t border-gray-200 p-3 bg-gray-50">
        <h2 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-2">
          Quick Access
        </h2>
        <nav className="space-y-1">
          <NavLink
            to="/messages?filter=active"
            className={({ isActive }) =>
              `flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-colors ${
                isActive
                  ? 'bg-primary-100 text-primary-700'
                  : 'text-gray-700 hover:bg-gray-100'
              }`
            }
          >
            <Database className="w-4 h-4" />
            Active Messages
          </NavLink>
          <NavLink
            to="/messages?filter=dlq"
            className={({ isActive }) =>
              `flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-colors ${
                isActive
                  ? 'bg-primary-100 text-primary-700'
                  : 'text-gray-700 hover:bg-gray-100'
              }`
            }
          >
            <AlertCircle className="w-4 h-4 text-red-500" />
            Dead-Letter
          </NavLink>
          <NavLink
            to="/messages?filter=scheduled"
            className={({ isActive }) =>
              `flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-colors ${
                isActive
                  ? 'bg-primary-50 text-primary-700'
                  : 'text-gray-700 hover:bg-gray-100'
              }`
            }
          >
            <Clock className="w-4 h-4" />
            Scheduled
          </NavLink>
        </nav>
      </div>

      {/* Add Connection CTA */}
      <div className="border-t border-gray-200 p-3">
        <NavLink
          to="/connect"
          className="flex items-center justify-center gap-2 w-full px-4 py-2 bg-primary-500 hover:bg-primary-600 text-white rounded-lg text-sm font-medium transition-colors"
        >
          <Plus className="w-4 h-4" />
          Add Connection
        </NavLink>
      </div>
    </aside>
  );
}
