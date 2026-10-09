import { expect, test, type Page } from '@playwright/test'

/**
 * The demo must be solid (4.2.0): every page, tab, modal and panel opens with content on all three clouds together and on each
 * one alone — nothing says "not in the demo", nothing shows an error, nothing is left for a server to answer.
 */
const entries: readonly (readonly [name: string, path: string, provider: string | null])[] = [
  ['all clouds', '/demo', null],
  ['Azure', '/demo/azure', 'azure'],
  ['AWS', '/demo/aws', 'aws'],
  ['Google', '/demo/gcp', 'gcp'],
]

const screens: readonly (readonly [name: string, path: string])[] = [
  ['Home', '/'],
  ['Dead letters', '/?tab=dlq'],
  ['Active messages', '/?tab=active'],
  ['Replayed', '/?tab=replayed'],
  ['Auto Replay', '/?panel=rules'],
  ['Help', '/?panel=help'],
  ['Settings', '/?modal=settings'],
  ['Add a cloud', '/?modal=add-cloud'],
  ['Approve', '/?modal=approve'],
  ['Advanced Overview', '/advanced'],
  ['Recovery Ledger', '/advanced/ledger'],
  ['Failure Signatures', '/advanced/signatures'],
  ['Agents', '/advanced/agents'],
]

async function enter(page: Page, entry: string) {
  await page.goto(entry)
  await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
}

async function check(page: Page, where: string) {
  await page.waitForLoadState('networkidle')
  const text = await page.locator('body').innerText()
  expect(text, `${where}: a screen fell back to "not in the demo"`).not.toMatch(/not in the demo|not part of the demo/i)
  expect(text, `${where}: a screen could not reach its data`).not.toMatch(/could not reach its API|Something went wrong/i)
  expect(await page.evaluate(() => (globalThis as { __demoUncovered?: string[] }).__demoUncovered ?? []), `${where}: the demo had no answer`).toEqual([])
}

test.describe('the demo is solid', () => {
  for (const [entryName, entry, provider] of entries) {
    test(`${entryName}: every screen opens with content`, async ({ page }) => {
      const failed: string[] = []
      page.on('pageerror', (e) => failed.push(e.message))
      await enter(page, entry)
      for (const [name, path] of screens) {
        const scoped = provider ? `${path}${path.includes('?') ? '&' : '?'}provider=${provider}` : path
        await page.goto(scoped)
        await check(page, `${entryName} › ${name}`)
      }
      expect(failed, 'a screen threw an error').toEqual([])
    })
  }

  test('the three clouds are shown together, and nothing is sent anywhere', async ({ page }) => {
    const calls: string[] = []
    page.on('request', (r) => { if (new URL(r.url()).pathname.startsWith('/api/')) calls.push(`${r.method()} ${r.url()}`) })
    await enter(page, '/demo')
    const body = page.locator('body')
    for (const cloud of ['Azure', 'AWS', 'Google Cloud']) await expect(body).toContainText(cloud)
    await expect(body).toContainText(/Contoso|Acme|Globex/)
    expect(calls).toEqual([])
  })

  test('a message opens with its body and why it failed', async ({ page }) => {
    await enter(page, '/demo/azure/?tab=dlq')
    await page.getByRole('link', { name: /^Details of message/ }).first().click()
    await check(page, 'message drawer')
    await expect(page.locator('body')).toContainText(/orderId|ORD-/)
  })

  test('Reset demo puts back what the visitor changed', async ({ page }) => {
    await enter(page, '/demo/azure/?tab=dlq')
    const replayLinks = page.getByRole('link', { name: /^Replay message/ })
    const firstName = await replayLinks.first().getAttribute('aria-label')
    await replayLinks.first().click()
    await page.getByRole('dialog', { name: /replay/i }).getByRole('button', { name: /^Replay 1 message/ }).first().click()
    await expect(page.getByRole('dialog').getByText(/Demo — nothing was sent/i).first()).toBeVisible()
    await page.goto('/?tab=dlq&provider=azure')
    await expect(page.getByRole('link', { name: firstName! })).toHaveCount(0)
    await page.getByRole('button', { name: 'Reset demo' }).click()
    await expect(page.getByRole('link', { name: firstName! })).toHaveCount(1)
  })
})
