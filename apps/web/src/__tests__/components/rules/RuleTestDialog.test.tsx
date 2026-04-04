import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RuleTestDialog } from '@/components/rules/RuleTestDialog';

vi.mock('@/hooks/useRules', () => ({
  useTestRule: vi.fn(),
}));

import { useTestRule } from '@/hooks/useRules';
import type { RuleResponse } from '@/lib/api/rules';

const mockUseTestRule = useTestRule as ReturnType<typeof vi.fn>;

const sampleRule: RuleResponse = {
  id: 1,
  name: 'Timeout Rule',
  description: null,
  enabled: true,
  conditions: [{ field: 'DeadLetterReason', operator: 'Contains', value: 'timeout' }],
  action: { autoReplay: true, delaySeconds: 60, maxRetries: 3, exponentialBackoff: false },
  maxReplaysPerHour: 100,
  matchCount: 5,
  successCount: 3,
  successRate: 0.6,
  pendingMatchCount: 0,
  createdAt: new Date().toISOString(),
  updatedAt: null,
};

describe('RuleTestDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseTestRule.mockReturnValue({
      mutate: vi.fn(),
      isPending: false,
      isError: false,
    });
  });

  it('renders nothing when open=false', () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <RuleTestDialog open={false} onClose={vi.fn()} />
      </QueryClientProvider>,
    );
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.queryByText('Test Rule Conditions')).not.toBeInTheDocument();
  });

  it('renders dialog with title from rule name', () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <RuleTestDialog open={true} onClose={vi.fn()} rule={sampleRule} />
      </QueryClientProvider>,
    );
    expect(screen.getByText('Test Rule: Timeout Rule')).toBeInTheDocument();
  });

  it('renders generic title when no rule provided', () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <RuleTestDialog open={true} onClose={vi.fn()} />
      </QueryClientProvider>,
    );
    expect(screen.getByText('Test Rule Conditions')).toBeInTheDocument();
  });

  it('shows Close button', () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <RuleTestDialog open={true} onClose={vi.fn()} />
      </QueryClientProvider>,
    );
    expect(screen.getByRole('button', { name: /close/i })).toBeInTheDocument();
  });

  it('calls onClose when Close button is clicked', () => {
    const onClose = vi.fn();
    render(
      <QueryClientProvider client={new QueryClient()}>
        <RuleTestDialog open={true} onClose={onClose} />
      </QueryClientProvider>,
    );
    fireEvent.click(screen.getByRole('button', { name: /close/i }));
    expect(onClose).toHaveBeenCalled();
  });

  it('shows loading spinner while test is pending', () => {
    mockUseTestRule.mockReturnValue({
      mutate: vi.fn(),
      isPending: true,
      isError: false,
    });
    render(
      <QueryClientProvider client={new QueryClient()}>
        <RuleTestDialog open={true} onClose={vi.fn()} rule={sampleRule} />
      </QueryClientProvider>,
    );
    expect(screen.getByText(/Testing against active DLQ messages/i)).toBeInTheDocument();
  });

  it('shows error message when test mutation fails', () => {
    mockUseTestRule.mockReturnValue({
      mutate: vi.fn(),
      isPending: false,
      isError: true,
    });
    render(
      <QueryClientProvider client={new QueryClient()}>
        <RuleTestDialog open={true} onClose={vi.fn()} rule={sampleRule} />
      </QueryClientProvider>,
    );
    expect(screen.getByText(/Failed to run test/i)).toBeInTheDocument();
  });

  it('shows Re-test button', () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <RuleTestDialog open={true} onClose={vi.fn()} rule={sampleRule} />
      </QueryClientProvider>,
    );
    expect(screen.getByRole('button', { name: /re-test/i })).toBeInTheDocument();
  });

  it('calls mutate when Re-test is clicked', () => {
    const mutate = vi.fn();
    mockUseTestRule.mockReturnValue({
      mutate,
      isPending: false,
      isError: false,
    });
    render(
      <QueryClientProvider client={new QueryClient()}>
        <RuleTestDialog open={true} onClose={vi.fn()} rule={sampleRule} />
      </QueryClientProvider>,
    );
    fireEvent.click(screen.getByRole('button', { name: /re-test/i }));
    expect(mutate).toHaveBeenCalled();
  });
});
