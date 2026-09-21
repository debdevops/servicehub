import { useDemoStats } from '@/hooks/useDemoStats';
import { useDemoData } from '@/providers/DemoDataProvider';
import { CloudProviderTabs } from '@/components/CloudProviderTabs';
import { DemoPanel } from '@/components/DemoPanel';
import { DemoEmptyState } from '@/components/DemoEmptyState';

export function DashboardPage() {
  const { data: stats, isLoading } = useDemoStats();
  const { cloudProvider } = useDemoData();

  const tiles = stats
    ? [
        { label: 'Active Messages', value: stats.totalActive },
        { label: 'Dead-lettered', value: stats.totalDlq },
        { label: 'Queues', value: stats.totalQueues },
        { label: 'Topics', value: stats.totalTopics },
        { label: 'Subscriptions', value: stats.totalSubscriptions },
        { label: 'Scheduled', value: stats.totalScheduled },
      ]
    : [];

  return (
    <div className="max-w-4xl mx-auto px-6 py-10">
      <h1 className="text-2xl font-bold text-gray-900 mb-1">Dashboard</h1>
      <p className="text-gray-600 mb-6">Fleet health at a glance for {cloudProvider}.</p>
      <CloudProviderTabs />
      <DemoPanel title="Overview">
        {isLoading ? (
          <DemoEmptyState message="Loading dashboard…" />
        ) : (
          <div className="grid grid-cols-2 sm:grid-cols-3 gap-4">
            {tiles.map(({ label, value }) => (
              <div key={label} className="rounded-lg border border-gray-200 p-4 text-center">
                <div className="text-2xl font-bold text-gray-900">{value}</div>
                <div className="text-xs text-gray-500 mt-1">{label}</div>
              </div>
            ))}
          </div>
        )}
      </DemoPanel>
    </div>
  );
}
