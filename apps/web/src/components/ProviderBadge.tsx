import type { CloudProviderType } from '@/lib/api/types';

interface ProviderBadgeProps {
  provider?: CloudProviderType | string;
}

/** Small colored badge for a cloud provider (Azure / AWS / GCP). */
export function ProviderBadge({ provider }: ProviderBadgeProps) {
  // Default to azure for namespaces created before multi-cloud support
  const p = (provider ?? 'azure').toLowerCase();
  if (p === 'aws') {
    return (
      <span
        className="shrink-0 inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-bold bg-orange-100 text-orange-700 border border-orange-200"
        title="Amazon Web Services"
      >
        AWS
      </span>
    );
  }
  if (p === 'gcp') {
    return (
      <span
        className="shrink-0 inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-bold bg-green-100 text-green-700 border border-green-200"
        title="Google Cloud Platform"
      >
        GCP
      </span>
    );
  }
  // Azure (default)
  return (
    <span
      className="shrink-0 inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-bold bg-blue-100 text-blue-700 border border-blue-200"
      title="Microsoft Azure"
    >
      AZ
    </span>
  );
}
