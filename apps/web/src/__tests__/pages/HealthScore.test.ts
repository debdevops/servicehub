import { describe, it, expect } from 'vitest';
import { getHealthGrade } from '@/pages/DashboardPage';

describe('getHealthGrade', () => {
  it('returns grade A when dlqRatio is 0', () => {
    const result = getHealthGrade(100, 0);
    expect(result.grade).toBe('A');
    expect(result.color).toBe('emerald');
  });

  it('returns grade B when dlqRatio is < 5%', () => {
    // 3 DLQ out of 100 total = 3%
    const result = getHealthGrade(97, 3);
    expect(result.grade).toBe('B');
    expect(result.color).toBe('green');
  });

  it('returns grade C when dlqRatio is < 15%', () => {
    // 10 DLQ out of 100 total = 10%
    const result = getHealthGrade(90, 10);
    expect(result.grade).toBe('C');
    expect(result.color).toBe('amber');
  });

  it('returns grade D when dlqRatio is < 40%', () => {
    // 30 DLQ out of 100 total = 30%
    const result = getHealthGrade(70, 30);
    expect(result.grade).toBe('D');
    expect(result.color).toBe('orange');
  });

  it('returns grade F when dlqRatio is >= 40%', () => {
    // 50 DLQ out of 100 total = 50%
    const result = getHealthGrade(50, 50);
    expect(result.grade).toBe('F');
    expect(result.color).toBe('red');
  });

  it('returns grade A when both counts are 0', () => {
    const result = getHealthGrade(0, 0);
    expect(result.grade).toBe('A');
  });

  it('returns grade F when only DLQ messages exist', () => {
    const result = getHealthGrade(0, 10);
    expect(result.grade).toBe('F');
  });
});
