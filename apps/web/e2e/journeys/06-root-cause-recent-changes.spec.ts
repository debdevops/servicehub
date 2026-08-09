/**
 * Suite C: Root Cause Explorer & Recent Changes — populated vs. empty (P1, no backend required)
 *
 * Both panels must correctly distinguish "no data yet" from "here's what we found."
 * Azure only — DEMO_SIGNATURE_DEFS is identical across providers.
 *
 * demo-deserialization-failure is the one curated signature with a non-empty
 * rootCauseMatches AND recentChanges fixture; demo-poison-message is empty on both.
 */
import { test, expect } from '../fixtures/base';

const NAMESPACE = 'demo-azure-contoso-prod';
const POPULATED_HASH = 'demo-deserialization-failure';
const EMPTY_HASH = 'demo-poison-message';

test.describe('Suite C — Root Cause Explorer / Recent Changes (azure)', () => {
  test('populated fixture: Root Cause Explorer and Recent Changes both show fleet data', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/signatures/${POPULATED_HASH}?namespace=${NAMESPACE}`);

    await expect(page.getByRole('heading', { name: 'Root Cause Explorer' })).toBeVisible();
    await expect(page.getByText(/Seen 24 time\(s\) across 2 namespace\(s\) in your fleet\./)).toBeVisible();
    await expect(page.getByText(/9 occurrence/)).toBeVisible();

    await expect(page.getByRole('heading', { name: 'Recent Changes Before Failure' })).toBeVisible();
    await expect(page.getByText('Rule.Create · Schema-version-gate')).toBeVisible();
    await expect(
      page.getByText(/1 change occurred in the 24h before this failure started — review before further action\./),
    ).toBeVisible();
  });

  test('empty fixture: Root Cause Explorer and Recent Changes both show their explicit empty state', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/signatures/${EMPTY_HASH}?namespace=${NAMESPACE}`);

    await expect(page.getByText('Not seen in any other namespace in your fleet.')).toBeVisible();
    await expect(
      page.getByText('No recorded configuration changes in the 24h before this failure started.'),
    ).toBeVisible();
  });
});
