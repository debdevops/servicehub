import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConfirmDialog, useConfirmDialog } from '@/components/ConfirmDialog';

const defaultProps = {
  isOpen: true,
  title: 'Delete item',
  message: 'Are you sure you want to delete this item?',
  onConfirm: vi.fn(),
  onCancel: vi.fn(),
};

describe('ConfirmDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── Visibility ────────────────────────────────────────────────────────────

  it('renders nothing when isOpen is false', () => {
    const { container } = render(<ConfirmDialog {...defaultProps} isOpen={false} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders the dialog when isOpen is true', () => {
    render(<ConfirmDialog {...defaultProps} />);
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
  });

  // ── Content rendering ─────────────────────────────────────────────────────

  it('renders the title text', () => {
    render(<ConfirmDialog {...defaultProps} title="Confirm Action" />);
    expect(screen.getByText('Confirm Action')).toBeInTheDocument();
  });

  it('renders the message text', () => {
    render(<ConfirmDialog {...defaultProps} message="This cannot be undone." />);
    expect(screen.getByText('This cannot be undone.')).toBeInTheDocument();
  });

  it('renders default button labels when not provided', () => {
    render(<ConfirmDialog {...defaultProps} />);
    expect(screen.getByText('Confirm')).toBeInTheDocument();
    expect(screen.getByText('Cancel')).toBeInTheDocument();
  });

  it('renders custom confirmLabel and cancelLabel', () => {
    render(<ConfirmDialog {...defaultProps} confirmLabel="Delete" cancelLabel="Go back" />);
    expect(screen.getByText('Delete')).toBeInTheDocument();
    expect(screen.getByText('Go back')).toBeInTheDocument();
  });

  // ── Accessibility ─────────────────────────────────────────────────────────

  it('has role="alertdialog"', () => {
    render(<ConfirmDialog {...defaultProps} />);
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
  });

  it('has aria-modal="true"', () => {
    render(<ConfirmDialog {...defaultProps} />);
    expect(screen.getByRole('alertdialog')).toHaveAttribute('aria-modal', 'true');
  });

  it('has aria-labelledby pointing to the title element', () => {
    render(<ConfirmDialog {...defaultProps} title="My Title" />);
    const dialog = screen.getByRole('alertdialog');
    expect(dialog).toHaveAttribute('aria-labelledby', 'confirm-dialog-title');
    expect(document.getElementById('confirm-dialog-title')).toHaveTextContent('My Title');
  });

  it('has aria-describedby pointing to the message element', () => {
    render(<ConfirmDialog {...defaultProps} message="My message" />);
    const dialog = screen.getByRole('alertdialog');
    expect(dialog).toHaveAttribute('aria-describedby', 'confirm-dialog-description');
    expect(document.getElementById('confirm-dialog-description')).toHaveTextContent('My message');
  });

  // ── Interactions — onConfirm ──────────────────────────────────────────────

  it('calls onConfirm when the confirm button is clicked', async () => {
    const onConfirm = vi.fn();
    render(<ConfirmDialog {...defaultProps} onConfirm={onConfirm} />);
    await userEvent.click(screen.getByText('Confirm'));
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  // ── Interactions — onCancel ───────────────────────────────────────────────

  it('calls onCancel when the cancel button is clicked', async () => {
    const onCancel = vi.fn();
    render(<ConfirmDialog {...defaultProps} onCancel={onCancel} />);
    await userEvent.click(screen.getByText('Cancel'));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('calls onCancel when the X close button is clicked', async () => {
    const onCancel = vi.fn();
    render(<ConfirmDialog {...defaultProps} onCancel={onCancel} />);
    await userEvent.click(screen.getByLabelText('Close dialog'));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('calls onCancel when the backdrop is clicked', async () => {
    const onCancel = vi.fn();
    render(<ConfirmDialog {...defaultProps} onCancel={onCancel} />);
    // The backdrop is an aria-hidden div behind the dialog
    const backdrop = document.querySelector('[aria-hidden="true"]') as HTMLElement;
    await userEvent.click(backdrop);
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('calls onCancel when the Escape key is pressed', async () => {
    const onCancel = vi.fn();
    render(<ConfirmDialog {...defaultProps} onCancel={onCancel} />);
    await userEvent.keyboard('{Escape}');
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('does NOT call onCancel on Escape when isOpen is false', async () => {
    const onCancel = vi.fn();
    render(<ConfirmDialog {...defaultProps} isOpen={false} onCancel={onCancel} />);
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onCancel).not.toHaveBeenCalled();
  });

  // ── Focus management (v3.6.0 P2-4) ────────────────────────────────────────
  //
  // The dialog already had role/aria-modal/aria-labelledby/Escape. What it lacked was focus
  // management: Tab walked straight out into the page behind, and closing dropped focus to
  // <body>. aria-modal tells a screen reader the background is inert; it does not make it so
  // for the Tab key.

  it('moves focus into the dialog when it opens', () => {
    render(<ConfirmDialog {...defaultProps} />);
    const dialog = screen.getByRole('alertdialog');
    expect(dialog.contains(document.activeElement)).toBe(true);
  });

  it('focuses Cancel for the danger variant rather than the first control', () => {
    // Deliberate: makes accidental confirmation of a destructive action harder. The focus trap
    // must not override a dialog's own considered choice of initial focus.
    render(<ConfirmDialog {...defaultProps} variant="danger" cancelLabel="Cancel" />);
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Cancel' }));
  });

  it('keeps Tab inside the dialog', async () => {
    const user = userEvent.setup();
    render(
      <>
        <button type="button">outside</button>
        <ConfirmDialog {...defaultProps} />
      </>
    );

    const outside = screen.getByRole('button', { name: 'outside' });
    const dialog = screen.getByRole('alertdialog');

    for (let i = 0; i < 6; i++) {
      await user.tab();
      expect(document.activeElement).not.toBe(outside);
      expect(dialog.contains(document.activeElement)).toBe(true);
    }
  });

  it('returns focus to the triggering element when it closes', () => {
    const { rerender } = render(
      <>
        <button type="button">trigger</button>
        <ConfirmDialog {...defaultProps} isOpen={false} />
      </>
    );

    const trigger = screen.getByRole('button', { name: 'trigger' });
    trigger.focus();

    rerender(
      <>
        <button type="button">trigger</button>
        <ConfirmDialog {...defaultProps} isOpen />
      </>
    );
    expect(document.activeElement).not.toBe(trigger);

    rerender(
      <>
        <button type="button">trigger</button>
        <ConfirmDialog {...defaultProps} isOpen={false} />
      </>
    );
    expect(document.activeElement).toBe(trigger);
  });

  // ── Variant: default ──────────────────────────────────────────────────────

  it('does NOT show the AlertTriangle icon for the default variant', () => {
    render(<ConfirmDialog {...defaultProps} variant="default" />);
    // The AlertTriangle is inside a red-circle container only for danger variant
    const redCircle = document.querySelector('.bg-red-100');
    expect(redCircle).not.toBeInTheDocument();
  });

  // ── Variant: danger ───────────────────────────────────────────────────────

  it('shows a red warning icon container for the danger variant', () => {
    render(<ConfirmDialog {...defaultProps} variant="danger" />);
    const redCircleWrapper = document.querySelector('.bg-red-100');
    expect(redCircleWrapper).toBeInTheDocument();
  });

  it('autoFocuses the cancel button for danger variant', () => {
    render(<ConfirmDialog {...defaultProps} variant="danger" />);
    const cancelBtn = screen.getByText('Cancel').closest('button') as HTMLElement;
    expect(document.activeElement).toBe(cancelBtn);
  });

  // ── isConfirming — prevents duplicate submission (H6) ──────────────────────

  it('disables the confirm button while isConfirming is true', () => {
    render(<ConfirmDialog {...defaultProps} isConfirming />);
    expect(screen.getByText('Working…').closest('button')).toBeDisabled();
  });

  it('disables the cancel button while isConfirming is true', () => {
    render(<ConfirmDialog {...defaultProps} isConfirming />);
    expect(screen.getByText('Cancel').closest('button')).toBeDisabled();
  });

  it('disables the X close button while isConfirming is true', () => {
    render(<ConfirmDialog {...defaultProps} isConfirming />);
    expect(screen.getByLabelText('Close dialog')).toBeDisabled();
  });

  it('does not call onConfirm again when the confirm button is clicked while isConfirming is true', async () => {
    const onConfirm = vi.fn();
    render(<ConfirmDialog {...defaultProps} onConfirm={onConfirm} isConfirming />);
    await userEvent.click(screen.getByText('Working…'));
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it('does NOT call onCancel on Escape while isConfirming is true', async () => {
    const onCancel = vi.fn();
    render(<ConfirmDialog {...defaultProps} onCancel={onCancel} isConfirming />);
    await userEvent.keyboard('{Escape}');
    expect(onCancel).not.toHaveBeenCalled();
  });

  it('does NOT call onCancel when the backdrop is clicked while isConfirming is true', async () => {
    const onCancel = vi.fn();
    render(<ConfirmDialog {...defaultProps} onCancel={onCancel} isConfirming />);
    const backdrop = document.querySelector('[aria-hidden="true"]') as HTMLElement;
    await userEvent.click(backdrop);
    expect(onCancel).not.toHaveBeenCalled();
  });

  it('re-enables the confirm button and restores its label once isConfirming becomes false', () => {
    const { rerender } = render(<ConfirmDialog {...defaultProps} confirmLabel="Delete" isConfirming />);
    expect(screen.getByText('Working…').closest('button')).toBeDisabled();
    rerender(<ConfirmDialog {...defaultProps} confirmLabel="Delete" isConfirming={false} />);
    expect(screen.getByText('Delete').closest('button')).not.toBeDisabled();
  });
});

// ── useConfirmDialog hook ─────────────────────────────────────────────────────

import { renderHook, waitFor } from '@testing-library/react';

describe('useConfirmDialog', () => {
  it('starts with dialog closed', () => {
    const { result } = renderHook(() => useConfirmDialog());
    expect(result.current.dialogProps.isOpen).toBe(false);
  });

  it('opens the dialog when confirm() is called', async () => {
    const { result } = renderHook(() => useConfirmDialog());
    act(() => {
      result.current.confirm({ title: 'Really?', message: 'Are you sure?' });
    });
    await waitFor(() => expect(result.current.dialogProps.isOpen).toBe(true));
    expect(result.current.dialogProps.title).toBe('Really?');
    expect(result.current.dialogProps.message).toBe('Are you sure?');
  });

  it('resolves true and closes when handleConfirm is called', async () => {
    const { result } = renderHook(() => useConfirmDialog());
    let resolved: boolean | undefined;
    act(() => {
      result.current.confirm({ title: 'T', message: 'M' }).then(v => { resolved = v; });
    });
    await waitFor(() => expect(result.current.dialogProps.isOpen).toBe(true));
    act(() => { result.current.dialogProps.onConfirm(); });
    await waitFor(() => expect(result.current.dialogProps.isOpen).toBe(false));
    expect(resolved).toBe(true);
  });

  it('resolves false and closes when handleCancel is called', async () => {
    const { result } = renderHook(() => useConfirmDialog());
    let resolved: boolean | undefined;
    act(() => {
      result.current.confirm({ title: 'T', message: 'M' }).then(v => { resolved = v; });
    });
    await waitFor(() => expect(result.current.dialogProps.isOpen).toBe(true));
    act(() => { result.current.dialogProps.onCancel(); });
    await waitFor(() => expect(result.current.dialogProps.isOpen).toBe(false));
    expect(resolved).toBe(false);
  });

  it('passes through custom options (confirmLabel, variant)', async () => {
    const { result } = renderHook(() => useConfirmDialog());
    act(() => {
      result.current.confirm({
        title: 'Delete',
        message: 'Confirm delete',
        confirmLabel: 'Yes, delete',
        variant: 'danger',
      });
    });
    await waitFor(() => expect(result.current.dialogProps.title).toBe('Delete'));
    expect(result.current.dialogProps.confirmLabel).toBe('Yes, delete');
    expect(result.current.dialogProps.variant).toBe('danger');
  });
});
