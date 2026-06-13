import { test as base, request as baseRequest } from '@playwright/test';

export const SIMULATOR_API = 'http://localhost:5200';

export const test = base.extend<{ simulatorReady: void }>({
  simulatorReady: [async ({ page }, use) => {
    // Suppress the auto-launch guided tour so it doesn't intercept pointer events
    await page.addInitScript(() => {
      localStorage.setItem('servicehub_tour_completed', 'true');
      // Also dismiss the v3.1.0 HKDF notice on ConnectPage
      localStorage.setItem('servicehub_v310_hkdf_notice_dismissed', 'true');
    });

    // Reset simulator to clean seeded state before each test
    const ctx = await baseRequest.newContext();
    try {
      await ctx.post(`${SIMULATOR_API}/api/v1/simulator/reset`);
    } catch {
      // If simulator not running, tests that need it will fail naturally
    } finally {
      await ctx.dispose();
    }
    await use();
  }, { auto: true }],
});

export const expect = base.expect;
