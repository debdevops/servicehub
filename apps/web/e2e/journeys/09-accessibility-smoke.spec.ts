/**
 * Suite F: Accessibility smoke check (P1, no backend required)
 *
 * axe-core scan of ServiceHub's highest-value surfaces, to catch obvious WCAG regressions.
 * Not a full accessibility audit, but no longer excludes the app-wide chrome (header, sidebar,
 * Quick Access, footer) the way earlier passes of this suite did — every one of those regions is
 * present on every page below and is scanned along with the page content, not carved out.
 *
 * Destructive confirmation dialogs (message Replay/Delete) are not reachable here: every demo
 * namespace is seeded as environment 'prod' (see 08-bulk-replay-purge-safety.spec.ts), so those
 * actions are disabled outright and their confirmation modal never mounts in Demo Mode. The
 * shared `ConfirmDialog` component they use is covered by unit tests
 * (ConfirmDialog.test.tsx / useFocusTrap.test.tsx) instead — role="alertdialog", aria-modal,
 * aria-labelledby/aria-describedby, Escape-to-cancel, and a full keyboard focus trap with
 * wrap-around and focus restoration on close.
 */
import AxeBuilder from '@axe-core/playwright';
import { test, expect } from '../fixtures/base';

const NAMESPACE = 'demo-azure-contoso-prod';

async function scan(page: import('@playwright/test').Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
  expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
}

test.describe('Suite F — Accessibility smoke (azure)', () => {
  test('DLQ History page has no WCAG2 A/AA violations', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/dlq-history?namespace=${NAMESPACE}`);
    await expect(page.getByRole('heading', { name: 'DLQ Intelligence' })).toBeVisible();
    await scan(page);
  });

  test('Active Messages page has no WCAG2 A/AA violations', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/messages?namespace=${NAMESPACE}&tab=active`);
    await page.waitForSelector('h1, h2');
    await scan(page);
  });

  test('Dead-Letter tab has no WCAG2 A/AA violations', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/messages?namespace=${NAMESPACE}&tab=deadletter`);
    await page.waitForSelector('h1, h2');
    await scan(page);
  });

  test('Live Tail page has no WCAG2 A/AA violations', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/live-tail?namespace=${NAMESPACE}`);
    await page.waitForSelector('h1, h2');
    await scan(page);
  });

  test('Failure Signatures page has no WCAG2 A/AA violations', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/signatures?namespace=${NAMESPACE}`);
    await page.waitForSelector('h1, h2');
    await scan(page);
  });

  test('Recovery Evidence ledger has no WCAG2 A/AA violations', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto('/demo/azure/recovery');
    await page.waitForSelector('h1, h2');
    await scan(page);
  });

  test('Fleet Health page has no WCAG2 A/AA violations', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto('/demo/azure/fleet');
    await page.waitForSelector('h1, h2');
    await scan(page);
  });

  test('Auto-Replay Rules page has no WCAG2 A/AA violations', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto('/demo/azure/rules');
    await page.waitForSelector('h1, h2');
    await scan(page);
  });

  test('Auto-Replay Rules builder modal has no WCAG2 A/AA violations', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto('/demo/azure/rules');

    await page.getByRole('button', { name: 'Create Rule' }).first().click();
    const dialog = page.getByRole('dialog', { name: 'Create Auto-Replay Rule' });
    await expect(dialog).toBeVisible();

    await scan(page);
  });
});
