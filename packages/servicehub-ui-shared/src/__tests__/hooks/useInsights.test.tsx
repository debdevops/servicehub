import { describe, it, expect, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('react-hot-toast', () => ({
  default: { error: vi.fn(), success: vi.fn() },
}));

const mockIsDemoMode = vi.fn(() => false);
vi.mock('../../lib/demo/DemoContext', async () => {
  const actual = await vi.importActual<typeof import('../../lib/demo/DemoContext')>('../../lib/demo/DemoContext');
  return {
    ...actual,
    useDemoContext: () => ({ isDemoMode: mockIsDemoMode() }),
  };
});

import { useClientSideInsights } from '../../hooks/useInsights';
import type { Message } from '../../lib/api/types';

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

function dlqBatch(prefix: string, reason: string, count: number): Message[] {
  return Array.from({ length: count }, (_, i) => ({
    messageId: `${prefix}-${i}`,
    sequenceNumber: i,
    enqueuedTime: new Date().toISOString(),
    deliveryCount: 5,
    state: 'Active' as const,
    contentType: 'application/json',
    body: '{}',
    deadLetterReason: reason,
    isFromDeadLetter: true,
  }));
}

describe('useClientSideInsights', () => {
  const context = {
    namespaceId: 'ns-1',
    entityName: 'orders',
    entityType: 'queue' as const,
  };

  it('does not leak affectedMessageIds across message sets of the same length (tab switch)', async () => {
    const wrapper = createWrapper();

    // Dead-letter tab: 5 messages, 3 sharing a reason -> produces a dlq-pattern insight.
    const deadLetterMessages = dlqBatch('dlq', 'MaxDeliveryCountExceeded', 5);
    const { result, rerender } = renderHook(
      ({ messages }) => useClientSideInsights(messages, context),
      { wrapper, initialProps: { messages: deadLetterMessages } }
    );

    await waitFor(() => expect(result.current.data).toBeDefined());
    const dlqInsight = result.current.data!.find(i => i.type === 'dlq-pattern');
    expect(dlqInsight).toBeDefined();
    expect(dlqInsight!.evidence.affectedMessageIds).toEqual(
      expect.arrayContaining(deadLetterMessages.slice(0, 3).map(m => m.messageId))
    );

    // Active tab: same length (5), but a completely different, non-DLQ message set —
    // must not resolve to the stale dead-letter insight computed above.
    const activeMessages: Message[] = Array.from({ length: 5 }, (_, i) => ({
      messageId: `active-${i}`,
      sequenceNumber: i,
      enqueuedTime: new Date().toISOString(),
      deliveryCount: 0,
      state: 'Active' as const,
      contentType: 'application/json',
      body: '{}',
      isFromDeadLetter: false,
    }));

    rerender({ messages: activeMessages });

    await waitFor(() => expect(result.current.isFetching).toBe(false));

    const staleAffectedIds = (result.current.data || []).flatMap(i => i.evidence.affectedMessageIds);
    expect(staleAffectedIds.some(id => id.startsWith('dlq-'))).toBe(false);
  });
});
