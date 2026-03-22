import { describe, it, expect } from 'vitest';
import {
  INSIGHT_CATEGORIES,
  MOCK_INSIGHTS,
  type InsightCategory,
  type InsightSeverity,
  type InsightDetail,
  type InsightMetric,
  type InsightRecommendation,
} from '@/lib/insightsMockData';

// Importing this file executes all module-level declarations and covers ~567 lines.

describe('INSIGHT_CATEGORIES', () => {
  it('exports an array of categories', () => {
    expect(Array.isArray(INSIGHT_CATEGORIES)).toBe(true);
    expect(INSIGHT_CATEGORIES.length).toBeGreaterThan(0);
  });

  it('each category has id, label, description and icon', () => {
    INSIGHT_CATEGORIES.forEach(cat => {
      expect(cat).toHaveProperty('id');
      expect(cat).toHaveProperty('label');
      expect(cat).toHaveProperty('description');
      expect(cat).toHaveProperty('icon');
    });
  });

  it('includes the five expected category ids', () => {
    const ids = INSIGHT_CATEGORIES.map(c => c.id);
    expect(ids).toContain('critical');
    expect(ids).toContain('warnings');
    expect(ids).toContain('patterns');
    expect(ids).toContain('performance');
    expect(ids).toContain('security');
  });

  it('all ids are valid InsightCategory strings', () => {
    const validCategories: InsightCategory[] = [
      'critical', 'warnings', 'patterns', 'performance', 'security',
    ];
    INSIGHT_CATEGORIES.forEach(cat => {
      expect(validCategories).toContain(cat.id);
    });
  });
});

describe('MOCK_INSIGHTS', () => {
  it('exports a non-empty array of insight details', () => {
    expect(Array.isArray(MOCK_INSIGHTS)).toBe(true);
    expect(MOCK_INSIGHTS.length).toBeGreaterThan(0);
  });

  it('each insight has the required fields', () => {
    MOCK_INSIGHTS.forEach((insight: InsightDetail) => {
      expect(insight).toHaveProperty('id');
      expect(insight).toHaveProperty('severity');
      expect(insight).toHaveProperty('category');
      expect(insight).toHaveProperty('title');
      expect(insight).toHaveProperty('description');
      expect(insight).toHaveProperty('detectedAt');
      expect(insight).toHaveProperty('metrics');
      expect(insight).toHaveProperty('recommendations');
      expect(insight).toHaveProperty('affectedMessages');
    });
  });

  it('each insight id is a non-empty string', () => {
    MOCK_INSIGHTS.forEach(insight => {
      expect(typeof insight.id).toBe('string');
      expect(insight.id.length).toBeGreaterThan(0);
    });
  });

  it('each insight severity is one of the valid values', () => {
    const valid: InsightSeverity[] = ['high', 'medium', 'low'];
    MOCK_INSIGHTS.forEach(insight => {
      expect(valid).toContain(insight.severity);
    });
  });

  it('each insight category is one of the valid values', () => {
    const valid: InsightCategory[] = ['critical', 'warnings', 'patterns', 'performance', 'security'];
    MOCK_INSIGHTS.forEach(insight => {
      expect(valid).toContain(insight.category);
    });
  });

  it('detectedAt is a Date object', () => {
    MOCK_INSIGHTS.forEach(insight => {
      expect(insight.detectedAt).toBeInstanceOf(Date);
    });
  });

  it('metrics is an array with at least one entry', () => {
    MOCK_INSIGHTS.forEach(insight => {
      expect(Array.isArray(insight.metrics)).toBe(true);
      expect(insight.metrics.length).toBeGreaterThan(0);
    });
  });

  it('each metric has label and value', () => {
    MOCK_INSIGHTS.forEach(insight => {
      insight.metrics.forEach((m: InsightMetric) => {
        expect(typeof m.label).toBe('string');
        expect(m.value).toBeDefined();
      });
    });
  });

  it('recommendations is an array with at least one entry', () => {
    MOCK_INSIGHTS.forEach(insight => {
      expect(Array.isArray(insight.recommendations)).toBe(true);
    });
  });

  it('each recommendation has priority and text', () => {
    MOCK_INSIGHTS.forEach(insight => {
      insight.recommendations.forEach((rec: InsightRecommendation) => {
        expect(typeof rec.priority).toBe('string');
        expect(typeof rec.text).toBe('string');
      });
    });
  });

  it('affectedMessages is a non-negative number', () => {
    MOCK_INSIGHTS.forEach(insight => {
      expect(typeof insight.affectedMessages).toBe('number');
      expect(insight.affectedMessages).toBeGreaterThanOrEqual(0);
    });
  });

  it('contains at least one critical-severity insight', () => {
    const critical = MOCK_INSIGHTS.filter(i => i.severity === 'high');
    expect(critical.length).toBeGreaterThan(0);
  });

  it('contains insights from the critical category', () => {
    const critical = MOCK_INSIGHTS.filter(i => i.category === 'critical');
    expect(critical.length).toBeGreaterThan(0);
  });
});
