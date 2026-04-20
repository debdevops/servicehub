import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { SecurityPage } from '@/pages/SecurityPage';

/**
 * Tests for SecurityPage component
 * Coverage target: 100% (currently 0%)
 * Importance: MEDIUM - Security information display
 */
describe('SecurityPage', () => {
  // Helper function to render with Router provider
  const renderWithRouter = (component: React.ReactElement) => {
    return render(
      <BrowserRouter>
        {component}
      </BrowserRouter>
    );
  };

  // ── Rendering ─────────────────────────────────────────────────────────────

  it('renders the page without errors', () => {
    expect(() => renderWithRouter(<SecurityPage />)).not.toThrow();
  });

  it('renders a heading', () => {
    renderWithRouter(<SecurityPage />);
    const headings = screen.queryAllByRole('heading');
    expect(headings.length).toBeGreaterThan(0);
  });

  it('displays content', () => {
    const { container } = renderWithRouter(<SecurityPage />);
    expect(container.querySelectorAll('*').length).toBeGreaterThan(0);
  });

  it('renders without throwing errors', () => {
    renderWithRouter(<SecurityPage />);
  });
});
