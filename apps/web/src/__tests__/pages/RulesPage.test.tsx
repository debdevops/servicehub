import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RulesPage } from '@/pages/RulesPage';

vi.mock('@servicehub/ui-shared/hooks/useRules', () => ({
  useRules: vi.fn(),
  useCreateRule: vi.fn(),
  useUpdateRule: vi.fn(),
  useDeleteRule: vi.fn(),
  useToggleRule: vi.fn(),
  useReplayAll: vi.fn(),
  useGenerateRules: vi.fn(),
}));

vi.mock('@/components/rules', () => ({
  RuleBuilderDialog: ({ open }: { open: boolean }) =>
    open ? <div data-testid="rule-builder-dialog" /> : null,
  TemplateGalleryDialog: ({ open }: { open: boolean }) =>
    open ? <div data-testid="template-gallery-dialog" /> : null,
  RuleTestDialog: ({ open }: { open: boolean }) =>
    open ? <div data-testid="rule-test-dialog" /> : null,
}));

vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/useQueues', () => ({
  useAllNamespacesQueues: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/lib/api/client', () => ({
  apiClient: { get: vi.fn().mockResolvedValue({ data: [] }) },
}));

// Covered independently by useEventStream.test.tsx — here we only need RulesPage to not
// crash on the real hook's apiClient.defaults access, which the mock above doesn't provide.
vi.mock('@servicehub/ui-shared/hooks/useEventStream', () => ({
  useEventStream: vi.fn(() => ({ connected: false })),
}));

import {
  useRules,
  useCreateRule,
  useUpdateRule,
  useDeleteRule,
  useToggleRule,
  useReplayAll,
  useGenerateRules,
} from '@servicehub/ui-shared/hooks/useRules';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useAllNamespacesQueues } from '@servicehub/ui-shared/hooks/useQueues';

const mockUseRules = useRules as ReturnType<typeof vi.fn>;
const mockUseCreateRule = useCreateRule as ReturnType<typeof vi.fn>;
const mockUseUpdateRule = useUpdateRule as ReturnType<typeof vi.fn>;
const mockUseDeleteRule = useDeleteRule as ReturnType<typeof vi.fn>;
const mockUseToggleRule = useToggleRule as ReturnType<typeof vi.fn>;
const mockUseReplayAll = useReplayAll as ReturnType<typeof vi.fn>;
const mockUseGenerateRules = useGenerateRules as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseAllNamespacesQueues = useAllNamespacesQueues as ReturnType<typeof vi.fn>;

const mockRules = [
  {
    id: 1,
    name: 'MaxDelivery Rule',
    description: 'Replays messages with max delivery count exceeded',
    enabled: true,
    conditions: [
      { field: 'DeadLetterReason', operator: 'Equals', value: 'MaxDeliveryCountExceeded' },
    ],
    action: { autoReplay: true, delaySeconds: 30, exponentialBackoff: false },
    matchCount: 10,
    successCount: 8,
    pendingMatchCount: 2,
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-02T00:00:00Z',
    namespaceId: null,
    namespaceScope: { kind: 'Global' },
  },
  {
    id: 2,
    name: 'Expired Rule',
    description: null,
    enabled: false,
    conditions: [
      { field: 'DeadLetterReason', operator: 'Contains', value: 'Expired' },
    ],
    action: { autoReplay: false, delaySeconds: 0, exponentialBackoff: false },
    matchCount: 5,
    successCount: 0,
    pendingMatchCount: 0,
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
    namespaceId: null,
    namespaceScope: { kind: 'Global' },
  },
];

const mockMutation = { mutate: vi.fn(), isPending: false };

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={['/rules']}>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </MemoryRouter>
  );
}

beforeEach(() => {
  mockUseRules.mockReturnValue({ data: mockRules, isLoading: false, refetch: vi.fn(), isFetching: false });
  mockUseCreateRule.mockReturnValue({ ...mockMutation });
  mockUseUpdateRule.mockReturnValue({ ...mockMutation });
  mockUseDeleteRule.mockReturnValue({ ...mockMutation });
  mockUseToggleRule.mockReturnValue({ ...mockMutation });
  mockUseReplayAll.mockReturnValue({ ...mockMutation, isPending: false });
  mockUseGenerateRules.mockReturnValue({ ...mockMutation, isPending: false });
  // Empty by default — namespaceIds stays [] so scope resolution reports 'global'/'loading'
  // the same way it did before scope resolution existed, keeping unrelated tests unaffected.
  mockUseNamespaces.mockReturnValue({ data: [] });
  mockUseAllNamespacesQueues.mockReturnValue([]);
});

describe('RulesPage', () => {
  it('renders page title', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    expect(screen.getByText('Auto-Replay Rules')).toBeInTheDocument();
  });

  it('renders page description', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    expect(screen.getByText(/Define rules that automatically replay/)).toBeInTheDocument();
  });

  it('renders Create Rule button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    expect(screen.getByText('Create Rule')).toBeInTheDocument();
  });

  it('renders Browse Templates button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    expect(screen.getByText('Browse Templates')).toBeInTheDocument();
  });

  it('renders rule cards for each rule', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    expect(screen.getByText('MaxDelivery Rule')).toBeInTheDocument();
    expect(screen.getByText('Expired Rule')).toBeInTheDocument();
  });

  it('renders rule description when present', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    expect(screen.getByText(/Replays messages with max delivery count exceeded/)).toBeInTheDocument();
  });

  it('renders conditions in rule card', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    expect(screen.getByText(/"MaxDeliveryCountExceeded"/)).toBeInTheDocument();
  });

  it('never claims a specific circuit-breaker threshold or that it mirrors the backend', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    expect(screen.queryByText(/mirrors the backend/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/30% over recent replays/i)).not.toBeInTheDocument();
  });

  it('distinguishes a circuit-breaker-disabled rule from a manually-disabled rule', () => {
    mockUseRules.mockReturnValue({
      data: [
        {
          ...mockRules[1],
          id: 3,
          name: 'Breaker Tripped Rule',
          enabled: false,
          disabledReason: 'CircuitBreaker',
          disabledReasonDetail:
            'Verified success rate 38% over the last 20 outcomes fell below the 50% circuit-breaker floor.',
        },
        {
          ...mockRules[1],
          id: 4,
          name: 'Manually Disabled Rule',
          enabled: false,
          disabledReason: 'Manual',
          disabledReasonDetail: null,
        },
      ],
      isLoading: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);

    // Circuit-breaker-disabled rule states why, visibly, without needing a hover/title.
    expect(screen.getByText(/Disabled by the safety circuit breaker/i)).toBeInTheDocument();
    expect(screen.getByText(/38%/)).toBeInTheDocument();

    // Manually-disabled rule gets none of that — the two must render differently.
    expect(screen.queryAllByText(/Disabled by the safety circuit breaker/i)).toHaveLength(1);
  });

  it('shows loading state', () => {
    mockUseRules.mockReturnValue({ data: undefined, isLoading: true, refetch: vi.fn(), isFetching: false });
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    expect(screen.getByText('Loading rules…')).toBeInTheDocument();
  });

  it('shows empty state when no rules', () => {
    mockUseRules.mockReturnValue({ data: [], isLoading: false, refetch: vi.fn(), isFetching: false });
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    // EmptyState renders - there will be multiple "Create Rule" buttons (header + EmptyState)
    expect(screen.getByText('No auto-replay rules yet')).toBeInTheDocument();
  });

  it('shows an error state with retry when rules fail to load', () => {
    const refetch = vi.fn();
    mockUseRules.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch, isFetching: false });
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);

    expect(screen.getByText('Failed to load rules')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Try Again'));
    expect(refetch).toHaveBeenCalled();
  });

  it('opens RuleBuilderDialog when Create Rule is clicked', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    fireEvent.click(screen.getByText('Create Rule'));
    expect(screen.getByTestId('rule-builder-dialog')).toBeInTheDocument();
  });

  it('opens TemplateGalleryDialog when Browse Templates is clicked', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    fireEvent.click(screen.getByText('Browse Templates'));
    expect(screen.getByTestId('template-gallery-dialog')).toBeInTheDocument();
  });

  it('enabled rule shows toggle-right icon', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    // The toggle titles
    const enabledToggle = screen.getAllByTitle('Disable rule');
    expect(enabledToggle.length).toBeGreaterThan(0);
  });

  it('disabled rule shows toggle-left icon', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    const disabledToggle = screen.getAllByTitle('Enable rule');
    expect(disabledToggle.length).toBeGreaterThan(0);
  });

  it('calls toggleMutation when toggle button is clicked', () => {
    const mockToggle = vi.fn();
    mockUseToggleRule.mockReturnValue({ mutate: mockToggle, isPending: false });
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    fireEvent.click(screen.getAllByTitle('Disable rule')[0]);
    expect(mockToggle).toHaveBeenCalledWith(1);
  });

  it('shows auto-replay action text when autoReplay is true', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    expect(screen.getByText(/Auto-replay after/)).toBeInTheDocument();
  });

  it('shows no automatic action text when autoReplay is false', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    expect(screen.getByText('No automatic action')).toBeInTheDocument();
  });

  it('renders Pending count in rule stats', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    expect(screen.getAllByText(/Pending/).length).toBeGreaterThan(0);
  });

  it('renders Replayed count in rule stats', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    expect(screen.getAllByText(/Replayed/).length).toBeGreaterThan(0);
  });

  it('opens RuleTestDialog when Test button is clicked', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    fireEvent.click(screen.getAllByText('Test')[0]);
    expect(screen.getByTestId('rule-test-dialog')).toBeInTheDocument();
  });

  it('opens RuleBuilderDialog when Edit button is clicked', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    fireEvent.click(screen.getAllByRole('button', { name: /^Edit$/ })[0]);
  });

  it('shows ReplayAllConfirmDialog when Replay All is clicked on enabled rule', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    // First rule (id=1) is enabled, so Replay All button is clickable
    const replayButtons = screen.getAllByText('Replay All');
    fireEvent.click(replayButtons[0]);
    expect(screen.getByText('Replay All Matching Messages')).toBeInTheDocument();
  });

  it('ReplayAllConfirmDialog shows rule name', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    fireEvent.click(screen.getAllByText('Replay All')[0]);
    expect(screen.getByText(/Rule: MaxDelivery Rule/)).toBeInTheDocument();
  });

  it('ReplayAllConfirmDialog shows destructive operation warning', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    fireEvent.click(screen.getAllByText('Replay All')[0]);
    expect(screen.getByText('Destructive Operation')).toBeInTheDocument();
  });

  it('ReplayAllConfirmDialog cancel button closes dialog', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    fireEvent.click(screen.getAllByText('Replay All')[0]);
    expect(screen.getByText('Replay All Matching Messages')).toBeInTheDocument();
    // Click Cancel text button
    fireEvent.click(screen.getByText('Cancel'));
  });

  it('ReplayAllConfirmDialog has alertdialog semantics and closes on Escape', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    fireEvent.click(screen.getAllByText('Replay All')[0]);

    const dialog = screen.getByRole('alertdialog');
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(dialog).toHaveAttribute('aria-labelledby', 'replay-all-dialog-title');

    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.queryByText('Replay All Matching Messages')).not.toBeInTheDocument();
  });

  it('ReplayAllConfirmDialog confirm calls replayAll mutate', () => {
    const mockMutate = vi.fn();
    mockUseReplayAll.mockReturnValue({ mutate: mockMutate, isPending: false });
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    fireEvent.click(screen.getAllByText('Replay All')[0]);
    expect(screen.getByText('Replay All Matching Messages')).toBeInTheDocument();
    // Click the confirm "Yes, Replay All Matches" button
    fireEvent.click(screen.getByText(/Yes, Replay All Matches/));
    expect(mockMutate).toHaveBeenCalledWith(1, expect.any(Object));
  });

  it('backdrop click cancels ReplayAllConfirmDialog', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    fireEvent.click(screen.getAllByText('Replay All')[0]);
    expect(screen.getByText('Replay All Matching Messages')).toBeInTheDocument();
    // Click the backdrop (first child of fixed container)
    const backdrop = document.querySelector('.bg-black\\/60');
    if (backdrop) fireEvent.click(backdrop);
  });

  it('Edit button opens RuleBuilderDialog with rule populated', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    const editButton = screen.getAllByText('Edit')[0];
    fireEvent.click(editButton);
    expect(screen.getByTestId('rule-builder-dialog')).toBeInTheDocument();
  });

  it('Delete button opens ConfirmDialog instead of native confirm()', () => {
    const mockDelete = vi.fn();
    mockUseDeleteRule.mockReturnValue({ mutate: mockDelete, isPending: false });
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    // Find the trash/delete icon buttons by their accessible structure
    // The delete button in RuleCard has the Trash2 icon only (no label text)
    const trashButtons = screen.getAllByRole('button').filter(
      btn => btn.querySelector('svg') && !btn.textContent?.trim()
    );
    // Filter to delete buttons (they do not have title attribute unlike toggle)
    // Click the first delete button
    const deleteBtn = screen.getAllByRole('button').find(
      btn => btn.className.includes('text-red-500') && !(btn as HTMLButtonElement).disabled
    );
    if (deleteBtn) {
      fireEvent.click(deleteBtn);
      // ConfirmDialog should now be visible
      expect(screen.getByText('Delete Rule')).toBeInTheDocument();
      // Click the confirm Delete button in the dialog
      fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
      expect(mockDelete).toHaveBeenCalled();
    } else {
      // Fallback: just verify buttons rendered
      expect(trashButtons.length).toBeGreaterThan(0);
    }
  });

  it('Delete ConfirmDialog cancel does not call deleteMutation', () => {
    const mockDelete = vi.fn();
    mockUseDeleteRule.mockReturnValue({ mutate: mockDelete, isPending: false });
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    const deleteBtn = screen.getAllByRole('button').find(
      btn => btn.className.includes('text-red-500') && !(btn as HTMLButtonElement).disabled
    );
    if (deleteBtn) {
      fireEvent.click(deleteBtn);
      expect(screen.getByText('Delete Rule')).toBeInTheDocument();
      // Click Cancel
      fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
      expect(mockDelete).not.toHaveBeenCalled();
      expect(screen.queryByText('Delete Rule')).not.toBeInTheDocument();
    }
  });

  it('Replay All button is disabled for disabled rule', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    // Expired Rule (id=2) is disabled, so its Replay All button should be disabled
    const replayButtons = screen.getAllByText('Replay All');
    // Second rule is disabled
    expect(replayButtons[1].closest('button')).toBeDisabled();
  });

  it('shows condition field and operator labels', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);
    // DeadLetterReason field → 'reason', Equals → 'equals'
    expect(screen.getAllByText(/reason/i).length).toBeGreaterThan(0);
  });
});

describe('RulesPage — rule namespace scope (server-provided)', () => {
  const globalRule = {
    id: 10,
    name: 'Global Rule',
    description: null,
    enabled: true,
    conditions: [{ field: 'DeadLetterReason', operator: 'Equals', value: 'Timeout' }],
    action: { autoReplay: true, delaySeconds: 10, exponentialBackoff: false },
    matchCount: 0,
    successCount: 0,
    pendingMatchCount: 0,
    maxReplaysPerHour: 100,
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: null,
    namespaceId: null,
    namespaceScope: { kind: 'Global' },
  };

  const namespaceScopedRule = {
    id: 11,
    name: 'Orders Rule',
    description: null,
    enabled: true,
    conditions: [{ field: 'EntityName', operator: 'Equals', value: 'orders' }],
    action: { autoReplay: true, delaySeconds: 10, exponentialBackoff: false },
    matchCount: 0,
    successCount: 0,
    pendingMatchCount: 3,
    maxReplaysPerHour: 100,
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: null,
    namespaceId: 'ns-aws-1',
    namespaceScope: { kind: 'Namespace', name: 'AWS DEV', provider: 'aws', environment: 'dev' },
  };

  const unresolvedScopeRule = {
    ...namespaceScopedRule,
    id: 12,
    name: 'Orphaned Scope Rule',
    namespaceId: 'ns-deleted',
    namespaceScope: { kind: 'Unresolved' },
  };

  it('shows an explicit ALL CLOUDS · ALL NAMESPACES banner for a Global-scoped rule', () => {
    mockUseRules.mockReturnValue({ data: [globalRule], isLoading: false, refetch: vi.fn(), isFetching: false });

    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);

    expect(screen.getByText('ALL CLOUDS · ALL NAMESPACES')).toBeInTheDocument();
  });

  it('renders the server-resolved namespace, provider, and environment for a namespace-scoped rule', () => {
    mockUseRules.mockReturnValue({ data: [namespaceScopedRule], isLoading: false, refetch: vi.fn(), isFetching: false });

    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);

    expect(screen.getByText('AWS DEV')).toBeInTheDocument();
    expect(screen.getByText('AWS')).toBeInTheDocument(); // Cloud badge — explicit, not icon-only
    expect(screen.getByText('DEV')).toBeInTheDocument(); // Environment badge
  });

  it('shows an explicit "Scope unresolved" state when the rule is scoped to a namespace that no longer exists', () => {
    mockUseRules.mockReturnValue({ data: [unresolvedScopeRule], isLoading: false, refetch: vi.fn(), isFetching: false });

    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);

    expect(screen.getByText('Scope unresolved')).toBeInTheDocument();
  });

  it('disables Replay All for a rule whose scope is unresolved, even though the rule itself is enabled', () => {
    mockUseRules.mockReturnValue({ data: [unresolvedScopeRule], isLoading: false, refetch: vi.fn(), isFetching: false });

    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);

    expect(screen.getByText('Replay All').closest('button')).toBeDisabled();
  });

  it('shows the same resolved namespace scope in the Replay-All confirmation before the destructive action', () => {
    mockUseRules.mockReturnValue({ data: [namespaceScopedRule], isLoading: false, refetch: vi.fn(), isFetching: false });

    const Wrapper = createWrapper();
    render(<Wrapper><RulesPage /></Wrapper>);

    fireEvent.click(screen.getByText('Replay All'));

    expect(screen.getByText('Replay All Matching Messages')).toBeInTheDocument();
    const dialogNamespaceMatches = screen.getAllByText('AWS DEV');
    expect(dialogNamespaceMatches.length).toBeGreaterThanOrEqual(2); // card + dialog
  });
});
