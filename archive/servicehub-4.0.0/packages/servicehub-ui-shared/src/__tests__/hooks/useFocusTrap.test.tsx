import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useFocusTrap } from '../../hooks/useFocusTrap';

function Dialog({ isOpen, includeButtons = true }: { isOpen: boolean; includeButtons?: boolean }) {
  const ref = useFocusTrap<HTMLDivElement>(isOpen);
  if (!isOpen) return null;
  return (
    <div ref={ref} role="dialog" aria-modal="true" aria-label="Test dialog">
      {includeButtons && (
        <>
          <button type="button">first</button>
          <button type="button">middle</button>
          <button type="button">last</button>
        </>
      )}
    </div>
  );
}

function Harness({ open, includeButtons = true }: { open: boolean; includeButtons?: boolean }) {
  return (
    <div>
      <button type="button">outside-before</button>
      <Dialog isOpen={open} includeButtons={includeButtons} />
      <button type="button">outside-after</button>
    </div>
  );
}

describe('useFocusTrap', () => {
  beforeEach(() => {
    document.body.innerHTML = '';
  });

  afterEach(() => {
    cleanup();
  });

  it('moves focus into the dialog when it opens', () => {
    render(<Harness open />);
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'first' }));
  });

  it('does not steal focus from an element the dialog focused deliberately', () => {
    // ConfirmDialog focuses Cancel (not the first control) for danger variants specifically to
    // make accidental confirmation harder. The trap must not override that intent.
    function AutoFocusDialog() {
      const ref = useFocusTrap<HTMLDivElement>(true);
      return (
        <div ref={ref} role="dialog" aria-modal="true" aria-label="d">
          <button type="button">confirm</button>
          {/* autoFocus is the behaviour under test: the trap must not fight it. */}
          <button type="button" autoFocus>
            cancel
          </button>
        </div>
      );
    }
    render(<AutoFocusDialog />);
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'cancel' }));
  });

  it('cycles Tab from the last focusable element back to the first', async () => {
    const user = userEvent.setup();
    render(<Harness open />);

    screen.getByRole('button', { name: 'last' }).focus();
    await user.tab();

    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'first' }));
  });

  it('cycles Shift+Tab from the first focusable element back to the last', async () => {
    const user = userEvent.setup();
    render(<Harness open />);

    screen.getByRole('button', { name: 'first' }).focus();
    await user.tab({ shift: true });

    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'last' }));
  });

  it('never lets Tab reach a control outside the dialog', async () => {
    const user = userEvent.setup();
    render(<Harness open />);

    const outsideBefore = screen.getByRole('button', { name: 'outside-before' });
    const outsideAfter = screen.getByRole('button', { name: 'outside-after' });

    for (let i = 0; i < 8; i++) {
      await user.tab();
      expect(document.activeElement).not.toBe(outsideBefore);
      expect(document.activeElement).not.toBe(outsideAfter);
    }
  });

  it('restores focus to the triggering element when the dialog closes', () => {
    const { rerender } = render(<Harness open={false} />);

    const trigger = screen.getByRole('button', { name: 'outside-before' });
    trigger.focus();
    expect(document.activeElement).toBe(trigger);

    rerender(<Harness open />);
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'first' }));

    rerender(<Harness open={false} />);
    expect(document.activeElement).toBe(trigger);
  });

  it('does not throw when the triggering element was removed while the dialog was open', () => {
    function Removable({ open, showTrigger }: { open: boolean; showTrigger: boolean }) {
      return (
        <div>
          {showTrigger && <button type="button">temp-trigger</button>}
          <Dialog isOpen={open} />
        </div>
      );
    }

    const { rerender } = render(<Removable open={false} showTrigger />);
    screen.getByRole('button', { name: 'temp-trigger' }).focus();

    rerender(<Removable open showTrigger />);
    // The row that opened the dialog is deleted by the action the dialog confirmed.
    rerender(<Removable open showTrigger={false} />);

    expect(() => rerender(<Removable open={false} showTrigger={false} />)).not.toThrow();
  });

  it('focuses the container itself when the dialog has nothing focusable', () => {
    render(<Harness open includeButtons={false} />);
    expect(document.activeElement).toBe(screen.getByRole('dialog'));
  });

  it('does nothing while the dialog is closed', () => {
    render(<Harness open={false} />);
    const trigger = screen.getByRole('button', { name: 'outside-before' });
    trigger.focus();
    expect(document.activeElement).toBe(trigger);
  });
});
