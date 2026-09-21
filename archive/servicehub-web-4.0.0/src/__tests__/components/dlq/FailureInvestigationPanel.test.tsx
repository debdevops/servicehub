import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactElement } from 'react';
import { FailureInvestigationPanel } from '@/components/dlq/FailureInvestigationPanel';
import type { DlqClusterSignature, FailureKnowledge } from '@servicehub/ui-shared/lib/api/dlqSignatures';

const NOW = new Date('2026-01-15T10:00:00Z');

function renderPanel(ui: ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

const createMockKnowledge = (overrides?: Partial<FailureKnowledge>): FailureKnowledge => ({
  rootCause: 'Database connection pool exhaustion',
  resolutionNotes: 'Increased pool size from 10 to 20 connections',
  operationalNotes: 'Monitor connection count during peak hours',
  runbookLink: 'https://wiki.example.com/db-pool-runbook',
  owner: 'platform-team',
  replayGuidance: 'Safe',
  lastUpdatedAt: new Date(NOW.getTime() - 86400000).toISOString(),
  knowledgeVersion: 2,
  reviewDueAt: new Date(NOW.getTime() + 604800000).toISOString(),
  tags: 'database,critical,performance',
  updatedBy: 'alice@example.com',
  isReviewOverdue: false,
  ...overrides,
});

const createMockCluster = (overrides?: Partial<DlqClusterSignature>): DlqClusterSignature => ({
  size: 42,
  messageIds: [1, 2, 3],
  dominantEntity: 'orders-topic',
  dominantDeadletterReason: 'MaxDeliveryCountExceeded',
  dominantDeadletterReasonCount: 40,
  topTerms: ['timeout', 'connection', 'retry'],
  isNew: false,
  firstSeenAt: new Date(NOW.getTime() - 2592000000).toISOString(), // 30 days ago
  occurrenceCount: 15,
  windowStart: new Date(NOW.getTime() - 604800000).toISOString(),
  windowEnd: new Date(NOW.getTime() - 3600000).toISOString(), // 1 hour ago
  explanation: 'Connection timeouts during database maintenance windows',
  knowledge: createMockKnowledge(),
  signatureHash: 'hash-1',
  status: 'Active',
  trend: 'Recurring',
  ...overrides,
});

describe('FailureInvestigationPanel', () => {
  it('renders an Add Knowledge entry point when cluster is new and has no knowledge', () => {
    const cluster = createMockCluster({ isNew: true, knowledge: null });
    renderPanel(<FailureInvestigationPanel cluster={cluster} namespaceId="ns-1" />);
    expect(screen.getByText(/No operational knowledge has been recorded/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /add knowledge/i })).toBeInTheDocument();
  });

  it('renders empty state when no knowledge recorded', () => {
    const cluster = createMockCluster({ isNew: false, knowledge: null });
    renderPanel(<FailureInvestigationPanel cluster={cluster} namespaceId="ns-1" />);
    expect(screen.getByText(/No operational knowledge has been recorded/i)).toBeInTheDocument();
  });

  it('displays known failure and recurring badges for non-new recurring failures', () => {
    const cluster = createMockCluster();
    renderPanel(<FailureInvestigationPanel cluster={cluster} namespaceId="ns-1" />);
    expect(screen.getByText('✓ Known Failure')).toBeInTheDocument();
    expect(screen.getByText('🔁 Recurring')).toBeInTheDocument();
  });

  it('displays replay guidance with correct color based on safety level', () => {
    const cluster = createMockCluster({
      knowledge: createMockKnowledge({ replayGuidance: 'Safe' }),
    });
    renderPanel(<FailureInvestigationPanel cluster={cluster} namespaceId="ns-1" />);
    expect(screen.getByText('Safe')).toBeInTheDocument();
  });

  it('displays key metrics: occurrence count and dates', () => {
    const cluster = createMockCluster();
    renderPanel(<FailureInvestigationPanel cluster={cluster} namespaceId="ns-1" />);
    expect(screen.getByText('Occurrences')).toBeInTheDocument();
    expect(screen.getByText('15')).toBeInTheDocument();
    expect(screen.getByText('First Seen')).toBeInTheDocument();
    expect(screen.getByText('Last Seen')).toBeInTheDocument();
  });

  it('collapses and expands details on button click', async () => {
    const cluster = createMockCluster();
    renderPanel(<FailureInvestigationPanel cluster={cluster} namespaceId="ns-1" />);

    const expandButton = screen.getByRole('button', { name: /expand details/i });
    expect(screen.queryByText('Root Cause')).not.toBeInTheDocument();

    await userEvent.click(expandButton);
    expect(screen.getByText('Root Cause')).toBeInTheDocument();
    expect(screen.getByText('Database connection pool exhaustion')).toBeInTheDocument();
  });

  it('displays root cause when expanded', async () => {
    const cluster = createMockCluster();
    renderPanel(<FailureInvestigationPanel cluster={cluster} namespaceId="ns-1" />);

    await userEvent.click(screen.getByRole('button', { name: /expand details/i }));
    expect(screen.getByText('Database connection pool exhaustion')).toBeInTheDocument();
  });

  it('displays resolution notes when expanded', async () => {
    const cluster = createMockCluster();
    renderPanel(<FailureInvestigationPanel cluster={cluster} namespaceId="ns-1" />);

    await userEvent.click(screen.getByRole('button', { name: /expand details/i }));
    expect(screen.getByText('How We Fixed It')).toBeInTheDocument();
    expect(screen.getByText('Increased pool size from 10 to 20 connections')).toBeInTheDocument();
  });

  it('displays owner information', async () => {
    const cluster = createMockCluster();
    renderPanel(<FailureInvestigationPanel cluster={cluster} namespaceId="ns-1" />);

    await userEvent.click(screen.getByRole('button', { name: /expand details/i }));
    expect(screen.getByText('Owner')).toBeInTheDocument();
    expect(screen.getByText('platform-team')).toBeInTheDocument();
  });

  it('displays runbook link as clickable anchor', async () => {
    const cluster = createMockCluster();
    renderPanel(<FailureInvestigationPanel cluster={cluster} namespaceId="ns-1" />);

    await userEvent.click(screen.getByRole('button', { name: /expand details/i }));
    const link = screen.getByRole('link', { name: /https:\/\/wiki\.example\.com/i });
    expect(link).toHaveAttribute('href', 'https://wiki.example.com/db-pool-runbook');
    expect(link).toHaveAttribute('target', '_blank');
  });

  it('displays operational notes', async () => {
    const cluster = createMockCluster();
    renderPanel(<FailureInvestigationPanel cluster={cluster} namespaceId="ns-1" />);

    await userEvent.click(screen.getByRole('button', { name: /expand details/i }));
    expect(screen.getByText('Operational Notes')).toBeInTheDocument();
    expect(screen.getByText('Monitor connection count during peak hours')).toBeInTheDocument();
  });

  it('displays tags parsed from comma-separated string', async () => {
    const cluster = createMockCluster();
    renderPanel(<FailureInvestigationPanel cluster={cluster} namespaceId="ns-1" />);

    await userEvent.click(screen.getByRole('button', { name: /expand details/i }));
    expect(screen.getByText('Tags')).toBeInTheDocument();
    expect(screen.getByText('database')).toBeInTheDocument();
    expect(screen.getByText('critical')).toBeInTheDocument();
    expect(screen.getByText('performance')).toBeInTheDocument();
  });

  it('displays review due date', async () => {
    const cluster = createMockCluster();
    renderPanel(<FailureInvestigationPanel cluster={cluster} namespaceId="ns-1" />);

    await userEvent.click(screen.getByRole('button', { name: /expand details/i }));
    expect(screen.getByText('Review Due')).toBeInTheDocument();
  });

  it('handles knowledge with only partial fields', async () => {
    const cluster = createMockCluster({
      knowledge: createMockKnowledge({
        rootCause: 'Test cause',
        resolutionNotes: null,
        operationalNotes: null,
        runbookLink: null,
        owner: null,
        tags: null,
        reviewDueAt: null,
      }),
    });
    renderPanel(<FailureInvestigationPanel cluster={cluster} namespaceId="ns-1" />);

    await userEvent.click(screen.getByRole('button', { name: /expand details/i }));
    expect(screen.getByText('Test cause')).toBeInTheDocument();
    expect(screen.queryByText('How We Fixed It')).not.toBeInTheDocument();
  });

  it('handles unsafe replay guidance', async () => {
    const cluster = createMockCluster({
      knowledge: createMockKnowledge({ replayGuidance: 'Unsafe' }),
    });
    renderPanel(<FailureInvestigationPanel cluster={cluster} namespaceId="ns-1" />);
    expect(screen.getByText('Unsafe')).toBeInTheDocument();
  });

  it('handles investigate replay guidance', async () => {
    const cluster = createMockCluster({
      knowledge: createMockKnowledge({ replayGuidance: 'Investigate' }),
    });
    renderPanel(<FailureInvestigationPanel cluster={cluster} namespaceId="ns-1" />);
    expect(screen.getByText('Investigate')).toBeInTheDocument();
  });

  it('displays knowledge version from metadata', () => {
    const cluster = createMockCluster({
      knowledge: createMockKnowledge({ knowledgeVersion: 3 }),
    });
    renderPanel(<FailureInvestigationPanel cluster={cluster} namespaceId="ns-1" />);
    expect(screen.getByText('Knowledge v3')).toBeInTheDocument();
  });

  it('does not render sections with null/empty knowledge fields', async () => {
    const cluster = createMockCluster({
      knowledge: createMockKnowledge({
        rootCause: null,
        operationalNotes: null,
        runbookLink: null,
      }),
    });
    renderPanel(<FailureInvestigationPanel cluster={cluster} namespaceId="ns-1" />);

    await userEvent.click(screen.getByRole('button', { name: /expand details/i }));
    expect(screen.queryByText('Root Cause')).not.toBeInTheDocument();
    expect(screen.queryByText('Operational Notes')).not.toBeInTheDocument();
    expect(screen.queryByText('Runbook')).not.toBeInTheDocument();
  });
});
