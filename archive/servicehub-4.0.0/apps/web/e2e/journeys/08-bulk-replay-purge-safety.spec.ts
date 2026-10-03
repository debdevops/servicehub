/**
 * Suite E: Bulk Replay / Bulk Purge production-namespace safety (P0, no backend required)
 *
 * Proves the highest-risk mutating bulk operations cannot be started against a
 * production namespace through the normal UI flow: the Bulk Replay / Bulk Purge
 * buttons on the DLQ History page are disabled outright — the confirmation modal
 * never mounts — rather than merely rejecting after the fact. Azure only, mirroring
 * 07-demo-mutation-safety.spec.ts's scoping.
 *
 * There is no "preview namespace, operation proceeds" counterpart here: every demo
 * namespace (Azure/AWS/GCP) is seeded as environment 'prod' (mockProviders.ts), so
 * Demo Mode has no non-production namespace to exercise the allowed path against.
 */
import { test, expect } from '../fixtures/base';

const NAMESPACE = 'demo-azure-contoso-prod';

test.describe('Suite E — Bulk Replay/Purge production guard (azure)', () => {
  test('Bulk Replay is disabled for a production namespace and its modal never opens', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/dlq-history?namespace=${NAMESPACE}`);

    const button = page.getByRole('button', { name: 'Bulk Replay' });
    await expect(button).toBeDisabled();
    await expect(button).toHaveAttribute('title', 'Bulk replay is blocked for production namespaces');

    await expect(page.getByRole('dialog', { name: 'Bulk replay preview' })).not.toBeVisible();
  });

  test('Bulk Purge is disabled for a production namespace and its modal never opens', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/dlq-history?namespace=${NAMESPACE}`);

    const button = page.getByRole('button', { name: 'Bulk Purge' });
    await expect(button).toBeDisabled();
    await expect(button).toHaveAttribute('title', 'Bulk purge is blocked for production namespaces');

    await expect(page.getByRole('dialog', { name: 'Bulk purge preview' })).not.toBeVisible();
  });
});
