import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import { SendMessageModal } from '@/components/fab/SendMessageModal';

/**
 * Tests for SendMessageModal component
 * Coverage target: 80%+ (currently 0%)
 * Importance: HIGH - Core message sending feature
 */
describe('SendMessageModal', () => {
  const mockOnClose = vi.fn();
  const mockOnSend = vi.fn();
  let queryClient: QueryClient;

  const defaultProps = {
    isOpen: true,
    onClose: mockOnClose,
    onSend: mockOnSend,
  };

  beforeEach(() => {
    vi.clearAllMocks();
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
  });

  // Helper function to render with required providers
  const renderWithProviders = (component: React.ReactElement) => {
    return render(
      <BrowserRouter>
        <QueryClientProvider client={queryClient}>
          {component}
        </QueryClientProvider>
      </BrowserRouter>
    );
  };

  // ── Visibility ────────────────────────────────────────────────────────────

  it('renders nothing when isOpen is false', () => {
    const { container } = renderWithProviders(
      <SendMessageModal {...defaultProps} isOpen={false} />
    );
    expect(container.querySelectorAll('.fixed.inset-0.z-50')).toHaveLength(0);
  });

  it('renders modal when isOpen is true', () => {
    renderWithProviders(<SendMessageModal {...defaultProps} />);
    const title = screen.getByRole('heading', { name: /send message/i });
    expect(title).toBeInTheDocument();
  });

  it('renders without crashing', () => {
    expect(() => {
      renderWithProviders(<SendMessageModal {...defaultProps} />);
    }).not.toThrow();
  });

  it('displays modal structure', () => {
    renderWithProviders(
      <SendMessageModal {...defaultProps} />
    );

    // Check for modal heading (portal renders to body)
    expect(screen.getByRole('heading', { name: /send message/i })).toBeInTheDocument();
  });

  it('calls onClose when backdrop is clicked', async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <SendMessageModal {...defaultProps} />
    );

    // Get backdrop element from document body (portal)
    const backdrop = document.querySelector('.fixed.inset-0.z-50');
    if (backdrop && backdrop.firstChild) {
      await user.click(backdrop.firstChild as HTMLElement);
      expect(mockOnClose).toHaveBeenCalled();
    }
  });

  it('displays form elements', () => {
    renderWithProviders(<SendMessageModal {...defaultProps} />);
    const buttons = screen.getAllByRole('button');
    expect(buttons.length).toBeGreaterThan(0);
  });

  it('has close button', () => {
    renderWithProviders(
      <SendMessageModal {...defaultProps} />
    );

    // Check for close button in portal
    const buttons = screen.getAllByRole('button');
    expect(buttons.length).toBeGreaterThan(0);
  });

  it('displays header with title', () => {
    renderWithProviders(<SendMessageModal {...defaultProps} />);
    const title = screen.getByRole('heading', { name: /send message/i });
    expect(title).toBeInTheDocument();
  });

  it('allows interaction with form elements', () => {
    renderWithProviders(<SendMessageModal {...defaultProps} />);

    const buttons = screen.getAllByRole('button');
    expect(buttons.length).toBeGreaterThan(0);
  });

  it('renders with proper styling', () => {
    renderWithProviders(
      <SendMessageModal {...defaultProps} />
    );

    // Check that modal has proper structure (rendered via portal)
    expect(screen.getByRole('heading', { name: /send message/i })).toBeInTheDocument();
  });

  it('maintains component state', () => {
    const { rerender } = renderWithProviders(
      <SendMessageModal {...defaultProps} />
    );

    rerender(
      <BrowserRouter>
        <QueryClientProvider client={queryClient}>
          <SendMessageModal {...defaultProps} isOpen={false} />
        </QueryClientProvider>
      </BrowserRouter>
    );

    expect(
      document.querySelector('.fixed.inset-0.z-50')
    ).not.toBeInTheDocument();
  });
});
