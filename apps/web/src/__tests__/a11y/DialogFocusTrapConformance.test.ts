import { describe, it, expect } from 'vitest';

/**
 * Accessibility conformance gate for modal dialogs.
 *
 * `aria-modal="true"` tells a screen reader the background is inert. It does not make it inert
 * for the Tab key — that requires a real focus trap. A dialog that declares the attribute
 * without wiring `useFocusTrap` is therefore claiming an accessibility guarantee it does not
 * deliver, which is exactly the regression this test exists to catch: the failure mode is a new
 * dialog copied from an existing one with the `ref` left off, which no per-dialog behavioural
 * test would notice because no such test would exist yet for that new dialog.
 *
 * The behavioural proof that the trap itself works (Tab cycling, Shift+Tab, initial focus,
 * restoration on close) lives in `useFocusTrap.test.tsx` and `ConfirmDialog.test.tsx`. This test
 * proves every dialog is actually connected to it.
 */

// Vite's glob import rather than fs — apps/web deliberately carries no Node type definitions,
// and this keeps the scan running against the same module graph the app itself builds from.
// The negated pattern matters: this file lives inside src/__tests__/a11y, so sibling test
// directories resolve to "../components/..." — one level up, without "__tests__" in the key —
// and a substring filter would let every *.test.tsx through as if it were a product component.
const sources = import.meta.glob(['../../**/*.tsx', '!../../__tests__/**'], {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const modalComponents = Object.entries(sources)
  .filter(([path, source]) => !/\.(test|spec)\.tsx$/.test(path) && source.includes('aria-modal'))
  .map(([path, source]) => ({ name: path.replace('../../', ''), source }))
  .sort((a, b) => a.name.localeCompare(b.name));

describe('modal dialog focus-trap conformance', () => {
  it('finds the modal dialogs to check', () => {
    // A collection bug that silently matched nothing would make every assertion below vacuous.
    expect(modalComponents.length).toBeGreaterThanOrEqual(17);
  });

  it.each(modalComponents.map((c) => c.name))('%s uses useFocusTrap', (name) => {
    const { source } = modalComponents.find((c) => c.name === name)!;
    expect(source).toContain('useFocusTrap');
  });

  it.each(modalComponents.map((c) => c.name))(
    '%s attaches a focus-trap ref to every element carrying aria-modal',
    (name) => {
      const { source } = modalComponents.find((c) => c.name === name)!;

      // A file may host more than one dialog (ScheduledMessagesPage has two), each with its own
      // trap — collect them all rather than assuming a single ref name.
      const refNames = [...source.matchAll(/const\s+(\w+)\s*=\s*useFocusTrap/g)].map((m) => m[1]);
      expect(refNames.length, `${name} calls useFocusTrap and keeps its ref`).toBeGreaterThan(0);

      // The ref must reach the DOM on the element that declares aria-modal itself. Attaching it
      // to an inner wrapper would leave part of the dialog outside the trapped subtree.
      const modalElements = source.match(/<[a-zA-Z][^>]*?aria-modal[^>]*?>/gs) ?? [];
      expect(modalElements.length, `${name} declares aria-modal`).toBeGreaterThan(0);

      for (const element of modalElements) {
        expect(
          refNames.some((ref) => element.includes(`ref={${ref}}`)),
          `${name} has an aria-modal element with no focus-trap ref attached`,
        ).toBe(true);
      }
    },
  );

  it.each(modalComponents.map((c) => c.name))(
    '%s passes an explicit open-state argument to useFocusTrap',
    (name) => {
      const { source } = modalComponents.find((c) => c.name === name)!;
      const args = [...source.matchAll(/useFocusTrap(?:<[^>]*>)?\(([^)]*)\)/g)].map((m) =>
        m[1].trim(),
      );
      expect(args.length, `${name} calls useFocusTrap`).toBeGreaterThan(0);

      // A literal `true` is correct only for a dialog the parent mounts conditionally, which is
      // the dominant pattern here; what must never happen is an empty/undefined argument, which
      // would leave the trap permanently disarmed while the dialog still claims aria-modal.
      for (const arg of args) {
        expect(arg.length, `${name} passes no open-state argument to useFocusTrap`).toBeGreaterThan(0);
        expect(arg).not.toBe('false');
        expect(arg).not.toBe('undefined');
      }
    },
  );
});
