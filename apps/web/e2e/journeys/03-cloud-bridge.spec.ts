/**
 * Journey 03: Cloud Bridge page
 *
 * These tests verify the Cloud Bridge page renders without crashing.
 *
 * NOTE: Requires the Vite dev server only (no API needed).
 */
import { test, expect } from '../fixtures/base';

test('cloud bridge page renders without crashing', async ({ page }) => {
  await page.goto('/cloud-bridge');
  // Page header must appear — text from CloudBridgePage.tsx
  await expect(page.getByText(/Cloud Bridge/i)).toBeVisible({ timeout: 10_000 });
  // Error boundary must NOT be triggered
  await expect(page.getByText(/Something went wrong/i)).not.toBeVisible();
});
