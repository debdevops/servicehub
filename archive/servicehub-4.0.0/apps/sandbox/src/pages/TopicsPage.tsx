import { Link } from 'react-router-dom';
import { useSandboxTopics } from '@/hooks/useSandboxTopics';
import { useSandboxData } from '@/providers/SandboxDataProvider';
import { CloudProviderTabs } from '@/components/CloudProviderTabs';
import { SandboxPanel } from '@/components/SandboxPanel';
import { SandboxEmptyState } from '@/components/SandboxEmptyState';

export function TopicsPage() {
  const { data: topics, isLoading } = useSandboxTopics();
  const { cloudProvider } = useSandboxData();

  return (
    <div className="max-w-4xl mx-auto px-6 py-10">
      <h1 className="text-2xl font-bold text-gray-900 mb-1">Topics</h1>
      <p className="text-gray-600 mb-6">Browse topics across simulated cloud providers.</p>
      <CloudProviderTabs />
      <SandboxPanel title="Topics">
        {isLoading ? (
          <SandboxEmptyState message="Loading topics…" />
        ) : !topics?.length ? (
          <SandboxEmptyState message={`No topics found for ${cloudProvider}.`} />
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-gray-500 border-b border-gray-200">
                <th className="py-2 pr-4 font-medium">Name</th>
                <th className="py-2 pr-4 font-medium">Subscriptions</th>
                <th className="py-2 font-medium">Status</th>
              </tr>
            </thead>
            <tbody>
              {topics.map((topic) => (
                <tr key={topic.name} className="border-b border-gray-100 last:border-0">
                  <td className="py-2 pr-4">
                    <Link
                      to={`/topics/${encodeURIComponent(topic.name)}`}
                      className="text-primary-700 font-medium hover:underline"
                    >
                      {topic.name}
                    </Link>
                  </td>
                  <td className="py-2 pr-4 text-gray-600">{topic.subscriptionCount}</td>
                  <td className="py-2 text-gray-600">{topic.status}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </SandboxPanel>
    </div>
  );
}
