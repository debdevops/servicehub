import { useDemoDlqMessages } from '@/hooks/useDemoDlqMessages';
import { useDemoData } from '@/providers/DemoDataProvider';
import { CloudProviderTabs } from '@/components/CloudProviderTabs';
import { DemoPanel } from '@/components/DemoPanel';
import { DemoEmptyState } from '@/components/DemoEmptyState';

export function DlqPage() {
  const { data, isLoading } = useDemoDlqMessages();
  const { cloudProvider } = useDemoData();

  return (
    <div className="max-w-4xl mx-auto px-6 py-10">
      <h1 className="text-2xl font-bold text-gray-900 mb-1">Dead-Letter Queue</h1>
      <p className="text-gray-600 mb-6">Investigate dead-lettered messages across simulated cloud providers.</p>
      <CloudProviderTabs />
      <DemoPanel title="Dead-Lettered Messages">
        {isLoading ? (
          <DemoEmptyState message="Loading dead-lettered messages…" />
        ) : !data?.items.length ? (
          <DemoEmptyState message={`No dead-lettered messages found for ${cloudProvider}.`} />
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-gray-500 border-b border-gray-200">
                <th className="py-2 pr-4 font-medium">Message ID</th>
                <th className="py-2 pr-4 font-medium">Enqueued</th>
                <th className="py-2 pr-4 font-medium">Delivery Count</th>
                <th className="py-2 font-medium">Dead-Letter Reason</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((message) => (
                <tr key={message.messageId} className="border-b border-gray-100 last:border-0">
                  <td className="py-2 pr-4 text-gray-900 font-medium">{message.messageId}</td>
                  <td className="py-2 pr-4 text-gray-600">{new Date(message.enqueuedTime).toLocaleString()}</td>
                  <td className="py-2 pr-4 text-gray-600">{message.deliveryCount}</td>
                  <td className="py-2 text-gray-600">
                    <span className="px-2 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-700">
                      {message.deadLetterReason ?? 'Unknown'}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </DemoPanel>
    </div>
  );
}
