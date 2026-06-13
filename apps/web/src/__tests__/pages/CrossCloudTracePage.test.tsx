import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { CrossCloudTracePage } from '@/pages/CrossCloudTracePage';

vi.mock('@/hooks/useCrossCloudTrace', () => ({
  useCrossCloudTrace: vi.fn(),
}));

vi.mock('@/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));

vi.mock('react-hot-toast', () => ({
  default: { error: vi.fn(), success: vi.fn() },
}));

import { useCrossCloudTrace } from '@/hooks/useCrossCloudTrace';
import { useNamespaces } from '@/hooks/useNamespaces';

const mockUseCrossCloudTrace = useCrossCloudTrace as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;

const mockMutate = vi.fn();

const defaultTraceState = {
  mutate: mockMutate,
  isPending: false,
  isSuccess: false,
  isError: false,
  data: undefined,
};

const azureNamespace = { id: 'ns-azure', name: 'azure-ns', displayName: 'Azure Prod', cloudProvider: 'azure' };
const awsNamespace = { id: 'ns-aws', name: 'aws-ns', displayName: 'AWS East', cloudProvider: 'aws' };

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={['/cross-cloud-trace']}>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </MemoryRouter>
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mockUseCrossCloudTrace.mockReturnValue(defaultTraceState);
  mockUseNamespaces.mockReturnValue({ data: [] });
});

describe('CrossCloudTracePage', () => {
  it('renders page header', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><CrossCloudTracePage /></Wrapper>);
    expect(screen.getByText('Multi-Cloud Trace')).toBeInTheDocument();
    expect(screen.getByText(/Trace a message as it routes across/)).toBeInTheDocument();
  });

  it('shows multi-cloud gate warning when fewer than 2 cloud providers are connected', () => {
    mockUseNamespaces.mockReturnValue({ data: [azureNamespace] });
    const Wrapper = createWrapper();
    render(<Wrapper><CrossCloudTracePage /></Wrapper>);
    expect(screen.getByText('Multi-cloud connection required')).toBeInTheDocument();
  });

  it('does not show gate warning when 2+ cloud providers are connected', () => {
    mockUseNamespaces.mockReturnValue({ data: [azureNamespace, awsNamespace] });
    const Wrapper = createWrapper();
    render(<Wrapper><CrossCloudTracePage /></Wrapper>);
    expect(screen.queryByText('Multi-cloud connection required')).not.toBeInTheDocument();
  });

  it('disables the Trace button when no traceId is entered', () => {
    mockUseNamespaces.mockReturnValue({ data: [azureNamespace, awsNamespace] });
    const Wrapper = createWrapper();
    render(<Wrapper><CrossCloudTracePage /></Wrapper>);
    const button = screen.getByRole('button', { name: /trace across clouds/i });
    expect(button).toBeDisabled();
  });

  it('enables the Trace button when a traceId is entered and multi-cloud is available', () => {
    mockUseNamespaces.mockReturnValue({ data: [azureNamespace, awsNamespace] });
    const Wrapper = createWrapper();
    render(<Wrapper><CrossCloudTracePage /></Wrapper>);
    const input = screen.getByLabelText('Trace ID');
    fireEvent.change(input, { target: { value: 'test-correlation-id-123' } });
    const button = screen.getByRole('button', { name: /trace across clouds/i });
    expect(button).not.toBeDisabled();
  });

  it('calls mutate with trimmed traceId when Trace button is clicked', () => {
    mockUseNamespaces.mockReturnValue({ data: [azureNamespace, awsNamespace] });
    const Wrapper = createWrapper();
    render(<Wrapper><CrossCloudTracePage /></Wrapper>);
    const input = screen.getByLabelText('Trace ID');
    fireEvent.change(input, { target: { value: '  my-trace-id  ' } });
    fireEvent.click(screen.getByRole('button', { name: /trace across clouds/i }));
    expect(mockMutate).toHaveBeenCalledWith('my-trace-id');
  });

  it('calls mutate on Enter key press', () => {
    mockUseNamespaces.mockReturnValue({ data: [azureNamespace, awsNamespace] });
    const Wrapper = createWrapper();
    render(<Wrapper><CrossCloudTracePage /></Wrapper>);
    const input = screen.getByLabelText('Trace ID');
    fireEvent.change(input, { target: { value: 'enter-trace-id' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(mockMutate).toHaveBeenCalledWith('enter-trace-id');
  });

  it('shows loading state while trace is pending', () => {
    mockUseNamespaces.mockReturnValue({ data: [azureNamespace, awsNamespace] });
    mockUseCrossCloudTrace.mockReturnValue({ ...defaultTraceState, isPending: true });
    const Wrapper = createWrapper();
    render(<Wrapper><CrossCloudTracePage /></Wrapper>);
    expect(screen.getByText('Tracing…')).toBeInTheDocument();
  });

  it('shows no-results empty state when trace succeeds with zero hops', () => {
    mockUseNamespaces.mockReturnValue({ data: [azureNamespace, awsNamespace] });
    const emptyResult = {
      traceId: 'test-id',
      hops: [],
      namespaceSummaries: [],
      totalHops: 0,
      cloudsInvolved: 0,
      cloudProviders: [],
      isMultiCloud: false,
      namespacesSearched: 2,
      entitiesSearched: 5,
      isPartialResult: false,
      searchDurationMs: 120,
    };
    mockUseCrossCloudTrace.mockReturnValue({
      ...defaultTraceState,
      isSuccess: true,
      data: emptyResult,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><CrossCloudTracePage /></Wrapper>);
    expect(screen.getByText(/No messages found with trace ID/)).toBeInTheDocument();
  });

  it('shows hop cards when trace returns hops', () => {
    mockUseNamespaces.mockReturnValue({ data: [azureNamespace, awsNamespace] });
    const hop = {
      cloudProvider: 'azure' as const,
      namespaceId: 'ns-azure',
      namespaceDisplayName: 'Azure Prod',
      entityName: 'orders',
      entityPath: 'orders',
      messageId: 'msg-123',
      sequenceNumber: 42,
      state: 'Active',
      timestamp: new Date().toISOString(),
      deadLetterReason: null,
      bodyPreview: '{"orderId": 1}',
      sizeInBytes: 256,
      source: 'queue',
      hopIndex: 0,
    };
    const resultWithHops = {
      traceId: 'test-id',
      hops: [hop],
      namespaceSummaries: [
        { namespaceId: 'ns-azure', namespaceDisplayName: 'Azure Prod', cloudProvider: 'azure' as const, wasSearched: true, skipReason: null, hopsFound: 1 },
      ],
      totalHops: 1,
      cloudsInvolved: 1,
      cloudProviders: ['azure'],
      isMultiCloud: false,
      namespacesSearched: 1,
      entitiesSearched: 3,
      isPartialResult: false,
      searchDurationMs: 88,
    };
    mockUseCrossCloudTrace.mockReturnValue({
      ...defaultTraceState,
      isSuccess: true,
      data: resultWithHops,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><CrossCloudTracePage /></Wrapper>);
    expect(screen.getByText(/Message Timeline/)).toBeInTheDocument();
    expect(screen.getAllByText('Azure Prod').length).toBeGreaterThan(0);
    expect(screen.getByText('Active')).toBeInTheDocument();
  });

  it('shows partial-result badge when isPartialResult is true', () => {
    mockUseNamespaces.mockReturnValue({ data: [azureNamespace, awsNamespace] });
    const partialResult = {
      traceId: 'test-id',
      hops: [],
      namespaceSummaries: [],
      totalHops: 0,
      cloudsInvolved: 0,
      cloudProviders: [],
      isMultiCloud: false,
      namespacesSearched: 1,
      entitiesSearched: 2,
      isPartialResult: true,
      searchDurationMs: 30000,
    };
    mockUseCrossCloudTrace.mockReturnValue({
      ...defaultTraceState,
      isSuccess: true,
      data: partialResult,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><CrossCloudTracePage /></Wrapper>);
    expect(screen.getByText(/Partial/)).toBeInTheDocument();
  });

  it('shows search coverage panel when namespaceSummaries are present', () => {
    mockUseNamespaces.mockReturnValue({ data: [azureNamespace, awsNamespace] });
    const resultWithSummaries = {
      traceId: 'test-id',
      hops: [],
      namespaceSummaries: [
        { namespaceId: 'ns-azure', namespaceDisplayName: 'Azure Prod', cloudProvider: 'azure' as const, wasSearched: true, skipReason: null, hopsFound: 0 },
        { namespaceId: 'ns-aws', namespaceDisplayName: 'AWS East', cloudProvider: 'aws' as const, wasSearched: false, skipReason: 'AWS Phase 2', hopsFound: 0 },
      ],
      totalHops: 0,
      cloudsInvolved: 0,
      cloudProviders: [],
      isMultiCloud: false,
      namespacesSearched: 1,
      entitiesSearched: 3,
      isPartialResult: false,
      searchDurationMs: 60,
    };
    mockUseCrossCloudTrace.mockReturnValue({
      ...defaultTraceState,
      isSuccess: true,
      data: resultWithSummaries,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><CrossCloudTracePage /></Wrapper>);
    expect(screen.getByText(/Search Coverage/i)).toBeInTheDocument();
    expect(screen.getAllByText('Azure Prod').length).toBeGreaterThan(0);
  });

  it('gate warning lists currently connected clouds', () => {
    mockUseNamespaces.mockReturnValue({ data: [azureNamespace] });
    const Wrapper = createWrapper();
    render(<Wrapper><CrossCloudTracePage /></Wrapper>);
    expect(screen.getByText(/Currently connected:/)).toBeInTheDocument();
    expect(screen.getAllByText(/Azure/).length).toBeGreaterThan(0);
  });

  it('gate warning shows "none" when no namespaces connected', () => {
    mockUseNamespaces.mockReturnValue({ data: [] });
    const Wrapper = createWrapper();
    render(<Wrapper><CrossCloudTracePage /></Wrapper>);
    expect(screen.getByText(/none/)).toBeInTheDocument();
  });

  it('input and button are disabled when gate is active', () => {
    mockUseNamespaces.mockReturnValue({ data: [azureNamespace] });
    const Wrapper = createWrapper();
    render(<Wrapper><CrossCloudTracePage /></Wrapper>);
    expect(screen.getByLabelText('Trace ID')).toBeDisabled();
    expect(screen.getByRole('button', { name: /trace across clouds/i })).toBeDisabled();
  });
});
