import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AIInsightsTab } from '@/components/messages/tabs/AIInsightsTab';

vi.mock('@/hooks/useMessages', () => ({
  useMessages: vi.fn(),
}));
vi.mock('@/hooks/useInsights', () => ({
  useClientSideInsights: vi.fn(),
}));

import { useMessages } from '@/hooks/useMessages';
import { useClientSideInsights } from '@/hooks/useInsights';

const mockUseMessages = useMessages as ReturnType<typeof vi.fn>;
const mockUseClientSideInsights = useClientSideInsights as ReturnType<typeof vi.fn>;

const mockMessage = {
  id: 'msg-1',
  enqueuedTime: new Date(),
  status: 'error' as const,
  preview: 'Test message body',
  contentType: 'application/json' as const,
  deliveryCount: 3,
  hasAIInsight: true,
  sequenceNumber: 1,
  properties: {},
  queueType: 'active' as const,
  body: '{"eventType":"OrderFailed"}',
  headers: {},
  timeToLive: '',
  lockToken: '',
};

const mockInsights = [
  {
    id: 'i1',
    title: 'MaxDelivery Exceeded Pattern',
    description: 'Multiple messages failing with max delivery count',
    confidence: { score: 85, level: 'high' },
    recommendations: [
      { priority: 'immediate', title: 'Check consumer processing logic' },
    ],
    evidence: {
      affectedMessageIds: ['msg-1', 'msg-2'],
      exampleMessageIds: ['msg-1'],
    },
  },
];

function createWrapper(path = '/messages?namespace=ns1&queue=test-queue') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={[path]}>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </MemoryRouter>
  );
}

beforeEach(() => {
  mockUseMessages.mockReturnValue({
    data: { items: [{ messageId: 'msg-1', sequenceNumber: 1, enqueuedTime: new Date().toISOString(), body: '{}', deliveryCount: 1 }] },
    isLoading: false,
  });
  mockUseClientSideInsights.mockReturnValue({ data: mockInsights, isLoading: false, isError: false });
});

describe('AIInsightsTab', () => {
  it('renders trust disclaimer banner when message is part of a pattern', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AIInsightsTab message={mockMessage} /></Wrapper>);
    expect(screen.getByText(/ServiceHub Interpretation/)).toBeInTheDocument();
  });

  it('shows pattern count for the message', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AIInsightsTab message={mockMessage} /></Wrapper>);
    expect(screen.getByText(/This message is part of/)).toBeInTheDocument();
    expect(screen.getByText('1')).toBeInTheDocument();
  });

  it('renders pattern card title', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AIInsightsTab message={mockMessage} /></Wrapper>);
    expect(screen.getByText('MaxDelivery Exceeded Pattern')).toBeInTheDocument();
  });

  it('renders pattern description', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AIInsightsTab message={mockMessage} /></Wrapper>);
    expect(screen.getByText(/Multiple messages failing with max delivery count/)).toBeInTheDocument();
  });

  it('renders confidence score', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AIInsightsTab message={mockMessage} /></Wrapper>);
    expect(screen.getByText(/85%/)).toBeInTheDocument();
  });

  it('renders recommendation in pattern card', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AIInsightsTab message={mockMessage} /></Wrapper>);
    expect(screen.getByText('Check consumer processing logic')).toBeInTheDocument();
  });

  it('renders affected message count', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AIInsightsTab message={mockMessage} /></Wrapper>);
    expect(screen.getByText(/2 affected messages/)).toBeInTheDocument();
  });

  it('calls onViewPattern when view affected messages button is clicked', () => {
    const mockViewPattern = vi.fn();
    const Wrapper = createWrapper();
    render(<Wrapper><AIInsightsTab message={mockMessage} onViewPattern={mockViewPattern} /></Wrapper>);
    fireEvent.click(screen.getByText(/View all 2 affected messages/));
    expect(mockViewPattern).toHaveBeenCalledWith(['msg-1', 'msg-2']);
  });

  it('shows loading state when insights are loading', () => {
    mockUseClientSideInsights.mockReturnValue({ data: undefined, isLoading: true, isError: false });
    const Wrapper = createWrapper();
    render(<Wrapper><AIInsightsTab message={mockMessage} /></Wrapper>);
    expect(screen.getByText(/Loading AI insights/)).toBeInTheDocument();
  });

  it('shows error state when insights fail', () => {
    mockUseClientSideInsights.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    const Wrapper = createWrapper();
    render(<Wrapper><AIInsightsTab message={mockMessage} /></Wrapper>);
    expect(screen.getByText('AI Insights Not Available')).toBeInTheDocument();
  });

  it('shows no patterns state when message not in any pattern', () => {
    mockUseClientSideInsights.mockReturnValue({
      data: [{
        id: 'i2',
        title: 'Other Pattern',
        description: 'Unrelated',
        confidence: { score: 70, level: 'medium' },
        recommendations: [],
        evidence: {
          affectedMessageIds: ['other-msg'],
          exampleMessageIds: [],
        },
      }],
      isLoading: false,
      isError: false,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><AIInsightsTab message={mockMessage} /></Wrapper>);
    expect(screen.getByText('No Patterns Detected')).toBeInTheDocument();
  });

  it('shows isExample badge when message is in exampleMessageIds', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AIInsightsTab message={mockMessage} /></Wrapper>);
    expect(screen.getByText(/📌 Example/)).toBeInTheDocument();
  });

  it('works with topic subscription path', () => {
    const Wrapper = createWrapper('/messages?namespace=ns1&topic=orders&subscription=sub1');
    render(<Wrapper><AIInsightsTab message={{ ...mockMessage, queueType: 'active' }} /></Wrapper>);
    expect(screen.getByText('MaxDelivery Exceeded Pattern')).toBeInTheDocument();
  });
});
