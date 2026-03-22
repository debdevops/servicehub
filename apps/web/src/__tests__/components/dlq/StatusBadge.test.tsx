import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { StatusBadge, CategoryBadge } from '@/components/dlq/StatusBadge';

// ─── StatusBadge ─────────────────────────────────────────────────────────────

describe('StatusBadge', () => {
  it('renders the status text', () => {
    render(<StatusBadge status="Active" />);
    expect(screen.getByText('Active')).toBeInTheDocument();
  });

  it('applies red styles for Active status', () => {
    const { container } = render(<StatusBadge status="Active" />);
    const badge = container.firstChild as HTMLElement;
    expect(badge.className).toContain('bg-red-100');
    expect(badge.className).toContain('text-red-700');
  });

  it('applies green styles for Replayed status', () => {
    const { container } = render(<StatusBadge status="Replayed" />);
    const badge = container.firstChild as HTMLElement;
    expect(badge.className).toContain('bg-green-100');
    expect(badge.className).toContain('text-green-700');
  });

  it('applies gray styles for Archived status', () => {
    const { container } = render(<StatusBadge status="Archived" />);
    const badge = container.firstChild as HTMLElement;
    expect(badge.className).toContain('bg-gray-100');
    expect(badge.className).toContain('text-gray-700');
  });

  it('applies yellow styles for Discarded status', () => {
    const { container } = render(<StatusBadge status="Discarded" />);
    const badge = container.firstChild as HTMLElement;
    expect(badge.className).toContain('bg-yellow-100');
    expect(badge.className).toContain('text-yellow-700');
  });

  it('applies orange styles for ReplayFailed status', () => {
    const { container } = render(<StatusBadge status="ReplayFailed" />);
    const badge = container.firstChild as HTMLElement;
    expect(badge.className).toContain('bg-orange-100');
    expect(badge.className).toContain('text-orange-700');
  });

  it('applies sky styles for Resolved status', () => {
    const { container } = render(<StatusBadge status="Resolved" />);
    const badge = container.firstChild as HTMLElement;
    expect(badge.className).toContain('bg-sky-100');
    expect(badge.className).toContain('text-sky-700');
  });

  it('falls back to Active (red) styles for an unknown status', () => {
    const { container } = render(<StatusBadge status="UnknownStatus" />);
    const badge = container.firstChild as HTMLElement;
    expect(badge.className).toContain('bg-red-100');
    expect(badge.className).toContain('text-red-700');
  });

  it('shows the status text for an unknown status value', () => {
    render(<StatusBadge status="SomeRandomStatus" />);
    expect(screen.getByText('SomeRandomStatus')).toBeInTheDocument();
  });

  it('uses small padding by default (size="sm")', () => {
    const { container } = render(<StatusBadge status="Active" />);
    const badge = container.firstChild as HTMLElement;
    expect(badge.className).toContain('px-2');
    expect(badge.className).toContain('text-xs');
  });

  it('uses larger padding when size="md"', () => {
    const { container } = render(<StatusBadge status="Active" size="md" />);
    const badge = container.firstChild as HTMLElement;
    expect(badge.className).toContain('px-3');
    expect(badge.className).toContain('text-sm');
  });

  it('renders a colored dot indicator span', () => {
    const { container } = render(<StatusBadge status="Active" />);
    const dot = container.querySelector('.w-1\\.5.h-1\\.5') as HTMLElement;
    expect(dot).toBeInTheDocument();
    expect(dot.className).toContain('bg-red-500');
  });
});

// ─── CategoryBadge ────────────────────────────────────────────────────────────

describe('CategoryBadge', () => {
  it('renders the category text', () => {
    render(<CategoryBadge category="Transient" />);
    expect(screen.getByText('Transient')).toBeInTheDocument();
  });

  it('applies blue styles for Transient category', () => {
    const { container } = render(<CategoryBadge category="Transient" />);
    const badge = container.firstChild as HTMLElement;
    expect(badge.className).toContain('bg-blue-100');
    expect(badge.className).toContain('text-blue-700');
  });

  it('applies red styles for MaxDelivery category', () => {
    const { container } = render(<CategoryBadge category="MaxDelivery" />);
    const badge = container.firstChild as HTMLElement;
    expect(badge.className).toContain('bg-red-100');
    expect(badge.className).toContain('text-red-700');
  });

  it('applies purple styles for DataQuality category', () => {
    const { container } = render(<CategoryBadge category="DataQuality" />);
    const badge = container.firstChild as HTMLElement;
    expect(badge.className).toContain('bg-purple-100');
  });

  it('applies gray/Unknown styles for an unknown category', () => {
    const { container } = render(<CategoryBadge category="NoSuchCategory" />);
    const badge = container.firstChild as HTMLElement;
    expect(badge.className).toContain('bg-gray-100');
  });

  it('does NOT render a confidence percentage when confidence is not provided', () => {
    render(<CategoryBadge category="Transient" />);
    expect(screen.queryByText(/%/)).not.toBeInTheDocument();
  });

  it('renders a confidence percentage when confidence > 0', () => {
    render(<CategoryBadge category="MaxDelivery" confidence={0.75} />);
    expect(screen.getByText('(75%)')).toBeInTheDocument();
  });

  it('rounds confidence to the nearest integer', () => {
    render(<CategoryBadge category="Transient" confidence={0.876} />);
    expect(screen.getByText('(88%)')).toBeInTheDocument();
  });

  it('does NOT render confidence percentage for confidence=0', () => {
    render(<CategoryBadge category="Transient" confidence={0} />);
    expect(screen.queryByText(/%/)).not.toBeInTheDocument();
  });

  it('shows a title attribute with confidence when provided', () => {
    const { container } = render(<CategoryBadge category="Transient" confidence={0.5} />);
    const badge = container.firstChild as HTMLElement;
    expect(badge).toHaveAttribute('title', 'Confidence: 50%');
  });

  it('does not have a title attribute when confidence is not provided', () => {
    const { container } = render(<CategoryBadge category="Transient" />);
    const badge = container.firstChild as HTMLElement;
    expect(badge).not.toHaveAttribute('title');
  });

  it('uses small padding by default (size="sm")', () => {
    const { container } = render(<CategoryBadge category="Expired" />);
    const badge = container.firstChild as HTMLElement;
    expect(badge.className).toContain('px-2');
    expect(badge.className).toContain('text-xs');
  });

  it('uses larger padding when size="md"', () => {
    const { container } = render(<CategoryBadge category="Expired" size="md" />);
    const badge = container.firstChild as HTMLElement;
    expect(badge.className).toContain('px-3');
    expect(badge.className).toContain('text-sm');
  });
});
