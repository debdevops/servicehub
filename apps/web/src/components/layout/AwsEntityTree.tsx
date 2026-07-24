import { NavLink } from 'react-router-dom';
import { CornerDownRight, Send, AlertTriangle, Inbox, Radio } from 'lucide-react';
import { useSubscriptions } from '@/hooks/useSubscriptions';
import type { Queue, Topic } from '@/lib/api/types';

// ============================================================================
// AWS-specific sidebar entity tree.
//
// AWS semantics differ from Azure Service Bus, so the tree mirrors how AWS
// actually works instead of forcing the Azure model:
//  - An SQS DLQ is a *separate queue* linked by RedrivePolicy. Source queues
//    show their DLQ nested beneath them, and queues that only serve as a DLQ
//    target are not repeated at the top level.
//  - An SNS topic stores no messages; it fans out to endpoint queues. Clicking
//    a topic opens the fan-out dashboard; its children link to endpoint queues.
// ============================================================================

interface AwsQueueListProps {
  queues: Queue[];
  namespaceId: string;
  messagesBasePath: string;
}

interface AwsTopicListProps {
  topics: Topic[];
  namespaceId: string;
  messagesBasePath: string;
}

function readSelectedParams() {
  const searchParams = new URLSearchParams(window.location.search);
  return {
    namespace: searchParams.get('namespace'),
    queue: searchParams.get('queue'),
    topic: searchParams.get('topic'),
    subscription: searchParams.get('subscription'),
    queueType: searchParams.get('queueType'),
  };
}

function AwsQueueNode({ queue, namespaceId, messagesBasePath }: {
  queue: Queue;
  namespaceId: string;
  messagesBasePath: string;
}) {
  const hasDlq = !!queue.deadLetterTargetQueue;

  return (
    <div>
      <NavLink
        to={`${messagesBasePath}?namespace=${namespaceId}&queue=${queue.name}&queueType=active`}
        className={({ isActive }) => {
          const p = readSelectedParams();
          const selected = isActive && p.namespace === namespaceId && p.queue === queue.name &&
            !p.topic && p.queueType !== 'deadletter';
          return `flex items-center justify-between px-3 py-2.5 rounded-lg text-sm transition-all duration-200 ${
            selected
              ? 'bg-orange-500 text-white shadow-xl border-2 border-orange-300 font-bold transform scale-[1.02] -ml-1 mr-1'
              : 'bg-white text-gray-700 hover:bg-orange-50 hover:text-orange-700 border border-gray-200 hover:border-orange-300'
          }`;
        }}
      >
        {() => {
          const p = readSelectedParams();
          const selected = p.namespace === namespaceId && p.queue === queue.name &&
            !p.topic && p.queueType !== 'deadletter';
          return (
            <>
              <span className="truncate flex items-center gap-1.5">
                {selected && <span className="w-1.5 h-1.5 bg-white rounded-full animate-pulse" />}
                <Inbox className={`w-3.5 h-3.5 shrink-0 ${selected ? 'text-white' : 'text-gray-400'}`} />
                {queue.name}
              </span>
              <span className={`px-2 py-0.5 text-xs font-bold rounded-full shrink-0 ${
                selected ? 'bg-white text-orange-700' : 'bg-green-100 text-green-700'
              }`}>
                {queue.activeMessageCount}
              </span>
            </>
          );
        }}
      </NavLink>

      {hasDlq && (
        <NavLink
          to={`${messagesBasePath}?namespace=${namespaceId}&queue=${queue.name}&queueType=deadletter`}
          className={({ isActive }) => {
            const p = readSelectedParams();
            const selected = isActive && p.namespace === namespaceId && p.queue === queue.name &&
              !p.topic && p.queueType === 'deadletter';
            return `ml-5 mt-0.5 flex items-center justify-between px-3 py-1.5 rounded-lg text-xs transition-all duration-200 ${
              selected
                ? 'bg-red-600 text-white shadow-lg border border-red-400 font-bold'
                : 'bg-red-50/60 text-red-700 hover:bg-red-100 border border-red-100 hover:border-red-300'
            }`;
          }}
          title={`Separate SQS queue "${queue.deadLetterTargetQueue}" receiving messages that failed delivery (RedrivePolicy)`}
        >
          {() => {
            const p = readSelectedParams();
            const selected = p.namespace === namespaceId && p.queue === queue.name &&
              !p.topic && p.queueType === 'deadletter';
            return (
              <>
                <span className="truncate flex items-center gap-1">
                  <AlertTriangle className="w-3 h-3 shrink-0" />
                  DLQ: {queue.deadLetterTargetQueue}
                </span>
                {queue.deadLetterMessageCount > 0 && (
                  <span className={`inline-flex items-center gap-1 px-1.5 py-0.5 text-[11px] font-bold rounded-full shrink-0 ${
                    selected ? 'bg-white text-red-700' : 'bg-red-200 text-red-800'
                  }`}>
                    <AlertTriangle className="w-2.5 h-2.5" />
                    {queue.deadLetterMessageCount}
                  </span>
                )}
              </>
            );
          }}
        </NavLink>
      )}
    </div>
  );
}

export function AwsQueueList({ queues, namespaceId, messagesBasePath }: AwsQueueListProps) {
  // Queues referenced as someone's redrive target render nested under their
  // source queue, not as top-level entries.
  const dlqNames = new Set(
    queues.map(q => q.deadLetterTargetQueue).filter((n): n is string => !!n)
  );
  const sourceQueues = queues.filter(q => !dlqNames.has(q.name));

  return (
    <div className="space-y-0.5">
      {sourceQueues.map(queue => (
        <AwsQueueNode
          key={queue.name}
          queue={queue}
          namespaceId={namespaceId}
          messagesBasePath={messagesBasePath}
        />
      ))}
    </div>
  );
}

function AwsTopicNode({ topic, namespaceId, messagesBasePath }: {
  topic: Topic;
  namespaceId: string;
  messagesBasePath: string;
}) {
  const { data: subscriptions } = useSubscriptions(namespaceId, topic.name);

  return (
    <div>
      <NavLink
        to={`${messagesBasePath}?namespace=${namespaceId}&topic=${topic.name}`}
        className={({ isActive }) => {
          const p = readSelectedParams();
          const selected = isActive && p.namespace === namespaceId && p.topic === topic.name && !p.subscription;
          return `flex items-center justify-between px-3 py-2.5 rounded-lg text-sm transition-all duration-200 ${
            selected
              ? 'bg-orange-500 text-white shadow-xl border-2 border-orange-300 font-bold transform scale-[1.02] -ml-1 mr-1'
              : 'bg-white text-gray-700 hover:bg-orange-50 hover:text-orange-700 border border-gray-200 hover:border-orange-300'
          }`;
        }}
        title="SNS topic — opens the fan-out dashboard (topics store no messages)"
      >
        {() => {
          const p = readSelectedParams();
          const selected = p.namespace === namespaceId && p.topic === topic.name && !p.subscription;
          return (
            <>
              <span className="truncate flex items-center gap-1.5">
                {selected && <span className="w-1.5 h-1.5 bg-white rounded-full animate-pulse" />}
                <Radio className={`w-3.5 h-3.5 shrink-0 ${selected ? 'text-white' : 'text-gray-400'}`} />
                {topic.name}
              </span>
              <span className={`flex items-center gap-1 px-2 py-0.5 text-[11px] font-bold rounded-full shrink-0 ${
                selected ? 'bg-white text-orange-700' : 'bg-orange-100 text-orange-700'
              }`}>
                <Send className="w-2.5 h-2.5" />
                {topic.subscriptionCount}
              </span>
            </>
          );
        }}
      </NavLink>

      {(subscriptions ?? []).map(sub => (
        <NavLink
          key={sub.name}
          to={`${messagesBasePath}?namespace=${namespaceId}&queue=${sub.name}&queueType=active`}
          className="ml-5 mt-0.5 flex items-center gap-1 px-3 py-1.5 rounded-lg text-xs bg-white text-gray-500 hover:bg-orange-50 hover:text-orange-700 border border-gray-100 hover:border-orange-200 transition-all"
          title={`Fan-out delivery to SQS queue "${sub.name}" — click to view its messages`}
        >
          <CornerDownRight className="w-3 h-3 shrink-0" />
          <span className="truncate">fan-out → {sub.name}</span>
          <span className="ml-auto text-[10px] uppercase tracking-wide text-gray-400 shrink-0">sqs</span>
        </NavLink>
      ))}
    </div>
  );
}

export function AwsTopicList({ topics, namespaceId, messagesBasePath }: AwsTopicListProps) {
  return (
    <div className="space-y-0.5">
      {topics.map(topic => (
        <AwsTopicNode
          key={topic.name}
          topic={topic}
          namespaceId={namespaceId}
          messagesBasePath={messagesBasePath}
        />
      ))}
    </div>
  );
}
