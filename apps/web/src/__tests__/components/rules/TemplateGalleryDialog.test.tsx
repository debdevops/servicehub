import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('@/hooks/useRules', () => ({
  useRuleTemplates: vi.fn(),
}));

import { useRuleTemplates } from '@/hooks/useRules';
import { TemplateGalleryDialog } from '@/components/rules/TemplateGalleryDialog';
import type { RuleTemplateResponse } from '@/lib/api/rules';

const mockUseRuleTemplates = useRuleTemplates as ReturnType<typeof vi.fn>;

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
}

const mockTemplates: RuleTemplateResponse[] = [
  {
    id: 't1',
    name: 'Max Delivery Exceeded',
    description: 'Auto-replay messages that exceeded max delivery count',
    category: 'MaxDelivery',
    usageCount: 125,
    rating: 4.7,
    conditions: [],
    action: { autoReplay: true, delaySeconds: 0, maxRetries: 3, exponentialBackoff: false },
  },
  {
    id: 't2',
    name: 'Transient Failures',
    description: 'Replay transient network failures automatically',
    category: 'Transient',
    usageCount: 82,
    rating: 4.2,
    conditions: [],
    action: { autoReplay: true, delaySeconds: 5, maxRetries: 5, exponentialBackoff: true },
  },
];

describe('TemplateGalleryDialog', () => {
  beforeEach(() => vi.clearAllMocks());

  it('renders nothing when open=false', () => {
    mockUseRuleTemplates.mockReturnValue({ data: mockTemplates, isLoading: false });
    const { container } = render(
      <TemplateGalleryDialog open={false} onClose={vi.fn()} onSelect={vi.fn()} />,
      { wrapper: createWrapper() }
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('renders dialog when open=true', () => {
    mockUseRuleTemplates.mockReturnValue({ data: mockTemplates, isLoading: false });
    render(
      <TemplateGalleryDialog open={true} onClose={vi.fn()} onSelect={vi.fn()} />,
      { wrapper: createWrapper() }
    );
    expect(screen.getByText('Choose a Rule Template')).toBeInTheDocument();
  });

  it('shows loading state', () => {
    mockUseRuleTemplates.mockReturnValue({ data: undefined, isLoading: true });
    render(
      <TemplateGalleryDialog open={true} onClose={vi.fn()} onSelect={vi.fn()} />,
      { wrapper: createWrapper() }
    );
    expect(screen.getByText('Loading templates...')).toBeInTheDocument();
  });

  it('shows no templates message when empty', () => {
    mockUseRuleTemplates.mockReturnValue({ data: [], isLoading: false });
    render(
      <TemplateGalleryDialog open={true} onClose={vi.fn()} onSelect={vi.fn()} />,
      { wrapper: createWrapper() }
    );
    expect(screen.getByText('No templates available')).toBeInTheDocument();
  });

  it('renders template names', () => {
    mockUseRuleTemplates.mockReturnValue({ data: mockTemplates, isLoading: false });
    render(
      <TemplateGalleryDialog open={true} onClose={vi.fn()} onSelect={vi.fn()} />,
      { wrapper: createWrapper() }
    );
    expect(screen.getByText('Max Delivery Exceeded')).toBeInTheDocument();
    expect(screen.getByText('Transient Failures')).toBeInTheDocument();
  });

  it('renders template descriptions', () => {
    mockUseRuleTemplates.mockReturnValue({ data: mockTemplates, isLoading: false });
    render(
      <TemplateGalleryDialog open={true} onClose={vi.fn()} onSelect={vi.fn()} />,
      { wrapper: createWrapper() }
    );
    expect(screen.getByText(/Auto-replay messages that exceeded max delivery count/)).toBeInTheDocument();
  });

  it('renders template categories', () => {
    mockUseRuleTemplates.mockReturnValue({ data: mockTemplates, isLoading: false });
    render(
      <TemplateGalleryDialog open={true} onClose={vi.fn()} onSelect={vi.fn()} />,
      { wrapper: createWrapper() }
    );
    expect(screen.getByText('MaxDelivery')).toBeInTheDocument();
    expect(screen.getByText('Transient')).toBeInTheDocument();
  });

  it('shows usage count', () => {
    mockUseRuleTemplates.mockReturnValue({ data: mockTemplates, isLoading: false });
    render(
      <TemplateGalleryDialog open={true} onClose={vi.fn()} onSelect={vi.fn()} />,
      { wrapper: createWrapper() }
    );
    expect(screen.getByText('Used 125 times')).toBeInTheDocument();
    expect(screen.getByText('Used 82 times')).toBeInTheDocument();
  });

  it('shows rating', () => {
    mockUseRuleTemplates.mockReturnValue({ data: mockTemplates, isLoading: false });
    render(
      <TemplateGalleryDialog open={true} onClose={vi.fn()} onSelect={vi.fn()} />,
      { wrapper: createWrapper() }
    );
    expect(screen.getByText('4.7')).toBeInTheDocument();
  });

  it('calls onSelect with template when "Use This Template" is clicked', () => {
    const mockOnSelect = vi.fn();
    mockUseRuleTemplates.mockReturnValue({ data: mockTemplates, isLoading: false });
    render(
      <TemplateGalleryDialog open={true} onClose={vi.fn()} onSelect={mockOnSelect} />,
      { wrapper: createWrapper() }
    );
    const buttons = screen.getAllByText('Use This Template');
    fireEvent.click(buttons[0]);
    expect(mockOnSelect).toHaveBeenCalledWith(mockTemplates[0]);
  });

  it('calls onClose when X button is clicked', () => {
    const mockOnClose = vi.fn();
    mockUseRuleTemplates.mockReturnValue({ data: mockTemplates, isLoading: false });
    render(
      <TemplateGalleryDialog open={true} onClose={mockOnClose} onSelect={vi.fn()} />,
      { wrapper: createWrapper() }
    );
    const closeButton = screen.getByRole('button', { name: '' });
    fireEvent.click(closeButton);
    expect(mockOnClose).toHaveBeenCalled();
  });
});
