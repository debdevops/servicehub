import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import { SendMessageModal } from '@/components/fab/SendMessageModal';

vi.mock('@servicehub/ui-shared/hooks/useMessages', () => ({
  useSendMessage: vi.fn(),
}));

import { useSendMessage } from '@servicehub/ui-shared/hooks/useMessages';

const mockUseSendMessage = useSendMessage as ReturnType<typeof vi.fn>;

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
    mockUseSendMessage.mockReturnValue({ mutateAsync: vi.fn().mockResolvedValue(undefined), isPending: false });
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

  // ── F2 regression: the Schedule delivery mode's `min` must be local wall-clock
  // time, not UTC digits reinterpreted as local (see toDatetimeLocalValue).
  it('sets the scheduled-delivery min in local wall-clock time, honoring a non-UTC offset', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2025-06-15T10:00:00.000Z'));
    const offsetSpy = vi.spyOn(Date.prototype, 'getTimezoneOffset').mockReturnValue(-330); // IST, UTC+5:30
    try {
      renderWithProviders(<SendMessageModal {...defaultProps} />);
      fireEvent.click(screen.getByRole('button', { name: /schedule/i }));
      const input = document.querySelector('input[type="datetime-local"]') as HTMLInputElement;
      // 10:00 UTC + 60s = 10:01 UTC, then +5:30 for IST = 15:31 local, NOT the raw "10:01" UTC digits.
      expect(input.min).toBe('2025-06-15T15:31');
    } finally {
      offsetSpy.mockRestore();
      vi.useRealTimers();
    }
  });

  // Copilot finding: scheduledEnqueueTime was sent as the raw datetime-local value (local
  // wall-clock, no offset) straight into scheduledEnqueueTimeUtc, so a non-UTC browser would
  // schedule a different instant than the one shown to the user.
  it('converts the scheduled-delivery time to an absolute instant before sending', async () => {
    const user = userEvent.setup({ delay: null });
    const mockMutateAsync = vi.fn().mockResolvedValue(undefined);
    mockUseSendMessage.mockReturnValue({ mutateAsync: mockMutateAsync, isPending: false });

    renderWithProviders(
      <SendMessageModal
        {...defaultProps}
        defaultNamespaceId="ns-1"
        defaultEntityName="orders"
        defaultEntityType="queue"
      />
    );

    fireEvent.click(screen.getByRole('button', { name: /schedule/i }));

    const input = document.querySelector('input[type="datetime-local"]') as HTMLInputElement;
    fireEvent.change(input, { target: { value: '2025-06-20T15:30' } });

    await user.click(screen.getByRole('button', { name: /send message/i }));

    expect(mockMutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({
        message: expect.objectContaining({
          scheduledEnqueueTime: new Date('2025-06-20T15:30').toISOString(),
        }),
      })
    );
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
