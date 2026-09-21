import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { KnowledgeHistoryPanel } from '@/components/dlq/KnowledgeHistoryPanel';

vi.mock('@servicehub/ui-shared/hooks/useDlqSignatures', () => ({
  useKnowledgeHistory: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/lib/demo/DemoContext', () => ({
  useDemoContext: vi.fn(),
}));

import { useKnowledgeHistory } from '@servicehub/ui-shared/hooks/useDlqSignatures';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

const mockUseKnowledgeHistory = useKnowledgeHistory as ReturnType<typeof vi.fn>;
const mockUseDemoContext = useDemoContext as ReturnType<typeof vi.fn>;

beforeEach(() => vi.clearAllMocks());

describe('KnowledgeHistoryPanel', () => {
  it('shows a Demo Mode notice instead of fetching history', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: true });
    mockUseKnowledgeHistory.mockReturnValue({ data: undefined, isLoading: false });
    render(<KnowledgeHistoryPanel namespaceId="ns-1" signatureHash="hash-1" />);
    expect(screen.getByText(/isn't available in Demo Mode/)).toBeInTheDocument();
  });

  it('shows a loading state', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: false });
    mockUseKnowledgeHistory.mockReturnValue({ data: undefined, isLoading: true });
    render(<KnowledgeHistoryPanel namespaceId="ns-1" signatureHash="hash-1" />);
    expect(screen.getByText('Loading history…')).toBeInTheDocument();
  });

  it('shows an empty state when there are no prior versions', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: false });
    mockUseKnowledgeHistory.mockReturnValue({ data: [], isLoading: false });
    render(<KnowledgeHistoryPanel namespaceId="ns-1" signatureHash="hash-1" />);
    expect(screen.getByText(/first recorded version/)).toBeInTheDocument();
  });

  it('renders prior versions with who/when and falls back to "Not provided"', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: false });
    mockUseKnowledgeHistory.mockReturnValue({
      data: [
        {
          knowledgeVersion: 2,
          rootCause: 'Second root cause',
          resolutionNotes: 'Second resolution',
          operationalNotes: null,
          runbookLink: null,
          owner: null,
          replayGuidance: null,
          tags: null,
          reviewDueAt: null,
          updatedBy: 'bob@example.com',
          updatedAt: '2026-01-02T00:00:00Z',
        },
        {
          knowledgeVersion: 1,
          rootCause: 'First root cause',
          resolutionNotes: null,
          operationalNotes: null,
          runbookLink: null,
          owner: null,
          replayGuidance: null,
          tags: null,
          reviewDueAt: null,
          updatedBy: null,
          updatedAt: '2026-01-01T00:00:00Z',
        },
      ],
      isLoading: false,
    });
    render(<KnowledgeHistoryPanel namespaceId="ns-1" signatureHash="hash-1" />);

    expect(screen.getByText('Version 2')).toBeInTheDocument();
    expect(screen.getByText('Second root cause')).toBeInTheDocument();
    expect(screen.getByText('bob@example.com')).toBeInTheDocument();
    expect(screen.getByText('Version 1')).toBeInTheDocument();
    expect(screen.getByText('First root cause')).toBeInTheDocument();
    expect(screen.getByText('Not provided')).toBeInTheDocument();
  });
});
