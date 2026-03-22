import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { formatRelativeTime, formatNumber } from '@/lib/utils';

// ─── formatRelativeTime ───────────────────────────────────────────────────────

describe('formatRelativeTime', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2025-01-01T12:00:00Z'));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('returns "just now" for a date less than 60 seconds ago', () => {
    const date = new Date('2025-01-01T11:59:30Z'); // 30 seconds ago
    expect(formatRelativeTime(date)).toBe('just now');
  });

  it('returns "just now" for a date exactly 59 seconds ago', () => {
    const date = new Date('2025-01-01T11:59:01Z'); // 59 seconds ago
    expect(formatRelativeTime(date)).toBe('just now');
  });

  it('returns "1m ago" for a date 1 minute ago', () => {
    const date = new Date('2025-01-01T11:59:00Z'); // exactly 60 seconds ago
    expect(formatRelativeTime(date)).toBe('1m ago');
  });

  it('returns "5m ago" for a date 5 minutes ago', () => {
    const date = new Date('2025-01-01T11:55:00Z');
    expect(formatRelativeTime(date)).toBe('5m ago');
  });

  it('returns "59m ago" for a date 59 minutes ago', () => {
    const date = new Date('2025-01-01T11:01:00Z');
    expect(formatRelativeTime(date)).toBe('59m ago');
  });

  it('returns "1h ago" for a date exactly 1 hour ago', () => {
    const date = new Date('2025-01-01T11:00:00Z');
    expect(formatRelativeTime(date)).toBe('1h ago');
  });

  it('returns "3h ago" for a date 3 hours ago', () => {
    const date = new Date('2025-01-01T09:00:00Z');
    expect(formatRelativeTime(date)).toBe('3h ago');
  });

  it('returns "23h ago" for a date 23 hours ago', () => {
    const date = new Date('2024-12-31T13:00:00Z');
    expect(formatRelativeTime(date)).toBe('23h ago');
  });

  it('returns "1d ago" for a date exactly 1 day ago', () => {
    const date = new Date('2024-12-31T12:00:00Z');
    expect(formatRelativeTime(date)).toBe('1d ago');
  });

  it('returns "7d ago" for a date 7 days ago', () => {
    const date = new Date('2024-12-25T12:00:00Z');
    expect(formatRelativeTime(date)).toBe('7d ago');
  });

  it('returns "30d ago" for a date 30 days ago', () => {
    const date = new Date('2024-12-02T12:00:00Z');
    expect(formatRelativeTime(date)).toBe('30d ago');
  });
});

// ─── formatNumber ─────────────────────────────────────────────────────────────

describe('formatNumber', () => {
  it('formats zero as "0"', () => {
    expect(formatNumber(0)).toBe('0');
  });

  it('formats a 3-digit number without separator', () => {
    expect(formatNumber(999)).toBe('999');
  });

  it('formats 1000 with a comma', () => {
    expect(formatNumber(1000)).toBe('1,000');
  });

  it('formats 3892 as "3,892"', () => {
    expect(formatNumber(3892)).toBe('3,892');
  });

  it('formats 1000000 as "1,000,000"', () => {
    expect(formatNumber(1_000_000)).toBe('1,000,000');
  });

  it('formats negative numbers correctly', () => {
    expect(formatNumber(-1500)).toBe('-1,500');
  });

  it('formats large number with multiple separators', () => {
    expect(formatNumber(1_234_567_890)).toBe('1,234,567,890');
  });
});
