import { useEffect, useMemo, useState } from 'react';
import { useSearchParams, useNavigate } from 'react-router-dom';
import { Radio, Play, Pause, Square, Trash2, Search, X, ChevronDown, ChevronRight, ExternalLink } from 'lucide-react';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useQueues } from '@servicehub/ui-shared/hooks/useQueues';
import { useTopics } from '@servicehub/ui-shared/hooks/useTopics';
import { useSubscriptions } from '@servicehub/ui-shared/hooks/useSubscriptions';
import { useLiveTail, type LiveTailStatus } from '@servicehub/ui-shared/hooks/useLiveTail';
import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { getProviderCapabilities } from '@servicehub/ui-shared/lib/api/cloudBridge';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { ProviderBadge, getProviderStyle, getProviderServiceName } from '@servicehub/ui-shared/lib/providerStyles';
import { EmptyState } from '@/components/EmptyState';
import type { Message as APIMessage, Namespace } from '@servicehub/ui-shared/lib/api/types';

// ============================================================================
// LiveTailPage — dedicated Quick Access workspace for watching one queue,
// topic/subscription in real time. Selection (Provider → Namespace → Entity)
// lives in the URL so it's linkable/preselectable; the actual streaming comes
// from the existing useLiveTail() session (same logic the in-context
// LiveTailPanel drawer on the Messages page uses) — nothing here talks to the
// backend directly.
// ============================================================================

const STATUS_LABEL: Record<LiveTailStatus, string> = {
  idle: 'Stopped',
  connecting: 'Connecting…',
  connected: 'Live',
  disconnected: 'Disconnected',
  unsupported: 'Unsupported',
};

const STATUS_DOT: Record<LiveTailStatus, string> = {
  idle: 'bg-gray-300',
  connecting: 'bg-amber-400 animate-pulse',
  connected: 'bg-emerald-500 animate-pulse',
  disconnected: 'bg-red-400',
  unsupported: 'bg-gray-300',
};

function formatTimestamp(iso: string): string {
  const date = new Date(iso);
  return date.toLocaleTimeString(undefined, { hour12: false }) + '.' + String(date.getMilliseconds()).padStart(3, '0');
}

function formatSize(bytes?: number): string | null {
  if (bytes === undefined || bytes === null) return null;
  if (bytes < 1024) return `${bytes} B`;
  return `${(bytes / 1024).toFixed(1)} KB`;
}

interface MessageRowProps {
  message: APIMessage;
  isExpanded: boolean;
  onToggle: () => void;
}

function MessageRow({ message, isExpanded, onToggle }: MessageRowProps) {
  const size = formatSize(message.sizeInBytes);
  return (
    <li className="border-b border-gray-100 last:border-b-0">
      <button
        onClick={onToggle}
        className="w-full flex items-start gap-2 px-4 py-2.5 text-left hover:bg-gray-50 transition-colors"
        aria-expanded={isExpanded}
      >
        {isExpanded ? (
          <ChevronDown className="w-3.5 h-3.5 text-gray-400 mt-0.5 shrink-0" />
        ) : (
          <ChevronRight className="w-3.5 h-3.5 text-gray-400 mt-0.5 shrink-0" />
        )}
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="text-xs font-mono text-gray-500 shrink-0">{formatTimestamp(message.enqueuedTime)}</span>
            <span className="text-xs font-mono text-gray-700 truncate">
              {message.messageId || `seq-${message.sequenceNumber}`}
            </span>
            {message.deliveryCount > 1 && (
              <span className="inline-block px-1.5 py-0.5 text-[10px] font-medium rounded bg-amber-50 text-amber-700 border border-amber-200 shrink-0">
                Delivery #{message.deliveryCount}
              </span>
            )}
            {size && <span className="text-[10px] text-gray-400 shrink-0">{size}</span>}
          </div>
          <p className="text-sm text-gray-700 truncate mt-0.5">{message.body ? message.body.slice(0, 160) : '[No body]'}</p>
        </div>
      </button>

      {isExpanded && (
        <div className="px-4 pb-3 pl-10 space-y-2 text-xs">
          <div>
            <div className="text-gray-400 font-semibold uppercase tracking-wide mb-1">Body</div>
            <pre className="bg-gray-50 border border-gray-200 rounded-lg p-2.5 whitespace-pre-wrap break-all text-gray-700 max-h-64 overflow-y-auto">
              {message.body || '[No body]'}
            </pre>
          </div>
          <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-gray-600">
            <div><span className="text-gray-400">Content-Type:</span> {message.contentType || 'n/a'}</div>
            <div><span className="text-gray-400">Sequence:</span> {message.sequenceNumber}</div>
            {message.correlationId && <div><span className="text-gray-400">Correlation-Id:</span> {message.correlationId}</div>}
            {message.sessionId && <div><span className="text-gray-400">Session-Id:</span> {message.sessionId}</div>}
            {message.deadLetterReason && (
              <div className="col-span-2 text-red-700"><span className="text-gray-400">Dead-letter reason:</span> {message.deadLetterReason}</div>
            )}
          </div>
          {message.applicationProperties && Object.keys(message.applicationProperties).length > 0 && (
            <div>
              <div className="text-gray-400 font-semibold uppercase tracking-wide mb-1">Properties</div>
              <pre className="bg-gray-50 border border-gray-200 rounded-lg p-2.5 whitespace-pre-wrap break-all text-gray-700">
                {JSON.stringify(message.applicationProperties, null, 2)}
              </pre>
            </div>
          )}
        </div>
      )}
    </li>
  );
}

function SourcePicker({ navPrefix }: { navPrefix: string }) {
  const navigate = useNavigate();
  const { data: namespaces, isLoading } = useNamespaces();

  if (isLoading) {
    return <div className="p-6 text-sm text-gray-500">Loading namespaces…</div>;
  }

  if (!namespaces || namespaces.length === 0) {
    return (
      <EmptyState
        icon={Radio}
        heading="No namespaces connected"
        subtext="Connect Azure, AWS, or GCP to watch messages live."
      />
    );
  }

  return (
    <div className="max-w-3xl mx-auto p-6 space-y-4">
      <p className="text-sm text-gray-500">Pick a namespace to watch, then a queue or topic subscription.</p>
      {namespaces.map((ns) => (
        <NamespacePicker key={ns.id} namespace={ns} navPrefix={navPrefix} navigate={navigate} />
      ))}
    </div>
  );
}

function NamespacePicker({
  namespace,
  navPrefix,
  navigate,
}: {
  namespace: Namespace;
  navPrefix: string;
  navigate: ReturnType<typeof useNavigate>;
}) {
  const style = getProviderStyle(namespace.cloudProvider);
  const { data: queues } = useQueues(namespace.id, false);
  const { data: topics } = useTopics(namespace.id, false);

  const openQueue = (queueName: string) => {
    navigate(`${navPrefix}/live-tail?namespace=${namespace.id}&queue=${encodeURIComponent(queueName)}`);
  };

  const hasEntities = (queues && queues.length > 0) || (topics && topics.length > 0);

  return (
    <section className={`bg-white rounded-xl border ${style.headerBorder} shadow-sm overflow-hidden`}>
      <div className={`flex items-center gap-2 px-4 py-2.5 ${style.headerBg} border-b ${style.headerBorder}`}>
        <ProviderBadge provider={namespace.cloudProvider} />
        <h2 className="text-sm font-semibold text-gray-900 truncate">{namespace.displayName || namespace.name}</h2>
      </div>
      <div className="p-3">
        {!hasEntities ? (
          <p className="text-xs text-gray-400 italic px-1 py-1">No queues or topics in this namespace</p>
        ) : (
          <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-3 gap-2">
            {(queues ?? []).map((q) => (
              <button
                key={q.name}
                onClick={() => openQueue(q.name)}
                className={`px-3 py-2 bg-white border border-gray-200 rounded-lg text-left text-sm text-gray-800 truncate transition-colors ${style.cardHover}`}
              >
                {q.name}
              </button>
            ))}
            {(topics ?? []).map((t) => (
              <TopicSubscriptionPicker key={t.name} namespace={namespace} topicName={t.name} navPrefix={navPrefix} navigate={navigate} />
            ))}
          </div>
        )}
      </div>
    </section>
  );
}

function TopicSubscriptionPicker({
  namespace,
  topicName,
  navPrefix,
  navigate,
}: {
  namespace: Namespace;
  topicName: string;
  navPrefix: string;
  navigate: ReturnType<typeof useNavigate>;
}) {
  const { data: subscriptions } = useSubscriptions(namespace.id, topicName, false);
  if (!subscriptions || subscriptions.length === 0) return null;

  return (
    <>
      {subscriptions.map((sub) => (
        <button
          key={`${topicName}/${sub.name}`}
          onClick={() =>
            navigate(
              `${navPrefix}/live-tail?namespace=${namespace.id}&topic=${encodeURIComponent(topicName)}&subscription=${encodeURIComponent(sub.name)}`,
            )
          }
          className="px-3 py-2 bg-white border border-gray-200 rounded-lg text-left text-sm text-gray-800 truncate transition-colors hover:border-gray-400 hover:bg-gray-50"
          title={`${topicName} / ${sub.name}`}
        >
          {topicName} <span className="text-gray-400">/ {sub.name}</span>
        </button>
      ))}
    </>
  );
}

export function LiveTailPage() {
  const [searchParams] = useSearchParams();
  const { isDemoMode, cloudProvider } = useDemoContext();
  const navPrefix = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';

  const namespaceId = searchParams.get('namespace') || '';
  const queueName = searchParams.get('queue') || '';
  const topicName = searchParams.get('topic') || '';
  const subscriptionName = searchParams.get('subscription') || '';

  const entityType: 'queue' | 'topic' = topicName ? 'topic' : 'queue';
  const entityName = queueName || topicName;
  const hasSelection = !!namespaceId && !!entityName;

  const { data: namespaces } = useNamespaces();
  const namespace = namespaces?.find((ns) => ns.id === namespaceId);

  const { data: capabilitiesMap } = useProviderCapabilities();
  const supportsRepeatablePeek = getProviderCapabilities(capabilitiesMap, namespace?.cloudProvider)?.supportsRepeatablePeek ?? true;

  const { status: liveStatus, messages, start, stop, clear } = useLiveTail({
    namespaceId,
    entityName,
    subscriptionName: subscriptionName || undefined,
  });

  // AWS (and any other provider without a safe repeat-peek) never opens a session at all —
  // this is a capability check, not a status the stream itself reports.
  const capabilityBlocked = hasSelection && !supportsRepeatablePeek;
  const status: LiveTailStatus = capabilityBlocked ? 'unsupported' : liveStatus;

  // Distinguishes "the operator paused this" from "the connection dropped" or "never started" —
  // stop() alone can't tell those apart, since Pause and Stop both call it (see below).
  const [pausedByUser, setPausedByUser] = useState(false);

  useEffect(() => {
    if (!hasSelection || capabilityBlocked) return;
    setPausedByUser(false);
    start();
    return () => stop();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hasSelection, capabilityBlocked, namespaceId, entityName, subscriptionName]);

  const [search, setSearch] = useState('');
  const [expandedId, setExpandedId] = useState<string | null>(null);

  const filteredMessages = useMemo(() => {
    if (!search.trim()) return messages;
    const q = search.toLowerCase();
    return messages.filter(
      (m) =>
        (m.messageId || '').toLowerCase().includes(q) ||
        (m.body || '').toLowerCase().includes(q),
    );
  }, [messages, search]);

  if (!hasSelection) {
    return (
      <div className="flex-1 flex flex-col overflow-hidden">
        <div className="px-6 py-4 shrink-0 bg-gradient-to-r from-emerald-600 to-emerald-500">
          <div className="flex items-center gap-3">
            <Radio className="w-6 h-6 text-white/80" />
            <div>
              <h1 className="text-xl font-semibold text-white">Live Tail</h1>
              <p className="text-sm text-emerald-100">Watch new messages arrive on one queue or subscription in real time</p>
            </div>
          </div>
        </div>
        <div className="flex-1 overflow-auto bg-gray-50">
          <SourcePicker navPrefix={navPrefix} />
        </div>
      </div>
    );
  }

  const isRunning = status === 'connected' || status === 'connecting';
  const providerLabel = namespace?.cloudProvider ? getProviderStyle(namespace.cloudProvider).label : '';
  const serviceLabel = getProviderServiceName(namespace?.cloudProvider);
  const serviceKind = entityType === 'topic' ? `${serviceLabel} Topic` : `${serviceLabel} Queue`;

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      {/* Header — always answers "what am I watching" */}
      <div className="px-6 py-4 shrink-0 bg-gradient-to-r from-emerald-600 to-emerald-500">
        <div className="flex items-center gap-3">
          <Radio className="w-6 h-6 text-white/80 shrink-0" />
          <div className="min-w-0">
            <h1 className="text-xl font-semibold text-white">Live Tail</h1>
            <p className="text-sm text-emerald-100 truncate">
              {providerLabel} · {namespace?.displayName || namespace?.name || namespaceId} · {serviceKind} ·{' '}
              {entityType === 'topic' ? `${topicName} / ${subscriptionName}` : queueName}
            </p>
          </div>
        </div>
      </div>

      {/* Status + controls — always visible, never scrolls away */}
      <div className="flex items-center justify-between gap-3 px-6 py-2.5 border-b border-gray-200 bg-white shrink-0 flex-wrap">
        <div className="flex items-center gap-2 text-sm">
          <span className={`w-2 h-2 rounded-full ${STATUS_DOT[status]}`} />
          <span className="text-gray-700 font-medium">
            {status === 'idle' && pausedByUser ? 'Paused' : STATUS_LABEL[status]}
          </span>
          <span className="text-gray-400">· {messages.length} received</span>
          {search && <span className="text-gray-400">· {filteredMessages.length} shown</span>}
        </div>

        <div className="flex items-center gap-2 flex-wrap">
          <div className="relative">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-400" />
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Filter by ID or body…"
              aria-label="Filter messages"
              className="pl-8 pr-7 py-1.5 w-48 rounded-lg text-sm bg-white border border-gray-300 text-gray-700 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500"
            />
            {search && (
              <button onClick={() => setSearch('')} aria-label="Clear filter" className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600">
                <X className="w-3.5 h-3.5" />
              </button>
            )}
          </div>

          <button
            onClick={clear}
            disabled={messages.length === 0}
            className="flex items-center gap-1.5 px-3 py-1.5 border border-gray-300 bg-white text-gray-600 hover:bg-gray-50 rounded-lg text-sm font-medium transition-colors disabled:opacity-40"
          >
            <Trash2 className="w-3.5 h-3.5" /> Clear
          </button>
          <button
            onClick={() => {
              if (isRunning) {
                setPausedByUser(true);
                stop();
              } else {
                setPausedByUser(false);
                start();
              }
            }}
            disabled={status === 'unsupported'}
            className="flex items-center gap-1.5 px-3 py-1.5 border border-gray-300 bg-white text-gray-600 hover:bg-gray-50 rounded-lg text-sm font-medium transition-colors disabled:opacity-40"
          >
            {isRunning ? <Pause className="w-3.5 h-3.5" /> : <Play className="w-3.5 h-3.5" />}
            {isRunning ? 'Pause' : 'Resume'}
          </button>
          <button
            onClick={() => {
              setPausedByUser(false);
              stop();
            }}
            disabled={status === 'idle' || status === 'unsupported'}
            className="flex items-center gap-1.5 px-3 py-1.5 border border-gray-300 bg-white text-gray-600 hover:bg-gray-50 rounded-lg text-sm font-medium transition-colors disabled:opacity-40"
          >
            <Square className="w-3.5 h-3.5" /> Stop
          </button>
        </div>
      </div>

      {/* Body — the only scroll region */}
      <div className="flex-1 overflow-y-auto bg-gray-50">
        {status === 'unsupported' && (
          <div className="p-4 m-4 bg-amber-50 border border-amber-200 rounded-lg text-sm text-amber-800">
            {isDemoMode ? (
              "Live Tail isn't available in Demo Mode — this is a read-only preview with pre-seeded data, not a live provider connection."
            ) : namespace?.cloudProvider === 'aws' ? (
              <>
                <p className="font-medium mb-1">Live Tail unavailable for AWS SQS</p>
                <p>
                  AWS SQS does not provide a non-destructive observation mechanism — every read is a real receive
                  that counts toward the queue's redelivery limit. Enabling continuous polling here could push
                  messages into the dead-letter queue by accident, so Live Tail is disabled for this entity.
                  Use{' '}
                  <a
                    href={`${navPrefix}/messages?namespace=${namespaceId}&queue=${encodeURIComponent(queueName)}`}
                    className="inline-flex items-center gap-1 underline hover:no-underline"
                  >
                    manual message inspection <ExternalLink className="w-3 h-3" />
                  </a>{' '}
                  instead.
                </p>
              </>
            ) : (
              "Live Tail isn't available for this namespace's provider."
            )}
          </div>
        )}

        {status !== 'unsupported' && filteredMessages.length === 0 && (
          <div className="flex flex-col items-center justify-center h-40 text-gray-400 text-sm px-6 text-center">
            {messages.length > 0
              ? 'No messages match your filter.'
              : status === 'connected'
                ? 'Watching for new messages…'
                : status === 'connecting'
                  ? 'Connecting…'
                  : 'No messages received yet.'}
          </div>
        )}

        {status !== 'unsupported' && filteredMessages.length > 0 && (
          <ul className="bg-white mx-4 my-3 rounded-lg border border-gray-200 divide-y divide-gray-100 overflow-hidden">
            {filteredMessages.map((message, index) => {
              const key = `${message.messageId || message.sequenceNumber}-${index}`;
              return (
                <MessageRow
                  key={key}
                  message={message}
                  isExpanded={expandedId === key}
                  onToggle={() => setExpandedId((prev) => (prev === key ? null : key))}
                />
              );
            })}
          </ul>
        )}
      </div>
    </div>
  );
}

export default LiveTailPage;
