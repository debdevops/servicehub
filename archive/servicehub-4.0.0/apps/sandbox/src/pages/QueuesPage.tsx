import { useSandboxQueues } from '@/hooks/useSandboxQueues';
import { useSandboxData } from '@/providers/SandboxDataProvider';
import { CloudProviderTabs } from '@/components/CloudProviderTabs';
import { SandboxPanel } from '@/components/SandboxPanel';
import { SandboxEmptyState } from '@/components/SandboxEmptyState';

export function QueuesPage() {
  const { data: queues, isLoading } = useSandboxQueues();
  const { cloudProvider } = useSandboxData();

  const emptyMessage =
    cloudProvider === 'gcp'
      ? 'GCP Pub/Sub has no standalone queue concept — see Topics instead.'
      : `No queues found for ${cloudProvider}.`;

  return (
    <div className="max-w-4xl mx-auto px-6 py-10">
      <h1 className="text-2xl font-bold text-gray-900 mb-1">Queues</h1>
      <p className="text-gray-600 mb-6">Browse queues across simulated cloud providers.</p>
      <CloudProviderTabs />
      <SandboxPanel title="Queues">
        {isLoading ? (
          <SandboxEmptyState message="Loading queues…" />
        ) : !queues?.length ? (
          <SandboxEmptyState message={emptyMessage} />
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-gray-500 border-b border-gray-200">
                <th className="py-2 pr-4 font-medium">Name</th>
                <th className="py-2 pr-4 font-medium">Active</th>
                <th className="py-2 pr-4 font-medium">Dead-letter</th>
                <th className="py-2 pr-4 font-medium">Scheduled</th>
                <th className="py-2 font-medium">Status</th>
              </tr>
            </thead>
            <tbody>
              {queues.map((queue) => (
                <tr key={queue.name} className="border-b border-gray-100 last:border-0">
                  <td className="py-2 pr-4 text-gray-900 font-medium">{queue.name}</td>
                  <td className="py-2 pr-4 text-gray-600">{queue.activeMessageCount}</td>
                  <td className="py-2 pr-4 text-gray-600">{queue.deadLetterMessageCount}</td>
                  <td className="py-2 pr-4 text-gray-600">{queue.scheduledMessageCount}</td>
                  <td className="py-2 text-gray-600">{queue.status}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </SandboxPanel>
    </div>
  );
}
