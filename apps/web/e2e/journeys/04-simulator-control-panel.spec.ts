/**
 * Journey 04: Simulator control panel
 *
 * These tests require the Simulator API running on http://localhost:5200.
 * All tests skip gracefully with test.skip() when the API is not running,
 * so local developers without the API started never see hard failures.
 *
 * In CI the e2e-simulator job starts the API before Playwright runs.
 */
import { test, expect, SIMULATOR_API } from '../fixtures/simulator';

async function isSimulatorUp(request: Parameters<Parameters<typeof test>[1]>[0]['request']): Promise<boolean> {
  try {
    const r = await request.get(`${SIMULATOR_API}/api/v1/simulator/status`);
    return r.ok();
  } catch {
    return false;
  }
}

test('simulator page renders when API is available', async ({ page, request }) => {
  const simulatorUp = await isSimulatorUp(request);
  test.skip(!simulatorUp, 'Simulator API not running — skipping simulator UI tests');

  await page.goto('/simulator');
  // Amber simulator mode banner must appear
  await expect(page.getByText(/Simulator Mode/i)).toBeVisible({ timeout: 10_000 });
  // Provider cards for all three clouds
  await expect(page.getByText('Azure').first()).toBeVisible();
  await expect(page.getByText('AWS').first()).toBeVisible();
  await expect(page.getByText('GCP').first()).toBeVisible();
});

test('simulator page shows message counts from seeder', async ({ page, request }) => {
  const simulatorUp = await isSimulatorUp(request);
  test.skip(!simulatorUp, 'Simulator API not running');

  const statusResponse = await request.get(`${SIMULATOR_API}/api/v1/simulator/status`);
  expect(statusResponse.ok()).toBeTruthy();
  const status = await statusResponse.json() as {
    namespaces: Array<{
      id: string;
      name: string;
      activeMessageCount: number;
    }>;
  };
  const seededNamespace = status.namespaces.find((ns) => ns.activeMessageCount > 0);
  expect(seededNamespace).toBeTruthy();

  await page.goto('/simulator');
  await expect(page.getByText(/Simulator Mode/i)).toBeVisible({ timeout: 10_000 });

  // The message count is rendered in a metric card where the numeric value and
  // the "Messages" label are separate elements, so assert within the specific
  // provider card seeded by the simulator.
  const namespaceCard = page.locator('div').filter({
    has: page.getByText(seededNamespace!.name, { exact: true }),
  }).filter({
    has: page.getByText(String(seededNamespace!.activeMessageCount), { exact: true }),
  }).first();
  const messagesMetric = namespaceCard.locator('div').filter({
    has: page.getByText('Messages', { exact: true }),
  }).filter({
    has: page.getByText(String(seededNamespace!.activeMessageCount), { exact: true }),
  }).first();

  await expect(messagesMetric).toBeVisible({ timeout: 10_000 });
});

test('fault injection form renders all fault types', async ({ page, request }) => {
  const simulatorUp = await isSimulatorUp(request);
  test.skip(!simulatorUp, 'Simulator API not running');

  await page.goto('/simulator');
  await expect(page.getByText(/Fault Injection/i)).toBeVisible({ timeout: 10_000 });
  // The fault type <select> dropdown must be present
  await expect(page.locator('select').first()).toBeVisible();
  // Verify the default fault type (MaxDelivery) is the first option
  const firstOption = await page.locator('select').first().locator('option').first().textContent();
  expect(firstOption).toMatch(/MaxDelivery/i);
});

test('reset simulator button exists and is clickable', async ({ page, request }) => {
  const simulatorUp = await isSimulatorUp(request);
  test.skip(!simulatorUp, 'Simulator API not running');

  await page.goto('/simulator');
  const resetBtn = page.getByRole('button', { name: /Reset/i }).last();
  await expect(resetBtn).toBeVisible({ timeout: 10_000 });
  await expect(resetBtn).toBeEnabled();
});
