import { Info } from 'lucide-react';
import { RECOVERY_LIMITATION_SENTENCE, type RecoveryLedgerEntry } from '@servicehub/ui-shared/lib/api/recovery';
import { RecoveryStateBadge } from './RecoveryStateBadge';

/**
 * Renders one entry's verification result plus its limitation sentence, verbatim, every time —
 * the honesty requirement (roadmap §13.4). This is the ONLY place in the app that should render a
 * verification result; every surface that needs one (the ledger list, the operation detail table,
 * the message-detail recovery record) renders through this component so the limitation can never
 * be forgotten on a new surface.
 *
 * `Recovered` never means "the business transaction completed" — it means "executed and did not
 * return within the observation window." The info affordance carries that distinction inline,
 * not behind a tooltip alone (§13.4 requires the text itself to render, not just be available on
 * hover).
 */
export function VerificationResultNote({ entry }: { entry: RecoveryLedgerEntry }) {
  const windowLabel = entry.observationWindowEndsAt
    ? `not returned within the observation window`
    : null;

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center gap-2 flex-wrap">
        <RecoveryStateBadge state={entry.state} />
        {entry.state === 'Recovered' && windowLabel && (
          <span className="text-xs text-gray-500">· {windowLabel}</span>
        )}
        {entry.verificationConfidence && (
          <span className="text-xs px-1.5 py-0.5 rounded bg-gray-100 text-gray-600">
            {entry.verificationConfidence === 'Heuristic' ? 'Heuristic match (body hash)' : 'Exact match (marker)'}
          </span>
        )}
      </div>
      <p className="flex items-start gap-1 text-xs text-gray-400 leading-snug">
        <Info className="w-3.5 h-3.5 shrink-0 mt-0.5" aria-hidden="true" />
        <span>{RECOVERY_LIMITATION_SENTENCE}</span>
      </p>
    </div>
  );
}
