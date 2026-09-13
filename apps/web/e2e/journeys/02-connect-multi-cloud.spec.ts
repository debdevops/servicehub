/**
 * Journey 02: Connect page — multi-cloud provider selector (no API required)
 *
 * These tests verify all three provider selector buttons exist, that selecting
 * each provider shows the correct credential fields, and that Azure is the default.
 * No real credentials are submitted — tests do NOT call the backend.
 */
import { test, expect, type Page } from '../fixtures/base';

/**
 * The connect form is a full card for a first-time user and a collapsed "Connect another
 * namespace" accordion once at least one namespace is saved (ConnectPage: `addFormOpen =
 * override ?? !hasNamespaces`) — deliberate returning-user behaviour. These tests assert on the
 * form's fields, so they must open it first; without this they only passed on an instance that
 * happened to have no saved connections (or whose namespace request failed), making the whole
 * spec silently dependent on the machine's data rather than on the page.
 */
async function openConnectForm(page: Page) {
  await page.goto('/connect');
  const accordion = page.getByRole('button', { name: 'Connect another namespace' });
  const providerSelector = page.getByTitle('Amazon Web Services SQS');
  // The form renders open while the namespace list is still loading and then collapses once it
  // arrives, so a single visibility check races that transition — poll until the form is
  // actually open (whichever way it got there).
  await expect(async () => {
    if (await accordion.isVisible()) await accordion.click();
    await expect(providerSelector).toBeVisible({ timeout: 1500 });
  }).toPass({ timeout: 25_000 });
}

test('connect page shows cloud provider selector', async ({ page }) => {
  await openConnectForm(page);
  await expect(page.getByText('Azure').first()).toBeVisible();
  await expect(page.getByText('AWS').first()).toBeVisible();
  await expect(page.getByText('GCP').first()).toBeVisible();
});

test('selecting Azure shows connection string field (default)', async ({ page }) => {
  await openConnectForm(page);
  // Azure is default — connection string field must be visible
  await expect(
    page.getByPlaceholder('Endpoint=sb://...;SharedAccessKey=...')
      .or(page.getByPlaceholder(/connection string/i))
  ).toBeVisible();
});

test('selecting AWS shows AWS-specific credential fields', async ({ page }) => {
  await openConnectForm(page);
  // Click the AWS cloud selector button — use the title attribute for an unambiguous
  // selector that cannot match saved-connection cards or demo callout buttons.
  await page.getByTitle('Amazon Web Services SQS').click();
  // Access Key ID field
  await expect(
    page.getByPlaceholder('AKIAIOSFODNN7EXAMPLE')
      .or(page.getByPlaceholder(/access key id/i))
  ).toBeVisible();
  // Secret Access Key field
  await expect(
    page.getByPlaceholder('wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY')
      .or(page.getByPlaceholder(/secret/i))
  ).toBeVisible();
  // Azure connection string field should NOT be visible
  await expect(
    page.getByPlaceholder('Endpoint=sb://...;SharedAccessKey=...')
  ).not.toBeVisible();
});

test('selecting GCP shows GCP-specific fields', async ({ page }) => {
  await openConnectForm(page);
  await page.getByTitle('Google Cloud Pub/Sub').click();
  // GCP Project ID field — exact placeholder to avoid ambiguity
  await expect(page.getByPlaceholder('my-project-123')).toBeVisible();
});

test('AWS region field has a default value', async ({ page }) => {
  await openConnectForm(page);
  await page.getByTitle('Amazon Web Services SQS').click();
  // The AWS region select must be present and show us-east-1 as default
  const regionSelect = page.locator('select').first();
  await expect(regionSelect).toBeVisible();
  const selectedValue = await regionSelect.inputValue();
  // Default is us-east-1 per ConnectPage.tsx line 29
  expect(selectedValue).toBe('us-east-1');
});
