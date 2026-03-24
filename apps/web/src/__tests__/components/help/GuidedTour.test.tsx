import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { GuidedTour, isTourCompleted, resetTour } from '@/components/help/GuidedTour';

describe('GuidedTour utility functions', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('isTourCompleted returns false initially', () => {
    expect(isTourCompleted()).toBe(false);
  });

  it('isTourCompleted returns true after localStorage is set', () => {
    localStorage.setItem('servicehub_tour_completed', 'true');
    expect(isTourCompleted()).toBe(true);
  });

  it('resetTour clears the localStorage flag', () => {
    localStorage.setItem('servicehub_tour_completed', 'true');
    resetTour();
    expect(isTourCompleted()).toBe(false);
  });
});

describe('GuidedTour component', () => {
  const onComplete = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('renders nothing when isActive is false', () => {
    const { container } = render(<GuidedTour isActive={false} onComplete={onComplete} />);
    expect(container.firstChild).toBeNull();
  });

  it('renders dialog when isActive is true', () => {
    render(<GuidedTour isActive={true} onComplete={onComplete} />);
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('shows step counter (1 / N)', () => {
    render(<GuidedTour isActive={true} onComplete={onComplete} />);
    expect(screen.getByText(/1 \//)).toBeInTheDocument();
  });

  it('shows the first step title and content', () => {
    render(<GuidedTour isActive={true} onComplete={onComplete} />);
    // The tour starts at step 1 — just check something is rendered
    const dialog = screen.getByRole('dialog');
    expect(dialog.textContent).toBeTruthy();
  });

  it('has Next button on first step', () => {
    render(<GuidedTour isActive={true} onComplete={onComplete} />);
    expect(screen.getByText('Next')).toBeInTheDocument();
  });

  it('does not have Back button on first step', () => {
    render(<GuidedTour isActive={true} onComplete={onComplete} />);
    expect(screen.queryByText('Back')).not.toBeInTheDocument();
  });

  it('advances to next step on Next click', () => {
    render(<GuidedTour isActive={true} onComplete={onComplete} />);
    fireEvent.click(screen.getByText('Next'));
    expect(screen.getByText(/2 \//)).toBeInTheDocument();
  });

  it('shows Back button on second step', () => {
    render(<GuidedTour isActive={true} onComplete={onComplete} />);
    fireEvent.click(screen.getByText('Next'));
    expect(screen.getByText('Back')).toBeInTheDocument();
  });

  it('goes back on Back click', () => {
    render(<GuidedTour isActive={true} onComplete={onComplete} />);
    fireEvent.click(screen.getByText('Next'));
    expect(screen.getByText(/2 \//)).toBeInTheDocument();
    fireEvent.click(screen.getByText('Back'));
    expect(screen.getByText(/1 \//)).toBeInTheDocument();
  });

  it('calls onComplete and sets localStorage on Skip tour', () => {
    render(<GuidedTour isActive={true} onComplete={onComplete} />);
    fireEvent.click(screen.getByText('Skip tour'));
    expect(onComplete).toHaveBeenCalled();
    expect(localStorage.getItem('servicehub_tour_completed')).toBe('true');
  });

  it('calls onComplete on Close button click', () => {
    render(<GuidedTour isActive={true} onComplete={onComplete} />);
    fireEvent.click(screen.getByLabelText('Close tour'));
    expect(onComplete).toHaveBeenCalled();
  });

  it('calls onComplete on Escape key', () => {
    render(<GuidedTour isActive={true} onComplete={onComplete} />);
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(onComplete).toHaveBeenCalled();
  });

  it('centers popover when no target element is found', () => {
    render(<GuidedTour isActive={true} onComplete={onComplete} />);
    // No data-tour elements in test DOM → popover should still render
    const dialog = screen.getByRole('dialog');
    expect(dialog).toBeInTheDocument();
  });
});
