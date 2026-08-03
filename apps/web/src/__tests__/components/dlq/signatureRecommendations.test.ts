import { describe, it, expect } from 'vitest';
import { getTrendRecommendation } from '@/components/dlq/signatureRecommendations';

describe('getTrendRecommendation', () => {
  it.each([
    ['New', 'No knowledge on file yet — start with Root Cause & Knowledge below.'],
    ['Recurring', 'Check the Timeline for prior attempts before acting again.'],
    ['Escalating', 'Review Replay Safety before replaying — occurrence rate is rising.'],
    ['UnknownTrend', null],
    ['', null],
  ])('trend %s → %s', (trend, expected) => {
    expect(getTrendRecommendation(trend)).toBe(expected);
  });
});
