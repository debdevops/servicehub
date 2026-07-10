import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MessageFAB } from '@/components/fab/MessageFAB';

// ── Mocks ──────────────────────────────────────────────────────────────────────

const mockQueryClient = vi.hoisted(() => ({
  invalidateQueries: vi.fn().mockResolvedValue(undefined),
}));

vi.mock('@tanstack/react-query', () => ({
  useQueryClient: () => mockQueryClient,
}));

const mockToastSuccess = vi.fn();
const mockToastError = vi.fn();
vi.mock('react-hot-toast', () => ({
  default: Object.assign(vi.fn(), {
    success: (...args: any[]) => mockToastSuccess(...args),
    error: (...args: any[]) => mockToastError(...args),
  }),
}));

// Mock child modals — keep them lightweight, expose the entity defaults they receive
vi.mock('@/components/fab/SendMessageModal', () => ({
  SendMessageModal: ({ isOpen, defaultEntityType, defaultQueueName }: { isOpen: boolean; defaultEntityType?: string; defaultQueueName?: string | null }) =>
    isOpen ? (
      <div data-testid="send-modal">
        SendMessageModal
        <span data-testid="send-modal-entity-type">{defaultEntityType ?? 'unset'}</span>
        <span data-testid="send-modal-entity-name">{defaultQueueName ?? 'unset'}</span>
      </div>
    ) : null,
}));

vi.mock('@/components/fab/MessageGeneratorModal', () => ({
  MessageGeneratorModal: ({ isOpen, defaultTargetType, defaultEntityName }: { isOpen: boolean; defaultTargetType?: string; defaultEntityName?: string | null }) =>
    isOpen ? (
      <div data-testid="generator-modal">
        MessageGeneratorModal
        <span data-testid="generator-modal-target-type">{defaultTargetType ?? 'unset'}</span>
        <span data-testid="generator-modal-entity-name">{defaultEntityName ?? 'unset'}</span>
      </div>
    ) : null,
}));

vi.mock('@/lib/api/messages', () => ({
  messagesApi: {
    deadLetter: vi.fn().mockResolvedValue({ deadLetteredCount: 3 }),
  },
}));

// ── Helpers ────────────────────────────────────────────────────────────────────

function renderFAB(overrides: Partial<React.ComponentProps<typeof MessageFAB>> = {}) {
  const props = {
    namespaceId: 'ns-1',
    queueName: 'orders',
    entityType: 'queue' as const,
    ...overrides,
  };
  return render(<MessageFAB {...props} />);
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe('MessageFAB', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── Rendering ─────────────────────────────────────────────────────────────────

  it('renders the main FAB button', () => {
    renderFAB();
    expect(screen.getByTitle('Open message menu')).toBeInTheDocument();
  });

  it('menu container is hidden (pointer-events-none) initially', () => {
    renderFAB();
    // The menu items are always in DOM for animation; visibility is via CSS class
    const menuContainer = document.querySelector('.pointer-events-none');
    expect(menuContainer).toBeInTheDocument();
  });

  // ── Menu open/close ───────────────────────────────────────────────────────────

  it('opens the context menu when FAB is clicked', async () => {
    renderFAB();
    await userEvent.click(screen.getByTitle('Open message menu'));
    // Menu items are in DOM; the container loses pointer-events-none when open
    expect(document.querySelector('.pointer-events-none')).not.toBeInTheDocument();
    expect(screen.getByText('Send Message')).toBeInTheDocument();
    expect(screen.getByText('Generate Messages')).toBeInTheDocument();
  });

  it('closes the menu when FAB is clicked again', async () => {
    renderFAB();
    await userEvent.click(screen.getByTitle('Open message menu'));
    expect(document.querySelector('.pointer-events-none')).not.toBeInTheDocument();
    await userEvent.click(screen.getByTitle('Close menu'));
    // Menu container should have pointer-events-none class again
    expect(document.querySelector('.pointer-events-none')).toBeInTheDocument();
  });

  it('closes menu when clicking outside', async () => {
    renderFAB();
    await userEvent.click(screen.getByTitle('Open message menu'));
    expect(document.querySelector('.pointer-events-none')).not.toBeInTheDocument();
    // Click outside the menu ref
    await userEvent.click(document.body);
    expect(document.querySelector('.pointer-events-none')).toBeInTheDocument();
  });

  // ── Send modal ────────────────────────────────────────────────────────────────

  it('opens SendMessageModal when Send Message is clicked', async () => {
    renderFAB();
    await userEvent.click(screen.getByTitle('Open message menu'));
    await userEvent.click(screen.getByText('Send Message'));
    expect(screen.getByTestId('send-modal')).toBeInTheDocument();
  });

  it('SendMessageModal is not visible initially', () => {
    renderFAB();
    expect(screen.queryByTestId('send-modal')).not.toBeInTheDocument();
  });

  // ── Generate modal ────────────────────────────────────────────────────────────

  it('opens MessageGeneratorModal when Generate Messages is clicked', async () => {
    renderFAB();
    await userEvent.click(screen.getByTitle('Open message menu'));
    await userEvent.click(screen.getByText('Generate Messages'));
    expect(screen.getByTestId('generator-modal')).toBeInTheDocument();
  });

  // ── Refresh ───────────────────────────────────────────────────────────────────

  it('shows Refresh All option in menu', async () => {
    renderFAB();
    await userEvent.click(screen.getByTitle('Open message menu'));
    expect(screen.getByText('Refresh All')).toBeInTheDocument();
  });

  it('calls invalidateQueries and shows toast when Refresh All is clicked', async () => {
    renderFAB();
    await userEvent.click(screen.getByTitle('Open message menu'));
    await userEvent.click(screen.getByText('Refresh All'));
    expect(mockQueryClient.invalidateQueries).toHaveBeenCalled();
    expect(mockToastSuccess).toHaveBeenCalledWith('Data refreshed');
  });

  // ── Dead-letter ───────────────────────────────────────────────────────────────

  it('shows Test DLQ option in menu', async () => {
    renderFAB();
    await userEvent.click(screen.getByTitle('Open message menu'));
    expect(screen.getByText('Test DLQ')).toBeInTheDocument();
  });

  it('DLQ button is disabled when no namespaceId provided', () => {
    renderFAB({ namespaceId: null });
    // The button is always in DOM (CSS-controlled visibility), just disabled
    const dlqButtons = document.querySelectorAll('button[disabled]');
    // At least one button should be disabled
    expect(dlqButtons.length).toBeGreaterThan(0);
  });

  it('DLQ button is disabled when topic mode has no subscription', () => {
    renderFAB({ namespaceId: 'ns-1', queueName: null, entityType: 'topic', topicName: 'orders-topic', subscriptionName: null });
    const dlqButtons = document.querySelectorAll('button[disabled]');
    expect(dlqButtons.length).toBeGreaterThan(0);
  });

  // ── Entity-type forwarding to modals ─────────────────────────────────────────
  // Regression: modals used to default to 'queue' even on topic pages, so Send/
  // Generate posted to a queue named after the topic (404 for every message).

  it('forwards queue entity type and queue name to SendMessageModal', async () => {
    renderFAB({ entityType: 'queue', queueName: 'orders' });
    await userEvent.click(screen.getByTitle('Open message menu'));
    await userEvent.click(screen.getByText('Send Message'));
    expect(screen.getByTestId('send-modal-entity-type')).toHaveTextContent('queue');
    expect(screen.getByTestId('send-modal-entity-name')).toHaveTextContent('orders');
  });

  it('forwards topic entity type and topic name to SendMessageModal on topic pages', async () => {
    renderFAB({ entityType: 'topic', queueName: null, topicName: 'orders-topic', subscriptionName: 'sub-1' });
    await userEvent.click(screen.getByTitle('Open message menu'));
    await userEvent.click(screen.getByText('Send Message'));
    expect(screen.getByTestId('send-modal-entity-type')).toHaveTextContent('topic');
    expect(screen.getByTestId('send-modal-entity-name')).toHaveTextContent('orders-topic');
  });

  it('forwards topic target type and topic name to MessageGeneratorModal on topic pages', async () => {
    renderFAB({ entityType: 'topic', queueName: null, topicName: 'orders-topic', subscriptionName: 'sub-1' });
    await userEvent.click(screen.getByTitle('Open message menu'));
    await userEvent.click(screen.getByText('Generate Messages'));
    expect(screen.getByTestId('generator-modal-target-type')).toHaveTextContent('topic');
    expect(screen.getByTestId('generator-modal-entity-name')).toHaveTextContent('orders-topic');
  });
});
