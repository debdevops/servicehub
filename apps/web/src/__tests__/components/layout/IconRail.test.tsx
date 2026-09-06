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
  it('renders exactly the five primary destinations plus More (roadmap next-chapter M4.2)', () => {
    renderAt();
    expect(screen.getByLabelText('Home')).toBeInTheDocument();
    expect(screen.getByLabelText('Incident Center')).toBeInTheDocument();
    expect(screen.getByLabelText('Namespace Overview')).toBeInTheDocument();
    expect(screen.getByLabelText('Approval Queue')).toBeInTheDocument();
    expect(screen.getByLabelText('Recovery Evidence')).toBeInTheDocument();
    expect(screen.getByLabelText('More destinations')).toBeInTheDocument();
  });

  it('relocates every other destination rather than removing it — not rendered directly in the rail', () => {
    renderAt();
    // F5's old regression target, Live Tail, and a handful of others: still reachable via Quick
    // Access and the command palette, just no longer a permanent icon in this 56px rail.
    expect(screen.queryByLabelText('Live Tail')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Fleet Health')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Auto-Replay Rules')).not.toBeInTheDocument();
  });

  it('the More button opens the command palette via the shared open-palette event', () => {
    const listener = vi.fn();
    window.addEventListener('servicehub:open-palette', listener);
    renderAt();
    screen.getByLabelText('More destinations').click();
    expect(listener).toHaveBeenCalledTimes(1);
    window.removeEventListener('servicehub:open-palette', listener);
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
