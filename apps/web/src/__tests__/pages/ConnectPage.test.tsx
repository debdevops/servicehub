import { vi, describe, it, expect, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ConnectPage } from '@/pages/ConnectPage';

// Mock hooks used by ConnectPage
vi.mock('@/hooks/useNamespaces', () => ({
  useNamespaces: () => ({ data: [], isLoading: false }),
  useNamespace: () => ({ data: undefined, isLoading: false }),
  useCreateNamespace: () => ({
    mutateAsync: vi.fn().mockResolvedValue({ id: 'ns-new', name: 'test-ns', displayName: 'Test NS' }),
    isPending: false,
  }),
  useDeleteNamespace: () => ({
    mutateAsync: vi.fn().mockResolvedValue(undefined),
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

describe('ConnectPage', () => {
  beforeEach(() => vi.clearAllMocks());

  it('renders without crashing', () => {
    const { container } = renderConnectPage();
    expect(container).not.toBeEmptyDOMElement();
  });

  it('renders the connection form with Display Name label', () => {
    renderConnectPage();
    expect(screen.getByText(/Display Name/)).toBeInTheDocument();
  });

  it('renders the connection string field', () => {
    renderConnectPage();
    const input = screen.getByPlaceholderText(/Endpoint=sb:/);
    expect(input).toBeInTheDocument();
  });

  it('renders the submit/connect button', () => {
    renderConnectPage();
    const submitBtn = screen.getByRole('button', { name: /connect|add/i });
    expect(submitBtn).toBeInTheDocument();
  });

  it('shows "Connect to Service Bus" heading', () => {
    renderConnectPage();
    expect(screen.getByText('Connect to Service Bus')).toBeInTheDocument();
  });

  it('updates display name input value on change', () => {
    renderConnectPage();
    const input = screen.getByPlaceholderText(/Production Service Bus/i) as HTMLInputElement;
    fireEvent.change(input, { target: { value: 'My Namespace' } });
    expect(input.value).toBe('My Namespace');
  });

  it('updates connection string input value on change', () => {
    renderConnectPage();
    const input = screen.getByPlaceholderText(/Endpoint=sb:/) as HTMLInputElement;
    fireEvent.change(input, { target: { value: 'Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=abc' } });
    expect(input.value).toContain('Endpoint=sb://');
  });

  it('renders Azure portal instructions for creating a policy', () => {
    renderConnectPage();
    expect(screen.getByText(/Azure Portal/)).toBeInTheDocument();
  });

  it('shows password toggle button', () => {
    renderConnectPage();
    const toggleButtons = screen.getAllByRole('button');
    expect(toggleButtons.length).toBeGreaterThanOrEqual(2);
  });

  it('toggles password field to visible on eye button click', () => {
    renderConnectPage();
    const connectionStringInput = screen.getByPlaceholderText(/Endpoint=sb:/) as HTMLInputElement;
    expect(connectionStringInput.type).toBe('password');

    const eyeButton = connectionStringInput.parentElement?.querySelector('button');
    if (eyeButton) {
      fireEvent.click(eyeButton);
      expect(connectionStringInput.type).toBe('text');
    }
  });

  it('shows saved connections section', () => {
    renderConnectPage();
    expect(screen.getByText('Saved Connections')).toBeInTheDocument();
  });
});
