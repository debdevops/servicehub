/**
 * Suite: Recovery Ageing Report (Demo Mode, Azure)
 *
 * The falsifiable form of "nothing is silently lost" (roadmap §7.2): a stuck, non-terminal
 * entry from an unrelated small incident stays on this report until it resolves or expires.
 */
import { test, expect } from '../fixtures/base';

const NAMESPACE = 'demo-azure-contoso-prod';

test.describe('Suite — Recovery Ageing Report (azure)', () => {
  test('an aged, non-terminal entry appears on the ageing report, flagged past the default threshold', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/recovery/ageing?namespace=${NAMESPACE}`);

    await expect(page.getByRole('heading', { name: 'Recovery Ageing Report' })).toBeVisible();
    await expect(page.getByText('payments-reconciliation')).toBeVisible();
    await expect(page.getByText('ExecutionUnknown')).toBeVisible();
    await expect(page.getByText('· flagged')).toBeVisible();
  });

  test('the aged entry links through to its operation, which is not the curated 214-message story', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/recovery/ageing?namespace=${NAMESPACE}`);

    await page.getByRole('link', { name: 'View operation' }).click();

    await expect(page.getByRole('heading', { name: /Replay — entity=payments-reconciliation/ })).toBeVisible();
    // Exactly the one stuck entry — not the unrelated 214-entry story's rollup.
    await expect(page.getByText('1 ExecutionUnknown')).toBeVisible();
  });

  test('the curated 214-message operation itself has no open entries left on the ageing report', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/recovery/ageing?namespace=${NAMESPACE}`);

    // Every one of the 214-message story's entries reaches a terminal state — none of its
    // target entity should appear here.
    await expect(page.getByText('orders-processing')).not.toBeVisible();
  });
});
