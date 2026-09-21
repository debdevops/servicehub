import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi } from 'vitest';
import type { ReactElement } from 'react';
import { SignatureSummaryCard, AutonomyStatus } from '@/components/dlq/SignatureSummaryCard';
import type { DlqClusterSignature } from '@servicehub/ui-shared/lib/api/dlqSignatures';
import type { Namespace } from '@servicehub/ui-shared/lib/api/types';
import type { DlqSummary } from '@servicehub/ui-shared/lib/api/dlqHistory';

const mockUseSignatureAutonomyStatus = vi.fn();
vi.mock('@servicehub/ui-shared/hooks/useRecoveryLedger', () => ({
  useSignatureAutonomyStatus: (...args: unknown[]) => mockUseSignatureAutonomyStatus(...args),
}));

function renderUi(ui: ReactElement) {
  return render(ui);
}

const NOW = new Date('2026-01-15T10:00:00Z');

const createSignature = (overrides?: Partial<DlqClusterSignature>): DlqClusterSignature => ({
  size: 42,
  messageIds: [1, 2, 3],
  dominantEntity: 'orders-queue',
  dominantDeadletterReason: 'MaxDeliveryCountExceeded',
  dominantDeadletterReasonCount: 40,
  topTerms: ['timeout', 'retry'],
  isNew: false,
  firstSeenAt: new Date(NOW.getTime() - 2 * 3600_000).toISOString(),
  occurrenceCount: 15,
  windowStart: new Date(NOW.getTime() - 3 * 3600_000).toISOString(),
  windowEnd: new Date(NOW.getTime() - 3600_000).toISOString(),
  explanation: '🟡 Max delivery count exceeded on orders-queue.',
  knowledge: null,
  signatureHash: 'sig-hash-1',
  status: 'Active',
  trend: 'Recurring',
  ...overrides,
});

const createNamespace = (overrides?: Partial<Namespace>): Namespace => ({
  id: 'ns-1',
  name: 'my-namespace',
  displayName: 'My Namespace',
  isActive: true,
  cloudProvider: 'aws',
  environment: 'dev',
  ...overrides,
} as Namespace);

describe('SignatureSummaryCard', () => {
  it('renders identity, count, and dominant reason from real backend fields', () => {
    mockUseSignatureAutonomyStatus.mockReturnValue({ data: undefined, isLoading: true });
    const signature = createSignature();
    const namespace = createNamespace();

    renderUi(<SignatureSummaryCard signature={signature} namespace={namespace} />);

    expect(screen.getByText('MaxDeliveryCountExceeded')).toBeInTheDocument();
    expect(screen.getByText('orders-queue')).toBeInTheDocument();
    expect(screen.getByText('My Namespace')).toBeInTheDocument();
    expect(screen.getByText(/42 messages/)).toBeInTheDocument();
  });

  it('computes % of DLQ from the namespace dlq summary when provided, and omits it otherwise', () => {
    mockUseSignatureAutonomyStatus.mockReturnValue({ data: undefined, isLoading: true });
    const signature = createSignature({ size: 25 });
    const dlqSummary = { activeMessages: 100 } as DlqSummary;

    const { rerender } = renderUi(<SignatureSummaryCard signature={signature} dlqSummary={dlqSummary} />);
    expect(screen.getByText(/25% of this namespace's DLQ/)).toBeInTheDocument();

    rerender(<SignatureSummaryCard signature={signature} />);
    expect(screen.queryByText(/% of this namespace's DLQ/)).not.toBeInTheDocument();
  });

  it('prefers an operator-recorded root cause over the heuristic explanation when present', () => {
    mockUseSignatureAutonomyStatus.mockReturnValue({ data: undefined, isLoading: true });
    const signature = createSignature({
      knowledge: {
        rootCause: 'Downstream payment API was returning 503s',
        resolutionNotes: null,
        operationalNotes: null,
        runbookLink: null,
        owner: null,
        replayGuidance: null,
        lastUpdatedAt: null,
        knowledgeVersion: 1,
        reviewDueAt: null,
        tags: null,
        updatedBy: null,
        isReviewOverdue: false,
      },
    });

    renderUi(<SignatureSummaryCard signature={signature} />);

    expect(screen.getByText(/Downstream payment API was returning 503s/)).toBeInTheDocument();
    expect(screen.getByText(/Likely cause \(recorded by an operator\)/)).toBeInTheDocument();
  });

  it('never fabricates a severity field — no such field is rendered anywhere', () => {
    mockUseSignatureAutonomyStatus.mockReturnValue({ data: undefined, isLoading: true });
    renderUi(<SignatureSummaryCard signature={createSignature()} />);
    expect(screen.queryByText(/severity/i)).not.toBeInTheDocument();
  });

  it('shows technical details only after the toggle is clicked, and hides them again on a second click', async () => {
    mockUseSignatureAutonomyStatus.mockReturnValue({ data: undefined, isLoading: true });
    const signature = createSignature();
    renderUi(<SignatureSummaryCard signature={signature} />);

    expect(screen.queryByText('Signature hash')).not.toBeInTheDocument();

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /show technical details/i }));
    expect(screen.getByText('Signature hash')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /hide technical details/i }));
    expect(screen.queryByText('Signature hash')).not.toBeInTheDocument();
  });
});

describe('AutonomyStatus', () => {
  it('shows a checking state while loading', () => {
    mockUseSignatureAutonomyStatus.mockReturnValue({ data: undefined, isLoading: true });
    renderUi(<AutonomyStatus signatureHash="sig-1" />);
    expect(screen.getByText(/Checking auto-replay eligibility/)).toBeInTheDocument();
  });

  it('shows automatic recovery available when the backend reports canAutoReplay', () => {
    mockUseSignatureAutonomyStatus.mockReturnValue({
      isLoading: false,
      data: {
        signatureHash: 'sig-1',
        actionKind: 'Replay',
        currentLevel: 4,
        levelLabel: 'Standing (L4)',
        canAutoReplay: true,
        canProveDlqAbsence: true,
        blockedReason: null,
      },
    });
    renderUi(<AutonomyStatus signatureHash="sig-1" />);
    expect(screen.getByText(/Automatic recovery available/)).toBeInTheDocument();
    expect(screen.getByText(/Standing \(L4\)/)).toBeInTheDocument();
  });

  it('shows a real, non-fabricated blocked reason when the provider cannot prove DLQ absence', () => {
    mockUseSignatureAutonomyStatus.mockReturnValue({
      isLoading: false,
      data: {
        signatureHash: 'sig-1',
        actionKind: 'Replay',
        currentLevel: 3,
        levelLabel: 'Approve (L3)',
        canAutoReplay: false,
        canProveDlqAbsence: false,
        blockedReason: 'Aws cannot currently provide sufficient verified recovery evidence for unattended replay — details here.',
      },
    });
    renderUi(<AutonomyStatus signatureHash="sig-1" />);
    expect(screen.getByText(/Automatic recovery blocked/)).toBeInTheDocument();
    expect(screen.getByText(/cannot currently provide sufficient verified recovery evidence/)).toBeInTheDocument();
  });

  it('shows manual-approval-required (not a hard block) when the provider capability allows earning L4/L5 but trust evidence is insufficient', () => {
    mockUseSignatureAutonomyStatus.mockReturnValue({
      isLoading: false,
      data: {
        signatureHash: 'sig-1',
        actionKind: 'Replay',
        currentLevel: 3,
        levelLabel: 'Approve (L3)',
        canAutoReplay: false,
        canProveDlqAbsence: true,
        blockedReason: 'This signature has not yet earned Standing (L4) or Unattended (L5) trust — replay currently requires human approval (L3).',
      },
    });
    renderUi(<AutonomyStatus signatureHash="sig-1" />);
    expect(screen.getByText(/Manual approval required/)).toBeInTheDocument();
    expect(screen.queryByText(/Automatic recovery blocked/)).not.toBeInTheDocument();
  });
});
