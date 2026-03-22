import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { AIRail } from '@/components/layout/AIRail';

describe('AIRail', () => {
  it('returns null when isOpen is false', () => {
    const { container } = render(<AIRail isOpen={false} onClose={vi.fn()} />);
    expect(container.firstChild).toBeNull();
  });

  it('renders when isOpen is true', () => {
    render(<AIRail isOpen={true} onClose={vi.fn()} />);
    expect(screen.getByText('AI Insights')).toBeInTheDocument();
  });

  it('shows the mock insight count badge', () => {
    render(<AIRail isOpen={true} onClose={vi.fn()} />);
    // There are 3 mock insights
    expect(screen.getByText('3')).toBeInTheDocument();
  });

  it('renders all 3 mock insight titles', () => {
    render(<AIRail isOpen={true} onClose={vi.fn()} />);
    expect(screen.getByText('Payment Gateway Timeout')).toBeInTheDocument();
    expect(screen.getByText('DLQ Accumulation')).toBeInTheDocument();
    expect(screen.getByText('High Retry Rate')).toBeInTheDocument();
  });

  it('renders close button', () => {
    render(<AIRail isOpen={true} onClose={vi.fn()} />);
    expect(screen.getByTitle('Close')).toBeInTheDocument();
  });

  it('calls onClose when close button is clicked', () => {
    const onClose = vi.fn();
    render(<AIRail isOpen={true} onClose={onClose} />);
    fireEvent.click(screen.getByTitle('Close'));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('renders "View N messages" buttons for each insight', () => {
    render(<AIRail isOpen={true} onClose={vi.fn()} />);
    const viewButtons = screen.getAllByText(/View \d+ messages/);
    expect(viewButtons.length).toBe(3);
  });

  it('renders severity labels for high severity insights', () => {
    render(<AIRail isOpen={true} onClose={vi.fn()} />);
    const highLabels = screen.getAllByText('high');
    expect(highLabels.length).toBeGreaterThan(0);
  });

  it('renders medium severity label', () => {
    render(<AIRail isOpen={true} onClose={vi.fn()} />);
    expect(screen.getByText('medium')).toBeInTheDocument();
  });

  it('renders timestamps for each insight', () => {
    render(<AIRail isOpen={true} onClose={vi.fn()} />);
    expect(screen.getByText('15 min ago')).toBeInTheDocument();
    expect(screen.getByText('32 min ago')).toBeInTheDocument();
    expect(screen.getByText('1 hour ago')).toBeInTheDocument();
  });
});
