import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { IconRail } from '@/components/layout/IconRail';

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
  it('renders every Quick Access destination, including Live Tail', () => {
    renderAt();
    // F5 regression: Live Tail was previously missing from this list entirely.
    expect(screen.getByLabelText('Live Tail')).toBeInTheDocument();
    expect(screen.getByLabelText('Home')).toBeInTheDocument();
    expect(screen.getByLabelText('Incident Center')).toBeInTheDocument();
  });

  it('highlights only the entry matching the current route', () => {
    renderAt('/dashboard');
    expect(screen.getByLabelText('Namespace Overview')).toHaveClass('bg-primary-100');
    expect(screen.getByLabelText('Fleet Health')).not.toHaveClass('bg-primary-100');
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
