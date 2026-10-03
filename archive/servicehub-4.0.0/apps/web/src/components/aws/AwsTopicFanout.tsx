import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowRight, CheckCircle2, Megaphone, Radio, Send } from 'lucide-react';
import { useSubscriptions } from '@servicehub/ui-shared/hooks/useSubscriptions';
import { useQueues } from '@servicehub/ui-shared/hooks/useQueues';
import { SendMessageModal } from '@/components/fab/SendMessageModal';

// ============================================================================
// AWS SNS topic fan-out dashboard.
//
// SNS topics store no messages — they fan each publish out to their
// subscriptions (SQS endpoint queues here). Instead of pretending a topic has
// a message list, this view shows the delivery graph with live queue depths
// and jumps straight to the endpoint queue where messages actually land.
// ============================================================================

interface AwsTopicFanoutProps {
  namespaceId: string;
  topicName: string;
}

export function AwsTopicFanout({ namespaceId, topicName }: AwsTopicFanoutProps) {
  const navigate = useNavigate();
  const { data: subscriptions, isLoading: subsLoading } = useSubscriptions(namespaceId, topicName);
  const { data: queues } = useQueues(namespaceId);
  const [publishOpen, setPublishOpen] = useState(false);

  const findQueue = (name: string) => queues?.find(q => q.name === name);

  return (
    <div className="flex-1 overflow-auto bg-gray-50" data-testid="aws-topic-fanout">
      {/* Header */}
      <div className="bg-gradient-to-r from-orange-500 to-amber-500 text-white px-8 py-6">
        <div className="flex items-center justify-between gap-4 max-w-4xl">
          <div className="flex items-center gap-3 min-w-0">
            <div className="p-3 bg-white/15 rounded-xl shrink-0">
              <Megaphone className="w-7 h-7" />
            </div>
            <div className="min-w-0">
              <div className="text-xs font-semibold uppercase tracking-wider text-orange-100">
                Amazon SNS Topic
              </div>
              <h1 className="text-2xl font-bold truncate">{topicName}</h1>
            </div>
          </div>
          <button
            onClick={() => setPublishOpen(true)}
            className="flex items-center gap-2 px-4 py-2.5 bg-white text-orange-600 rounded-lg font-semibold shadow hover:bg-orange-50 transition-colors shrink-0"
          >
            <Send className="w-4 h-4" />
            Publish message
          </button>
        </div>
        <p className="mt-3 text-sm text-orange-100 max-w-3xl">
          SNS topics don't hold messages — every publish is delivered immediately to each
          subscription below. To inspect delivered messages, open the endpoint queue.
        </p>
      </div>

      {/* Fan-out graph */}
      <div className="px-8 py-6 max-w-4xl">
        <h2 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-3">
          Fan-out · {subscriptions?.length ?? 0} subscription{(subscriptions?.length ?? 0) === 1 ? '' : 's'}
        </h2>

        {subsLoading ? (
          <div className="text-sm text-gray-500 py-8">Loading subscriptions…</div>
        ) : !subscriptions || subscriptions.length === 0 ? (
          <div className="bg-white border border-gray-200 rounded-xl p-8 text-center text-gray-500">
            <Radio className="w-10 h-10 text-gray-300 mx-auto mb-3" />
            <p className="font-medium text-gray-700">No subscriptions</p>
            <p className="text-sm mt-1">
              Published messages are dropped until a subscription is added to this topic in AWS.
            </p>
          </div>
        ) : (
          <div className="space-y-3">
            {subscriptions.map(sub => {
              const endpointQueue = findQueue(sub.name);
              return (
                <div
                  key={sub.name}
                  className="bg-white border border-gray-200 rounded-xl p-4 flex items-center gap-4 shadow-sm hover:border-orange-300 transition-colors"
                >
                  <div className="flex items-center gap-2 text-orange-500 shrink-0">
                    <ArrowRight className="w-4 h-4" />
                    <span className="text-[10px] font-bold uppercase tracking-wide bg-orange-100 text-orange-700 px-1.5 py-0.5 rounded">
                      sqs
                    </span>
                  </div>

                  <div className="flex-1 min-w-0">
                    <div className="font-semibold text-gray-900 truncate">📥 {sub.name}</div>
                    <div className="flex items-center gap-3 mt-1 text-xs text-gray-500">
                      <span className="flex items-center gap-1 text-emerald-600">
                        <CheckCircle2 className="w-3.5 h-3.5" />
                        {sub.status === 'Active' ? 'Confirmed' : sub.status}
                      </span>
                      {endpointQueue && (
                        <>
                          <span>
                            queue depth:{' '}
                            <strong className="text-gray-700">{endpointQueue.activeMessageCount}</strong>
                          </span>
                          <span>
                            DLQ:{' '}
                            <strong className={endpointQueue.deadLetterMessageCount > 0 ? 'text-red-600' : 'text-gray-700'}>
                              {endpointQueue.deadLetterMessageCount}
                            </strong>
                          </span>
                        </>
                      )}
                    </div>
                  </div>

                  <button
                    onClick={() => navigate(`/messages?namespace=${namespaceId}&queue=${sub.name}&queueType=active`)}
                    className="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-orange-700 bg-orange-50 hover:bg-orange-100 border border-orange-200 rounded-lg transition-colors shrink-0"
                  >
                    View messages
                    <ArrowRight className="w-3.5 h-3.5" />
                  </button>
                </div>
              );
            })}
          </div>
        )}

        {/* How SNS differs from Azure — the pain point, addressed in-place */}
        <div className="mt-6 bg-sky-50 border border-sky-100 rounded-xl p-4 text-xs text-sky-800 space-y-1">
          <p className="font-semibold">How this differs from Azure Service Bus</p>
          <p>
            Azure topics store messages per subscription; SNS delivers and forgets. The messages
            you publish here live in the SQS endpoint queues above — including their own separate
            dead-letter queues.
          </p>
        </div>
      </div>

      <SendMessageModal
        isOpen={publishOpen}
        onClose={() => setPublishOpen(false)}
        onSend={() => setPublishOpen(false)}
        defaultNamespaceId={namespaceId}
        defaultEntityName={topicName}
        defaultEntityType="topic"
      />
    </div>
  );
}
