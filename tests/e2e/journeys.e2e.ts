import { expect, test, type Page } from '@playwright/test'

/**
 * Whole-screen journeys in demo mode (`/demo/azure`, unit 6.5): the client answers from made-up data, so there is no API and no cloud.
 * The other specs here measure how the app looks and feels (axe, keyboard, layout, words); these prove it WORKS — that a person can
 * get from a problem to a recovery, that the safety steps stand between the two, and that URLs carry state so a reload, a shared
 * link and the Back button all land in the same place.
 */
async function demo(page: Page, path = '/') {
  await page.goto('/demo/azure')
  await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
  await page.goto(path)
  await page.waitForLoadState('networkidle')
}

const firstDetails = (page: Page) => page.getByRole('link', { name: /^Details of message/ }).first()

test.describe('Simple: from a dead letter to a recovery', () => {
  test('Dead letters, Active messages and Replayed each show their own list', async ({ page }) => {
    await demo(page, '/?tab=dlq')
    await expect(firstDetails(page)).toBeVisible()
    await expect(page.getByRole('table').first()).toBeVisible()

    await page.goto('/?tab=active')
    await expect(page.getByRole('table').first()).toBeVisible()

    await page.goto('/?tab=replayed')
    await expect(page.getByRole('region', { name: /^Replays, / })).toBeVisible()
  })

  test('opening a message puts it in the URL, and Back closes it', async ({ page }) => {
    await demo(page, '/?tab=dlq')
    await firstDetails(page).click()
    await expect(page.getByRole('dialog')).toBeVisible()
    await expect(page).toHaveURL(/message=/)

    await page.goBack()
    await expect(page.getByRole('dialog')).toHaveCount(0)
    await expect(page).not.toHaveURL(/message=/)
  })

  test('a shared link reopens the same message after a reload', async ({ page }) => {
    await demo(page, '/?tab=dlq')
    await firstDetails(page).click()
    await expect(page.getByRole('dialog')).toBeVisible()
    const url = page.url()

    await page.reload()
    await expect(page.getByRole('dialog')).toBeVisible()
    expect(page.url()).toBe(url)
  })

  test('replay shows what will happen first, and only sends when asked', async ({ page }) => {
    await demo(page, '/?tab=dlq')
    await page.getByRole('link', { name: /^Replay message/ }).first().click()

    const modal = page.getByRole('dialog', { name: /replay/i })
    await expect(modal).toBeVisible()
    // The proposal: what, the risk, the checks, and what comes after — all before anything is sent.
    await expect(modal.getByRole('region', { name: 'What will happen' })).toBeVisible()
    await expect(modal.getByRole('region', { name: 'Safety checks' })).toBeVisible()
    await expect(modal.getByRole('region', { name: 'After it runs' })).toBeVisible()
    await expect(modal.getByRole('status').filter({ hasText: /Sent back/ })).toHaveCount(0)

    // Cancel sends nothing.
    await modal.getByRole('button', { name: 'Cancel' }).click()
    await expect(page.getByRole('dialog')).toHaveCount(0)
  })

  test('in the demo, confirming a replay is refused in words and nothing is claimed as sent', async ({ page }) => {
    await demo(page, '/?tab=dlq')
    await page.getByRole('link', { name: /^Replay message/ }).first().click()
    const modal = page.getByRole('dialog', { name: /replay/i })
    await modal.getByRole('button', { name: /^Replay 1 message/ }).first().click()

    await expect(modal.getByRole('alert').first()).toContainText(/demo — nothing is sent/i)
    await expect(modal.getByText(/^Sent back$/)).toHaveCount(0)
    await modal.getByRole('button', { name: 'Cancel' }).click()
    await expect(page.getByRole('dialog')).toHaveCount(0)
  })

  test('bulk replay previews the selection, and cancelling leaves the table as it was', async ({ page }) => {
    await demo(page, '/?tab=dlq')
    await page.getByRole('checkbox').nth(1).check()
    await page.getByRole('link', { name: /Replay selected/ }).first().click()

    const modal = page.getByRole('dialog', { name: /bulk replay/i })
    await expect(modal).toBeVisible()
    await expect(page).toHaveURL(/modal=bulk-replay/)

    await page.keyboard.press('Escape')
    await expect(page.getByRole('dialog')).toHaveCount(0)
    await expect(page.getByRole('checkbox').nth(1)).toBeChecked()
  })

  test('search narrows the list and puts the query in the URL', async ({ page }) => {
    await demo(page, '/?tab=dlq')
    const before = await page.getByRole('link', { name: /^Details of message/ }).count()
    const box = page.getByPlaceholder(/Search ID, queue, reason, error/)
    await box.fill('zzz-no-such-message')
    await expect(page).toHaveURL(/q=zzz-no-such-message/)
    await expect(page.getByRole('link', { name: /^Details of message/ })).toHaveCount(0)
    await box.fill('')
    await expect(page.getByRole('link', { name: /^Details of message/ })).toHaveCount(before)
  })
})

test.describe('the shell', () => {
  test('Simple and Advanced are two surfaces of one app, and the way back is always there', async ({ page }) => {
    await demo(page, '/')
    await page.goto('/advanced')
    await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
    await expect(page).toHaveURL(/\/advanced/)

    await page.goBack()
    await expect(page).not.toHaveURL(/\/advanced/)
  })

  for (const path of ['/advanced', '/advanced/ledger', '/advanced/signatures', '/advanced/agents']) {
    test(`${path} renders with a heading and no error screen`, async ({ page }) => {
      await demo(page, path)
      await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
      await expect(page.getByText(/something went wrong|unexpected error/i)).toHaveCount(0)
    })
  }

  test('a URL that names no screen says so and offers the way home', async ({ page }) => {
    await demo(page, '/no/such/place')
    await expect(page.getByRole('heading', { name: /That page doesn’t exist/ })).toBeVisible()
    await page.getByRole('link', { name: 'Go to Home' }).click()
    await expect(page).toHaveURL(/\/(\?.*)?$/)
  })

  test('the panels and modals open from the URL and close back to it', async ({ page }) => {
    await demo(page, '/?panel=help')
    await expect(page.getByRole('dialog').or(page.getByRole('complementary')).first()).toBeVisible()
    await demo(page, '/?modal=add-cloud')
    await expect(page.getByRole('dialog')).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(page.getByRole('dialog')).toHaveCount(0)
  })
})

test.describe('demo mode is safe', () => {
  test('no request leaves the browser for the API', async ({ page }) => {
    const calls: string[] = []
    page.on('request', (r) => { if (new URL(r.url()).pathname.startsWith('/api/')) calls.push(`${r.method()} ${r.url()}`) })
    await demo(page, '/?tab=dlq')
    await page.getByRole('link', { name: /^Replay message/ }).first().click()
    await page.getByRole('dialog', { name: /replay/i }).getByRole('button', { name: /^Replay 1 message/ }).first().click()
    await expect(page.getByRole('dialog').getByRole('alert').first()).toContainText(/demo/i)
    expect(calls).toEqual([])
  })
})
