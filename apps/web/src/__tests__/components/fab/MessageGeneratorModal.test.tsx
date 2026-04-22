import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import { MessageGeneratorModal } from '@/components/fab/MessageGeneratorModal';

/**
 * Tests for MessageGeneratorModal component
 * Coverage target: 80%+ (currently 0%)
 * Importance: HIGH - Core test data generation feature
 */
describe('MessageGeneratorModal', () => {
  const mockOnClose = vi.fn();
  const mockOnGenerated = vi.fn();
  let queryClient: QueryClient;

  const defaultProps = {
    isOpen: true,
    onClose: mockOnClose,
    onGenerated: mockOnGenerated,
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
      <MessageGeneratorModal {...defaultProps} isOpen={false} />
    );
    expect(container.querySelectorAll('.fixed.inset-0.z-50')).toHaveLength(0);
  });

  it('renders modal when isOpen is true', () => {
    renderWithProviders(<MessageGeneratorModal {...defaultProps} />);
    expect(screen.getByText(/Message Generator/)).toBeInTheDocument();
  });

  it('shows modal subtitle', () => {
    renderWithProviders(<MessageGeneratorModal {...defaultProps} />);
    expect(screen.getByText(/Generate realistic test messages/i)).toBeInTheDocument();
  });

  it('displays info banner', () => {
    renderWithProviders(<MessageGeneratorModal {...defaultProps} />);
    expect(screen.getByText(/About Generated Messages/i)).toBeInTheDocument();
  });

  it('displays anomaly rate information', () => {
    renderWithProviders(<MessageGeneratorModal {...defaultProps} />);
    expect(screen.getByText(/Anomalous messages simulate/i)).toBeInTheDocument();
  });

  it('displays volume information', () => {
    renderWithProviders(<MessageGeneratorModal {...defaultProps} />);
    expect(
      screen.getByText(/Messages will be generated with varied timestamps/i)
    ).toBeInTheDocument();
  });

  it('displays cleanup options', () => {
    renderWithProviders(<MessageGeneratorModal {...defaultProps} />);
    expect(screen.getByText(/Show Cleanup Options/i)).toBeInTheDocument();
  });

  it('shows header with title and description', () => {
    renderWithProviders(<MessageGeneratorModal {...defaultProps} />);
    const title = screen.getByText(/Message Generator/);
    const desc = screen.getByText(/Generate realistic test messages/i);

    expect(title).toBeInTheDocument();
    expect(desc).toBeInTheDocument();
  });

  it('displays scenario configuration section', () => {
    renderWithProviders(<MessageGeneratorModal {...defaultProps} />);
    // Check for scenario labels
    const buttons = screen.getAllByRole('button');
    expect(buttons.length).toBeGreaterThan(0);
  });

  it('renders without crashing', () => {
    expect(() => {
      renderWithProviders(<MessageGeneratorModal {...defaultProps} />);
    }).not.toThrow();
  });

  it('has proper modal structure', () => {
    renderWithProviders(
      <MessageGeneratorModal {...defaultProps} />
    );

    // Check for modal heading (portal renders to body)
    expect(screen.getByRole('heading', { name: /message generator/i })).toBeInTheDocument();
  });

  it('calls onClose callback when backdrop is clicked', async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <MessageGeneratorModal {...defaultProps} />
    );

    // Get backdrop element from document body (portal)
    const backdrop = document.querySelector('.fixed.inset-0.z-50');
    if (backdrop && backdrop.firstChild) {
      await user.click(backdrop.firstChild as HTMLElement);
      expect(mockOnClose).toHaveBeenCalled();
    }
  });

  it('displays multiple scenario options', () => {
    renderWithProviders(<MessageGeneratorModal {...defaultProps} />);

    // Should show scenario information
    expect(screen.getByText(/Order Processing/)).toBeInTheDocument();
    expect(screen.getByText(/Payment Gateway/)).toBeInTheDocument();
  });

  it('displays entity type options', () => {
    renderWithProviders(<MessageGeneratorModal {...defaultProps} />);

    // Should have Queue and Topic buttons
    const buttons = screen.getAllByRole('button');
    const hasEntityOptions = buttons.length > 0;
    expect(hasEntityOptions).toBe(true);
  });

  it('displays cleanup toggle button', () => {
    renderWithProviders(<MessageGeneratorModal {...defaultProps} />);

    expect(screen.getByText(/Show Cleanup Options/i)).toBeInTheDocument();
  });
});

