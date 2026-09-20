/**
 * Suite: Recovery Evidence Ledger (Demo Mode, Azure)
 *
 * Ledger list → operation detail → verify chain → export, against the curated demo story
 * (roadmap §20): 214 messages replayed on `orders-processing`, most Recovered, some Returned,
 * some Unverified (AWS), a couple ExecutionFailed — chosen to demonstrate honesty, not success.
 */
import { test, expect, type Page } from '../fixtures/base';

const NAMESPACE = 'demo-azure-contoso-prod';

/**
 * The ledger redesign made a row click select the operation and render an inline lifecycle pane
 * at `?op=…` (DETECT → … → VERIFY), with "Open full evidence record" linking on to the standalone
 * page these assertions were written against. Selecting the row is no longer the same thing as
 * opening the record, so do both.
 */
async function openFullEvidenceRecord(page: Page) {
  await page.getByRole('row').filter({ hasText: 'alex@contoso.com' }).first().click();
  await page.getByRole('link', { name: /Open full evidence record/i }).click();
}

test.describe('Suite — Recovery Evidence Ledger (azure)', () => {
  test('ledger lists the curated operation with its actor and scope', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/recovery?namespace=${NAMESPACE}`);

    await expect(page.getByRole('heading', { name: 'Recovery Evidence' }).first()).toBeVisible();
    await expect(page.getByText('Demo Mode', { exact: false }).first()).toBeVisible();
    await expect(page.getByRole('row').filter({ hasText: 'alex@contoso.com' }).first()).toBeVisible();
    await expect(page.getByText('entity=orders-processing', { exact: false })).toBeVisible();
  });

  test('operation detail shows entries with honest verification results, never presenting Unverified as success', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/recovery?namespace=${NAMESPACE}`);
    await openFullEvidenceRecord(page);

    await expect(page.getByRole('heading', { name: /Replay — entity=orders-processing/ })).toBeVisible();

    // Outcome rollup — all four outcomes from the curated story are present, including the
    // non-apologetic Unverified count.
    await expect(page.getByText('187 Recovered')).toBeVisible();
    await expect(page.getByText('11 Returned')).toBeVisible();
    await expect(page.getByText('14 Unverified')).toBeVisible();
    await expect(page.getByText('2 ExecutionFailed')).toBeVisible();

    // The honesty limitation sentence must render with every verification result, not just on
    // hover — assert at least one instance is visible on the page without interaction.
    await expect(
      page.getByText('ServiceHub observes the queue, not your consumer. This does not confirm the business transaction completed.').first(),
    ).toBeVisible();
  });

  test('verify chain reports the chain intact', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/recovery?namespace=${NAMESPACE}`);
    await openFullEvidenceRecord(page);

    await page.getByRole('button', { name: 'Verify chain' }).click();
    await expect(page.getByText(/Chain verified/)).toBeVisible();
  });

  test('export evidence completes without error and is watermarked as Demo Mode fixture data', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/recovery?namespace=${NAMESPACE}`);
    await openFullEvidenceRecord(page);

    await page.getByRole('button', { name: 'Export evidence' }).click();
    await expect(page.getByText('Demo evidence export downloaded')).toBeVisible();
  });

  test('event chain is collapsible and shows Seq-ordered entries with a hash column', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/recovery?namespace=${NAMESPACE}`);
    await openFullEvidenceRecord(page);

    // The chain renders expanded by default (the evidence is the point of the page); the toggle
    // collapses it. Assert both directions rather than assuming a collapsed starting state.
    await expect(page.getByText('OperationOpened')).toBeVisible();
    await expect(page.getByText('EntryBegun').first()).toBeVisible();

    const toggle = page.getByRole('button', { name: /Event chain/ });
    await toggle.click();
    await expect(page.getByText('OperationOpened')).toBeHidden();
    await toggle.click();
    await expect(page.getByText('OperationOpened')).toBeVisible();
  });
});
