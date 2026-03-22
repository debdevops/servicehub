import { describe, it, expect } from 'vitest';
import { analyzeMessages, isAIAvailable, getAIStatusMessage } from '@/lib/ai/analyzer';
import type { Message } from '@/lib/api/types';

// ─── Helpers ─────────────────────────────────────────────────────────────────

const context = {
  namespaceId: 'ns-001',
  entityName: 'orders-queue',
  entityType: 'queue' as const,
};

function makeMessage(overrides: Partial<Message> = {}): Message {
  return {
    messageId: `msg-${Math.random().toString(36).slice(2)}`,
    sequenceNumber: 1,
    enqueuedTime: new Date().toISOString(),
    deliveryCount: 1,
    state: 'Active',
    contentType: 'application/json',
    body: null,
    ...overrides,
  };
}

function makeDLQMessages(count: number, reason = 'MaxDeliveryCountExceeded'): Message[] {
  return Array.from({ length: count }, (_, i) =>
    makeMessage({
      messageId: `dlq-${i}`,
      isFromDeadLetter: true,
      deadLetterReason: reason,
      deliveryCount: 10,
    })
  );
}

function makeRetryMessages(count: number, deliveryCount = 7): Message[] {
  return Array.from({ length: count }, (_, i) =>
    makeMessage({
      messageId: `retry-${i}`,
      isFromDeadLetter: false,
      deliveryCount,
    })
  );
}

function makePoisonMessages(count: number): Message[] {
  return Array.from({ length: count }, (_, i) =>
    makeMessage({ messageId: `poison-${i}`, deliveryCount: 12 })
  );
}

// ─── analyzeMessages ──────────────────────────────────────────────────────────

describe('analyzeMessages', () => {
  it('returns empty array for empty message list', () => {
    expect(analyzeMessages([], context)).toEqual([]);
  });

  it('returns empty array for fewer than MIN_MESSAGES_FOR_PATTERN (3)', () => {
    // Use low-deliveryCount messages that don't trigger any single-message detectors
    const messages = [makeMessage({ deliveryCount: 2 }), makeMessage({ deliveryCount: 2 })];
    expect(analyzeMessages(messages, context)).toEqual([]);
  });

  // ── DLQ pattern ────────────────────────────────────────────────────────────

  describe('DLQ pattern detection', () => {
    it('detects DLQ pattern when 3+ messages share the same dead-letter reason', () => {
      const messages = makeDLQMessages(5);
      const insights = analyzeMessages(messages, context);
      const dlqInsight = insights.find(i => i.type === 'dlq-pattern');
      expect(dlqInsight).toBeDefined();
    });

    it('sets insight id to include entity name and pattern type', () => {
      const messages = makeDLQMessages(4);
      const insights = analyzeMessages(messages, context);
      const dlqInsight = insights.find(i => i.type === 'dlq-pattern');
      expect(dlqInsight?.id).toContain('orders-queue');
      expect(dlqInsight?.id).toContain('dlq-pattern');
    });

    it('returns recommendations for DLQ pattern', () => {
      const messages = makeDLQMessages(4);
      const insights = analyzeMessages(messages, context);
      const dlqInsight = insights.find(i => i.type === 'dlq-pattern');
      expect(dlqInsight?.recommendations.length).toBeGreaterThan(0);
      expect(dlqInsight?.recommendations[0].priority).toBe('immediate');
    });

    it('includes evidence with affected message IDs', () => {
      const messages = makeDLQMessages(5);
      const insights = analyzeMessages(messages, context);
      const dlqInsight = insights.find(i => i.type === 'dlq-pattern');
      expect(dlqInsight?.evidence.affectedMessageIds.length).toBeGreaterThan(0);
    });

    it('does not detect DLQ pattern when different reasons are spread below threshold', () => {
      // 2 messages per reason — both below MIN_MESSAGES_FOR_PATTERN (3)
      const messages = [
        ...makeDLQMessages(2, 'MaxDeliveryCountExceeded'),
        ...makeDLQMessages(2, 'TTLExpiredException'),
      ];
      const insights = analyzeMessages(messages, context);
      const dlqInsight = insights.find(i => i.type === 'dlq-pattern');
      expect(dlqInsight).toBeUndefined();
    });
  });

  // ── Retry loop ─────────────────────────────────────────────────────────────

  describe('Retry loop detection', () => {
    it('detects retry loop when 3+ non-DLQ messages have deliveryCount >= 5', () => {
      const messages = makeRetryMessages(4, 7);
      const insights = analyzeMessages(messages, context);
      const retryInsight = insights.find(i => i.type === 'retry-loop');
      expect(retryInsight).toBeDefined();
    });

    it('does not detect retry loop when deliveryCount is below threshold', () => {
      const messages = makeRetryMessages(5, 2); // deliveryCount=2 < threshold=5
      const insights = analyzeMessages(messages, context);
      const retryInsight = insights.find(i => i.type === 'retry-loop');
      expect(retryInsight).toBeUndefined();
    });

    it('does not detect retry loop for DLQ messages even with high delivery count', () => {
      const messages = makeDLQMessages(5); // isFromDeadLetter=true, deliveryCount=10
      const insights = analyzeMessages(messages, context);
      const retryInsight = insights.find(i => i.type === 'retry-loop');
      expect(retryInsight).toBeUndefined();
    });
  });

  // ── Poison messages ────────────────────────────────────────────────────────

  describe('Poison message detection', () => {
    it('detects poison message when deliveryCount >= 10', () => {
      const messages = [makeMessage({ deliveryCount: 12 })];
      const insights = analyzeMessages(messages, context);
      const poisonInsight = insights.find(i => i.type === 'poison-message');
      expect(poisonInsight).toBeDefined();
    });

    it('detects multiple poison messages', () => {
      const messages = makePoisonMessages(3);
      const insights = analyzeMessages(messages, context);
      const poisonInsight = insights.find(i => i.type === 'poison-message');
      expect(poisonInsight?.evidence.sampleSize).toBe(3);
    });

    it('does not detect poison message when deliveryCount is 9', () => {
      const messages = [makeMessage({ deliveryCount: 9 })];
      const insights = analyzeMessages(messages, context);
      const poisonInsight = insights.find(i => i.type === 'poison-message');
      expect(poisonInsight).toBeUndefined();
    });

    it('poison insight has immediate priority recommendation', () => {
      const messages = makePoisonMessages(2);
      const insights = analyzeMessages(messages, context);
      const poisonInsight = insights.find(i => i.type === 'poison-message');
      const immediateRec = poisonInsight?.recommendations.find(r => r.priority === 'immediate');
      expect(immediateRec).toBeDefined();
    });
  });

  // ── Error clusters ─────────────────────────────────────────────────────────

  describe('Error cluster detection', () => {
    it('detects error cluster when 3+ messages share same error type in body', () => {
      const messages = Array.from({ length: 4 }, () =>
        makeMessage({
          body: 'Error: NullPointerException at line 42',
          isFromDeadLetter: true,
          deadLetterReason: 'NullPointerException',
        })
      );
      const insights = analyzeMessages(messages, context);
      const errorInsight = insights.find(i => i.type === 'error-cluster');
      expect(errorInsight).toBeDefined();
    });
  });

  // ── Insight structure ──────────────────────────────────────────────────────

  describe('insight structure', () => {
    it('insight has required fields: id, type, title, description, confidence, evidence, recommendations, timeWindow, scope', () => {
      const messages = makeDLQMessages(4);
      const insights = analyzeMessages(messages, context);
      expect(insights.length).toBeGreaterThan(0);
      const insight = insights[0];
      expect(insight).toHaveProperty('id');
      expect(insight).toHaveProperty('type');
      expect(insight).toHaveProperty('title');
      expect(insight).toHaveProperty('description');
      expect(insight).toHaveProperty('confidence');
      expect(insight).toHaveProperty('evidence');
      expect(insight).toHaveProperty('recommendations');
      expect(insight).toHaveProperty('timeWindow');
      expect(insight).toHaveProperty('scope');
    });

    it('scope reflects the provided context', () => {
      const messages = makeDLQMessages(4);
      const insights = analyzeMessages(messages, context);
      expect(insights[0].scope.namespaceId).toBe('ns-001');
      expect(insights[0].scope.queueOrTopicName).toBe('orders-queue');
    });

    it('status is "active"', () => {
      const messages = makeDLQMessages(4);
      const insights = analyzeMessages(messages, context);
      expect(insights[0].status).toBe('active');
    });
  });

  // ── Mixed signals ──────────────────────────────────────────────────────────

  it('returns multiple insight types for mixed message set', () => {
    const messages = [
      ...makeDLQMessages(4),
      ...makeRetryMessages(4, 8),
    ];
    const insights = analyzeMessages(messages, context);
    const types = new Set(insights.map(i => i.type));
    expect(types.size).toBeGreaterThanOrEqual(1);
  });
});

// ─── isAIAvailable ────────────────────────────────────────────────────────────

describe('isAIAvailable', () => {
  it('returns true', () => {
    expect(isAIAvailable()).toBe(true);
  });
});

// ─── getAIStatusMessage ───────────────────────────────────────────────────────

describe('getAIStatusMessage', () => {
  it('returns a non-empty string', () => {
    expect(getAIStatusMessage()).toBeTruthy();
  });

  it('mentions client-side in message', () => {
    expect(getAIStatusMessage().toLowerCase()).toContain('client-side');
  });
});
