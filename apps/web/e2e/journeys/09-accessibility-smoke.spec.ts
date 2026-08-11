/**
 * Suite F: Accessibility smoke check (P1, no backend required)
 *
 * Lightweight axe-core scan of the highest-value ServiceHub surfaces, to catch
 * obvious WCAG regressions. Not a full accessibility audit — two flows only:
 *
 *   1. DLQ History page (priority #1 from the RC1 task).
 *   2. Auto-Replay Rules builder modal (substituted for the Bulk Replay/Purge
 *      preview modal, priority #2, which cannot be reached in Demo Mode: every
 *      demo namespace is seeded as environment 'prod' — see
 *      08-bulk-replay-purge-safety.spec.ts — so the Bulk Replay/Bulk Purge
 *      buttons are disabled outright and their confirmation modal never mounts).
 *
 * Scans exclude the shared app chrome (header/sidebar/footer, present on every
 * page) so this smoke test gates on the page/modal content it names, not on
 * pre-existing, app-wide chrome issues out of scope for this check.
 */
import AxeBuilder from '@axe-core/playwright';
import { test, expect } from '../fixtures/base';

const NAMESPACE = 'demo-azure-contoso-prod';
const CHROME_EXCLUDES = [
  'header',
  'div[data-tour="sidebar"]',
  'div[data-tour="quick-access"]',
  'div[data-testid="demo-mode-banner"]',
  'footer',
];

test.describe('Suite F — Accessibility smoke (azure)', () => {
  test('DLQ History page has no WCAG2 A/AA violations', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/dlq-history?namespace=${NAMESPACE}`);
    await expect(page.getByRole('heading', { name: 'DLQ Intelligence' })).toBeVisible();

    const results = await CHROME_EXCLUDES.reduce(
      (builder, selector) => builder.exclude(selector),
      new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']),
    ).analyze();

    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });

  test('Auto-Replay Rules builder modal has no WCAG2 A/AA violations', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto('/demo/azure/rules');

    await page.getByRole('button', { name: 'Create Rule' }).first().click();
    const dialog = page.getByRole('dialog', { name: 'Create Auto-Replay Rule' });
    await expect(dialog).toBeVisible();

    const results = await CHROME_EXCLUDES.reduce(
      (builder, selector) => builder.exclude(selector),
      new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']),
    ).analyze();

    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });
});
