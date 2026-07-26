import type { CloudProviderType } from '@/lib/api/types';
import { ProviderIcon } from '@/components/ProviderIcon';

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
  /** Left-border accent for card-style rows — a subtle, scannable provider cue. */
  accentBorder: string;
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
    // Exact brand accent (#0078D4) — reserved for small, high-signal elements
    // (status dots, border accents), not backgrounds, per the light-blue enterprise theme.
    dot: 'bg-[#0078D4]',
    accentBorder: 'border-l-[#0078D4]',
  },
  aws: {
    label: 'AWS',
    badge: 'bg-orange-100 text-orange-700 border-orange-200',
    headerBg: 'bg-orange-50',
    headerBorder: 'border-orange-200',
    accentText: 'text-orange-700',
    cardHover: 'hover:border-orange-400 hover:bg-orange-50',
    countBadge: 'bg-orange-100 text-orange-700',
    dot: 'bg-[#FF9900]',
    accentBorder: 'border-l-[#FF9900]',
  },
  gcp: {
    label: 'GCP',
    badge: 'bg-green-100 text-green-700 border-green-200',
    headerBg: 'bg-green-50',
    headerBorder: 'border-green-200',
    accentText: 'text-green-700',
    cardHover: 'hover:border-green-400 hover:bg-green-50',
    countBadge: 'bg-green-100 text-green-700',
    dot: 'bg-[#34A853]',
    accentBorder: 'border-l-[#34A853]',
  },
};

// eslint-disable-next-line react-refresh/only-export-components -- style map + badge belong together
export function getProviderStyle(provider?: CloudProviderType): ProviderStyle {
  return PROVIDER_STYLES[provider ?? 'azure'];
}

export function ProviderBadge({ provider }: { provider?: CloudProviderType }) {
  const style = getProviderStyle(provider);
  return (
    <span className={`inline-flex items-center gap-1 pl-1 pr-2 py-0.5 text-xs font-bold rounded-full border ${style.badge}`}>
      <ProviderIcon provider={provider} className="w-3.5 h-3.5 shrink-0" />
      {style.label}
    </span>
  );
}
