import { test as base } from '@playwright/test';

export const test = base.extend<{ tourSuppressed: void }>({
  tourSuppressed: [async ({ page }, use) => {
    // Suppress the auto-launch guided tour so it doesn't intercept pointer events
    await page.addInitScript(() => {
      localStorage.setItem('servicehub_tour_completed', 'true');
      // Also dismiss the v3.1.0 HKDF notice on ConnectPage
      localStorage.setItem('servicehub_v310_hkdf_notice_dismissed', 'true');
    });
    await use();
  }, { auto: true }],
});

export const expect = base.expect;
