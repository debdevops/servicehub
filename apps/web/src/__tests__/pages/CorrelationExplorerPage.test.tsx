import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { CorrelationExplorerPage } from '@/pages/CorrelationExplorerPage';

vi.mock('@/hooks/useCorrelation', () => ({
  useCorrelationSearch: vi.fn(),
}));

vi.mock('@/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));

vi.mock('react-hot-toast', () => ({
  default: { error: vi.fn(), success: vi.fn() },
}));

import { useCorrelationSearch } from '@/hooks/useCorrelation';
import { useNamespaces } from '@/hooks/useNamespaces';

const mockUseCorrelationSearch = useCorrelationSearch as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;

const mockMutate = vi.fn();

const defaultSearchState = {
  mutate: mockMutate,
  isPending: false,
  isSuccess: false,
  isError: false,
  data: undefined,
};

const mockNamespaces = [
  { id: 'ns-1', name: 'prod-namespace', displayName: 'Production' },
  { id: 'ns-2', name: 'dev-namespace', displayName: 'Development' },
];

function createWrapper(initialPath = '/correlation') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={[initialPath]}>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </MemoryRouter>
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mockUseCorrelationSearch.mockReturnValue(defaultSearchState);
  mockUseNamespaces.mockReturnValue({ data: mockNamespaces });
});

describe('CorrelationExplorerPage', () => {
  it('renders page header', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><CorrelationExplorerPage /></Wrapper>);
    expect(screen.getByText('Correlation Explorer')).toBeInTheDocument();
  });

  it('shows placeholder state when no search has been performed', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><CorrelationExplorerPage /></Wrapper>);
    expect(screen.getByText('Enter a Correlation ID')).toBeInTheDocument();
  });

  it('shows loading spinner while search is pending', () => {
    mockUseCorrelationSearch.mockReturnValue({ ...defaultSearchState, isPending: true });

    const Wrapper = createWrapper();
    render(<Wrapper><CorrelationExplorerPage /></Wrapper>);

    expect(screen.getByText(/Searching across/)).toBeInTheDocument();
  });

  it('shows empty state when search returns 0 results', () => {
    mockUseCorrelationSearch.mockReturnValue({
      ...defaultSearchState,
      isSuccess: true,
      data: {
        correlationId: 'corr-abc',
        entries: [],
        totalCount: 0,
        namespacesSearched: 2,
        entitiesSearched: 5,
        isPartialResult: false,
        searchDurationMs: 300,
      },
    });

    const Wrapper = createWrapper();
    render(<Wrapper><CorrelationExplorerPage /></Wrapper>);

    expect(screen.getByText('No messages found')).toBeInTheDocument();
  });

  it('shows timeline entries when search returns results', () => {
    mockUseCorrelationSearch.mockReturnValue({
      ...defaultSearchState,
      isSuccess: true,
      data: {
        correlationId: 'corr-xyz',
        entries: [
          {
            source: 'Live',
            namespaceId: 'ns-1',
            namespaceDisplayName: 'Production',
            entityName: 'orders-queue',
            entityPath: 'orders-queue',
            messageId: 'msg-1',
            sequenceNumber: 100,
            state: 'Active',
            timestamp: '2024-01-15T10:30:00Z',
            deadLetterReason: null,
            bodyPreview: '{"orderId": 123}',
            sizeInBytes: 512,
          },
        ],
        totalCount: 1,
        namespacesSearched: 2,
        entitiesSearched: 5,
        isPartialResult: false,
        searchDurationMs: 250,
      },
    });

    const Wrapper = createWrapper();
    render(<Wrapper><CorrelationExplorerPage /></Wrapper>);

    expect(screen.getByText('orders-queue')).toBeInTheDocument();
    expect(screen.getByText('Active')).toBeInTheDocument();
    expect(screen.getByText('Live')).toBeInTheDocument();
  });

  it('shows partial result banner when isPartialResult is true', () => {
    mockUseCorrelationSearch.mockReturnValue({
      ...defaultSearchState,
      isSuccess: true,
      data: {
        correlationId: 'corr-xyz',
        entries: [
          {
            source: 'Live' as const,
            namespaceId: 'ns-1',
            namespaceDisplayName: 'Production',
            entityName: 'orders-queue',
            entityPath: 'orders-queue',
            messageId: 'msg-partial',
            sequenceNumber: 1,
            state: 'Active',
            timestamp: '2024-01-15T10:30:00Z',
            deadLetterReason: null,
            bodyPreview: null,
            sizeInBytes: 0,
          },
        ],
        totalCount: 1,
        namespacesSearched: 3,
        entitiesSearched: 8,
        isPartialResult: true,
        searchDurationMs: 30100,
      },
    });

    const Wrapper = createWrapper();
    render(<Wrapper><CorrelationExplorerPage /></Wrapper>);

    expect(screen.getByText(/Search timed out/)).toBeInTheDocument();
  });

  it('pre-fills correlationId from URL search param and auto-searches', () => {
    const Wrapper = createWrapper('/correlation?correlationId=url-corr-id');
    render(<Wrapper><CorrelationExplorerPage /></Wrapper>);

    const input = screen.getByPlaceholderText('Enter Correlation ID…') as HTMLInputElement;
    expect(input.value).toBe('url-corr-id');
    // auto-search fires on mount
    expect(mockMutate).toHaveBeenCalledWith({
      correlationId: 'url-corr-id',
      namespaceId: undefined,
    });
  });

  it('disables Search button when correlationId input is empty', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><CorrelationExplorerPage /></Wrapper>);

    const button = screen.getByRole('button', { name: /Search/i });
    expect(button).toBeDisabled();
  });

  it('enables Search button when correlationId input has value', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><CorrelationExplorerPage /></Wrapper>);

    const input = screen.getByPlaceholderText('Enter Correlation ID…');
    fireEvent.change(input, { target: { value: 'my-corr-id' } });

    const button = screen.getByRole('button', { name: /Search/i });
    expect(button).not.toBeDisabled();
  });

  it('calls mutate with correlationId on Search button click', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><CorrelationExplorerPage /></Wrapper>);

    const input = screen.getByPlaceholderText('Enter Correlation ID…');
    fireEvent.change(input, { target: { value: 'click-corr' } });
    fireEvent.click(screen.getByRole('button', { name: /Search/i }));

    expect(mockMutate).toHaveBeenCalledWith({
      correlationId: 'click-corr',
      namespaceId: undefined,
    });
  });
});
