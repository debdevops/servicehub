import { describe, it, expect, vi, beforeEach, beforeAll } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CommandPalette } from '@/components/CommandPalette';

// ── Mocks ──────────────────────────────────────────────────────────────────────

// jsdom doesn't implement scrollIntoView — mock it globally
beforeAll(() => {
  window.HTMLElement.prototype.scrollIntoView = vi.fn();
});

const mockNavigate = vi.fn();
vi.mock('react-router-dom', () => ({
  useNavigate: () => mockNavigate,
}));

vi.mock('@/hooks/useNamespaces', () => ({
  useNamespaces: () => ({
    data: [
      { id: 'ns-1', name: 'prod-bus', displayName: 'Production Bus', environment: 'Production' },
      { id: 'ns-2', name: 'dev-bus', displayName: 'Dev Bus', environment: 'Development' },
    ],
  }),
}));

// ── Helpers ────────────────────────────────────────────────────────────────────

function renderOpen() {
  const onClose = vi.fn();
  render(<CommandPalette open={true} onClose={onClose} />);
  return { onClose };
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe('CommandPalette', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── Visibility ───────────────────────────────────────────────────────────────

  it('renders nothing when open is false', () => {
    const { container } = render(<CommandPalette open={false} onClose={vi.fn()} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders the dialog when open is true', () => {
    renderOpen();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('has aria-label="Command palette"', () => {
    renderOpen();
    expect(screen.getByRole('dialog')).toHaveAttribute('aria-label', 'Command palette');
  });

  // ── Content ──────────────────────────────────────────────────────────────────

  it('renders the search input with correct placeholder', () => {
    renderOpen();
    expect(screen.getByPlaceholderText('Search pages, namespaces, actions…')).toBeInTheDocument();
  });

  it('shows page items including Dashboard and Messages', () => {
    renderOpen();
    expect(screen.getByText('Dashboard')).toBeInTheDocument();
    expect(screen.getByText('Messages')).toBeInTheDocument();
    expect(screen.getByText('Health')).toBeInTheDocument();
  });

  it('shows namespace items from useNamespaces', () => {
    renderOpen();
    expect(screen.getByText('Production Bus')).toBeInTheDocument();
    expect(screen.getByText('Dev Bus')).toBeInTheDocument();
  });

  // ── Filtering ─────────────────────────────────────────────────────────────────

  it('filters items when user types', async () => {
    renderOpen();
    const input = screen.getByPlaceholderText('Search pages, namespaces, actions…');
    await userEvent.type(input, 'health');
    // HighlightMatch breaks text into chars; use role="option" count or label substring
    const options = screen.getAllByRole('option');
    // Only Health-related item should remain (1 option)
    expect(options.length).toBeGreaterThanOrEqual(1);
    // Dashboard item should be gone
    expect(screen.queryByText('Dashboard')).not.toBeInTheDocument();
  });

  it('shows no-results message when query matches nothing', async () => {
    renderOpen();
    const input = screen.getByPlaceholderText('Search pages, namespaces, actions…');
    await userEvent.type(input, 'zzznonexistent');
    expect(screen.getByText(/No results for/i)).toBeInTheDocument();
  });

  // ── Keyboard navigation ───────────────────────────────────────────────────────

  it('calls onClose when Escape is pressed in the input', async () => {
    const { onClose } = renderOpen();
    const input = screen.getByPlaceholderText('Search pages, namespaces, actions…');
    await userEvent.type(input, '{Escape}');
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('navigates to dashboard and calls onClose when Dashboard item is clicked', async () => {
    const { onClose } = renderOpen();
    await userEvent.click(screen.getByText('Dashboard'));
    expect(mockNavigate).toHaveBeenCalledWith('/');
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('navigates down with ArrowDown key', async () => {
    renderOpen();
    const input = screen.getByPlaceholderText('Search pages, namespaces, actions…');
    // First item (Dashboard) should be active initially — pressing ArrowDown moves to next
    fireEvent.keyDown(input, { key: 'ArrowDown' });
    // Second item should now be active (aria-selected="true")
    const options = screen.getAllByRole('option');
    expect(options[1]).toHaveAttribute('aria-selected', 'true');
  });

  it('activates item with Enter key', async () => {
    const { onClose } = renderOpen();
    const input = screen.getByPlaceholderText('Search pages, namespaces, actions…');
    fireEvent.keyDown(input, { key: 'Enter' });
    // First item is Dashboard → navigate to '/'
    expect(mockNavigate).toHaveBeenCalledWith('/');
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  // ── Backdrop close ────────────────────────────────────────────────────────────

  it('calls onClose when backdrop is clicked', async () => {
    const { onClose } = renderOpen();
    const backdrop = document.querySelector('.absolute.inset-0') as HTMLElement;
    await userEvent.click(backdrop);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  // ── Namespace navigation ──────────────────────────────────────────────────────

  it('navigates to namespace dashboard when namespace item is clicked', async () => {
    const { onClose } = renderOpen();
    await userEvent.click(screen.getByText('Production Bus'));
    expect(mockNavigate).toHaveBeenCalledWith('/?namespace=ns-1');
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
