/**
 * Journey 03: Cloud Bridge page
 *
 * These tests verify the Cloud Bridge page renders without crashing.
 * Tests that need real provider data skip gracefully if the simulator is not running.
 *
 * NOTE: Requires the Vite dev server only (no API needed for basic render tests).
 *       Full provider data tests require the Simulator API on http://localhost:5200.
 */
import { test, expect } from '../fixtures/simulator';

test('cloud bridge page renders without crashing', async ({ page }) => {
  await page.goto('/cloud-bridge');
  // Page header must appear — text from CloudBridgePage.tsx
  await expect(page.getByText(/Cloud Bridge/i)).toBeVisible({ timeout: 10_000 });
  // Error boundary must NOT be triggered
  await expect(page.getByText(/Something went wrong/i)).not.toBeVisible();
});

test('cloud bridge shows AWS and GCP providers when simulator is running', async ({ page, request }) => {
  // Gracefully skip if simulator is not running
  let simulatorRunning = false;
  try {
    const res = await request.get('http://localhost:5200/api/v1/simulator/status');
    simulatorRunning = res.ok();
  } catch {
    /* simulator not running in this environment */
  }

  await page.goto('/cloud-bridge');
  if (simulatorRunning) {
    const mainContent = page.locator('main');
    const providerCards = mainContent.locator('div.rounded-lg.px-4.py-2.text-sm.font-medium.border');

    await expect(mainContent.getByRole('heading', { name: 'Provider Status' })).toBeVisible({ timeout: 10_000 });
    await expect(providerCards.filter({ hasText: /AWS SQS \/ SNS/ }).first()).toBeVisible({ timeout: 10_000 });
    await expect(providerCards.filter({ hasText: /GCP Pub\/Sub/ }).first()).toBeVisible({ timeout: 10_000 });
  } else {
    // Without simulator, page should still render gracefully
    await expect(page.getByText(/Cloud Bridge/i)).toBeVisible();
  }
});
