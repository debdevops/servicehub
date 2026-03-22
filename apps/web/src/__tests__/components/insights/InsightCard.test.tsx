import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { InsightCard } from '@/components/insights/InsightCard';
import type { InsightDetail } from '@/lib/insightsMockData';

function makeInsight(overrides: Partial<InsightDetail> = {}): InsightDetail {
  return {
    id: 'ins-001',
    severity: 'high',
    category: 'critical',
    title: 'Payment Gateway Timeout',
    description: 'Timeouts increased by 340% in the last hour.',
    detectedAt: new Date('2025-01-01T09:00:00Z'),
    metrics: [
      { label: 'Failed Messages', value: '23' },
      { label: 'Avg Response Time', value: '8.7s', highlight: true },
    ],
    recommendations: [
      { priority: 'immediate', text: 'Increase timeout threshold' },
      { priority: 'short-term', text: 'Add circuit breaker' },
    ],
    affectedMessages: 23,
    ...overrides,
  };
}

describe('InsightCard', () => {
  // ── Content rendering ─────────────────────────────────────────────────────

  it('renders the insight title', () => {
    render(<InsightCard insight={makeInsight()} />);
    expect(screen.getByText('Payment Gateway Timeout')).toBeInTheDocument();
  });

  it('renders the insight description', () => {
    render(<InsightCard insight={makeInsight()} />);
    expect(screen.getByText('Timeouts increased by 340% in the last hour.')).toBeInTheDocument();
  });

  it('renders metric labels', () => {
    render(<InsightCard insight={makeInsight()} />);
    expect(screen.getByText('Failed Messages')).toBeInTheDocument();
    expect(screen.getByText('Avg Response Time')).toBeInTheDocument();
  });

  it('renders metric values', () => {
    render(<InsightCard insight={makeInsight()} />);
    expect(screen.getByText('23')).toBeInTheDocument();
    expect(screen.getByText('8.7s')).toBeInTheDocument();
  });

  it('renders recommendation text', () => {
    render(<InsightCard insight={makeInsight()} />);
    expect(screen.getByText('Increase timeout threshold')).toBeInTheDocument();
    expect(screen.getByText('Add circuit breaker')).toBeInTheDocument();
  });

  it('renders recommendation priority badges', () => {
    render(<InsightCard insight={makeInsight()} />);
    expect(screen.getByText('Immediate')).toBeInTheDocument();
    expect(screen.getByText('Short-term')).toBeInTheDocument();
  });

  // ── Severity variants ─────────────────────────────────────────────────────

  it('applies red border for HIGH severity', () => {
    const { container } = render(<InsightCard insight={makeInsight({ severity: 'high' })} />);
    const card = container.firstChild as HTMLElement;
    expect(card.className).toContain('border-l-red-500');
  });

  it('applies amber border for MEDIUM severity', () => {
    const { container } = render(<InsightCard insight={makeInsight({ severity: 'medium' })} />);
    const card = container.firstChild as HTMLElement;
    expect(card.className).toContain('border-l-amber-500');
  });

  it('applies primary border for LOW severity', () => {
    const { container } = render(<InsightCard insight={makeInsight({ severity: 'low' })} />);
    const card = container.firstChild as HTMLElement;
    expect(card.className).toContain('border-l-primary-500');
  });

  it('shows HIGH SEVERITY label for high severity', () => {
    render(<InsightCard insight={makeInsight({ severity: 'high' })} />);
    expect(screen.getByText('HIGH SEVERITY')).toBeInTheDocument();
  });

  it('shows MEDIUM SEVERITY label for medium severity', () => {
    render(<InsightCard insight={makeInsight({ severity: 'medium' })} />);
    expect(screen.getByText('MEDIUM SEVERITY')).toBeInTheDocument();
  });

  it('shows LOW SEVERITY label for low severity', () => {
    render(<InsightCard insight={makeInsight({ severity: 'low' })} />);
    expect(screen.getByText('LOW SEVERITY')).toBeInTheDocument();
  });

  // ── Highlighted metrics ───────────────────────────────────────────────────

  it('applies red color class to highlighted metrics', () => {
    const insight = makeInsight({
      metrics: [{ label: 'Impact', value: '9.2/10', highlight: true }],
    });
    const { container } = render(<InsightCard insight={insight} />);
    const highlighted = container.querySelector('.text-red-600');
    expect(highlighted).toBeInTheDocument();
  });

  it('does not apply red to non-highlighted metrics', () => {
    const insight = makeInsight({
      metrics: [{ label: 'Normal', value: '5', highlight: false }],
    });
    const { container } = render(<InsightCard insight={insight} />);
    // The value "5" should NOT be in a red element
    // No red text for non-highlighted metrics (header still has red from severity separately)
    const metricSection = container.querySelector('.grid');
    expect(metricSection?.querySelector('.text-red-600')).toBeNull();
  });

  // ── Long-term priority ────────────────────────────────────────────────────

  it('renders long-term recommendation with correct badge', () => {
    const insight = makeInsight({
      recommendations: [{ priority: 'long-term', text: 'Refactor messaging layer' }],
    });
    render(<InsightCard insight={insight} />);
    expect(screen.getByText('Long-term')).toBeInTheDocument();
  });

  it('renders prevention recommendation with correct badge', () => {
    const insight = makeInsight({
      recommendations: [{ priority: 'prevention', text: 'Add monitoring' }],
    });
    render(<InsightCard insight={insight} />);
    expect(screen.getByText('Prevention')).toBeInTheDocument();
  });
});
