import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { KnowledgeEditDialog } from '@/components/dlq/KnowledgeEditDialog';
import type { FailureKnowledge } from '@servicehub/ui-shared/lib/api/dlqSignatures';

vi.mock('@servicehub/ui-shared/hooks/useDlqSignatures', () => ({
  useUpsertKnowledge: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/lib/demo/DemoContext', () => ({
  useDemoContext: vi.fn(),
}));

import { useUpsertKnowledge } from '@servicehub/ui-shared/hooks/useDlqSignatures';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

const mockUseUpsertKnowledge = useUpsertKnowledge as ReturnType<typeof vi.fn>;
const mockUseDemoContext = useDemoContext as ReturnType<typeof vi.fn>;

const existingKnowledge: FailureKnowledge = {
  rootCause: 'Database timeout',
  resolutionNotes: 'Scaled out the pool',
  operationalNotes: null,
  runbookLink: 'https://wiki.example.com/runbook',
  owner: 'platform-team@example.com',
  replayGuidance: 'Safe',
  lastUpdatedAt: '2026-01-01T00:00:00Z',
  knowledgeVersion: 2,
  reviewDueAt: null,
  tags: 'database,timeout',
  updatedBy: 'alice@example.com',
  isReviewOverdue: false,
};

function setup(knowledge: FailureKnowledge | null = existingKnowledge, isDemoMode = false) {
  const mutate = vi.fn();
  mockUseUpsertKnowledge.mockReturnValue({ mutate, isPending: false });
  mockUseDemoContext.mockReturnValue({ isDemoMode });
  const onClose = vi.fn();
  render(
    <KnowledgeEditDialog namespaceId="ns-1" signatureHash="hash-1" knowledge={knowledge} onClose={onClose} />,
  );
  return { mutate, onClose };
}

beforeEach(() => vi.clearAllMocks());

describe('KnowledgeEditDialog', () => {
  it('prefills fields from existing knowledge', () => {
    setup();
    expect(screen.getByDisplayValue('Database timeout')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Scaled out the pool')).toBeInTheDocument();
    expect(screen.getByDisplayValue('https://wiki.example.com/runbook')).toBeInTheDocument();
    expect(screen.getByDisplayValue('platform-team@example.com')).toBeInTheDocument();
    expect(screen.getByDisplayValue('database,timeout')).toBeInTheDocument();
    expect(screen.getByText('Edit Knowledge')).toBeInTheDocument();
  });

  it('shows "Add Knowledge" title and empty fields when there is no existing knowledge', () => {
    setup(null);
    expect(screen.getByText('Add Knowledge')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('What causes this failure?')).toHaveValue('');
  });

  it('disables Save when root cause is empty', () => {
    setup(null);
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });

  it('shows a validation error for an invalid runbook link', () => {
    setup();
    const runbookInput = screen.getByPlaceholderText(/wiki\.example\.com\/runbooks/i);
    fireEvent.change(runbookInput, { target: { value: 'not-a-url' } });
    expect(screen.getByText('Must be a valid http(s) URL')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });

  it('calls mutate with the trimmed request and closes on success', () => {
    const { mutate, onClose } = setup(null);

    fireEvent.change(screen.getByPlaceholderText('What causes this failure?'), {
      target: { value: '  Timeout under load  ' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(mutate).toHaveBeenCalledWith(
      {
        namespaceId: 'ns-1',
        signatureHash: 'hash-1',
        request: {
          rootCause: 'Timeout under load',
          resolutionNotes: undefined,
          operationalNotes: undefined,
          runbookLink: undefined,
          owner: undefined,
          replayGuidance: undefined,
          tags: undefined,
          reviewDueAt: undefined,
          changedBy: undefined,
        },
      },
      expect.objectContaining({ onSuccess: onClose }),
    );
  });

  it('calls onClose when Cancel is clicked', () => {
    const { onClose } = setup();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onClose).toHaveBeenCalled();
  });

  it('disables Save and shows a notice in Demo Mode', () => {
    setup(existingKnowledge, true);
    expect(screen.getByText(/Demo Mode/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });
});
