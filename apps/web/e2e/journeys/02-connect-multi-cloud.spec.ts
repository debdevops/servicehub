/**
 * Journey 02: Connect page — multi-cloud provider selector (no API required)
 *
 * These tests verify all three provider selector buttons exist, that selecting
 * each provider shows the correct credential fields, and that Azure is the default.
 * No real credentials are submitted — tests do NOT call the backend.
 */
import { test, expect } from '../fixtures/simulator';

test('connect page shows cloud provider selector', async ({ page }) => {
  await page.goto('/connect');
  await expect(page.getByText('Azure').first()).toBeVisible();
  await expect(page.getByText('AWS').first()).toBeVisible();
  await expect(page.getByText('GCP').first()).toBeVisible();
});

test('selecting Azure shows connection string field (default)', async ({ page }) => {
  await page.goto('/connect');
  // Azure is default — connection string field must be visible
  await expect(
    page.getByPlaceholder('Endpoint=sb://...;SharedAccessKey=...')
      .or(page.getByPlaceholder(/connection string/i))
  ).toBeVisible();
});

test('selecting AWS shows AWS-specific credential fields', async ({ page }) => {
  await page.goto('/connect');
  // Click the AWS cloud selector button
  await page.getByRole('button', { name: /AWS/i }).first().click();
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
  await page.goto('/connect');
  await page.getByRole('button', { name: /GCP/i }).first().click();
  // GCP Project ID field — exact placeholder to avoid ambiguity
  await expect(page.getByPlaceholder('my-project-123')).toBeVisible();
});

test('AWS region field has a default value', async ({ page }) => {
  await page.goto('/connect');
  await page.getByRole('button', { name: /AWS/i }).first().click();
  // The AWS region select must be present and show us-east-1 as default
  const regionSelect = page.locator('select').first();
  await expect(regionSelect).toBeVisible();
  const selectedValue = await regionSelect.inputValue();
  // Default is us-east-1 per ConnectPage.tsx line 29
  expect(selectedValue).toBe('us-east-1');
});
