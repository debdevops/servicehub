import type { RecoveryEntryState } from '@servicehub/ui-shared/lib/api/recovery';

const STATE_STYLES: Record<RecoveryEntryState, { bg: string; text: string }> = {
  Executing: { bg: 'bg-blue-100', text: 'text-blue-700' },
  Observing: { bg: 'bg-sky-100', text: 'text-sky-700' },
  ExecutionFailed: { bg: 'bg-red-100', text: 'text-red-700' },
  ExecutionUnknown: { bg: 'bg-orange-100', text: 'text-orange-700' },
  Recovered: { bg: 'bg-green-100', text: 'text-green-700' },
  Returned: { bg: 'bg-amber-100', text: 'text-amber-700' },
  Discarded: { bg: 'bg-gray-200', text: 'text-gray-700' },
  Unverified: { bg: 'bg-purple-100', text: 'text-purple-700' },
  WrittenOff: { bg: 'bg-gray-100', text: 'text-gray-500' },
  Expired: { bg: 'bg-gray-100', text: 'text-gray-500' },
};

/**
 * State pill for a Recovery Ledger entry. Deliberately distinguishes `Recovered` (green — "did
 * not return") from anything that could be mistaken for business-level success — `Unverified`
 * and `ExecutionUnknown` render in colors that read as "unresolved," never as a muted variant of
 * "successful," per the roadmap's honesty requirement (§13.4): never present "not observed" as
 * "successful."
 */
export function RecoveryStateBadge({ state, size = 'sm' }: { state: RecoveryEntryState; size?: 'sm' | 'md' }) {
  const style = STATE_STYLES[state] ?? { bg: 'bg-gray-100', text: 'text-gray-600' };
  const sizeClass = size === 'md' ? 'px-2.5 py-1 text-xs' : 'px-2 py-0.5 text-xs';
  return (
    <span className={`inline-flex items-center font-medium rounded-full ${style.bg} ${style.text} ${sizeClass}`}>
      {state}
    </span>
  );
}
