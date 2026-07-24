import type { CloudProviderType } from '@/lib/api/types';
import { ProviderIcon } from '@/components/ProviderIcon';

interface ProviderBadgeProps {
  provider?: CloudProviderType | string;
}

/** Small colored badge for a cloud provider (Azure / AWS / GCP), for dense table/list rows. */
export function ProviderBadge({ provider }: ProviderBadgeProps) {
  // Default to azure for namespaces created before multi-cloud support
  const p = (provider ?? 'azure').toLowerCase() as CloudProviderType;
  if (p === 'aws') {
    return (
      <span
        className="shrink-0 inline-flex items-center gap-1 pl-0.5 pr-1.5 py-0.5 rounded text-[10px] font-bold bg-orange-100 text-orange-700 border border-orange-200"
        title="Amazon Web Services"
      >
        <ProviderIcon provider="aws" className="w-3 h-3 shrink-0" />
        AWS
      </span>
    );
  }
  if (p === 'gcp') {
    return (
      <span
        className="shrink-0 inline-flex items-center gap-1 pl-0.5 pr-1.5 py-0.5 rounded text-[10px] font-bold bg-green-100 text-green-700 border border-green-200"
        title="Google Cloud Platform"
      >
        <ProviderIcon provider="gcp" className="w-3 h-3 shrink-0" />
        GCP
      </span>
    );
  }
  // Azure (default)
  return (
    <span
      className="shrink-0 inline-flex items-center gap-1 pl-0.5 pr-1.5 py-0.5 rounded text-[10px] font-bold bg-blue-100 text-blue-700 border border-blue-200"
      title="Microsoft Azure"
    >
      <ProviderIcon provider="azure" className="w-3 h-3 shrink-0" />
      AZ
    </span>
  );
}
