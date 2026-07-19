import type { CloudProviderType } from '@/lib/api/types';

// Shared per-provider visual identity used by the multi-cloud overview widgets:
// Azure = sky blue, AWS = light orange, GCP = light green. Chrome-level theming
// (whole-app tint) lives in providerTheme.ts / index.css; these classes are for
// provider-scoped cards and badges that must stay distinguishable side by side.

export interface ProviderStyle {
  label: string;
  badge: string;
  headerBg: string;
  headerBorder: string;
  accentText: string;
  cardHover: string;
  countBadge: string;
  dot: string;
}

// eslint-disable-next-line react-refresh/only-export-components -- style map + badge belong together
export const PROVIDER_STYLES: Record<CloudProviderType, ProviderStyle> = {
  azure: {
    label: 'Azure',
    badge: 'bg-sky-100 text-sky-700 border-sky-200',
    headerBg: 'bg-sky-50',
    headerBorder: 'border-sky-200',
    accentText: 'text-sky-700',
    cardHover: 'hover:border-sky-400 hover:bg-sky-50',
    countBadge: 'bg-sky-100 text-sky-700',
    dot: 'bg-sky-500',
  },
  aws: {
    label: 'AWS',
    badge: 'bg-orange-100 text-orange-700 border-orange-200',
    headerBg: 'bg-orange-50',
    headerBorder: 'border-orange-200',
    accentText: 'text-orange-700',
    cardHover: 'hover:border-orange-400 hover:bg-orange-50',
    countBadge: 'bg-orange-100 text-orange-700',
    dot: 'bg-orange-500',
  },
  gcp: {
    label: 'GCP',
    badge: 'bg-green-100 text-green-700 border-green-200',
    headerBg: 'bg-green-50',
    headerBorder: 'border-green-200',
    accentText: 'text-green-700',
    cardHover: 'hover:border-green-400 hover:bg-green-50',
    countBadge: 'bg-green-100 text-green-700',
    dot: 'bg-green-500',
  },
};

// eslint-disable-next-line react-refresh/only-export-components -- style map + badge belong together
export function getProviderStyle(provider?: CloudProviderType): ProviderStyle {
  return PROVIDER_STYLES[provider ?? 'azure'];
}

export function ProviderBadge({ provider }: { provider?: CloudProviderType }) {
  const style = getProviderStyle(provider);
  return (
    <span className={`px-2 py-0.5 text-xs font-bold rounded-full border ${style.badge}`}>
      {style.label}
    </span>
  );
}
