/**
 * Suite B: Signature List -> Investigation Workspace (P0, no backend required)
 *
 * The flagship "operator investigates a failure" journey. Azure only — the demo
 * signature fixtures (DEMO_SIGNATURE_DEFS) are identical across providers, so this
 * suite validates provider-independent Investigation Workspace behavior once.
 */
import { test, expect } from '../fixtures/base';

const NAMESPACE = 'demo-azure-contoso-prod';
const SIGNATURES_URL = `/demo/azure/signatures?namespace=${NAMESPACE}`;
const DESERIALIZATION_HASH = 'demo-deserialization-failure';
const DEMO_REASONS = [
  'MaxDeliveryCountExceeded',
  'PoisonMessage',
  'DeserializationError',
  'AuthenticationFailure',
  'DuplicateMessage',
];

test.describe('Suite B — Signature List → Investigation Workspace (azure)', () => {
  test('signature list renders all curated demo signatures', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(SIGNATURES_URL);
    await expect(page.getByRole('heading', { name: 'Failure Signatures' })).toBeVisible();

    for (const reason of DEMO_REASONS) {
      await expect(page.getByText(reason, { exact: true })).toBeVisible();
    }
    await expect(page.getByRole('link', { name: 'View details →' })).toHaveCount(DEMO_REASONS.length);
  });

  test('clicking into a signature opens the Investigation Workspace and stays in Demo Mode', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(SIGNATURES_URL);
    await page.getByRole('link', { name: 'View details →' }).first().click();
    await expect(page).toHaveURL(/\/demo\/azure\/signatures\/[^/?]+\?namespace=/);
  });

  test.describe('demo-deserialization-failure detail', () => {
    test.beforeEach(async ({ page }) => {
      await page.goto(`/demo/azure/signatures/${DESERIALIZATION_HASH}?namespace=${NAMESPACE}`);
    });

    test('header, panels, timeline, and related messages all render', async ({ page, noConsoleErrors }) => {
      void noConsoleErrors;
      // Header / identity
      await expect(page.getByRole('heading', { name: /DeserializationError/ })).toBeVisible();
      await expect(page.getByText(`Fingerprint: ${DESERIALIZATION_HASH}`)).toBeVisible();

      // Replay Safety & History
      await expect(page.getByRole('heading', { name: 'Replay Safety & History' })).toBeVisible();

      // Root Cause Explorer — this hash has a populated fixture (see Suite C for the assertion depth)
      await expect(page.getByRole('heading', { name: 'Root Cause Explorer' })).toBeVisible();

      // Recent Changes Before Failure
      await expect(page.getByRole('heading', { name: 'Recent Changes Before Failure' })).toBeVisible();

      // Timeline
      await expect(page.getByRole('heading', { name: 'Timeline' })).toBeVisible();

      // Related Messages — always empty in Demo Mode (getMockDlqSignatureDetail hardcodes
      // relatedMessages: []), so this is the correct empty state, not a bug.
      await expect(page.getByRole('heading', { name: 'Related Messages (0)' })).toBeVisible();
      await expect(page.getByText('No related messages available.')).toBeVisible();
    });

    test('back navigation returns to the signature list within Demo Mode', async ({ page, noConsoleErrors }) => {
      void noConsoleErrors;
      await page.getByRole('link', { name: 'Back to signatures' }).click();
      await expect(page).toHaveURL(/\/demo\/azure\/signatures\?namespace=/);
      await expect(page.getByRole('heading', { name: 'Failure Signatures' })).toBeVisible();
    });
  });
});
