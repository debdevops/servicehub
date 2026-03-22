import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { InsightsSidebar } from '@/components/insights/InsightsSidebar';
import type { InsightCategory } from '@/lib/insightsMockData';

const defaultCounts: Record<InsightCategory, number> = {
  critical: 5,
  warnings: 3,
  patterns: 2,
  performance: 1,
  security: 0,
};

describe('InsightsSidebar', () => {
  // ── Header ────────────────────────────────────────────────────────────────

  it('renders the "Issue Categories" heading', () => {
    render(
      <InsightsSidebar
        selectedCategory="critical"
        onSelectCategory={vi.fn()}
        categoryCounts={defaultCounts}
      />
    );
    expect(screen.getByText('Issue Categories')).toBeInTheDocument();
  });

  it('renders the subtitle description', () => {
    render(
      <InsightsSidebar
        selectedCategory="critical"
        onSelectCategory={vi.fn()}
        categoryCounts={defaultCounts}
      />
    );
    expect(screen.getByText('AI-detected patterns & anomalies')).toBeInTheDocument();
  });

  // ── Category list ─────────────────────────────────────────────────────────

  it('renders all five category labels', () => {
    render(
      <InsightsSidebar
        selectedCategory="critical"
        onSelectCategory={vi.fn()}
        categoryCounts={defaultCounts}
      />
    );
    expect(screen.getByText('Critical Issues')).toBeInTheDocument();
    expect(screen.getByText('Warnings')).toBeInTheDocument();
    expect(screen.getByText('Patterns Detected')).toBeInTheDocument();
    expect(screen.getByText('Performance')).toBeInTheDocument();
    expect(screen.getByText('Security')).toBeInTheDocument();
  });

  it('renders all category count badges', () => {
    render(
      <InsightsSidebar
        selectedCategory="critical"
        onSelectCategory={vi.fn()}
        categoryCounts={defaultCounts}
      />
    );
    expect(screen.getByText('5')).toBeInTheDocument(); // critical
    expect(screen.getByText('3')).toBeInTheDocument(); // warnings
    expect(screen.getByText('2')).toBeInTheDocument(); // patterns
    expect(screen.getByText('1')).toBeInTheDocument(); // performance
    expect(screen.getByText('0')).toBeInTheDocument(); // security
  });

  // ── Selected state ────────────────────────────────────────────────────────

  it('highlights the selected category row', () => {
    const { container } = render(
      <InsightsSidebar
        selectedCategory="warnings"
        onSelectCategory={vi.fn()}
        categoryCounts={defaultCounts}
      />
    );
    // The selected item should have a distinct class
    const selectedItem = Array.from(container.querySelectorAll('button')).find(btn =>
      btn.textContent?.includes('Warnings')
    );
    expect(selectedItem?.className).toMatch(/bg-/);
  });

  // ── Click interactions ────────────────────────────────────────────────────

  it('calls onSelectCategory with the clicked category', async () => {
    const onSelectCategory = vi.fn();
    render(
      <InsightsSidebar
        selectedCategory="critical"
        onSelectCategory={onSelectCategory}
        categoryCounts={defaultCounts}
      />
    );
    await userEvent.click(screen.getByText('Warnings'));
    expect(onSelectCategory).toHaveBeenCalledWith('warnings');
  });

  it('calls onSelectCategory for each category when clicked', async () => {
    const onSelectCategory = vi.fn();
    render(
      <InsightsSidebar
        selectedCategory="critical"
        onSelectCategory={onSelectCategory}
        categoryCounts={defaultCounts}
      />
    );

    await userEvent.click(screen.getByText('Patterns Detected'));
    expect(onSelectCategory).toHaveBeenCalledWith('patterns');

    await userEvent.click(screen.getByText('Performance'));
    expect(onSelectCategory).toHaveBeenCalledWith('performance');

    await userEvent.click(screen.getByText('Security'));
    expect(onSelectCategory).toHaveBeenCalledWith('security');
  });

  // ── Category descriptions ─────────────────────────────────────────────────

  it('renders category descriptions', () => {
    render(
      <InsightsSidebar
        selectedCategory="critical"
        onSelectCategory={vi.fn()}
        categoryCounts={defaultCounts}
      />
    );
    expect(screen.getByText('Requires immediate attention')).toBeInTheDocument();
    expect(screen.getByText('Performance degradation')).toBeInTheDocument();
    expect(screen.getByText('Recurring behaviors')).toBeInTheDocument();
  });
});
