import { Link, useParams } from 'react-router-dom';
import { useSandboxSubscriptions } from '@/hooks/useSandboxSubscriptions';
import { SandboxPanel } from '@/components/SandboxPanel';
import { SandboxEmptyState } from '@/components/SandboxEmptyState';

export function SubscriptionsPage() {
  const { topicName = '' } = useParams<{ topicName: string }>();
  const decodedTopicName = decodeURIComponent(topicName);
  const { data: subscriptions, isLoading } = useSandboxSubscriptions(decodedTopicName);

  return (
    <div className="max-w-4xl mx-auto px-6 py-10">
      <Link to="/topics" className="text-sm text-primary-700 hover:underline">
        ← Back to Topics
      </Link>
      <h1 className="text-2xl font-bold text-gray-900 mt-2 mb-1">Subscriptions</h1>
      <p className="text-gray-600 mb-6">Topic: {decodedTopicName}</p>
      <SandboxPanel title="Subscriptions">
        {isLoading ? (
          <SandboxEmptyState message="Loading subscriptions…" />
        ) : !subscriptions?.length ? (
          <SandboxEmptyState message={`No subscriptions found for "${decodedTopicName}".`} />
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-gray-500 border-b border-gray-200">
                <th className="py-2 pr-4 font-medium">Name</th>
                <th className="py-2 pr-4 font-medium">Active</th>
                <th className="py-2 pr-4 font-medium">Dead-letter</th>
                <th className="py-2 font-medium">Status</th>
              </tr>
            </thead>
            <tbody>
              {subscriptions.map((sub) => (
                <tr key={sub.name} className="border-b border-gray-100 last:border-0">
                  <td className="py-2 pr-4 text-gray-900 font-medium">{sub.name}</td>
                  <td className="py-2 pr-4 text-gray-600">{sub.activeMessageCount}</td>
                  <td className="py-2 pr-4 text-gray-600">{sub.deadLetterMessageCount}</td>
                  <td className="py-2 text-gray-600">{sub.status}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </SandboxPanel>
    </div>
  );
}
