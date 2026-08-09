import type { AuditLogItem } from '@servicehub/ui-shared/lib/api/audit';

/**
 * Recent Changes Before Failure — a fixed lookup over the count of already-fetched audit
 * entries in the fixed lookback window, never a ranked or scored inference. States that
 * changes exist and asks the operator to review; never claims one caused the failure.
 */
export function computeRecentChangesVerdict(changes: AuditLogItem[]): string {
  if (changes.length === 0) {
    return 'No recorded configuration changes in the 24h before this failure started.';
  }
  const count = changes.length;
  return `${count} change${count === 1 ? '' : 's'} occurred in the 24h before this failure started — review before further action.`;
}
