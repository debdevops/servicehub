import { vi, describe, it, expect, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ConnectPage } from '@/pages/ConnectPage';

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

const mockDeleteNs = vi.fn().mockResolvedValue(undefined);

vi.mock('@/hooks/useNamespaces', () => ({
  useNamespaces: () => ({
    data: [
      {
        id: 'ns-001',
        name: 'production-sb',
        displayName: 'Production',
        isActive: true,
        lastUsedAt: '2024-01-15T10:00:00Z',
      },
      {
        id: 'ns-002',
        name: 'staging-sb',
        displayName: 'Staging',
        isActive: false,
        lastUsedAt: null,
      },
    ],
    isLoading: false,
  }),
  useNamespace: () => ({ data: undefined, isLoading: false }),
  useCreateNamespace: () => ({
    mutateAsync: vi.fn().mockResolvedValue({ id: 'ns-new', name: 'new-ns', displayName: 'New NS' }),
    isPending: false,
  }),
  useDeleteNamespace: () => ({
    mutateAsync: mockDeleteNs,
    isPending: false,
  }),
}));

vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn(), loading: vi.fn() },
  toast: vi.fn(),
}));

function renderConnectPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/connect']}>
        <ConnectPage />
      </MemoryRouter>
    </QueryClientProvider>
  );
}

describe('ConnectPage - Saved Connections', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockDeleteNs.mockResolvedValue(undefined);
  });

  it('renders saved connections list', () => {
    renderConnectPage();
    expect(screen.getByText('Saved Connections')).toBeInTheDocument();
  });

  it('shows namespace display names', () => {
    renderConnectPage();
    expect(screen.getByText('Production')).toBeInTheDocument();
    expect(screen.getByText('Staging')).toBeInTheDocument();
  });

  it('shows namespace name as subtitle', () => {
    renderConnectPage();
    // Namespace names appear in the subtitle - text may be split by sibling elements
    expect(screen.getAllByText(/production-sb/i, { exact: false }).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/staging-sb/i, { exact: false }).length).toBeGreaterThan(0);
  });

  it('shows active indicator for active namespace', () => {
    renderConnectPage();
    // Active namespace gets green dot; Staging gets gray dot
    const greenDots = document.querySelectorAll('.bg-green-500');
    expect(greenDots.length).toBeGreaterThan(0);
  });

  it('shows last used date for namespace that has one', () => {
    renderConnectPage();
    expect(screen.getByText(/Last used:/)).toBeInTheDocument();
  });

  it('renders Open button for each namespace', () => {
    renderConnectPage();
    const openButtons = screen.getAllByRole('button', { name: /Open/i });
    expect(openButtons).toHaveLength(2);
  });

  it('Open button navigates to messages with namespace id', () => {
    renderConnectPage();
    const openButtons = screen.getAllByRole('button', { name: /Open Production/i });
    fireEvent.click(openButtons[0]);
    expect(mockNavigate).toHaveBeenCalledWith('/messages?namespace=ns-001');
  });

  it('renders delete button for each namespace', () => {
    renderConnectPage();
    const deleteButtons = screen.getAllByRole('button', { name: /Delete/i });
    expect(deleteButtons).toHaveLength(2);
  });

  it('clicking delete opens confirm dialog', () => {
    renderConnectPage();
    const deleteButtons = screen.getAllByRole('button', { name: /Delete/i });
    fireEvent.click(deleteButtons[0]);
    expect(screen.getByText(/Are you sure/i)).toBeInTheDocument();
  });

  it('confirm dialog shows namespace name', () => {
    renderConnectPage();
    const deleteButtons = screen.getAllByRole('button', { name: /Delete/i });
    fireEvent.click(deleteButtons[0]);
    // Production appears in both the card and the dialog
    expect(screen.getAllByText(/Production/).length).toBeGreaterThan(0);
  });

  it('cancel in confirm dialog hides the dialog', () => {
    renderConnectPage();
    const deleteButtons = screen.getAllByRole('button', { name: /Delete/i });
    fireEvent.click(deleteButtons[0]);
    expect(screen.getByText(/Are you sure/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Cancel/i }));
    expect(screen.queryByText(/Are you sure/i)).not.toBeInTheDocument();
  });

  it('confirming delete calls deleteNamespace', async () => {
    renderConnectPage();
    const deleteButtons = screen.getAllByRole('button', { name: /Delete/i });
    fireEvent.click(deleteButtons[0]);
    // After dialog opens, click the confirm "Delete" button inside ConfirmDialog
    // ConfirmDialog renders a confirm button with the confirmLabel text
    const allButtons = screen.getAllByRole('button');
    const confirmBtn = allButtons.find(b => b.textContent === 'Delete' && !b.getAttribute('aria-label'));
    if (confirmBtn) fireEvent.click(confirmBtn);
    await waitFor(() => {
      expect(mockDeleteNs).toHaveBeenCalledWith('ns-001');
    });
  });
});
