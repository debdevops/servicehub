import type { ProviderCapabilities } from '@servicehub/ui-shared/lib/api/cloudBridge';
import type { BulkOperationJob } from '@servicehub/ui-shared/lib/api/bulkOperations';

export type ReplaySafetyVerdict = 'safe' | 'review';

/**
 * §6.4's replay-safety verdict — a fixed lookup over two already-existing facts (provider
 * capability, most recent job outcome), never a computed/weighted score.
 */
export function computeReplaySafetyVerdict(
  capabilities: ProviderCapabilities | undefined,
  mostRecentJob: BulkOperationJob | undefined,
): ReplaySafetyVerdict {
  const destructivePeek = capabilities !== undefined && !capabilities.supportsRepeatablePeek;
  const lastJobUnsuccessful =
    mostRecentJob !== undefined &&
    (mostRecentJob.status === 'Failed' || mostRecentJob.status === 'CompletedWithErrors');
  return destructivePeek || lastJobUnsuccessful ? 'review' : 'safe';
}
