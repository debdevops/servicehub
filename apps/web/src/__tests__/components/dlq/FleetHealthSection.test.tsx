import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { FleetHealthSection } from '@/components/dlq/FleetHealthSection';
import type { FleetHealthSummary } from '@servicehub/ui-shared/hooks/useInvestigationQueue';
import type { FleetNamespaceHealth } from '@servicehub/ui-shared/lib/api/fleet';

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

function namespaceHealth(overrides: Partial<FleetNamespaceHealth> = {}): FleetNamespaceHealth {
  return {
    namespaceId: 'ns-1',
    namespaceName: 'prod-orders',
    provider: 'Azure',
    environment: 'Prod',
    activeCount: 12,
    newInWindow: 4,
    resolvedInWindow: 1,
    totalCount: 20,
    topEntity: 'orders-queue',
    topEntityCount: 8,
    topCategory: 'Timeout',
    oldestActiveDetectedAt: '2026-08-01T00:00:00Z',
    severity: 'critical',
    coverage: 'scanned',
    coverageNote: null,
    ...overrides,
  };
}

function summary(overrides: Partial<FleetHealthSummary> = {}): FleetHealthSummary {
  return {
    namespaceCount: 3,
    totalActive: 15,
    totalNewInWindow: 4,
    totalResolvedInWindow: 1,
    topUnhealthyNamespaces: [namespaceHealth()],
    ...overrides,
  };
}

function renderSection(fleetHealth: FleetHealthSummary | null | undefined) {
  return render(<FleetHealthSection fleetHealth={fleetHealth} />, { wrapper: MemoryRouter });
}

describe('FleetHealthSection', () => {
  it('renders nothing when fleetHealth is null', () => {
    const { container } = renderSection(null);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders nothing when fleetHealth is undefined', () => {
    const { container } = renderSection(undefined);
    expect(container).toBeEmptyDOMElement();
  });

  it('shows an all-healthy empty state when there are no unhealthy namespaces', () => {
    renderSection(summary({ topUnhealthyNamespaces: [], namespaceCount: 5, totalActive: 0 }));

    expect(screen.getByText('All namespaces are healthy')).toBeInTheDocument();
    expect(screen.getByText(/5 namespaces monitored, 0 active dead-letters/)).toBeInTheDocument();
  });

  it('renders a card per unhealthy namespace with severity and Open Namespace action', () => {
    renderSection(summary({
      topUnhealthyNamespaces: [
        namespaceHealth({ namespaceId: 'ns-1', namespaceName: 'prod-orders', severity: 'critical' }),
        namespaceHealth({ namespaceId: 'ns-2', namespaceName: 'staging-billing', severity: 'warning', newInWindow: 0 }),
      ],
    }));

    expect(screen.getByText('prod-orders')).toBeInTheDocument();
    expect(screen.getByText('staging-billing')).toBeInTheDocument();
    expect(screen.getByText('Critical')).toBeInTheDocument();
    expect(screen.getByText('Warning')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /Open Namespace/i })).toHaveLength(2);
  });

  it('navigates to the DLQ history deep link for the namespace on Open Namespace click', async () => {
    const user = userEvent.setup();
    renderSection(summary({
      topUnhealthyNamespaces: [namespaceHealth({ namespaceId: 'ns-42', namespaceName: 'prod-orders' })],
    }));

    await user.click(screen.getByRole('button', { name: 'Open namespace prod-orders' }));

    expect(mockNavigate).toHaveBeenCalledWith('/dlq-history?namespace=ns-42');
  });

  it('links to the full Fleet Health page', () => {
    renderSection(summary());

    expect(screen.getByRole('link', { name: /View all fleet health/i })).toHaveAttribute('href', '/fleet');
  });
});
