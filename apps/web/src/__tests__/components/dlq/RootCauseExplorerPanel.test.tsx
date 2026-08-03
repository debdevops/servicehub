import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { RootCauseExplorerPanel } from '@/components/dlq/RootCauseExplorerPanel';
import type { RootCauseExplorerResponse, RootCauseMatch } from '@servicehub/ui-shared/lib/api/dlqSignatures';
import type { Namespace } from '@servicehub/ui-shared/lib/api/types';

vi.mock('@servicehub/ui-shared/hooks/useDlqSignatures', () => ({
  useRootCauseMatches: vi.fn(),
}));

import { useRootCauseMatches } from '@servicehub/ui-shared/hooks/useDlqSignatures';

const mockUseRootCauseMatches = useRootCauseMatches as ReturnType<typeof vi.fn>;

function makeMatch(overrides: Partial<RootCauseMatch> = {}): RootCauseMatch {
  return {
    namespaceId: 'ns-2',
    occurrenceCount: 5,
    firstSeenAt: '2026-01-01T00:00:00Z',
    lastSeenAt: '2026-01-02T00:00:00Z',
    lifecycleStatus: 'Active',
    knowledge: null,
    ...overrides,
  };
}

function makeNamespace(overrides: Partial<Namespace> = {}): Namespace {
  return {
    id: 'ns-2',
    name: 'orders-eu',
    displayName: 'Orders (EU)',
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    cloudProvider: 'azure',
    hasListenPermission: true,
    hasSendPermission: false,
    hasManagePermission: false,
    ...overrides,
  } as Namespace;
}

beforeEach(() => vi.clearAllMocks());

describe('RootCauseExplorerPanel', () => {
  it('shows a loading state while fetching', () => {
    mockUseRootCauseMatches.mockReturnValue({ data: undefined, isLoading: true });

    render(<RootCauseExplorerPanel namespaceId="ns-1" signatureHash="hash-1" dominantDeadletterReason="MaxDeliveryCountExceeded" />);

    expect(screen.getByText('Searching the fleet…')).toBeInTheDocument();
  });

  it('shows the empty state when no other namespace has this signature', () => {
    const response: RootCauseExplorerResponse = {
      signatureHash: 'hash-1',
      dominantDeadletterReason: 'MaxDeliveryCountExceeded',
      topTerms: ['timeout'],
      totalOccurrencesAcrossFleet: 4,
      matches: [],
    };
    mockUseRootCauseMatches.mockReturnValue({ data: response, isLoading: false });

    render(<RootCauseExplorerPanel namespaceId="ns-1" signatureHash="hash-1" dominantDeadletterReason="MaxDeliveryCountExceeded" />);

    expect(screen.getByText('Not seen in any other namespace in your fleet.')).toBeInTheDocument();
  });

  it('renders a match, resolving the namespace name/provider from the given namespace list', () => {
    const response: RootCauseExplorerResponse = {
      signatureHash: 'hash-1',
      dominantDeadletterReason: 'MaxDeliveryCountExceeded',
      topTerms: ['timeout'],
      totalOccurrencesAcrossFleet: 9,
      matches: [makeMatch({ namespaceId: 'ns-2', occurrenceCount: 5, lifecycleStatus: 'Resolved' })],
    };
    mockUseRootCauseMatches.mockReturnValue({ data: response, isLoading: false });

    render(
      <RootCauseExplorerPanel
        namespaceId="ns-1"
        signatureHash="hash-1"
        dominantDeadletterReason="MaxDeliveryCountExceeded"
        namespaces={[makeNamespace()]}
      />,
    );

    expect(screen.getByText('Orders (EU)')).toBeInTheDocument();
    expect(screen.getByText('Resolved')).toBeInTheDocument();
    expect(screen.getByText(/5 occurrences/)).toBeInTheDocument();
    expect(screen.getByText(/Seen/)).toHaveTextContent('Seen 9 time(s) across 2 namespace(s) in your fleet.');
  });

  it('falls back to a short namespace identifier when the match namespace is not in the given list', () => {
    const response: RootCauseExplorerResponse = {
      signatureHash: 'hash-1',
      dominantDeadletterReason: 'PoisonMessage',
      topTerms: ['schema'],
      totalOccurrencesAcrossFleet: 6,
      matches: [makeMatch({ namespaceId: 'unlisted-namespace-guid' })],
    };
    mockUseRootCauseMatches.mockReturnValue({ data: response, isLoading: false });

    render(
      <RootCauseExplorerPanel
        namespaceId="ns-1"
        signatureHash="hash-1"
        dominantDeadletterReason="PoisonMessage"
        namespaces={[makeNamespace({ id: 'some-other-ns' })]}
      />,
    );

    expect(screen.getByText(/Namespace unlisted/)).toBeInTheDocument();
  });

  it('shows the recorded root cause and resolution notes when the match has knowledge', () => {
    const response: RootCauseExplorerResponse = {
      signatureHash: 'hash-1',
      dominantDeadletterReason: 'PoisonMessage',
      topTerms: ['schema'],
      totalOccurrencesAcrossFleet: 6,
      matches: [
        makeMatch({
          knowledge: {
            rootCause: 'Malformed payload from a legacy producer',
            resolutionNotes: 'Added schema validation at the producer boundary',
            operationalNotes: null,
            runbookLink: null,
            owner: 'checkout-team',
            replayGuidance: 'Safe',
            lastUpdatedAt: '2026-01-01T00:00:00Z',
            knowledgeVersion: 2,
            reviewDueAt: null,
            tags: null,
            updatedBy: null,
            isReviewOverdue: false,
          },
        }),
      ],
    };
    mockUseRootCauseMatches.mockReturnValue({ data: response, isLoading: false });

    render(
      <RootCauseExplorerPanel
        namespaceId="ns-1"
        signatureHash="hash-1"
        dominantDeadletterReason="PoisonMessage"
        namespaces={[makeNamespace()]}
      />,
    );

    expect(screen.getByText('Malformed payload from a legacy producer')).toBeInTheDocument();
    expect(screen.getByText(/Added schema validation at the producer boundary/)).toBeInTheDocument();
    expect(screen.getByText(/checkout-team/)).toBeInTheDocument();
  });

  it('shows "no root cause recorded" for a match with no knowledge', () => {
    const response: RootCauseExplorerResponse = {
      signatureHash: 'hash-1',
      dominantDeadletterReason: 'PoisonMessage',
      topTerms: ['schema'],
      totalOccurrencesAcrossFleet: 6,
      matches: [makeMatch({ knowledge: null })],
    };
    mockUseRootCauseMatches.mockReturnValue({ data: response, isLoading: false });

    render(
      <RootCauseExplorerPanel
        namespaceId="ns-1"
        signatureHash="hash-1"
        dominantDeadletterReason="PoisonMessage"
        namespaces={[makeNamespace()]}
      />,
    );

    expect(screen.getByText('No root cause recorded there yet.')).toBeInTheDocument();
  });
});
