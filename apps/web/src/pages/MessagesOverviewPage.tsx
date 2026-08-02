import { useState } from 'react';
import { useSearchParams, useNavigate } from 'react-router-dom';
import { Inbox, AlertTriangle, Globe, Plus, MessageSquare, Radio, Search, X, ChevronDown, RefreshCw } from 'lucide-react';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useQueues } from '@servicehub/ui-shared/hooks/useQueues';
import { useTopics } from '@servicehub/ui-shared/hooks/useTopics';
import { useSubscriptions } from '@servicehub/ui-shared/hooks/useSubscriptions';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { ProviderBadge, getProviderStyle } from '@servicehub/ui-shared/lib/providerStyles';
import { EmptyState } from '@/components/EmptyState';
import { setThemeProvider } from '@servicehub/ui-shared/lib/providerTheme';
import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { getProviderCapabilities } from '@servicehub/ui-shared/lib/api/cloudBridge';
import type { Namespace } from '@servicehub/ui-shared/lib/api/types';

// ============================================================================
// MessagesOverviewPage — multi-cloud entry point for Active / Dead-Letter
// browsing. Shows every registered namespace (Azure, AWS, GCP) with its
// queues and topic subscriptions as clickable widgets; clicking one jumps
// straight into that entity's message view with the right provider theme.
// Reached from Quick Access "Active Messages" and "Dead-Letter".
//
// Built to stay usable at scale: sections are collapsible, entities are
// sorted by message count (busiest first), each grid scrolls inside a capped
// height, and a global search narrows every section — so even hundreds of
// queues fit in a single window.
// ============================================================================

type OverviewTab = 'active' | 'deadletter';

// unsupported = the provider has no message-count API (e.g. GCP Pub/Sub) — the raw value is
// always 0 regardless of real backlog, so a dash is shown instead of a misleading "0".
function CountBadge({ value, tab, unsupported }: { value: number; tab: OverviewTab; unsupported?: boolean }) {
  if (unsupported) {
    return (
      <span
        className="px-2 py-0.5 rounded-full text-xs font-bold bg-gray-100 text-gray-400"
        title="This provider has no message-count API — open the subscription to see actual messages"
      >
        —
      </span>
    );
  }
  if (tab === 'deadletter') {
    return (
      <span
        className={`px-2 py-0.5 rounded-full text-xs font-bold ${
          value > 0 ? 'bg-red-100 text-red-700' : 'bg-gray-100 text-gray-400'
        }`}
      >
        {value.toLocaleString()}
      </span>
    );
  }
  return (
    <span
      className={`px-2 py-0.5 rounded-full text-xs font-bold ${
        value > 0 ? 'bg-sky-100 text-sky-700' : 'bg-gray-100 text-gray-400'
      }`}
    >
      {value.toLocaleString()}
    </span>
  );
}

function TopicSubscriptions({
  namespace,
  topicName,
  tab,
  navPrefix,
}: {
  namespace: Namespace;
  topicName: string;
  tab: OverviewTab;
  navPrefix: string;
}) {
  const navigate = useNavigate();
  const style = getProviderStyle(namespace.cloudProvider);
  const { data: subscriptions } = useSubscriptions(namespace.id, topicName, false);
  const { data: capabilitiesMap } = useProviderCapabilities();
  const supportsCounts = getProviderCapabilities(capabilitiesMap, namespace.cloudProvider)?.supportsMessageCounts ?? true;

  if (!subscriptions || subscriptions.length === 0) {
    return (
      <p className="text-xs text-gray-400 italic px-3 py-2">
        No subscriptions — publishes to this topic are dropped
      </p>
    );
  }

  return (
    <div className="space-y-1">
      {subscriptions.map((sub) => (
        <button
          key={sub.name}
          onClick={() => {
            setThemeProvider(namespace.cloudProvider);
            navigate(
              `${navPrefix}/messages?namespace=${namespace.id}&topic=${encodeURIComponent(topicName)}&subscription=${encodeURIComponent(sub.name)}&queueType=${tab}`,
            );
          }}
          className={`w-full flex items-center justify-between gap-2 px-3 py-2 bg-white border border-gray-200 rounded-lg text-left transition-colors ${style.cardHover}`}
        >
          <span className="text-sm text-gray-700 truncate">↳ {sub.name}</span>
          <CountBadge
            value={tab === 'deadletter' ? sub.deadLetterMessageCount : sub.activeMessageCount}
            tab={tab}
            unsupported={!supportsCounts}
          />
        </button>
      ))}
    </div>
  );
}

function NamespaceEntitiesSection({
  namespace,
  tab,
  navPrefix,
  filter,
}: {
  namespace: Namespace;
  tab: OverviewTab;
  navPrefix: string;
  filter: string;
}) {
  const navigate = useNavigate();
  const style = getProviderStyle(namespace.cloudProvider);
  const { data: queues, isLoading: queuesLoading, isError: queuesError } = useQueues(namespace.id, false);
  const { data: topics } = useTopics(namespace.id, false);
  const isAws = namespace.cloudProvider === 'aws';
  const [collapsed, setCollapsed] = useState(false);
  // While searching, every section stays open so matches are never hidden.
  const isOpen = filter ? true : !collapsed;

  // AWS surfaces each redrive DLQ as its own queue; those are reached via the
  // source queue's DLQ tab, so hide them as standalone widgets.
  const dlqTargets = new Set(
    (queues ?? []).map((q) => q.deadLetterTargetQueue).filter(Boolean) as string[],
  );
  const countOf = (q: { activeMessageCount: number; deadLetterMessageCount: number }) =>
    tab === 'deadletter' ? q.deadLetterMessageCount : q.activeMessageCount;

  // Busiest entities first, so the ones that matter are visible without scrolling.
  const visibleQueues = (queues ?? [])
    .filter((q) => !dlqTargets.has(q.name))
    .filter((q) => !filter || q.name.toLowerCase().includes(filter))
    .sort((a, b) => countOf(b) - countOf(a) || a.name.localeCompare(b.name));

  // SNS topics have no DLQ of their own — hide them on the dead-letter tab.
  const visibleTopics = (isAws && tab === 'deadletter' ? [] : (topics ?? [])).filter(
    (t) => !filter || t.name.toLowerCase().includes(filter),
  );

  const totalCount = visibleQueues.reduce((sum, q) => sum + countOf(q), 0);
  const entityCount = visibleQueues.length + visibleTopics.length;

  const openQueue = (queueName: string) => {
    setThemeProvider(namespace.cloudProvider);
    navigate(
      `${navPrefix}/messages?namespace=${namespace.id}&queue=${encodeURIComponent(queueName)}&queueType=${tab}`,
    );
  };

  const openTopic = (topicName: string) => {
    setThemeProvider(namespace.cloudProvider);
    navigate(`${navPrefix}/messages?namespace=${namespace.id}&topic=${encodeURIComponent(topicName)}`);
  };

  if (filter && entityCount === 0 && !queuesLoading && !queuesError) {
    return null;
  }

  return (
    <section className={`bg-white rounded-xl border ${style.headerBorder} shadow-sm overflow-hidden`}>
      {/* Provider-tinted header — clickable to collapse the section */}
      <button
        onClick={() => setCollapsed((v) => !v)}
        aria-expanded={isOpen}
        className={`w-full flex items-center gap-2 px-5 py-3 ${style.headerBg} border-b ${style.headerBorder} text-left`}
      >
        <ChevronDown
          className={`w-4 h-4 text-gray-400 transition-transform ${isOpen ? '' : '-rotate-90'}`}
        />
        <ProviderBadge provider={namespace.cloudProvider} />
        <h2 className="text-sm font-semibold text-gray-900 truncate">
          {namespace.displayName || namespace.name}
        </h2>
        <span className="px-1.5 py-0.5 rounded text-[10px] font-semibold bg-white/70 text-gray-500 border border-gray-200">
          {entityCount} entit{entityCount === 1 ? 'y' : 'ies'}
        </span>
        <span className={`ml-auto text-xs font-semibold ${tab === 'deadletter' && totalCount > 0 ? 'text-red-700' : style.accentText}`}>
          {totalCount.toLocaleString()} {tab === 'deadletter' ? 'dead-lettered' : 'active'}
        </span>
      </button>

      {isOpen && (
        <div className="p-4 space-y-4">
          {queuesLoading ? (
            <p className="text-sm text-gray-400 flex items-center gap-2">
              <RefreshCw className="w-3.5 h-3.5 animate-spin" />
              Loading entities…
            </p>
          ) : queuesError ? (
            <div className="flex items-center gap-2 text-sm text-red-600">
              <AlertTriangle className="w-4 h-4 shrink-0" />
              Unable to reach this namespace
            </div>
          ) : visibleQueues.length === 0 && visibleTopics.length === 0 ? (
            <p className="text-sm text-gray-400 italic">
              {filter ? 'No entities match your search' : 'No entities in this namespace'}
            </p>
          ) : (
            <>
              {visibleQueues.length > 0 && (
                <div>
                  <h3 className="flex items-center gap-1.5 text-[11px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
                    <MessageSquare className="w-3.5 h-3.5" /> Queues ({visibleQueues.length})
                  </h3>
                  {/* Capped height + inner scroll: many queues never blow up the page */}
                  <div className="max-h-56 overflow-y-auto pr-1">
                    <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-3 gap-2">
                      {visibleQueues.map((queue) => (
                        <button
                          key={queue.name}
                          onClick={() => openQueue(queue.name)}
                          className={`flex items-center justify-between gap-2 px-3 py-2.5 bg-white border border-gray-200 rounded-lg text-left transition-colors ${style.cardHover}`}
                          title={
                            tab === 'deadletter' && queue.deadLetterTargetQueue
                              ? `DLQ: ${queue.deadLetterTargetQueue}`
                              : undefined
                          }
                        >
                          <span className="text-sm font-medium text-gray-800 truncate">{queue.name}</span>
                          <CountBadge value={countOf(queue)} tab={tab} />
                        </button>
                      ))}
                    </div>
                  </div>
                </div>
              )}

              {visibleTopics.length > 0 && (
                <div>
                  <h3 className="flex items-center gap-1.5 text-[11px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
                    <Radio className="w-3.5 h-3.5" /> Topics ({visibleTopics.length})
                  </h3>
                  <div className="max-h-64 overflow-y-auto pr-1 space-y-3">
                    {visibleTopics.map((topic) => (
                      <div key={topic.name} className="border border-gray-100 rounded-lg p-2 bg-gray-50/50">
                        {isAws ? (
                          <button
                            onClick={() => openTopic(topic.name)}
                            className={`w-full flex items-center justify-between gap-2 px-3 py-2 bg-white border border-gray-200 rounded-lg text-left transition-colors ${style.cardHover}`}
                          >
                            <span className="text-sm font-medium text-gray-800 truncate">📢 {topic.name}</span>
                            <span className={`text-xs font-medium ${style.accentText}`}>Fan-out →</span>
                          </button>
                        ) : (
                          <>
                            <p className="text-sm font-medium text-gray-800 px-1 pb-1.5 truncate">📢 {topic.name}</p>
                            <TopicSubscriptions
                              namespace={namespace}
                              topicName={topic.name}
                              tab={tab}
                              navPrefix={navPrefix}
                            />
                          </>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </>
          )}
        </div>
      )}
    </section>
  );
}

export function MessagesOverviewPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const { data: namespaces, isLoading } = useNamespaces();
  const { isDemoMode, cloudProvider } = useDemoContext();
  const navPrefix = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';
  const [search, setSearch] = useState('');
  const filter = search.trim().toLowerCase();

  const tab: OverviewTab = searchParams.get('tab') === 'deadletter' ? 'deadletter' : 'active';

  const setTab = (next: OverviewTab) => {
    setSearchParams({ tab: next }, { replace: true });
  };

  const isDeadLetter = tab === 'deadletter';

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      {/* Header */}
      <div
        className={`px-6 py-4 shrink-0 bg-gradient-to-r ${
          isDeadLetter ? 'from-red-600 to-red-500' : 'from-sky-600 to-sky-500'
        }`}
      >
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            {isDeadLetter ? (
              <AlertTriangle className="w-6 h-6 text-white/80" />
            ) : (
              <Inbox className="w-6 h-6 text-white/80" />
            )}
            <div>
              <h1 className="text-xl font-semibold text-white">
                {isDeadLetter ? 'Dead-Letter Overview' : 'Active Messages Overview'}
              </h1>
              <p className={`text-sm ${isDeadLetter ? 'text-red-100' : 'text-sky-100'}`}>
                All connected clouds — pick an entity to open its {isDeadLetter ? 'DLQ' : 'messages'}
              </p>
            </div>
          </div>

          {/* Tab toggle */}
          <div className="flex rounded-lg overflow-hidden border border-white/30">
            <button
              onClick={() => setTab('active')}
              className={`px-4 py-1.5 text-sm font-medium transition-colors ${
                !isDeadLetter ? 'bg-white text-sky-700' : 'bg-white/10 text-white hover:bg-white/20'
              }`}
            >
              Active
            </button>
            <button
              onClick={() => setTab('deadletter')}
              className={`px-4 py-1.5 text-sm font-medium transition-colors ${
                isDeadLetter ? 'bg-white text-red-700' : 'bg-white/10 text-white hover:bg-white/20'
              }`}
            >
              Dead-Letter
            </button>
          </div>
        </div>
      </div>

      {/* Search bar — narrows every namespace section at once */}
      <div className="bg-white border-b border-gray-200 px-6 py-2.5 shrink-0">
        <div className="relative max-w-md">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search queues and topics across all clouds…"
            aria-label="Search entities"
            className="w-full pl-9 pr-8 py-2 rounded-lg text-sm bg-white border border-gray-300 text-gray-700 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-sky-500 focus:border-sky-500"
          />
          {search && (
            <button
              onClick={() => setSearch('')}
              aria-label="Clear search"
              className="absolute right-2.5 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
            >
              <X className="w-4 h-4" />
            </button>
          )}
        </div>
      </div>

      {/* Body */}
      <div className="flex-1 overflow-auto bg-gray-50 p-6">
        {isLoading ? (
          <div className="flex items-center justify-center h-full text-gray-500 gap-2 text-sm">
            <RefreshCw className="w-4 h-4 animate-spin" />
            Loading namespaces…
          </div>
        ) : !namespaces || namespaces.length === 0 ? (
          <EmptyState
            icon={Globe}
            heading="No namespaces connected"
            subtext="Connect Azure, AWS, or GCP to browse messages."
            action={{ label: 'Connect a namespace', icon: Plus, onClick: () => navigate('/connect') }}
          />
        ) : (
          <div className="space-y-5 max-w-5xl mx-auto">
            {namespaces.map((ns) => (
              <NamespaceEntitiesSection
                key={ns.id}
                namespace={ns}
                tab={tab}
                navPrefix={navPrefix}
                filter={filter}
              />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

export default MessagesOverviewPage;
