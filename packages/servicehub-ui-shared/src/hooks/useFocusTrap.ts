import { useEffect, useRef } from 'react';

/**
 * Selector for elements that can hold keyboard focus. `[tabindex="-1"]` is deliberately excluded:
 * such elements are focusable programmatically but must not appear in the Tab cycle.
 */
const FOCUSABLE_SELECTOR = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(',');

function isVisible(el: HTMLElement): boolean {
  // checkVisibility() understands display:none, visibility:hidden, content-visibility and the
  // hidden attribute in one call. Deliberately NOT offsetParent/getClientRects: those depend on
  // layout, which jsdom does not perform, so a layout-based filter would classify every element
  // in a test environment as hidden and silently disable the trap under test.
  if (typeof el.checkVisibility === 'function') {
    return el.checkVisibility();
  }
  return !el.hasAttribute('hidden') && el.closest('[hidden]') === null;
}

function getFocusableElements(container: HTMLElement): HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)).filter(
    (el) =>
      !el.hasAttribute('disabled') &&
      el.getAttribute('aria-hidden') !== 'true' &&
      isVisible(el),
  );
}

/**
 * Traps keyboard focus inside an open dialog and restores it on close.
 *
 * Every dialog in ServiceHub already sets `role="dialog"`/`aria-modal="true"`, labels itself with
 * `aria-labelledby`/`aria-describedby`, and closes on Escape. What none of them did was manage
 * focus: Tab walked straight out of the dialog into the page behind it, and closing a dialog
 * dropped focus back to `<body>`, so a keyboard user lost their place entirely. `aria-modal`
 * tells a screen reader the background is inert; it does not make it so for the Tab key.
 *
 * This satisfies WCAG 2.4.3 (Focus Order), which the existing markup did not — WCAG 2.1.2 (No
 * Keyboard Trap) was already fine, because focus could always leave.
 *
 * Usage — one line per dialog, no restructuring:
 *
 * ```tsx
 * const dialogRef = useFocusTrap<HTMLDivElement>(isOpen);
 * ...
 * <div ref={dialogRef} role="dialog" aria-modal="true">
 * ```
 *
 * Deliberately does NOT handle Escape: each dialog already owns that, and several disarm it
 * conditionally (`ConfirmDialog` while a confirmation is in flight). Duplicating it here would
 * silently override those decisions.
 *
 * @param isOpen Whether the dialog is currently open.
 * @returns A ref to attach to the dialog container element.
 */
export function useFocusTrap<T extends HTMLElement = HTMLElement>(isOpen: boolean) {
  const containerRef = useRef<T | null>(null);
  const previouslyFocusedRef = useRef<HTMLElement | null>(null);
  const wasOpenRef = useRef(false);

  // Captured during render, on the closed -> open transition, rather than in the effect below.
  // React applies `autoFocus` during commit, which happens BEFORE effects run — so by the time
  // the effect executes, a dialog that autofocuses a control (ConfirmDialog focuses Cancel for
  // danger variants) has already moved focus inside itself. Reading activeElement there would
  // capture that inner control as the "previously focused" element and restore focus to a node
  // that no longer exists on close, silently dropping focus to <body>. Render runs before
  // commit, so activeElement is still the real trigger here.
  if (isOpen && !wasOpenRef.current) {
    previouslyFocusedRef.current = document.activeElement as HTMLElement | null;
  }
  wasOpenRef.current = isOpen;

  useEffect(() => {
    if (!isOpen) return;

    const container = containerRef.current;
    if (!container) return;

    // Move focus into the dialog. Respect an existing autoFocus target if the dialog already
    // placed focus deliberately — ConfirmDialog focuses Cancel for danger variants specifically
    // to make accidental confirmation harder, and that intent must win over "focus the first
    // thing".
    if (!container.contains(document.activeElement)) {
      const focusable = getFocusableElements(container);
      if (focusable.length > 0) {
        focusable[0].focus();
      } else {
        // A dialog with nothing focusable still needs to receive focus, or the screen reader
        // stays on the page behind it.
        container.setAttribute('tabindex', '-1');
        container.focus();
      }
    }

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Tab') return;

      const focusable = getFocusableElements(container);
      if (focusable.length === 0) {
        // Nothing to cycle between — keep focus on the container rather than letting Tab
        // escape to the page behind.
        event.preventDefault();
        return;
      }

      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      const active = document.activeElement;

      if (event.shiftKey) {
        if (active === first || !container.contains(active)) {
          event.preventDefault();
          last.focus();
        }
        return;
      }

      if (active === last || !container.contains(active)) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener('keydown', handleKeyDown, true);

    return () => {
      document.removeEventListener('keydown', handleKeyDown, true);

      // Restore focus to whatever opened the dialog. The guard matters: if that element was
      // removed from the DOM while the dialog was open (a row deleted by the very action the
      // dialog confirmed), focusing it does nothing and focus would silently fall to <body>.
      const previous = previouslyFocusedRef.current;
      if (previous && document.contains(previous)) {
        previous.focus();
      }
    };
  }, [isOpen]);

  return containerRef;
}
