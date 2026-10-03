/**
 * Suite A: Queue/Topic Browser & Message Operations (P0, no backend required)
 *
 * Validates the core message-inspection loop across all three providers' default
 * demo entities (Azure/AWS: queue, GCP: topic+subscription) — DLQ tab, filters,
 * AI Findings, message selection, Live Tail, and cross-page navigation that must
 * stay inside the /demo/{cloud} route tree.
 */
import { test, expect } from '../fixtures/base';

interface ProviderConfig {
  cloud: 'azure' | 'aws' | 'gcp';
  // Matches the fallback pattern already proven in 01-welcome-and-demo.spec.ts — the
  // "Demo Mode" banner text doesn't literally say "<Provider> Demo", so both specs key
  // off the seeded org name instead.
  demoBanner: RegExp;
  activeTabLabel: RegExp;
  deadletterTabLabel: RegExp;
  /** supportsRepeatablePeek in DEMO_CAPABILITIES — gates the Live Tail button. */
  hasLiveTail: boolean;
  /** supportsScheduledMessages AND the default entity being a queue — gates the Scheduled button. */
  hasScheduled: boolean;
}

const PROVIDERS: ProviderConfig[] = [
  {
    cloud: 'azure',
    demoBanner: /Contoso/i,
    activeTabLabel: /^Active/i,
    deadletterTabLabel: /Dead-Letter/i,
    hasLiveTail: true,
    hasScheduled: true,
  },
  {
    cloud: 'aws',
    demoBanner: /AcmeRetail/i,
    activeTabLabel: /^Queue/i,
    deadletterTabLabel: /^DLQ/i,
    hasLiveTail: false,
    hasScheduled: false,
  },
  {
    cloud: 'gcp',
    demoBanner: /MedStream/i,
    activeTabLabel: /^Active/i,
    deadletterTabLabel: /Dead-Letter/i,
    // GCP Pub/Sub's pull-then-release peek still counts as a delivery attempt toward
    // MaxDeliveryAttempts, so it no longer declares supportsRepeatablePeek — same as AWS.
    hasLiveTail: false,
    hasScheduled: false,
  },
];

for (const cfg of PROVIDERS) {
  test.describe(`Suite A — Queue/Topic Browser (${cfg.cloud})`, () => {
    test.beforeEach(async ({ page }) => {
      // The index route under /demo/{cloud} redirects to messages with a pre-seeded
      // entity selected (orders-queue / order-processing / lab-results+results-router-sub).
      await page.goto(`/demo/${cfg.cloud}`);
      await expect(page.getByText(cfg.demoBanner).first()).toBeVisible({ timeout: 10_000 });
    });

    test('loads the default entity with queue/topic navigation available', async ({ page, noConsoleErrors }) => {
      void noConsoleErrors;
      // Sidebar entity tree (queue/topic/subscription list) is present.
      await expect(page.getByRole('navigation').or(page.locator('aside')).first()).toBeVisible();
      await expect(page.locator('main').getByRole('button', { name: cfg.activeTabLabel })).toBeVisible();
      await expect(page.locator('main').getByRole('button', { name: cfg.deadletterTabLabel })).toBeVisible();
    });

    test('switches to the Dead-Letter tab', async ({ page, noConsoleErrors }) => {
      void noConsoleErrors;
      await page.locator('main').getByRole('button', { name: cfg.deadletterTabLabel }).click();
      await expect(page).toHaveURL(/queueType=deadletter/);
    });

    test('filters messages by status', async ({ page, noConsoleErrors }) => {
      void noConsoleErrors;
      const filterButton = page.getByRole('button', { name: 'Filter messages by status' });
      await filterButton.click();
      await expect(filterButton).toHaveAttribute('aria-expanded', 'true');

      await page.getByRole('button', { name: 'Dead-Letter', exact: true }).click();

      // Dropdown closes after a selection, and the active-filter indicator appears on the button.
      await expect(filterButton).toHaveAttribute('aria-expanded', 'false');
    });

    test('opens and closes the AI Findings dropdown', async ({ page, noConsoleErrors }) => {
      void noConsoleErrors;
      await page.getByRole('button', { name: 'AI Findings' }).click();
      const dialog = page.getByRole('dialog', { name: 'Active AI Patterns' });
      await expect(dialog).toBeVisible();
      await dialog.getByRole('button', { name: 'Close' }).click();
      await expect(dialog).not.toBeVisible();
    });

    test('selecting a message populates the detail panel', async ({ page, noConsoleErrors }) => {
      void noConsoleErrors;
      await expect(page.getByRole('heading', { name: 'No Message Selected' })).toBeVisible();

      // MessageCard rows have no accessible role/name (tracked as a follow-up); scope tightly
      // to the message list column and take the first rendered row.
      const firstMessage = page.locator('main').locator('div.cursor-pointer').first();
      await firstMessage.click();

      await expect(page.getByRole('heading', { name: 'No Message Selected' })).not.toBeVisible();
      await expect(page.getByRole('button', { name: 'Properties' })).toBeVisible();
    });

    if (cfg.hasLiveTail) {
      test('opens and closes Live Tail', async ({ page, noConsoleErrors }) => {
        void noConsoleErrors;
        await page.getByRole('button', { name: 'Open Live Tail' }).click();
        await expect(page.getByRole('heading', { name: 'Live Tail' })).toBeVisible();
        await page.getByRole('button', { name: 'Close Live Tail' }).click();
        await expect(page.getByRole('heading', { name: 'Live Tail' })).not.toBeVisible();
      });
    }

    if (cfg.hasScheduled) {
      test('navigates to Scheduled Messages and stays in Demo Mode', async ({ page, noConsoleErrors }) => {
        void noConsoleErrors;
        await page.getByRole('button', { name: 'View scheduled messages for this queue' }).click();
        await expect(page).toHaveURL(new RegExp(`/demo/${cfg.cloud}/scheduled\\?`));
      });
    }

    test('navigates to DLQ History from the Dead-Letter tab and stays in Demo Mode', async ({ page, noConsoleErrors }) => {
      void noConsoleErrors;
      await page.locator('main').getByRole('button', { name: cfg.deadletterTabLabel }).click();
      await page.getByRole('button', { name: 'View DLQ history for this entity' }).click();
      await expect(page).toHaveURL(new RegExp(`/demo/${cfg.cloud}/dlq-history\\?`));
    });
  });
}
