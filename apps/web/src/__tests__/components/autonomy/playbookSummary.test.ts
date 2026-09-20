import { describe, it, expect } from 'vitest';
import {
  approvalMeaning, computePlaybookStats, humanizeIdentifier, summarizeProposal, toReadableRows,
} from '@/components/autonomy/playbookSummary';
import type { PlaybookEntry } from '@servicehub/ui-shared/lib/api/playbook';

function entry(overrides: Partial<PlaybookEntry> = {}): PlaybookEntry {
  return {
    id: 'p1',
    pillarKind: 'Investigate',
    proposalKind: 'AnomalyFlag',
    evidenceRefJson: '{}',
    proposalJson: '{}',
    proposedAt: '2026-09-10T10:00:00Z',
    proposerIdentity: 'System:AnomalyDetectionWorker',
    proposerKind: 'System',
    signatureHashSnapshot: null,
    namespaceId: null,
    namespaceNameSnapshot: null,
    providerSnapshot: null,
    environmentSnapshot: null,
    relatedRecoveryOperationId: null,
    expiresAt: '2026-09-17T10:00:00Z',
    state: 'Proposed',
    disposition: null,
    closedAt: null,
    ...overrides,
  };
}

describe('humanizeIdentifier', () => {
  it('turns PascalCase into a sentence and keeps DLQ upper-case', () => {
    expect(humanizeIdentifier('DlqGrowthSpike')).toBe('DLQ growth spike');
    expect(humanizeIdentifier('MaxDeliveryCountExceeded')).toBe('Max delivery count exceeded');
  });
});

describe('summarizeProposal', () => {
  it('summarizes an anomaly from the detector payload', () => {
    const s = summarizeProposal(entry({
      proposalJson: JSON.stringify({ EntityName: 'orders', Type: 'DlqGrowthSpike', Severity: 80, Description: 'DLQ grew fast', RecommendedActions: ['Check the consumer'] }),
    }));
    expect(s.title).toBe('DLQ growth spike on orders');
    expect(s.detail).toBe('DLQ grew fast');
    expect(s.severity).toBe(80);
    expect(s.recommendedActions).toEqual(['Check the consumer']);
  });

  it('summarizes a cross-cloud correlation', () => {
    const s = summarizeProposal(entry({
      proposalKind: 'CorrelationHypothesis',
      proposalJson: JSON.stringify({ Providers: ['Azure', 'Aws'], Members: [{ EntityName: 'orders' }, { EntityName: 'payments' }] }),
    }));
    expect(s.title).toBe('2 related failures across Azure and Aws');
    expect(s.detail).toBe('Involves orders, payments.');
  });

  it('reads camelCase payloads too', () => {
    const s = summarizeProposal(entry({ proposalKind: 'PreventionRuleProposal', proposalJson: JSON.stringify({ name: 'Retry storm', entityName: 'orders' }) }));
    expect(s.title).toBe('Prevention rule: Retry storm');
  });

  it('falls back to the proposal kind — never a guess — for unknown kinds or bad JSON', () => {
    expect(summarizeProposal(entry({ proposalKind: 'SomethingNew', proposalJson: 'not json' })).title).toBe('Something new');
  });

  it('never invents a severity or confidence', () => {
    expect(summarizeProposal(entry({ proposalKind: 'ReplayPlan', proposalJson: JSON.stringify({ EntityName: 'orders' }) })).severity).toBeNull();
  });
});

describe('computePlaybookStats', () => {
  it('has no approval rate until a human has decided something', () => {
    expect(computePlaybookStats([entry(), entry({ state: 'Expired' })]).approvalRate).toBeNull();
  });

  it('counts awaiting, decided, and AI-authored proposals', () => {
    const stats = computePlaybookStats([
      entry({ state: 'Proposed' }),
      entry({ state: 'UnderReview', proposerKind: 'ReasoningAgent' }),
      entry({ state: 'Approved', pillarKind: 'Prevent' }),
      entry({ state: 'Approved' }),
      entry({ state: 'Rejected' }),
    ]);
    expect(stats.awaiting).toBe(2);
    expect(stats.approvalRate).toBeCloseTo(2 / 3);
    expect(stats.aiAuthored).toBe(1);
    expect(stats.byPillar.Prevent).toBe(1);
  });
});

describe('approvalMeaning', () => {
  it('keeps approval separate from execution for every pillar', () => {
    expect(approvalMeaning({ pillarKind: 'Prevent', proposalKind: 'PreventionRuleProposal' })).toMatch(/observe-only/);
    expect(approvalMeaning({ pillarKind: 'Recover', proposalKind: 'ReplayPlan' })).toMatch(/does not replay anything/);
    expect(approvalMeaning({ pillarKind: 'Investigate', proposalKind: 'AnomalyFlag' })).toMatch(/never triggers a replay or purge/);
  });
});

describe('toReadableRows', () => {
  it('flattens evidence to readable label/value pairs and skips empty values', () => {
    expect(toReadableRows(JSON.stringify({ AnomalyId: 'abc', Empty: null }))).toEqual([{ label: 'Anomaly id', value: 'abc' }]);
  });
});
