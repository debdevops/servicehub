import { vi, describe, it, expect, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ConnectPage } from '@/pages/ConnectPage';
import toast from 'react-hot-toast';

const { mockCreateNs, mockProviderStatus } = vi.hoisted(() => ({
  mockCreateNs: vi.fn(),
  mockProviderStatus: vi.fn(),
}));

// Mock hooks used by ConnectPage
vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: () => ({ data: [], isLoading: false }),
  useNamespace: () => ({ data: undefined, isLoading: false }),
  useCreateNamespace: () => ({
    mutateAsync: mockCreateNs,
    isPending: false,
  }),
  useDeleteNamespace: () => ({
    mutateAsync: vi.fn().mockResolvedValue(undefined),
    isPending: false,
  }),
}));

vi.mock('@servicehub/ui-shared/hooks/useCloudBridge', () => ({
  useProviderStatus: () => mockProviderStatus(),
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
  beforeEach(() => {
    vi.clearAllMocks();
    mockCreateNs.mockResolvedValue({ id: 'ns-new', name: 'test-ns', displayName: 'Test NS' });
    mockProviderStatus.mockReturnValue({ data: { Aws: true, Gcp: true } });
  });

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
    // Multi-cloud rewrite: heading is now "Connect to Cloud Messaging"
    expect(screen.getByText(/connect to cloud messaging/i)).toBeInTheDocument();
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
    expect(screen.getAllByText(/Azure Portal/).length).toBeGreaterThanOrEqual(1);
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

describe('ConnectPage multi-cloud provider gating', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockCreateNs.mockResolvedValue({ id: 'ns-new', name: 'test-ns', displayName: 'Test NS' });
    mockProviderStatus.mockReturnValue({ data: { Aws: true, Gcp: true } });
  });

  const selectProvider = (title: 'Amazon Web Services SQS' | 'Google Cloud Pub/Sub') => {
    fireEvent.click(screen.getByTitle(title));
  };

  // The display-name placeholder is provider-specific; match on the shared prefix.
  const fillDisplayName = () => {
    fireEvent.change(screen.getByPlaceholderText(/e\.g\., Production/i), {
      target: { value: 'My Connection' },
    });
  };

  it('disables submit and explains when AWS is disabled on the server', () => {
    mockProviderStatus.mockReturnValue({ data: { Aws: false, Gcp: false } });
    renderConnectPage();
    selectProvider('Amazon Web Services SQS');

    expect(screen.getByText(/AWS SQS\/SNS is disabled on this server/i)).toBeInTheDocument();
    expect(screen.getByText(/CloudProviders:Aws:Enabled/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Connect$/ })).toBeDisabled();
  });

  it('disables submit and explains when GCP is disabled on the server', () => {
    mockProviderStatus.mockReturnValue({ data: { Aws: false, Gcp: false } });
    renderConnectPage();
    selectProvider('Google Cloud Pub/Sub');

    expect(screen.getByText(/GCP Pub\/Sub is disabled on this server/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Connect$/ })).toBeDisabled();
  });

  it('treats providers as disabled while status is still loading', () => {
    mockProviderStatus.mockReturnValue({ data: undefined });
    renderConnectPage();
    selectProvider('Amazon Web Services SQS');

    expect(screen.getByRole('button', { name: /^Connect$/ })).toBeDisabled();
  });

  it('shows preview copy and keeps submit enabled when AWS is enabled', () => {
    renderConnectPage();
    selectProvider('Amazon Web Services SQS');

    expect(screen.getByText(/AWS SQS\/SNS — preview/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Connect$/ })).toBeEnabled();
  });

  it('submits AWS namespaces with authType awsAccessKey and AKID:Secret credentials', async () => {
    renderConnectPage();
    selectProvider('Amazon Web Services SQS');
    fillDisplayName();
    fireEvent.change(screen.getByPlaceholderText('AKIAIOSFODNN7EXAMPLE'), {
      target: { value: 'AKIAIOSFODNN7EXAMPLE' },
    });
    fireEvent.change(screen.getByPlaceholderText(/wJalrXUtnFEMI/), {
      target: { value: 'wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY' },
    });

    fireEvent.click(screen.getByRole('button', { name: /^Connect$/ }));

    await waitFor(() => expect(mockCreateNs).toHaveBeenCalledTimes(1));
    expect(mockCreateNs).toHaveBeenCalledWith(
      expect.objectContaining({
        authType: 'awsAccessKey',
        cloudProvider: 'aws',
        connectionString: 'AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY',
        awsRegion: 'us-east-1',
      })
    );
  });

  it('submits GCP namespaces with authType gcpServiceAccount and the uploaded key file content', async () => {
    renderConnectPage();
    selectProvider('Google Cloud Pub/Sub');
    fillDisplayName();
    const saJson = '{"type":"service_account","project_id":"my-project-123"}';
    const file = new File([saJson], 'my-key.json', { type: 'application/json' });
    fireEvent.change(screen.getByLabelText(/service account json key file/i), {
      target: { files: [file] },
    });
    // FileReader is async — wait for the selected-file chip to render
    await screen.findByText('my-key.json');
    // project_id is auto-filled from the key file
    expect(screen.getByPlaceholderText('my-project-123')).toHaveValue('my-project-123');

    fireEvent.click(screen.getByRole('button', { name: /^Connect$/ }));

    await waitFor(() => expect(mockCreateNs).toHaveBeenCalledTimes(1));
    expect(mockCreateNs).toHaveBeenCalledWith(
      expect.objectContaining({
        authType: 'gcpServiceAccount',
        cloudProvider: 'gcp',
        connectionString: saJson,
        gcpProjectId: 'my-project-123',
      })
    );
  });

  it('rejects an uploaded file that is not a service account key', async () => {
    renderConnectPage();
    selectProvider('Google Cloud Pub/Sub');
    fillDisplayName();
    const file = new File(['{"type":"authorized_user"}'], 'not-a-key.json', {
      type: 'application/json',
    });
    fireEvent.change(screen.getByLabelText(/service account json key file/i), {
      target: { files: [file] },
    });

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('service account key'))
    );
    expect(screen.queryByText('not-a-key.json')).not.toBeInTheDocument();
  });

  it('does not call create when the selected provider is disabled', async () => {
    mockProviderStatus.mockReturnValue({ data: { Aws: false, Gcp: false } });
    renderConnectPage();
    selectProvider('Amazon Web Services SQS');
    fillDisplayName();

    // Button is disabled; submit the form directly to exercise the handler guard too.
    const form = screen.getByRole('button', { name: /^Connect$/ }).closest('form')!;
    fireEvent.submit(form);

    await waitFor(() => expect(mockCreateNs).not.toHaveBeenCalled());
  });
});
