import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { KeyboardShortcutsOverlay } from '@/components/KeyboardShortcutsOverlay';

describe('KeyboardShortcutsOverlay', () => {
  const onClose = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── Visibility ─────────────────────────────────────────────────────────────

  it('renders nothing when open is false', () => {
    const { container } = render(<KeyboardShortcutsOverlay open={false} onClose={onClose} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders the dialog when open is true', () => {
    render(<KeyboardShortcutsOverlay open={true} onClose={onClose} />);
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  // ── Accessibility ──────────────────────────────────────────────────────────

  it('has role="dialog" and aria-label="Keyboard shortcuts"', () => {
    render(<KeyboardShortcutsOverlay open={true} onClose={onClose} />);
    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveAttribute('aria-label', 'Keyboard shortcuts');
  });

  it('has aria-modal="true"', () => {
    render(<KeyboardShortcutsOverlay open={true} onClose={onClose} />);
    expect(screen.getByRole('dialog')).toHaveAttribute('aria-modal', 'true');
  });

  // ── Content ────────────────────────────────────────────────────────────────

  it('renders all shortcut group headings', () => {
    render(<KeyboardShortcutsOverlay open={true} onClose={onClose} />);
    expect(screen.getByText('Navigation')).toBeInTheDocument();
    expect(screen.getByText('Message List')).toBeInTheDocument();
    expect(screen.getByText('Global')).toBeInTheDocument();
  });

  it('renders shortcut descriptions', () => {
    render(<KeyboardShortcutsOverlay open={true} onClose={onClose} />);
    expect(screen.getByText('Open command palette')).toBeInTheDocument();
    expect(screen.getByText('Next message')).toBeInTheDocument();
    expect(screen.getByText('Previous message')).toBeInTheDocument();
    expect(screen.getByText('Open message detail')).toBeInTheDocument();
  });

  it('renders keyboard key labels', () => {
    render(<KeyboardShortcutsOverlay open={true} onClose={onClose} />);
    const allKbd = Array.from(document.querySelectorAll('kbd'));
    const labels = allKbd.map(el => el.textContent);
    expect(labels).toContain('J');
    expect(labels).toContain('K');
    expect(labels).toContain('⌘');
  });

  // ── Interactions ───────────────────────────────────────────────────────────

  it('calls onClose when the close button is clicked', async () => {
    render(<KeyboardShortcutsOverlay open={true} onClose={onClose} />);
    await userEvent.click(screen.getByLabelText('Close'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('calls onClose when Escape is pressed', async () => {
    render(<KeyboardShortcutsOverlay open={true} onClose={onClose} />);
    await userEvent.keyboard('{Escape}');
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('calls onClose when the backdrop is clicked', async () => {
    render(<KeyboardShortcutsOverlay open={true} onClose={onClose} />);
    // Backdrop is the absolute-positioned div (first child inside the dialog)
    const backdrop = document.querySelector('.absolute.inset-0') as HTMLElement;
    await userEvent.click(backdrop);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('does NOT call onClose on Escape when overlay is closed', () => {
    render(<KeyboardShortcutsOverlay open={false} onClose={onClose} />);
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).not.toHaveBeenCalled();
  });

  // ── Focus management ───────────────────────────────────────────────────────
  // End-to-end proof that a real dialog — not just the useFocusTrap hook in isolation —
  // keeps keyboard focus inside itself and hands it back on close.

  describe('keyboard accessibility', () => {
    function renderWithTrigger() {
      const trigger = document.createElement('button');
      trigger.textContent = 'Open shortcuts';
      document.body.appendChild(trigger);
      trigger.focus();
      const outside = document.createElement('button');
      outside.textContent = 'Behind the dialog';
      document.body.appendChild(outside);
      return { trigger, outside };
    }

    it('moves focus into the dialog when it opens', () => {
      renderWithTrigger();
      render(<KeyboardShortcutsOverlay open onClose={onClose} />);

      const dialog = screen.getByRole('dialog');
      expect(dialog.contains(document.activeElement)).toBe(true);
    });

    it('keeps Tab inside the dialog instead of reaching the page behind it', async () => {
      const { outside } = renderWithTrigger();
      render(<KeyboardShortcutsOverlay open onClose={onClose} />);
      const dialog = screen.getByRole('dialog');

      for (let i = 0; i < 6; i++) {
        await userEvent.tab();
        expect(document.activeElement).not.toBe(outside);
        expect(dialog.contains(document.activeElement)).toBe(true);
      }
    });

    it('keeps Shift+Tab inside the dialog', async () => {
      const { outside } = renderWithTrigger();
      render(<KeyboardShortcutsOverlay open onClose={onClose} />);
      const dialog = screen.getByRole('dialog');

      for (let i = 0; i < 6; i++) {
        await userEvent.tab({ shift: true });
        expect(document.activeElement).not.toBe(outside);
        expect(dialog.contains(document.activeElement)).toBe(true);
      }
    });

    it('restores focus to the triggering element when it closes', () => {
      const { trigger } = renderWithTrigger();
      const { rerender } = render(<KeyboardShortcutsOverlay open onClose={onClose} />);
      expect(document.activeElement).not.toBe(trigger);

      rerender(<KeyboardShortcutsOverlay open={false} onClose={onClose} />);
      expect(document.activeElement).toBe(trigger);
    });
  });
});
