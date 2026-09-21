/**
 * Suite D: Demo Mode mutation safety net (P1, no backend required)
 *
 * Every mutation entry point in the Investigation Workspace must fail safely in
 * Demo Mode — no crash, an explicit rejection, UI remains usable. Azure only.
 *
 * Two demo signatures are used to reach every SignatureLifecycleActions button
 * (a single status only exposes 3 of the 4 transitions):
 *  - demo-deserialization-failure (Active): Mark Resolved, Suppress, Archive, Replay, Knowledge
 *  - demo-max-delivery-count-exceeded (Resolved): Reopen
 */
import { test, expect } from '../fixtures/base';

const NAMESPACE = 'demo-azure-contoso-prod';
const ACTIVE_HASH = 'demo-deserialization-failure';
const RESOLVED_HASH = 'demo-max-delivery-count-exceeded';
const DEMO_REJECTION_TEXT = 'Not available in Demo Mode';

test.describe('Suite D — Demo Mode mutation safety (azure)', () => {
  test.describe('signature lifecycle actions are rejected with a toast, not silently applied', () => {
    test('Mark Resolved', async ({ page, noConsoleErrors }) => {
      void noConsoleErrors;
      await page.goto(`/demo/azure/signatures/${ACTIVE_HASH}?namespace=${NAMESPACE}`);
      const actions = page.getByRole('group', { name: 'Lifecycle actions' });
      const button = actions.getByRole('button', { name: 'Mark Resolved' });
      await button.click();
      await expect(page.getByText(DEMO_REJECTION_TEXT).first()).toBeVisible();
      await expect(button).not.toBeDisabled();
    });

    test('Suppress', async ({ page, noConsoleErrors }) => {
      void noConsoleErrors;
      await page.goto(`/demo/azure/signatures/${ACTIVE_HASH}?namespace=${NAMESPACE}`);
      const actions = page.getByRole('group', { name: 'Lifecycle actions' });
      const button = actions.getByRole('button', { name: 'Suppress' });
      await button.click();
      await expect(page.getByText(DEMO_REJECTION_TEXT).first()).toBeVisible();
      await expect(button).not.toBeDisabled();
    });

    test('Archive', async ({ page, noConsoleErrors }) => {
      void noConsoleErrors;
      await page.goto(`/demo/azure/signatures/${ACTIVE_HASH}?namespace=${NAMESPACE}`);
      const actions = page.getByRole('group', { name: 'Lifecycle actions' });
      const button = actions.getByRole('button', { name: 'Archive' });
      await button.click();
      await expect(page.getByText(DEMO_REJECTION_TEXT).first()).toBeVisible();
      await expect(button).not.toBeDisabled();
    });

    test('Reopen', async ({ page, noConsoleErrors }) => {
      void noConsoleErrors;
      await page.goto(`/demo/azure/signatures/${RESOLVED_HASH}?namespace=${NAMESPACE}`);
      const actions = page.getByRole('group', { name: 'Lifecycle actions' });
      const button = actions.getByRole('button', { name: 'Reopen' });
      await button.click();
      await expect(page.getByText(DEMO_REJECTION_TEXT).first()).toBeVisible();
      await expect(button).not.toBeDisabled();
    });
  });

  test('Replay preview fails safely: modal opens, rejection toast shown, confirm stays disabled', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/signatures/${ACTIVE_HASH}?namespace=${NAMESPACE}`);

    await page.getByRole('button', { name: 'Start Replay' }).click();

    const modal = page.getByRole('dialog', { name: 'Replay signature preview' });
    await expect(modal).toBeVisible();
    await expect(modal.getByText('Failed to preview this replay. Close and try again.')).toBeVisible();
    await expect(page.getByText(DEMO_REJECTION_TEXT).first()).toBeVisible();

    const confirmButton = modal.getByRole('button', { name: /^Replay \d+ messages?$/ });
    await expect(confirmButton).toBeDisabled();

    await modal.getByRole('button', { name: 'Close preview' }).click();
    await expect(modal).not.toBeVisible();
  });

  test('Knowledge edit Save is disabled in Demo Mode, not toast-rejected', async ({ page, noConsoleErrors }) => {
    void noConsoleErrors;
    await page.goto(`/demo/azure/signatures/${ACTIVE_HASH}?namespace=${NAMESPACE}`);

    await page.getByRole('button', { name: 'Edit knowledge' }).click();

    const dialog = page.getByRole('dialog', { name: 'Edit Knowledge' });
    await expect(dialog).toBeVisible();
    await expect(
      dialog.getByText('Demo Mode — this is a read-only preview with pre-seeded data. Editing is disabled.'),
    ).toBeVisible();

    const saveButton = dialog.getByRole('button', { name: 'Save' });
    await expect(saveButton).toBeDisabled();

    await dialog.getByRole('button', { name: 'Cancel' }).click();
    await expect(dialog).not.toBeVisible();
  });
});
