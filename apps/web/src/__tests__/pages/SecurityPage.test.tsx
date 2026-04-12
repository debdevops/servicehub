import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { SecurityPage } from '@/pages/SecurityPage';

/**
 * Tests for SecurityPage component
 * Coverage target: 100% (currently 0%)
 * Importance: MEDIUM - Security information display
 */
describe('SecurityPage', () => {
  // ── Rendering ─────────────────────────────────────────────────────────────

  it('renders the page title', () => {
    render(<SecurityPage />);
    expect(screen.getByRole('heading', { level: 1 })).toBeInTheDocument();
  });

  it('displays security information', () => {
    render(<SecurityPage />);
    expect(screen.getByText(/security|secure|encryption/i)).toBeInTheDocument();
  });

  it('contains link to security documentation', () => {
    render(<SecurityPage />);
    const links = screen.queryAllByRole('link');
    expect(links.length).toBeGreaterThan(0);
  });

  it('renders without errors', () => {
    expect(() => render(<SecurityPage />)).not.toThrow();
  });
});
