import { defineConfig } from '@playwright/test'

/**
 * Browser-level checks (unit 6.6): axe with colour contrast ON, and a real-keyboard walk of the core loop. The unit suite runs
 * axe too, but jsdom has no colours or layout — this is where contrast and focus order are actually measured.
 *
 * Everything runs in DEMO MODE (`/demo/azure`, unit 6.5): the client answers from made-up data, so there is no API, no cloud and
 * no flakiness from live traffic. Served by `vite preview` over the production build, so it is the bundle people get.
 *
 * Locally, `PW_CHANNEL=chrome npm run e2e` uses the Chrome you already have; CI installs Chromium.
 */
const port = 4173
export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.e2e.ts',
  fullyParallel: true,
  reportSlowTests: null,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: `http://localhost:${port}`,
    // Gate 6's screen size.
    viewport: { width: 1366, height: 768 },
    channel: process.env.PW_CHANNEL || undefined,
    trace: 'retain-on-failure',
  },
  webServer: {
    command: `npm run build && npx vite preview --port ${port} --strictPort`,
    url: `http://localhost:${port}`,
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
  },
})
