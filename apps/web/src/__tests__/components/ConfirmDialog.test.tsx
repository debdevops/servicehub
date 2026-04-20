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
