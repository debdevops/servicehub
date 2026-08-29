import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import PlaybookLedgerPage from '@/pages/PlaybookLedgerPage';
import {
  usePlaybookEntries,
  usePlaybookEntry,
  useMarkPlaybookEntryUnderReview,
  useDispositionPlaybookEntry,
  useCorrelationAccountability,
} from '@servicehub/ui-shared/hooks/usePlaybookLedger';

vi.mock('@servicehub/ui-shared/hooks/usePlaybookLedger', () => ({
  usePlaybookEntries: vi.fn(),
  usePlaybookEntry: vi.fn(() => ({ data: undefined, isLoading: false })),
  useMarkPlaybookEntryUnderReview: vi.fn(() => ({ mutate: vi.fn(), isPending: false })),
  useDispositionPlaybookEntry: vi.fn(() => ({ mutate: vi.fn(), isPending: false })),
  useCorrelationAccountability: vi.fn(() => ({ data: undefined, isLoading: false })),
}));

const mockUsePlaybookEntries = usePlaybookEntries as ReturnType<typeof vi.fn>;
const mockUsePlaybookEntry = usePlaybookEntry as ReturnType<typeof vi.fn>;
const mockUseMarkUnderReview = useMarkPlaybookEntryUnderReview as ReturnType<typeof vi.fn>;
const mockUseDisposition = useDispositionPlaybookEntry as ReturnType<typeof vi.fn>;
const mockUseCorrelationAccountability = useCorrelationAccountability as ReturnType<typeof vi.fn>;

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/playbook']}>
      <PlaybookLedgerPage />
    </MemoryRouter>,
  );
}

const sampleEntry = {
  id: 'entry-1',
  pillarKind: 'Investigate' as const,
  proposalKind: 'AnomalyFlag',
  evidenceRefJson: '{"anomalyId":"abc-123"}',
  proposalJson: '{"severity":80}',
  proposedAt: '2026-08-29T09:00:00Z',
  proposerIdentity: 'System:AnomalyDetectionWorker',
  proposerKind: 'System' as const,
  signatureHashSnapshot: null,
  namespaceId: 'ns-1',
  namespaceNameSnapshot: 'contoso-prod',
  providerSnapshot: 'azure',
  environmentSnapshot: 'prod',
  relatedRecoveryOperationId: null,
  expiresAt: '2026-09-05T09:00:00Z',
  state: 'Proposed' as const,
  disposition: null,
  closedAt: null,
};

describe('PlaybookLedgerPage', () => {
  it('shows a loading state', () => {
    mockUsePlaybookEntries.mockReturnValue({ data: undefined, isLoading: true, isError: false, refetch: vi.fn(), isFetching: true });
    renderPage();
    expect(screen.getByText('Playbook Ledger')).toBeInTheDocument();
  });

  it('shows the empty state when there are no entries', () => {
    mockUsePlaybookEntries.mockReturnValue({ data: [], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText('No proposals recorded')).toBeInTheDocument();
  });

  it('renders an entry row with pillar, proposal kind, and namespace', () => {
    mockUsePlaybookEntries.mockReturnValue({ data: [sampleEntry], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();
    const table = screen.getByRole('table', { name: 'Playbook Ledger entries' });
    expect(screen.getByText('AnomalyFlag')).toBeInTheDocument();
    expect(within(table).getByText('Investigate')).toBeInTheDocument();
    expect(screen.getByText('contoso-prod')).toBeInTheDocument();
    expect(within(table).getByText('Proposed')).toBeInTheDocument();
  });

  it('shows "Fleet-wide" for a correlation hypothesis with no namespace', () => {
    mockUsePlaybookEntries.mockReturnValue({
      data: [{ ...sampleEntry, namespaceId: null, namespaceNameSnapshot: null, providerSnapshot: null, environmentSnapshot: null }],
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    renderPage();
    expect(screen.getByText('Fleet-wide')).toBeInTheDocument();
  });

  it('shows an error state with a retry option', () => {
    mockUsePlaybookEntries.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText('Failed to load Playbook Ledger entries')).toBeInTheDocument();
  });

  it('expands a row on click to show evidence/proposal JSON and disposition actions', () => {
    mockUsePlaybookEntries.mockReturnValue({ data: [sampleEntry], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    mockUsePlaybookEntry.mockReturnValue({
      data: {
        entry: sampleEntry,
        events: [{
          id: 'evt-1', seq: 1, entryId: 'entry-1', eventType: 'Proposed', occurredAt: '2026-08-29T09:00:00Z',
          actorIdentity: 'System:AnomalyDetectionWorker', actorKind: 'System', detailJson: null,
          prevHash: '0'.repeat(64), entryHash: 'abc', schemaVersion: 1,
        }],
      },
      isLoading: false,
    });
    renderPage();

    fireEvent.click(screen.getByText('AnomalyFlag'));

    expect(screen.getByText('Approve')).toBeInTheDocument();
    expect(screen.getByText('Reject')).toBeInTheDocument();
    expect(screen.getByText('Mark under review')).toBeInTheDocument();
  });

  it('calls the mark-under-review mutation when clicked', () => {
    const mutate = vi.fn();
    mockUsePlaybookEntries.mockReturnValue({ data: [sampleEntry], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    mockUsePlaybookEntry.mockReturnValue({ data: { entry: sampleEntry, events: [] }, isLoading: false });
    mockUseMarkUnderReview.mockReturnValue({ mutate, isPending: false });

    renderPage();
    fireEvent.click(screen.getByText('AnomalyFlag'));
    fireEvent.click(screen.getByText('Mark under review'));

    expect(mutate).toHaveBeenCalledWith('entry-1');
  });

  it('calls the disposition mutation with Approved when Approve is clicked', () => {
    const mutate = vi.fn();
    mockUsePlaybookEntries.mockReturnValue({ data: [sampleEntry], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    mockUsePlaybookEntry.mockReturnValue({ data: { entry: sampleEntry, events: [] }, isLoading: false });
    mockUseDisposition.mockReturnValue({ mutate, isPending: false });

    renderPage();
    fireEvent.click(screen.getByText('AnomalyFlag'));
    fireEvent.click(screen.getByText('Approve'));

    expect(mutate).toHaveBeenCalledWith({ entryId: 'entry-1', disposition: 'Approved' });
  });

  describe('correlation accountability strip', () => {
    it('shows "no hypotheses proposed yet" when the report is all zeros', () => {
      mockUsePlaybookEntries.mockReturnValue({ data: [], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
      mockUseCorrelationAccountability.mockReturnValue({
        data: {
          generatedAt: '2026-08-29T09:00:00Z', totalHypotheses: 0, proposedCount: 0, underReviewCount: 0,
          approvedCount: 0, rejectedCount: 0, expiredCount: 0, supersededCount: 0, approvalRate: null,
        },
        isLoading: false,
      });
      renderPage();
      expect(screen.getByText(/no correlation hypotheses proposed yet/)).toBeInTheDocument();
    });

    it('shows "not enough evidence yet" when nothing has reached a terminal disposition', () => {
      mockUsePlaybookEntries.mockReturnValue({ data: [], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
      mockUseCorrelationAccountability.mockReturnValue({
        data: {
          generatedAt: '2026-08-29T09:00:00Z', totalHypotheses: 2, proposedCount: 2, underReviewCount: 0,
          approvedCount: 0, rejectedCount: 0, expiredCount: 0, supersededCount: 0, approvalRate: null,
        },
        isLoading: false,
      });
      renderPage();
      expect(screen.getByText(/not enough evidence yet/)).toBeInTheDocument();
    });

    it('shows the computed approval rate once hypotheses have been dispositioned', () => {
      mockUsePlaybookEntries.mockReturnValue({ data: [], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
      mockUseCorrelationAccountability.mockReturnValue({
        data: {
          generatedAt: '2026-08-29T09:00:00Z', totalHypotheses: 3, proposedCount: 1, underReviewCount: 0,
          approvedCount: 1, rejectedCount: 1, expiredCount: 0, supersededCount: 0, approvalRate: 0.5,
        },
        isLoading: false,
      });
      renderPage();
      expect(screen.getByText(/50% approved \(1 of 2 dispositioned\)/)).toBeInTheDocument();
    });
  });
});
