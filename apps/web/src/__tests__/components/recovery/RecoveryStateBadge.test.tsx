import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { RecoveryStateBadge } from '@/components/recovery/RecoveryStateBadge';
import type { RecoveryEntryState } from '@servicehub/ui-shared/lib/api/recovery';

describe('RecoveryStateBadge', () => {
  const states: RecoveryEntryState[] = [
    'Executing', 'Observing', 'ExecutionFailed', 'ExecutionUnknown',
    'Recovered', 'Returned', 'Discarded', 'Unverified', 'WrittenOff', 'Expired',
  ];

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
});
