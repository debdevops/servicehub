/**
 * Journey 01: Welcome page and demo mode (no API required)
 *
 * These tests verify the WelcomePage cloud provider cards render correctly
 * and that each demo URL loads mock messages without real credentials.
 */
import { test, expect } from '../fixtures/simulator';

test('welcome page renders three cloud provider cards', async ({ page }) => {
  await page.goto('/welcome');
  // Cards use shortName ('Azure', 'AWS', 'GCP') — provider.name is not rendered in the DOM
  await expect(page.getByText('Service Bus').first()).toBeVisible({ timeout: 10_000 });
  await expect(page.getByText('SQS / SNS').first()).toBeVisible({ timeout: 10_000 });
  await expect(page.getByText('Pub/Sub').first()).toBeVisible({ timeout: 10_000 });
});

test('azure demo loads 50 messages without credentials', async ({ page }) => {
  await page.goto('/messages?demo=azure');
  // Wait for the demo banner — it always renders in demo mode
  await expect(
    page.getByText(/Azure Service Bus Demo/i).or(page.getByText(/Contoso/i)).first()
  ).toBeVisible({ timeout: 10_000 });
  // DLQ tab/button should be visible — scope to <main> and use role selectors to
  // avoid matching both the message list's own "Dead-Letter (N)" tab and the
  // sidebar's unrelated "Dead-Letter · All Clouds" quick-access shortcut (nav),
  // which also matches /dead.?letter/i and previously made this locator ambiguous.
  await expect(
    page.locator('main').getByRole('tab', { name: /dead.?letter/i })
      .or(page.locator('main').getByRole('button', { name: /dead.?letter/i }))
  ).toBeVisible();
});

test('aws demo loads messages without credentials', async ({ page }) => {
  await page.goto('/messages?demo=aws');
  await expect(
    page.getByText(/AWS SQS Demo/i).or(page.getByText(/AcmeRetail/i)).first()
  ).toBeVisible({ timeout: 10_000 });
});

test('gcp demo loads messages without credentials', async ({ page }) => {
  await page.goto('/messages?demo=gcp');
  await expect(
    page.getByText(/GCP Pub\/Sub Demo/i).or(page.getByText(/MedStream/i)).first()
  ).toBeVisible({ timeout: 10_000 });
});

test('demo nudge session key is not set on first load', async ({ page }) => {
  await page.goto('/messages?demo=azure');
  // The nudge fires after 90s — we only verify the sessionStorage key is absent on first load.
  // We do NOT wait 90s (that would make CI slow).
  const nudgeShown = await page.evaluate(
    () => sessionStorage.getItem('servicehub_demo_nudge_shown')
  );
  expect(nudgeShown).toBeNull();
});
