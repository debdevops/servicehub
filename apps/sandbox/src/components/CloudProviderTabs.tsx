import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';
import { useSandboxData } from '@/providers/SandboxDataProvider';

const PROVIDERS: { id: CloudProviderType; label: string; activeClassName: string }[] = [
  { id: 'azure', label: 'Azure', activeClassName: 'bg-sky-100 text-sky-700 border-sky-200' },
  { id: 'aws', label: 'AWS', activeClassName: 'bg-orange-100 text-orange-700 border-orange-200' },
  { id: 'gcp', label: 'GCP', activeClassName: 'bg-green-100 text-green-700 border-green-200' },
];

export function CloudProviderTabs() {
  const { cloudProvider, setCloudProvider } = useSandboxData();

  return (
    <div className="flex gap-2 mb-6">
      {PROVIDERS.map(({ id, label, activeClassName }) => (
        <button
          key={id}
          type="button"
          onClick={() => setCloudProvider(id)}
          className={`px-3 py-1.5 rounded-md border text-sm font-medium transition-colors ${
            id === cloudProvider ? activeClassName : 'border-gray-200 text-gray-500 hover:bg-gray-50'
          }`}
        >
          {label}
        </button>
      ))}
    </div>
  );
}
