import { test as base, expect as baseExpect } from '@playwright/test';

export const test = base.extend<{ tourSuppressed: void; noConsoleErrors: void }>({
  tourSuppressed: [async ({ page }, use) => {
    // Suppress the auto-launch guided tour so it doesn't intercept pointer events
    await page.addInitScript(() => {
      localStorage.setItem('servicehub_tour_completed', 'true');
      // Also dismiss the v3.1.0 HKDF notice on ConnectPage
      localStorage.setItem('servicehub_v310_hkdf_notice_dismissed', 'true');
    });
    await use();
  }, { auto: true }],

  // Opt-in (not auto) so it doesn't change behavior for existing specs that don't request it.
  // Fails the test if a console error or uncaught page error occurs — the Demo Mode
  // suites' "no console errors" requirement, enforced once here instead of per-spec.
  noConsoleErrors: [async ({ page }, use) => {
    const errors: string[] = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error') errors.push(msg.text());
    });
    page.on('pageerror', (err) => {
      errors.push(err.message);
    });
    await use();
    baseExpect(errors, `Console/page errors occurred:\n${errors.join('\n')}`).toEqual([]);
  }, {}],
});

export const expect = base.expect;
