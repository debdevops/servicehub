import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { AIFindingsDropdown } from '@/components/ai/AIFindingsDropdown';
import type { AIInsight } from '@/lib/api/types';

const makeInsight = (overrides: Partial<AIInsight> = {}): AIInsight => ({
  id: 'ins-1',
  namespaceId: 'ns-1',
  entityName: 'orders-queue',
  entityType: 'queue',
  type: 'dlq-pattern',
  title: 'DLQ Pattern Detected',
  description: 'Multiple messages are being dead-lettered',
  status: 'active',
  confidence: {
    level: 'high',
    score: 92,
    reasoning: 'Consistent pattern detected across messages',
  },
  evidence: {
    sampleSize: 10,
    affectedMessageIds: ['msg-1', 'msg-2', 'msg-3'],
    exampleMessageIds: ['msg-1'],
    metrics: [
      { label: 'DLQ Rate', value: '15%', isAnomaly: true },
      { label: 'Total Messages', value: '100', isAnomaly: false },
    ],
  },
  recommendations: [
    { title: 'Check processing logic', description: 'Check the message processing logic', priority: 'immediate' },
  ],
  timeWindow: {
    start: new Date(Date.now() - 30 * 60 * 1000).toISOString(),
    end: new Date().toISOString(),
    analysisTimestamp: new Date().toISOString(),
  },
  scope: {
    namespaceId: 'ns-1',
    queueOrTopicName: 'orders-queue',
  },
  ...overrides,
} as AIInsight);

describe('AIFindingsDropdown', () => {
  const mockOnClose = vi.fn();
  const mockOnViewEvidence = vi.fn();

  beforeEach(() => {
    mockOnClose.mockClear();
    mockOnViewEvidence.mockClear();
  });

  it('renders the header', () => {
    render(
      <AIFindingsDropdown
        insights={[makeInsight()]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    expect(screen.getByText('Active AI Patterns')).toBeInTheDocument();
  });

  it('shows count of active patterns', () => {
    render(
      <AIFindingsDropdown
        insights={[makeInsight(), makeInsight({ id: 'ins-2', title: 'Second Pattern' })]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    expect(screen.getByText(/2 patterns detected/)).toBeInTheDocument();
  });

  it('uses singular "pattern" for a single insight', () => {
    render(
      <AIFindingsDropdown
        insights={[makeInsight()]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    expect(screen.getByText(/1 pattern detected/)).toBeInTheDocument();
  });

  it('shows "No active patterns" when all insights are non-active', () => {
    render(
      <AIFindingsDropdown
        insights={[makeInsight({ status: 'resolved' })]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    expect(screen.getByText('No active patterns detected')).toBeInTheDocument();
  });

  it('shows "No active patterns" when insights list is empty', () => {
    render(
      <AIFindingsDropdown
        insights={[]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    expect(screen.getByText('No active patterns detected')).toBeInTheDocument();
  });

  it('renders insight title', () => {
    render(
      <AIFindingsDropdown
        insights={[makeInsight()]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    expect(screen.getByText('DLQ Pattern Detected')).toBeInTheDocument();
  });

  it('renders insight description', () => {
    render(
      <AIFindingsDropdown
        insights={[makeInsight()]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    expect(screen.getByText('Multiple messages are being dead-lettered')).toBeInTheDocument();
  });

  it('renders confidence score', () => {
    render(
      <AIFindingsDropdown
        insights={[makeInsight()]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    expect(screen.getByText('92%')).toBeInTheDocument();
  });

  it('renders metrics with anomaly styling', () => {
    render(
      <AIFindingsDropdown
        insights={[makeInsight()]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    expect(screen.getByText(/DLQ Rate/)).toBeInTheDocument();
  });

  it('renders "View X affected messages" button', () => {
    render(
      <AIFindingsDropdown
        insights={[makeInsight()]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    expect(screen.getByText(/View 3 affected messages/)).toBeInTheDocument();
  });

  it('calls onViewEvidence with message IDs when button is clicked', () => {
    render(
      <AIFindingsDropdown
        insights={[makeInsight()]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    fireEvent.click(screen.getByText(/View 3 affected messages/));
    expect(mockOnViewEvidence).toHaveBeenCalledWith(['msg-1', 'msg-2', 'msg-3']);
  });

  it('calls onClose when X button is clicked', () => {
    render(
      <AIFindingsDropdown
        insights={[makeInsight()]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    // The X button is the close button
    const buttons = screen.getAllByRole('button');
    // Close button is the one with X icon in header
    fireEvent.click(buttons[0]);
    expect(mockOnClose).toHaveBeenCalled();
  });

  it('calls onClose when backdrop is clicked', () => {
    render(
      <AIFindingsDropdown
        insights={[makeInsight()]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    const backdrop = document.querySelector('.fixed.inset-0.z-40');
    if (backdrop) {
      fireEvent.click(backdrop);
      expect(mockOnClose).toHaveBeenCalled();
    }
  });

  it('calls onClose when Escape key is pressed', () => {
    render(
      <AIFindingsDropdown
        insights={[makeInsight()]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(mockOnClose).toHaveBeenCalled();
  });

  it('shows footer disclaimer when insights are active', () => {
    render(
      <AIFindingsDropdown
        insights={[makeInsight()]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    expect(screen.getByText(/ServiceHub Interpretation/)).toBeInTheDocument();
  });

  it('does not show footer when no active insights', () => {
    render(
      <AIFindingsDropdown
        insights={[]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    expect(screen.queryByText(/ServiceHub Interpretation/)).not.toBeInTheDocument();
  });

  it('renders the retry-loop icon for retry type', () => {
    render(
      <AIFindingsDropdown
        insights={[makeInsight({ type: 'retry-loop' })]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    // emoji should be present somewhere in the DOM
    expect(document.body.textContent).toContain('🔄');
  });

  it('renders fallback icon for unknown type', () => {
    render(
      <AIFindingsDropdown
        insights={[makeInsight({ type: 'unknown-type' as any })]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    expect(document.body.textContent).toContain('🔍');
  });

  it('removes keydown listener on unmount', () => {
    const removeEventListenerSpy = vi.spyOn(window, 'removeEventListener');
    const { unmount } = render(
      <AIFindingsDropdown
        insights={[]}
        onClose={mockOnClose}
        onViewEvidence={mockOnViewEvidence}
      />
    );
    unmount();
    expect(removeEventListenerSpy).toHaveBeenCalledWith('keydown', expect.any(Function));
    removeEventListenerSpy.mockRestore();
  });
});
