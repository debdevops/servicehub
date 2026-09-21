import { describe, it, expect } from 'vitest';
import { computeRecentChangesVerdict } from '@/components/dlq/recentChangesVerdict';
import type { AuditLogItem } from '@servicehub/ui-shared/lib/api/audit';

function change(overrides: Partial<AuditLogItem> = {}): AuditLogItem {
  return {
    id: 'audit-1',
    timestamp: '2026-08-03T09:14:00Z',
    userIdentity: 'alice@example.com',
    action: 'Rule.Toggle',
    outcome: 'Success',
    namespaceId: 'ns-1',
    namespaceName: 'prod-orders',
    entityName: null,
    cloudProvider: 'azure',
    environment: 'Prod',
    resourceName: 'Retry-on-timeout',
    sequenceNumber: null,
    detailsJson: null,
    errorDetails: null,
    clientIp: null,
    userAgent: null,
    correlationId: null,
    httpMethod: null,
    httpPath: null,
    ...overrides,
  };
}

describe('computeRecentChangesVerdict', () => {
  it('returns the no-changes message for an empty list', () => {
    expect(computeRecentChangesVerdict([])).toBe(
      'No recorded configuration changes in the 24h before this failure started.',
    );
  });

  it('uses singular "change" for exactly one entry', () => {
    expect(computeRecentChangesVerdict([change()])).toBe(
      '1 change occurred in the 24h before this failure started — review before further action.',
    );
  });

  it('uses plural "changes" for more than one entry', () => {
    expect(computeRecentChangesVerdict([change(), change({ id: 'audit-2' })])).toBe(
      '2 changes occurred in the 24h before this failure started — review before further action.',
    );
  });
});
