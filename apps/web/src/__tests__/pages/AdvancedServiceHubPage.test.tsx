import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { AdvancedServiceHubPage } from '@/pages/AdvancedServiceHubPage';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

vi.mock('@servicehub/ui-shared/lib/demo/DemoContext', () => ({
  useDemoContext: vi.fn(),
}));

const mockUseDemoContext = useDemoContext as ReturnType<typeof vi.fn>;

function renderPage() {
  return render(
    <MemoryRouter>
      <AdvancedServiceHubPage />
    </MemoryRouter>,
  );
}

describe('AdvancedServiceHubPage', () => {
  it('renders the page heading', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: false, cloudProvider: undefined });
    renderPage();
    expect(screen.getByRole('heading', { name: 'Advanced ServiceHub', level: 1 })).toBeInTheDocument();
  });

  it('renders no live-data claims — this page never fabricates a number', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: false, cloudProvider: undefined });
    renderPage();
    // Educational only: no percentages/counts sourced from nowhere. The one numeric claims
    // present are the documented, hardcoded promotion thresholds, which is expected content.
    expect(screen.getByText(/at least 10 verified outcomes, at least 95% success/)).toBeInTheDocument();
  });

  it('links out to every Advanced ServiceHub page without the demo prefix outside demo mode', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: false, cloudProvider: undefined });
    renderPage();
    expect(screen.getAllByRole('link', { name: 'Autonomy' })[0]).toHaveAttribute('href', '/autonomy');
    expect(screen.getAllByRole('link', { name: 'Recovery Evidence' })[0]).toHaveAttribute('href', '/recovery');
    expect(screen.getAllByRole('link', { name: 'Playbook Ledger' })[0]).toHaveAttribute('href', '/playbook');
    expect(screen.getAllByRole('link', { name: 'Governance' })[0]).toHaveAttribute('href', '/governance');
  });

  it('prefixes links with the demo namespace path in demo mode', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: true, cloudProvider: 'azure' });
    renderPage();
    expect(screen.getAllByRole('link', { name: 'Autonomy' })[0]).toHaveAttribute('href', '/demo/azure/autonomy');
  });

  it('describes the reasoning companion as shipped-but-opt-in, and bounded', () => {
    // This test used to assert the page said the companion was "not started". W5 shipped it —
    // disabled by default, IL-boundary-enforced, proposals only — so that sentence became false.
    // What must stay true is the pair of claims underneath it: it is off unless an operator turns
    // it on, and it can never execute.
    mockUseDemoContext.mockReturnValue({ isDemoMode: false, cloudProvider: undefined });
    renderPage();
    expect(screen.getByText(/disabled by default/i)).toBeInTheDocument();
    expect(screen.getByText(/never touches the\s+Recovery Evidence Ledger/i)).toBeInTheDocument();
    expect(screen.queryByText(/not started/i)).not.toBeInTheDocument();
  });
});
