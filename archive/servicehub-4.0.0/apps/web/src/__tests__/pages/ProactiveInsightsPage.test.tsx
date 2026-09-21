import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import userEvent from '@testing-library/user-event';
import ProactiveInsightsPage from '@/pages/ProactiveInsightsPage';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import {
  useGenerateNarrations,
  useDetectCorrelationFindings,
  useForecastBacklog,
  useExportContractViolations,
} from '@servicehub/ui-shared/hooks/useProactiveInsights';

vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/lib/demo/DemoContext', () => ({
  useDemoContext: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/useProactiveInsights', () => ({
  useGenerateNarrations: vi.fn(),
  useDetectCorrelationFindings: vi.fn(),
  useForecastBacklog: vi.fn(),
  useExportContractViolations: vi.fn(),
}));

const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseDemoContext = useDemoContext as ReturnType<typeof vi.fn>;
const mockUseGenerateNarrations = useGenerateNarrations as ReturnType<typeof vi.fn>;
const mockUseDetectCorrelationFindings = useDetectCorrelationFindings as ReturnType<typeof vi.fn>;
const mockUseForecastBacklog = useForecastBacklog as ReturnType<typeof vi.fn>;
const mockUseExportContractViolations = useExportContractViolations as ReturnType<typeof vi.fn>;

function renderPage(initialEntry = '/insights') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <ProactiveInsightsPage />
    </MemoryRouter>,
  );
}

describe('ProactiveInsightsPage', () => {
  beforeEach(() => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: false, cloudProvider: null });
    mockUseNamespaces.mockReturnValue({ data: [{ id: 'ns-1', name: 'orders-ns', isActive: true }], isLoading: false });
    mockUseGenerateNarrations.mockReturnValue({ mutate: vi.fn(), isPending: false, isSuccess: false, data: undefined });
    mockUseDetectCorrelationFindings.mockReturnValue({ mutate: vi.fn(), isPending: false, isSuccess: false, data: undefined });
    mockUseForecastBacklog.mockReturnValue({ mutate: vi.fn(), isPending: false, isSuccess: false, data: undefined });
    mockUseExportContractViolations.mockReturnValue({ mutate: vi.fn(), isPending: false, isSuccess: false, data: undefined });
  });

  it('defaults to the narrations tab', () => {
    renderPage();
    expect(screen.getByText('Proactive Insights')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Generate narrations/ })).toBeInTheDocument();
  });

  it('generates narrations and renders results on demand', async () => {
    const mutate = vi.fn();
    mockUseGenerateNarrations.mockReturnValue({ mutate, isPending: false, isSuccess: false, data: undefined });
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: /Generate narrations/ }));
    expect(mutate).toHaveBeenCalled();
  });

  it('shows the empty state once narration generation succeeds with nothing to narrate', () => {
    mockUseGenerateNarrations.mockReturnValue({ mutate: vi.fn(), isPending: false, isSuccess: true, data: { narrations: [] } });
    renderPage();
    expect(screen.getByText('No anomaly, drift, or correlation activity in the last 24 hours to narrate.')).toBeInTheDocument();
  });

  it('renders narration cards from real generation results', () => {
    mockUseGenerateNarrations.mockReturnValue({
      mutate: vi.fn(),
      isPending: false,
      isSuccess: true,
      data: {
        narrations: [
          {
            id: 'n1',
            kind: 'CrossNamespaceCorrelation',
            headline: 'DLQ spike across two namespaces',
            summary: 'orders-ns and billing-ns both spiked within the same window.',
            severity: 80,
            recommendedActions: ['Check upstream schema change'],
            generatedAt: '2026-08-30T00:00:00Z',
          },
        ],
      },
    });
    renderPage();
    expect(screen.getByText('DLQ spike across two namespaces')).toBeInTheDocument();
    expect(screen.getByText('Severity 80')).toBeInTheDocument();
    expect(screen.getByText('Check upstream schema change')).toBeInTheDocument();
  });

  it('switches to the correlation findings tab', async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole('button', { name: /Correlation Findings/ }));
    expect(screen.getByRole('button', { name: /Detect correlations/ })).toBeInTheDocument();
  });

  it('switches to the backlog forecasts tab and lets a namespace be selected', async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole('button', { name: /Backlog Forecasts/ }));
    expect(screen.getByRole('button', { name: 'Forecast' })).toBeInTheDocument();
    expect(screen.getByText('orders-ns')).toBeInTheDocument();
  });

  it('honors the tab query parameter on load', () => {
    renderPage('/insights?tab=contract-export');
    expect(screen.getByRole('button', { name: /Generate export/ })).toBeInTheDocument();
  });

  it('shows the demo-mode banner and disables compute-on-demand actions', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: true, cloudProvider: 'azure' });
    renderPage();
    expect(screen.getByText(/Demo Mode/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Generate narrations/ })).toBeDisabled();
  });
});
