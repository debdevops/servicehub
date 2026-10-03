import { NavLink } from 'react-router-dom';
import { ChevronDown, ChevronRight, Inbox, Radio, GitBranch, RefreshCw, AlertCircle, AlertTriangle, Plus } from 'lucide-react';
import { useState } from 'react';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useQueues } from '@servicehub/ui-shared/hooks/useQueues';
import { useTopics } from '@servicehub/ui-shared/hooks/useTopics';
import { useSubscriptions } from '@servicehub/ui-shared/hooks/useSubscriptions';
import { useInsightsSummary } from '@servicehub/ui-shared/hooks/useInsights';
import { AwsQueueList, AwsTopicList } from '@/components/layout/AwsEntityTree';
import { setThemeProvider } from '@servicehub/ui-shared/lib/providerTheme';
import { useProviderCapabilities, useProviderStatus } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { getProviderCapabilities } from '@servicehub/ui-shared/lib/api/cloudBridge';
import { getProviderStyle, PROVIDER_STATE_STYLES } from '@servicehub/ui-shared/lib/providerStyles';
import { getProviderConnectionState, type ProviderInstallState } from '@servicehub/ui-shared/lib/providerConnectionState';
import { ProviderIcon } from '@servicehub/ui-shared/components/ProviderIcon';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { ResizablePanel } from './ResizablePanel';

const ALL_PROVIDERS: CloudProviderType[] = ['azure', 'aws', 'gcp'];

interface NamespaceItemProps {
  namespace: {
    id: string;
    name: string;
    displayName?: string;
    isActive: boolean;
    cloudProvider?: CloudProviderType;
    environment?: string;
    lastConnectionTestSucceeded?: boolean | null;
  };
}

interface QueueItemProps {
  queue: {
    name: string;
    activeMessageCount: number;
    deadLetterMessageCount: number;
  };
  namespaceId: string;
  messagesBasePath: string;
}

interface TopicItemProps {
  topic: {
    name: string;
    subscriptionCount: number;
  };
  namespaceId: string;
  messagesBasePath: string;
  cloudProvider?: CloudProviderType;
}

interface SubscriptionItemProps {
  subscription: {
    name: string;
    activeMessageCount: number;
    deadLetterMessageCount: number;
  };
  namespaceId: string;
  topicName: string;
  messagesBasePath: string;
  cloudProvider?: CloudProviderType;
}

const ENV_STYLES: Record<string, string> = {
  prod: 'bg-red-100 text-red-700',
  uat: 'bg-amber-100 text-amber-700',
  dev: 'bg-green-100 text-green-700',
};

function QueueItem({ queue, namespaceId, messagesBasePath }: QueueItemProps) {
  const { data: insightsSummary } = useInsightsSummary(namespaceId, queue.name);
  const hasAIInsight = (insightsSummary?.activeCount || 0) > 0;

  return (
    <NavLink
      key={queue.name}
      to={`${messagesBasePath}?namespace=${namespaceId}&queue=${queue.name}`}
      className={({ isActive }) => {
        const searchParams = new URLSearchParams(window.location.search);
        const namespaceParam = searchParams.get('namespace');
        const queueParam = searchParams.get('queue');
        const topicParam = searchParams.get('topic');
        const isExactMatch = isActive && namespaceParam === namespaceId && queueParam === queue.name && !topicParam;

        return `flex items-center justify-between px-3 py-2.5 rounded-lg text-sm transition-all duration-200 ${
          isExactMatch
            ? 'bg-sky-600 text-white shadow-xl border-2 border-sky-400 font-bold transform scale-[1.02] -ml-1 mr-1'
            : 'bg-white text-gray-700 hover:bg-sky-50 hover:text-sky-700 border border-gray-200 hover:border-sky-300'
        }`;
      }}
    >
      {() => {
        const searchParams = new URLSearchParams(window.location.search);
        const namespaceParam = searchParams.get('namespace');
        const queueParam = searchParams.get('queue');
        const topicParam = searchParams.get('topic');
        const isExactMatch = namespaceParam === namespaceId && queueParam === queue.name && !topicParam;

        return (
          <>
            <span className="truncate flex items-center gap-1.5">
              {isExactMatch && <span className="w-1.5 h-1.5 bg-white rounded-full animate-pulse" />}
              <Inbox className={`w-3.5 h-3.5 shrink-0 ${isExactMatch ? 'text-white' : 'text-gray-400'}`} />
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
                <span className={`inline-flex items-center gap-1 px-2 py-0.5 text-xs font-bold rounded-full ${
                  isExactMatch
                    ? 'bg-red-200 text-red-800'
                    : 'bg-red-100 text-red-700'
                }`}>
                  <AlertTriangle className="w-3 h-3" />
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

function TopicItem({ topic, namespaceId, messagesBasePath, cloudProvider }: TopicItemProps) {
  const [showSubscriptions, setShowSubscriptions] = useState(false);
  const { data: subscriptions, isLoading: subsLoading } = useSubscriptions(namespaceId, topic.name);

  return (
    <div>
      <button
        onClick={() => setShowSubscriptions(!showSubscriptions)}
        className="w-full flex items-center justify-between px-3 py-1.5 rounded text-sm text-gray-600 hover:bg-gray-50 transition-colors"
      >
        <span className="truncate flex items-center gap-1.5 text-gray-500">
          {showSubscriptions ? (
            <ChevronDown className="w-3 h-3 shrink-0" />
          ) : (
            <ChevronRight className="w-3 h-3 shrink-0" />
          )}
          <Radio className="w-3.5 h-3.5 shrink-0 text-gray-400" />
          <span className="text-gray-700">{topic.name}</span>
        </span>
        <span className="px-1.5 py-0.5 bg-gray-100 text-gray-600 text-xs font-medium rounded shrink-0">
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
                messagesBasePath={messagesBasePath}
                cloudProvider={cloudProvider}
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

function SubscriptionItem({ subscription, namespaceId, topicName, messagesBasePath, cloudProvider }: SubscriptionItemProps) {
  const { data: capabilitiesMap } = useProviderCapabilities();
  // GCP Pub/Sub has no message-count API (see ProviderCapabilities.Gcp) — counts are always 0
  // regardless of real backlog. Showing a bare "0" there reads as "definitely empty" when it's
  // really "unknown"; render a dash instead so it isn't mistaken for a live count.
  const supportsCounts = getProviderCapabilities(capabilitiesMap, cloudProvider)?.supportsMessageCounts ?? true;
  return (
    <NavLink
      to={`${messagesBasePath}?namespace=${namespaceId}&topic=${topicName}&subscription=${subscription.name}`}
      className={({ isActive }) => {
        const searchParams = new URLSearchParams(window.location.search);
        const namespaceParam = searchParams.get('namespace');
        const subscriptionParam = searchParams.get('subscription');
        const topicParam = searchParams.get('topic');
        const isExactMatch = isActive && namespaceParam === namespaceId && subscriptionParam === subscription.name && topicParam === topicName;

        return `flex items-center justify-between px-3 py-2.5 rounded-lg text-sm transition-all duration-200 ${
          isExactMatch
            ? 'bg-sky-600 text-white shadow-xl border-2 border-sky-400 font-bold transform scale-[1.02] -ml-1 mr-1'
            : 'bg-white text-gray-600 hover:bg-sky-50 hover:text-sky-700 border border-gray-200 hover:border-sky-300'
        }`;
      }}
    >
      {() => {
        const searchParams = new URLSearchParams(window.location.search);
        const namespaceParam = searchParams.get('namespace');
        const subscriptionParam = searchParams.get('subscription');
        const topicParam = searchParams.get('topic');
        const isExactMatch = namespaceParam === namespaceId && subscriptionParam === subscription.name && topicParam === topicName;

        return (
          <>
            <span className="truncate flex items-center gap-1.5">
              {isExactMatch && <span className="w-1.5 h-1.5 bg-white rounded-full animate-pulse" />}
              <GitBranch className={`w-3.5 h-3.5 shrink-0 ${isExactMatch ? 'text-white' : 'text-gray-400'}`} />
              {subscription.name}
            </span>
            <div className="flex items-center gap-1 shrink-0">
              <span
                className={`px-2 py-0.5 text-xs font-bold rounded-full ${
                  isExactMatch
                    ? 'bg-white text-sky-700'
                    : supportsCounts
                    ? 'bg-green-100 text-green-700'
                    : 'bg-gray-100 text-gray-400'
                }`}
                title={supportsCounts ? undefined : 'This provider has no message-count API — open the subscription to see actual messages'}
              >
                {supportsCounts ? subscription.activeMessageCount : '—'}
              </span>
              {supportsCounts && subscription.deadLetterMessageCount > 0 && (
                <span className={`inline-flex items-center gap-1 px-2 py-0.5 text-xs font-bold rounded-full ${
                  isExactMatch
                    ? 'bg-red-200 text-red-800'
                    : 'bg-red-100 text-red-700'
                }`}>
                  <AlertTriangle className="w-3 h-3" />
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

function NamespaceCard({ namespace }: NamespaceItemProps) {
  const { data: queues, isLoading: queuesLoading, isError: queuesError } = useQueues(namespace.id);
  const { data: topics, isLoading: topicsLoading, isError: topicsError } = useTopics(namespace.id);
  const [isExpanded, setIsExpanded] = useState(namespace.isActive);
  const [showQueues, setShowQueues] = useState(true);
  const [showTopics, setShowTopics] = useState(true);
  const { isDemoMode, cloudProvider } = useDemoContext();
  const style = getProviderStyle(namespace.cloudProvider);

  const messagesBasePath = isDemoMode && cloudProvider ? `/demo/${cloudProvider}/messages` : '/messages';
  const env = (namespace.environment ?? 'dev').toLowerCase();

  return (
    <div className={`mb-2 rounded-xl border border-l-4 border-gray-200 ${style.accentBorder} bg-white hover:border-primary-300 hover:border-l-current transition-colors overflow-hidden shadow-sm`}>
      {/* Namespace Card Header — clicking also declares "I'm working in this cloud",
          which drives the provider theme (blue/orange/green) */}
      <button
        onClick={() => {
          setThemeProvider(namespace.cloudProvider ?? 'azure');
          setIsExpanded(!isExpanded);
        }}
        className={`w-full flex items-start gap-2.5 p-3 text-left transition-colors ${
          namespace.isActive ? style.headerBg : 'hover:bg-gray-50'
        }`}
      >
        <div
          className="w-9 h-9 rounded-lg flex items-center justify-center shrink-0 border border-gray-200 bg-white shadow-sm overflow-hidden"
          aria-hidden="true"
        >
          <ProviderIcon provider={namespace.cloudProvider} className="w-full h-full" />
        </div>
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-1.5">
            <span className="font-semibold text-sm text-gray-900 truncate">
              {namespace.displayName || namespace.name}
            </span>
          </div>
          <div className="text-xs text-gray-500 truncate">{namespace.name}</div>
          <div className="flex items-center gap-1.5 mt-1.5">
            <span className={`px-1.5 py-0.5 text-[10px] font-bold rounded uppercase ${style.countBadge}`}>
              {style.label}
            </span>
            <span className={`px-1.5 py-0.5 text-[10px] font-bold rounded uppercase ${ENV_STYLES[env] ?? ENV_STYLES.dev}`}>
              {env}
            </span>
            <span className="inline-flex items-center gap-1 text-[10px] font-semibold text-gray-500">
              <span className={`w-1.5 h-1.5 rounded-full shrink-0 ${
                !namespace.isActive
                  ? 'bg-gray-300'
                  : namespace.lastConnectionTestSucceeded === false
                    ? 'bg-amber-500'
                    : 'bg-green-500'
              }`} />
              {!namespace.isActive
                ? 'Inactive'
                : namespace.lastConnectionTestSucceeded === false
                  ? 'Connection issue'
                  : 'Connected'}
            </span>
          </div>
        </div>
        <div className="shrink-0 pt-1.5 text-gray-400">
          {isExpanded ? <ChevronDown className="w-4 h-4" /> : <ChevronRight className="w-4 h-4" />}
        </div>
      </button>

      {/* Expanded Content */}
      {isExpanded && namespace.isActive && (
        <div className="px-3 pb-3 space-y-1 border-t border-gray-100 pt-2">
          {/* Queues Section */}
          <button
            onClick={() => setShowQueues(!showQueues)}
            className="w-full flex items-center gap-2 px-2 py-1 text-xs font-semibold text-gray-500 uppercase tracking-wider hover:text-gray-700"
          >
            {showQueues ? <ChevronDown className="w-3 h-3" /> : <ChevronRight className="w-3 h-3" />}
            <Inbox className="w-3 h-3" />
            Queues ({queues?.length || 0})
          </button>

          {showQueues && (
            <div className="space-y-0.5">
              {queuesError ? (
                <div className="px-3 py-2 text-xs text-amber-600 flex items-center gap-1">
                  <AlertCircle className="w-3 h-3" />
                  Connection unavailable
                </div>
              ) : queuesLoading ? (
                <div className="px-3 py-2 text-xs text-gray-500">Loading...</div>
              ) : queues && queues.length > 0 ? (
                namespace.cloudProvider === 'aws' ? (
                  <AwsQueueList queues={queues} namespaceId={namespace.id} messagesBasePath={messagesBasePath} />
                ) : (
                  queues.map((queue) => (
                    <QueueItem key={queue.name} queue={queue} namespaceId={namespace.id} messagesBasePath={messagesBasePath} />
                  ))
                )
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
            {showTopics ? <ChevronDown className="w-3 h-3" /> : <ChevronRight className="w-3 h-3" />}
            <Radio className="w-3 h-3" />
            Topics ({topics?.length || 0})
          </button>

          {showTopics && (
            <div className="space-y-0.5">
              {topicsError ? (
                <div className="px-3 py-2 text-xs text-amber-600 flex items-center gap-1">
                  <AlertCircle className="w-3 h-3" />
                  Connection unavailable
                </div>
              ) : topicsLoading ? (
                <div className="px-3 py-2 text-xs text-gray-500">Loading...</div>
              ) : topics && topics.length > 0 ? (
                namespace.cloudProvider === 'aws' ? (
                  <AwsTopicList topics={topics} namespaceId={namespace.id} messagesBasePath={messagesBasePath} />
                ) : (
                  topics.map((topic) => (
                    <TopicItem
                      key={topic.name}
                      topic={topic}
                      namespaceId={namespace.id}
                      messagesBasePath={messagesBasePath}
                      cloudProvider={namespace.cloudProvider}
                    />
                  ))
                )
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

/** A provider with no namespace card above — either not part of this installation
 * at all, or part of it but never connected. Quiet, inert, no numbers/dots that could
 * be mistaken for liveness — the whole point is that it never looks like empty data. */
function OtherProviderRow({ provider, state }: { provider: CloudProviderType; state: ProviderInstallState }) {
  const style = getProviderStyle(provider);
  const stateStyle = PROVIDER_STATE_STYLES[state];

  return (
    <div className="flex items-center gap-2 px-2 py-1.5 rounded-lg">
      <div className={`w-6 h-6 rounded-md flex items-center justify-center shrink-0 border border-gray-100 bg-white overflow-hidden ${stateStyle.iconClass}`}>
        <ProviderIcon provider={provider} className="w-full h-full" />
      </div>
      <div className="min-w-0 flex-1 flex items-center gap-1.5">
        <span className="text-xs font-semibold text-gray-500">{style.label}</span>
        {stateStyle.dotClass && <span className={`w-1.5 h-1.5 rounded-full shrink-0 ${stateStyle.dotClass}`} />}
        <span className={`text-[10px] font-medium truncate ${stateStyle.textClass}`}>{stateStyle.label}</span>
      </div>
      {state === 'available-unconfigured' ? (
        <NavLink to="/connect" className="text-[10px] font-semibold text-primary-600 hover:text-primary-700 shrink-0">
          + Connect
        </NavLink>
      ) : (
        <span
          className="text-[10px] text-gray-400 shrink-0"
          title="An operator must enable this provider's feature flag on the server to make it available here."
        >
          Ask an operator
        </span>
      )}
    </div>
  );
}

/**
 * Namespaces / Connections — every connected cloud (Azure, AWS, GCP) as an expandable
 * card showing provider, environment, connection status, and its queues/topics/subscriptions.
 * Second panel, independently draggable and resizable from Quick Access.
 */
export function NamespacesPanel() {
  const { data: namespaces, isLoading, refetch, isRefetching } = useNamespaces();
  const { data: providerStatus } = useProviderStatus();
  const { isDemoMode } = useDemoContext();

  // "Not configured" must never look like "no data" — every provider this installation
  // doesn't already show a namespace card for gets a quiet, explicit row here instead of
  // silently not existing. Skipped in Demo Mode: there's no real provider-status to check,
  // and a "not configured" row inside a single-provider demo would be nonsensical (mirrors
  // useProviderStatus()'s own `enabled: !isDemoMode` gate).
  const otherProviders = isDemoMode
    ? []
    : ALL_PROVIDERS
        .map((provider) => ({ provider, state: getProviderConnectionState(providerStatus, namespaces, provider) }))
        .filter(({ state }) => state === 'unavailable' || state === 'available-unconfigured');

  return (
    <ResizablePanel
      panelId="namespaces"
      title="Namespaces / Connections"
      defaultWidth={320}
      minWidth={260}
      maxWidth={520}
      // Aligned with Header's connection-chip breakpoint (Tailwind `lg`, 1024px) so there is no
      // dead band where the chip shows a namespace but this panel — the surface that lets an
      // operator confirm/switch it — is already collapsed.
      narrowBreakpoint={1024}
      dataTour="sidebar"
      headerActions={
        <>
          <button
            onClick={() => refetch()}
            className="p-1 hover:bg-primary-50 rounded transition-colors group"
            title="Refresh Namespaces"
            aria-label="Refresh namespaces list"
          >
            <RefreshCw className={`w-3.5 h-3.5 text-primary-500 transition-transform duration-300 ${isRefetching ? 'animate-spin' : 'group-hover:rotate-180'}`} />
          </button>
          <NavLink
            to="/connect"
            className="p-1 hover:bg-gray-100 rounded transition-colors"
            title="Add Connection"
            aria-label="Add new connection"
            data-tour="add-connection"
          >
            <Plus className="w-3.5 h-3.5 text-gray-500" />
          </NavLink>
        </>
      }
    >
      <div className="p-3">
        {isLoading ? (
          <div className="px-3 py-4 text-sm text-gray-500 text-center">Loading namespaces...</div>
        ) : namespaces && namespaces.length > 0 ? (
          namespaces.map((ns) => <NamespaceCard key={ns.id} namespace={ns} />)
        ) : (
          <div className="px-3 py-4 text-sm text-gray-500 text-center">
            <p className="mb-2">No connections yet</p>
            <NavLink to="/connect" className="text-primary-600 hover:text-primary-700 font-medium">
              Add your first connection
            </NavLink>
          </div>
        )}
      </div>

      {otherProviders.length > 0 && (
        <div className="px-3 pb-2 pt-1 border-t border-gray-100">
          <p className="px-1 pb-1.5 text-[10px] font-semibold text-gray-400 uppercase tracking-wider">
            Other providers
          </p>
          <div className="space-y-0.5">
            {otherProviders.map(({ provider, state }) => (
              <OtherProviderRow key={provider} provider={provider} state={state} />
            ))}
          </div>
        </div>
      )}

      {/* Add Connection CTA */}
      <div className="border-t border-gray-100 p-3 bg-white sticky bottom-0">
        <NavLink
          to="/connect"
          className="flex items-center justify-center gap-2 w-full px-4 py-2.5 bg-sky-700 hover:bg-sky-800 text-white rounded-lg text-sm font-medium transition-all shadow-md hover:shadow-lg"
        >
          <Plus className="w-4 h-4" />
          Add Connection
        </NavLink>
      </div>
    </ResizablePanel>
  );
}
