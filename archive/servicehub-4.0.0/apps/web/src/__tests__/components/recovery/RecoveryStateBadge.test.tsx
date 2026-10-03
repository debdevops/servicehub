import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { RecoveryStateBadge } from '@/components/recovery/RecoveryStateBadge';
import { RECOVERY_STATE_EXPLANATIONS, type RecoveryEntryState } from '@servicehub/ui-shared/lib/api/recovery';

describe('RecoveryStateBadge', () => {
  // Derived from the exhaustive explanations map, not hand-maintained, so a new
  // RecoveryEntryState is automatically covered here the moment it's added there — a
  // hand-maintained list previously drifted from the backend's `Declined` state and crashed
  // RecoveryStateBadge in production before this test caught it.
  const states = Object.keys(RECOVERY_STATE_EXPLANATIONS) as RecoveryEntryState[];

  it.each(states)('renders the state label for %s', (state) => {
    render(<RecoveryStateBadge state={state} />);
    expect(screen.getByText(state)).toBeInTheDocument();
  });

  it('does not render Unverified in the same color as Recovered', () => {
    const { container: recoveredContainer } = render(<RecoveryStateBadge state="Recovered" />);
    const { container: unverifiedContainer } = render(<RecoveryStateBadge state="Unverified" />);
    const recoveredClass = recoveredContainer.querySelector('span')?.className;
    const unverifiedClass = unverifiedContainer.querySelector('span')?.className;
    expect(recoveredClass).not.toBe(unverifiedClass);
  });

  it.each(states)('opening the details panel for %s shows all four explanation parts', (state) => {
    render(<RecoveryStateBadge state={state} />);
    const details = screen.getByText(state).closest('details') as HTMLDetailsElement;
    details.open = true;
    expect(screen.getByText(/^What happened:/)).toBeInTheDocument();
    expect(screen.getByText(/^Why ServiceHub knows this:/)).toBeInTheDocument();
    expect(screen.getByText(/^What ServiceHub cannot prove:/)).toBeInTheDocument();
    expect(screen.getByText(/^What you can do:/)).toBeInTheDocument();
  });

  it('renders a recorded reason when one is supplied', () => {
    render(<RecoveryStateBadge state="Unverified" reasonText="AWS SQS has no non-destructive peek." />);
    expect(screen.getByText('AWS SQS has no non-destructive peek.')).toBeInTheDocument();
    expect(screen.getByText(/^Recorded reason:/)).toBeInTheDocument();
  });

  it('never fabricates a recorded-reason line when none was supplied', () => {
    render(<RecoveryStateBadge state="Unverified" />);
    expect(screen.queryByText(/^Recorded reason:/)).not.toBeInTheDocument();
  });
});
