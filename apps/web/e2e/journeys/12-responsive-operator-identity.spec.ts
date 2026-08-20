/**
 * Suite L: Responsive operator identity (P1, no backend required)
 *
 * Blueprint Gap 3 — an operator must never be one click from Replay/Purge without an
 * on-screen statement of which cloud, namespace, environment, and entity they're acting
 * on. Verifies the connection chip and the Messages page's identity header stay visible
 * (not hidden behind a breakpoint) at laptop-sized viewports, that there's no horizontal
 * overflow at those widths, and that browser Back restores the correct identity.
 */
import { test, expect } from '../fixtures/base';

const VIEWPORTS = [
  { name: '900px', width: 900, height: 800 },
  { name: '1100px', width: 1100, height: 800 },
  { name: '1400px', width: 1400, height: 900 },
];

test.describe('Suite L — Responsive operator identity', () => {
  for (const viewport of VIEWPORTS) {
    test(`shows provider, namespace, and environment at ${viewport.name} without expanding any panel`, async ({ page, noConsoleErrors }) => {
      void noConsoleErrors;
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      await page.goto('/demo/azure');
      await expect(page.getByText(/Contoso/i).first()).toBeVisible({ timeout: 10_000 });

      const connectionChip = page.locator('[data-tour="header-connection"]');
      await expect(connectionChip).toBeVisible();
      await expect(connectionChip.getByText('PROD')).toBeVisible();

      // The Messages page's own identity header (Gap 3 + Gap 10's missing <h1>).
      const heading = page.getByRole('heading', { level: 1 });
      await expect(heading).toBeVisible();
      await expect(heading).not.toHaveText('');
    });

    test(`has no horizontal overflow at ${viewport.name}`, async ({ page, noConsoleErrors }) => {
      void noConsoleErrors;
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      await page.goto('/demo/azure');
      await expect(page.getByText(/Contoso/i).first()).toBeVisible({ timeout: 10_000 });

      const overflow = await page.evaluate(() => document.body.scrollWidth - window.innerWidth);
      expect(overflow).toBeLessThanOrEqual(1); // 1px tolerance for sub-pixel rounding
    });
  }

  test('browser Back restores the correct entity identity after switching queues', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    const namespaceId = 'demo-aws-acme-prod';
    await page.goto(`/demo/aws/messages?namespace=${namespaceId}&queue=order-processing`);
    await expect(page.getByText(/AcmeRetail/i).first()).toBeVisible({ timeout: 10_000 });
    const heading = page.getByRole('heading', { level: 1 });
    await expect(heading).toHaveText(/order-processing/);

    await page.goto(`/demo/aws/messages?namespace=${namespaceId}&queue=payment-gateway-events`);
    await expect(heading).toHaveText(/payment-gateway-events/);

    await page.goBack();
    await expect(heading).toHaveText(/order-processing/);
  });
});
