import { expect, test, type Page } from '@playwright/test'

/**
 * The demo, the addresses and the core loop in the 5.0.0 layout (cutover, build-plan units I1 and I2). `next.e2e.ts` opens every
 * page; this file proves the ways IN still work (`/demo`, `/demo/<cloud>`, a 4.2.0 bookmark), that nothing leaves the browser,
 * and that a person can open a failed message, replay it and put the demo back.
 */
const azureDetail = '/demo/azure/c/service-bus/demo-azure-0000-0000-000000000011'

async function settled(page: Page) {
  await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
  await page.waitForLoadState('networkidle')
}

test.describe('the demo addresses', () => {
  for (const [path, cloud, heading] of [
    ['/demo', 'All clouds', 'All clouds'],
    ['/demo/azure', 'Azure', /Azure/],
    ['/demo/aws', 'AWS', /AWS/],
    ['/demo/gcp', 'Google Cloud', /Google Cloud/],
  ] as const) {
    test(`${path} opens ${cloud}, says it is a demo, and sends nothing anywhere`, async ({ page }) => {
      const calls: string[] = []
      page.on('request', (r) => { if (new URL(r.url()).pathname.startsWith('/api/')) calls.push(`${r.method()} ${r.url()}`) })
      await page.goto(path)
      await settled(page)
      await expect(page.getByRole('heading', { level: 1 }).first()).toHaveText(heading)
      await expect(page.getByRole('banner')).toContainText(cloud)
      await expect(page.getByText('Everything here is made up')).toBeVisible()
      expect(new URL(page.url()).pathname).toBe(path)
      expect(calls).toEqual([])
    })
  }

  test('a page beneath a demo cloud keeps its address, and the menu stays inside the demo', async ({ page }) => {
    await page.goto('/demo/aws/attention')
    await settled(page)
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Needs your attention')
    await page.getByRole('navigation', { name: 'ServiceHub' }).getByRole('link', { name: /Replay history/ }).click()
    await settled(page)
    expect(new URL(page.url()).pathname).toBe('/demo/aws/replays')
  })

  test('an address that names no cloud is "that page doesn’t exist", never another cloud under a wrong name', async ({ page }) => {
    for (const path of ['/demo/other', '/demo/other/attention']) {
      await page.goto(path)
      await expect(page.getByRole('heading', { level: 1 }).first()).toHaveText(/doesn’t exist/)
    }
  })
})

test.describe('4.2.0 addresses still land somewhere real', () => {
  for (const [old, now] of [
    ['/?tab=dlq&provider=azure', '/azure'],
    ['/?tab=active&provider=aws', '/aws/active'],
    ['/?tab=replayed', '/all/replays'],
    ['/?panel=rules', '/all/rules'],
    ['/advanced', '/'],
    ['/advanced/ledger', '/all/ledger'],
    ['/advanced/signatures', '/all/signatures'],
    ['/advanced/agents', '/all/agents'],
    ['/fleet', '/'],
    ['/next/gcp/attention', '/gcp/attention'],
  ] as const) {
    test(`${old} → ${now}`, async ({ page }) => {
      await page.goto('/demo')
      await settled(page)
      await page.goto(old)
      await settled(page)
      expect(new URL(page.url()).pathname).toBe(now)
      await expect(page.getByRole('heading', { level: 1 }).first()).not.toHaveText(/doesn’t exist/)
    })
  }
})

test.describe('the core loop, in the demo', () => {
  test('a failed message opens with its body and why it failed', async ({ page }) => {
    await page.goto(`${azureDetail}?tab=dead-letters`)
    await settled(page)
    await page.getByRole('link', { name: /^Open message/ }).first().click()
    const dialog = page.getByRole('dialog')
    await expect(dialog).toBeVisible()
    await expect(dialog).toContainText(/orderId|ORD-/)
    await expect(dialog).toContainText(/ValidationFailed|MaxDeliveryCountExceeded|UnauthorizedAccess|DeserializationFailed/)
  })

  test('replaying one message says nothing was sent, and Reset demo puts it back', async ({ page }) => {
    await page.goto(`${azureDetail}?tab=dead-letters`)
    await settled(page)
    const first = page.getByRole('link', { name: /^Open message/ }).first()
    const name = (await first.getAttribute('aria-label')) ?? (await first.innerText())
    await first.click()
    await page.getByRole('dialog').getByRole('link', { name: /^Replay/ }).first().click()
    await page.getByRole('dialog').getByRole('button', { name: /^Replay 1 message/ }).first().click()
    await expect(page.getByRole('dialog').getByText(/Demo — nothing was sent/i).first()).toBeVisible()

    await page.goto(`${azureDetail}?tab=dead-letters`)
    await settled(page)
    await expect(page.getByRole('link', { name })).toHaveCount(0)
    await page.getByRole('button', { name: 'Reset demo' }).click()
    await expect(page.getByRole('link', { name })).toHaveCount(1)
  })
})
