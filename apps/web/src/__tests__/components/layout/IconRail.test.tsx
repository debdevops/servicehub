import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { IconRail } from '@/components/layout/IconRail';
import { vi } from 'vitest';

vi.mock('@servicehub/ui-shared/lib/demo/DemoContext', () => ({
  useDemoContext: vi.fn(() => ({ isDemoMode: false, cloudProvider: null })),
}));

import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

const mockUseDemoContext = useDemoContext as ReturnType<typeof vi.fn>;

function renderAt(initialPath = '/dashboard') {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <IconRail />
    </MemoryRouter>,
  );
}

describe('IconRail', () => {
  it('renders every Quick Access destination when there is room for all of them', () => {
    // jsdom never lays elements out (clientHeight is always 0), which IconRail treats as "not
    // yet measured" rather than "no room" — so in this test environment nothing ever collapses,
    // exercising the same code path a tall real window does.
    renderAt();
    expect(screen.getByLabelText('Home')).toBeInTheDocument();
    expect(screen.getByLabelText('Incident Center')).toBeInTheDocument();
    expect(screen.getByLabelText('Namespace Overview')).toBeInTheDocument();
    expect(screen.getByLabelText('Approval Queue')).toBeInTheDocument();
    expect(screen.getByLabelText('Autonomy Control Center')).toBeInTheDocument();
    expect(screen.getByLabelText('Live Tail')).toBeInTheDocument();
    expect(screen.getByLabelText('Fleet Overview')).toBeInTheDocument();
    expect(screen.getByLabelText('Auto-Replay Rules')).toBeInTheDocument();
  });

  it('shows no "More" button when every destination already fits', () => {
    renderAt();
    expect(screen.queryByLabelText('More destinations')).not.toBeInTheDocument();
  });

  it('highlights only the entry matching the current route', () => {
    renderAt('/dashboard');
    expect(screen.getByLabelText('Namespace Overview')).toHaveClass('bg-primary-100');
    expect(screen.getByLabelText('Home')).not.toHaveClass('bg-primary-100');
  });

  it('links to the demo-prefixed route when in Demo Mode', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: true, cloudProvider: 'azure' });
    renderAt('/demo/azure/dashboard');
    expect(screen.getByLabelText('Home').closest('a')).toHaveAttribute('href', '/demo/azure/home');
  });

  it('always links Connect to the real (non-demo) route', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: true, cloudProvider: 'azure' });
    renderAt('/demo/azure/dashboard');
    expect(screen.getByLabelText('Connect').closest('a')).toHaveAttribute('href', '/connect');
  });
});
