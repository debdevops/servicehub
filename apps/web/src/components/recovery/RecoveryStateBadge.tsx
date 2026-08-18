import { RECOVERY_STATE_EXPLANATIONS, type RecoveryEntryState } from '@servicehub/ui-shared/lib/api/recovery';

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
 *
 * Wrapped in a native `<details>` so every state carries a progressive-disclosure explanation
 * (what happened / why ServiceHub knows it / what it cannot prove / what to do next) without a
 * redesign of any page that already renders this badge — no extra JS, works identically in the
 * ledger list and the operation detail page.
 */
export function RecoveryStateBadge({
  state,
  size = 'sm',
  reasonText,
}: {
  state: RecoveryEntryState;
  size?: 'sm' | 'md';
  /** The recorded detail-event reason (e.g. for `Unverified`), already humanized. Omit or pass
   * null when no reason was recorded — never fabricate one. */
  reasonText?: string | null;
}) {
  const style = STATE_STYLES[state] ?? { bg: 'bg-gray-100', text: 'text-gray-600' };
  const sizeClass = size === 'md' ? 'px-2.5 py-1 text-xs' : 'px-2 py-0.5 text-xs';
  const explanation = RECOVERY_STATE_EXPLANATIONS[state];

  return (
    <details className="inline-block">
      <summary className="list-none cursor-pointer marker:content-none" title={explanation.summary}>
        <span className={`inline-flex items-center font-medium rounded-full ${style.bg} ${style.text} ${sizeClass}`}>
          {state}
        </span>
      </summary>
      <div className="mt-1 max-w-xs text-xs text-gray-600 bg-white border border-gray-200 rounded-lg p-2.5 shadow-sm space-y-1.5">
        <p><span className="font-semibold text-gray-700">What happened: </span>{explanation.whatHappened}</p>
        <p><span className="font-semibold text-gray-700">Why ServiceHub knows this: </span>{explanation.whyKnown}</p>
        <p><span className="font-semibold text-gray-700">What ServiceHub cannot prove: </span>{explanation.cannotProve}</p>
        <p><span className="font-semibold text-gray-700">What you can do: </span>{explanation.nextStep}</p>
        {reasonText && (
          <p><span className="font-semibold text-gray-700">Recorded reason: </span>{reasonText}</p>
        )}
      </div>
    </details>
  );
}
