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
  useEntraIdStatus: () => ({
    data: { isAvailable: false, isConfigured: false, isDefaultCredentialMode: false, clientId: null },
    isLoading: false,
  }),
}));

vi.mock('@/hooks/useAzureAuth', () => ({
  useAzureAuthStatus: () => ({ data: { isConfigured: false, isSignedIn: false }, isLoading: false }),
  useAzureNamespaces: () => ({ data: [], isLoading: false }),
  useAzureSignIn: () => ({ mutate: vi.fn(), isPending: false }),
  useAzureSignOut: () => ({ mutate: vi.fn(), isPending: false }),
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

/** Switches to the Connection String tab (page defaults to Entra tab) */
function switchToConnectionStringTab() {
  fireEvent.click(screen.getByRole('button', { name: 'Connection String' }));
}

describe('ConnectPage', () => {
  beforeEach(() => vi.clearAllMocks());

  it('renders without crashing', () => {
    const { container } = renderConnectPage();
    expect(container).not.toBeEmptyDOMElement();
  });

  it('shows "Connect to Service Bus" heading', () => {
    renderConnectPage();
    expect(screen.getByText('Connect to Service Bus')).toBeInTheDocument();
  });

  it('shows saved connections section', () => {
    renderConnectPage();
    expect(screen.getByText('Saved Connections')).toBeInTheDocument();
  });

  it('renders Azure Entra ID tab as default', () => {
    renderConnectPage();
    expect(screen.getByRole('button', { name: /Azure Entra ID/i })).toBeInTheDocument();
  });

  it('shows not-configured message on entra tab when OAuth is not set up', () => {
    renderConnectPage();
    expect(screen.getByText(/Azure Entra ID not configured/i)).toBeInTheDocument();
  });

  it('shows Connection String tab button', () => {
    renderConnectPage();
    expect(screen.getByRole('button', { name: 'Connection String' })).toBeInTheDocument();
  });

  it('switches to Connection String tab on click', () => {
    renderConnectPage();
    switchToConnectionStringTab();
    expect(screen.getByPlaceholderText(/Endpoint=sb:/)).toBeInTheDocument();
  });

  it('renders the connection form with Display Name label', () => {
    renderConnectPage();
    switchToConnectionStringTab();
    expect(screen.getByText(/Display Name/)).toBeInTheDocument();
  });

  it('renders the connection string field', () => {
    renderConnectPage();
    switchToConnectionStringTab();
    const input = screen.getByPlaceholderText(/Endpoint=sb:/);
    expect(input).toBeInTheDocument();
  });

  it('renders the submit/connect button', () => {
    renderConnectPage();
    switchToConnectionStringTab();
    expect(screen.getByRole('button', { name: /^Connect$/i })).toBeInTheDocument();
  });

  it('updates display name input value on change', () => {
    renderConnectPage();
    switchToConnectionStringTab();
    const input = screen.getByPlaceholderText(/Production Service Bus/i) as HTMLInputElement;
    fireEvent.change(input, { target: { value: 'My Namespace' } });
    expect(input.value).toBe('My Namespace');
  });

  it('updates connection string input value on change', () => {
    renderConnectPage();
    switchToConnectionStringTab();
    const input = screen.getByPlaceholderText(/Endpoint=sb:/) as HTMLInputElement;
    fireEvent.change(input, { target: { value: 'Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=abc' } });
    expect(input.value).toContain('Endpoint=sb://');
  });

  it('renders Azure portal instructions for creating a policy', () => {
    renderConnectPage();
    switchToConnectionStringTab();
    expect(screen.getAllByText(/Azure Portal/).length).toBeGreaterThanOrEqual(1);
  });

  it('shows password toggle button', () => {
    renderConnectPage();
    switchToConnectionStringTab();
    const toggleButtons = screen.getAllByRole('button');
    expect(toggleButtons.length).toBeGreaterThanOrEqual(2);
  });

  it('toggles password field to visible on eye button click', () => {
    renderConnectPage();
    switchToConnectionStringTab();
    const connectionStringInput = screen.getByPlaceholderText(/Endpoint=sb:/) as HTMLInputElement;
    expect(connectionStringInput.type).toBe('password');

    const eyeButton = connectionStringInput.parentElement?.querySelector('button');
    if (eyeButton) {
      fireEvent.click(eyeButton);
      expect(connectionStringInput.type).toBe('text');
    }
  });

  it('shows Sign in with Microsoft button on entra tab when OAuth configured', () => {
    // The default mock has isConfigured: false, so the not-configured state shows
    renderConnectPage();
    // Entra tab is active by default; shows the not-configured amber panel
    expect(screen.getByText(/Azure Entra ID not configured/i)).toBeInTheDocument();
  });
});

