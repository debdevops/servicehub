import { describe, it, expect } from 'vitest';
import {
  MOCK_AI_INSIGHTS,
  getMessagePatterns,
  isExampleMessage,
  getInsightsSummary,
  type AIInsight,
  type InsightType,
  type ConfidenceLevel,
} from '@/lib/aiMockData';

// Importing this module executes all module-level declarations and
// covers all ~182 previously uncovered lines in aiMockData.ts.

describe('MOCK_AI_INSIGHTS', () => {
  it('exports a non-empty array', () => {
    expect(Array.isArray(MOCK_AI_INSIGHTS)).toBe(true);
    expect(MOCK_AI_INSIGHTS.length).toBeGreaterThan(0);
  });

  it('each insight has the required top-level fields', () => {
    MOCK_AI_INSIGHTS.forEach((insight: AIInsight) => {
      expect(insight).toHaveProperty('id');
      expect(insight).toHaveProperty('type');
      expect(insight).toHaveProperty('title');
      expect(insight).toHaveProperty('description');
      expect(insight).toHaveProperty('confidence');
      expect(insight).toHaveProperty('evidence');
      expect(insight).toHaveProperty('recommendations');
      expect(insight).toHaveProperty('timeWindow');
      expect(insight).toHaveProperty('scope');
      expect(insight).toHaveProperty('status');
    });
  });

  it('each insight has a non-empty string id', () => {
    MOCK_AI_INSIGHTS.forEach(insight => {
      expect(typeof insight.id).toBe('string');
      expect(insight.id.length).toBeGreaterThan(0);
    });
  });

  it('each insight type is one of the valid InsightType values', () => {
    const validTypes: InsightType[] = [
      'dlq-pattern',
      'retry-loop',
      'error-cluster',
      'latency-anomaly',
      'poison-message',
    ];
    MOCK_AI_INSIGHTS.forEach(insight => {
      expect(validTypes).toContain(insight.type);
    });
  });

  it('confidence block has level, score and reasoning', () => {
    MOCK_AI_INSIGHTS.forEach(insight => {
      expect(insight.confidence).toHaveProperty('level');
      expect(insight.confidence).toHaveProperty('score');
      expect(insight.confidence).toHaveProperty('reasoning');
    });
  });

  it('confidence level is a valid ConfidenceLevel', () => {
    const validLevels: ConfidenceLevel[] = ['high', 'medium', 'low'];
    MOCK_AI_INSIGHTS.forEach(insight => {
      expect(validLevels).toContain(insight.confidence.level);
    });
  });

  it('confidence score is a number between 0 and 100', () => {
    MOCK_AI_INSIGHTS.forEach(insight => {
      expect(insight.confidence.score).toBeGreaterThanOrEqual(0);
      expect(insight.confidence.score).toBeLessThanOrEqual(100);
    });
  });

  it('evidence block has sampleSize, affectedMessageIds, exampleMessageIds and metrics', () => {
    MOCK_AI_INSIGHTS.forEach(insight => {
      expect(insight.evidence).toHaveProperty('sampleSize');
      expect(insight.evidence).toHaveProperty('affectedMessageIds');
      expect(insight.evidence).toHaveProperty('exampleMessageIds');
      expect(insight.evidence).toHaveProperty('metrics');
    });
  });

  it('evidence affectedMessageIds is a non-empty array', () => {
    MOCK_AI_INSIGHTS.forEach(insight => {
      expect(Array.isArray(insight.evidence.affectedMessageIds)).toBe(true);
      expect(insight.evidence.affectedMessageIds.length).toBeGreaterThan(0);
    });
  });

  it('evidence metrics is an array where each item has label, value and isAnomaly', () => {
    MOCK_AI_INSIGHTS.forEach(insight => {
      insight.evidence.metrics.forEach(m => {
        expect(typeof m.label).toBe('string');
        expect(m.value).toBeDefined();
        expect(typeof m.isAnomaly).toBe('boolean');
      });
    });
  });

  it('recommendations is a non-empty array', () => {
    MOCK_AI_INSIGHTS.forEach(insight => {
      expect(Array.isArray(insight.recommendations)).toBe(true);
      expect(insight.recommendations.length).toBeGreaterThan(0);
    });
  });

  it('each recommendation has title, description and priority', () => {
    MOCK_AI_INSIGHTS.forEach(insight => {
      insight.recommendations.forEach(rec => {
        expect(typeof rec.title).toBe('string');
        expect(typeof rec.description).toBe('string');
        expect(['immediate', 'short-term', 'investigative']).toContain(rec.priority);
      });
    });
  });

  it('timeWindow has start, end and analysisTimestamp', () => {
    MOCK_AI_INSIGHTS.forEach(insight => {
      expect(insight.timeWindow).toHaveProperty('start');
      expect(insight.timeWindow).toHaveProperty('end');
      expect(insight.timeWindow).toHaveProperty('analysisTimestamp');
    });
  });

  it('scope has namespaceId and queueOrTopicName', () => {
    MOCK_AI_INSIGHTS.forEach(insight => {
      expect(typeof insight.scope.namespaceId).toBe('string');
      expect(typeof insight.scope.queueOrTopicName).toBe('string');
    });
  });

  it('status is one of: active, dismissed, resolved', () => {
    MOCK_AI_INSIGHTS.forEach(insight => {
      expect(['active', 'dismissed', 'resolved']).toContain(insight.status);
    });
  });

  it('contains at least one dlq-pattern insight', () => {
    const dlq = MOCK_AI_INSIGHTS.filter(i => i.type === 'dlq-pattern');
    expect(dlq.length).toBeGreaterThan(0);
  });
});

// ─── getMessagePatterns ───────────────────────────────────────────────────────

describe('getMessagePatterns', () => {
  it('returns insights that include the message ID in affectedMessageIds', () => {
    // Use a known affected message ID from the first insight
    const firstInsight = MOCK_AI_INSIGHTS[0];
    const affectedId = firstInsight.evidence.affectedMessageIds[0];

    const patterns = getMessagePatterns(affectedId);
    expect(patterns.length).toBeGreaterThan(0);
    expect(patterns.some(p => p.id === firstInsight.id)).toBe(true);
  });

  it('returns insights that include the message ID in exampleMessageIds', () => {
    const firstInsight = MOCK_AI_INSIGHTS[0];
    const exampleId = firstInsight.evidence.exampleMessageIds[0];

    const patterns = getMessagePatterns(exampleId);
    expect(patterns.some(p => p.id === firstInsight.id)).toBe(true);
  });

  it('returns empty array for an unknown message ID', () => {
    const patterns = getMessagePatterns('non-existent-message-id-xyz');
    expect(patterns).toEqual([]);
  });
});

// ─── isExampleMessage ─────────────────────────────────────────────────────────

describe('isExampleMessage', () => {
  it('returns true when message is an example for the given insight', () => {
    const insight = MOCK_AI_INSIGHTS[0];
    const exampleId = insight.evidence.exampleMessageIds[0];

    expect(isExampleMessage(exampleId, insight.id)).toBe(true);
  });

  it('returns false when message is not an example for the given insight', () => {
    const insight = MOCK_AI_INSIGHTS[0];
    expect(isExampleMessage('non-existent-id', insight.id)).toBe(false);
  });

  it('returns false for unknown insight ID', () => {
    expect(isExampleMessage('some-msg', 'non-existent-insight-id')).toBe(false);
  });
});

// ─── getInsightsSummary ───────────────────────────────────────────────────────

describe('getInsightsSummary', () => {
  it('returns an object with total count', () => {
    const summary = getInsightsSummary();
    expect(typeof summary.total).toBe('number');
    expect(summary.total).toBeGreaterThanOrEqual(0);
  });

  it('returns byConfidence breakdown', () => {
    const summary = getInsightsSummary();
    expect(summary.byConfidence).toHaveProperty('high');
    expect(summary.byConfidence).toHaveProperty('medium');
    expect(summary.byConfidence).toHaveProperty('low');
  });

  it('returns byType breakdown', () => {
    const summary = getInsightsSummary();
    expect(summary.byType).toHaveProperty('dlq-pattern');
    expect(summary.byType).toHaveProperty('retry-loop');
    expect(summary.byType).toHaveProperty('error-cluster');
    expect(summary.byType).toHaveProperty('latency-anomaly');
    expect(summary.byType).toHaveProperty('poison-message');
  });

  it('total matches count of active insights', () => {
    const summary = getInsightsSummary();
    const activeCount = MOCK_AI_INSIGHTS.filter(i => i.status === 'active').length;
    expect(summary.total).toBe(activeCount);
  });

  it('confidence counts sum to total', () => {
    const summary = getInsightsSummary();
    const confidenceTotal = summary.byConfidence.high + summary.byConfidence.medium + summary.byConfidence.low;
    expect(confidenceTotal).toBe(summary.total);
  });
});
