import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'

/** "Show me around" (4.2.0): the demo's guided tour drives the real screens, starts once, and never gets in the way again. */
const card = (page: Page) => page.getByRole('dialog', { name: /./ }).filter({ hasText: 'Show me around' })
const next = (page: Page) => card(page).getByRole('button', { name: /^(Next|Done)$/ })

// A first-time visitor: nothing remembered.
test.use({ storageState: { cookies: [], origins: [] } })

test.describe('Show me around', () => {
  for (const [cloud, entry, ending] of [['Azure', '/demo/azure', /Verified/], ['AWS', '/demo/aws', /verification required/]] as const) {
    test(`${cloud}: the whole site in 13 stops, on the real screens, ending with the truth about that cloud`, async ({ page }) => {
      await page.goto(entry)
      await expect(card(page)).toContainText('1 of 13')
      await expect(card(page)).toContainText('Messages that failed')

      await next(page).click()
      await expect(card(page)).toContainText('2 of 13')
      await expect(page.getByRole('link', { name: /^Details of message/ }).first()).toBeVisible()

      await next(page).click()
      await expect(card(page)).toContainText('Why it failed')
      await expect(page.getByRole('region', { name: 'Why it failed' })).toBeVisible()

      await next(page).click()
      await expect(card(page)).toContainText('Put it back')
      await expect(page.getByRole('button', { name: /^Replay 1 message/ }).first()).toBeVisible()

      // Next presses Replay for a visitor who has not.
      await next(page).click()
      await expect(card(page)).toContainText('Did it stay fixed?')
      await expect(card(page)).toContainText(ending)
      await expect(page.getByText('Sent back', { exact: true })).toBeVisible()
      await expect(page.getByText(/Demo — nothing was sent/).first()).toBeVisible()

      await next(page).click()
      await expect(card(page)).toContainText('6 of 13')
      await expect(page.locator('#agent-bar')).toBeVisible()
      await next(page).click()
      await expect(card(page)).toContainText('Help and information')
      await expect(page.getByRole('link', { name: 'Help for this page' }).first()).toBeVisible()
      for (const title of ['Auto Replay rules', 'Advanced: the whole picture', 'Recovery Ledger', 'Failure Signatures', 'Agents', 'Connections']) {
        await next(page).click()
        await expect(card(page)).toContainText(title)
      }
      await expect(card(page)).toContainText('13 of 13')
      await next(page).click()
      await expect(card(page)).toHaveCount(0)

      // It does not start again by itself…
      await page.goto('/')
      await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
      await expect(card(page)).toHaveCount(0)
      // …but the banner brings it back.
      await page.getByRole('button', { name: 'Show me around' }).click()
      await expect(card(page)).toContainText('1 of 13')
    })
  }

  test('Skip and Escape both end it, and it is remembered', async ({ page }) => {
    await page.goto('/demo')
    await expect(card(page)).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(card(page)).toHaveCount(0)
    await page.reload()
    await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
    await expect(card(page)).toHaveCount(0)
  })

  test('a stop passes the accessibility gate and does not make the page scroll sideways', async ({ page }) => {
    await page.goto('/demo/azure')
    await next(page).click()
    await expect(card(page)).toContainText('2 of 13')
    await page.waitForLoadState('networkidle')
    const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
    expect(result.violations.map((v) => `${v.id}: ${v.help}`)).toEqual([])
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
  })

  test('it never appears outside the demo', async ({ page }) => {
    await page.goto('/')
    await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
    await expect(card(page)).toHaveCount(0)
  })
})

test.describe('demo addresses', () => {
  test('the Demo button on Home always walks through, even after the tour was seen', async ({ page }) => {
    await page.goto('/demo/azure')
    await expect(card(page)).toContainText('1 of 13')
    await page.getByRole('button', { name: 'Skip' }).click()
    await page.goto('/demo?tour=1')
    await expect(card(page)).toContainText('1 of 13')
  })

  test('the demo stays under /demo and each cloud under its own address', async ({ page }) => {
    await page.goto('/demo')
    await expect(page).toHaveURL(/\/demo\/?$/)
    await page.goto('/demo/aws')
    await expect(page).toHaveURL(/\/demo\/aws(\/|\?|$)/)
    await expect(page.getByText(/Everything here is made up/)).toBeVisible()
  })
})

test.describe('demo deep links', () => {
  test('a link to a panel is not taken over by the first-visit tour', async ({ page }) => {
    await page.goto('/demo/?panel=connections')
    // Scoped to the Connections list: Home's own Azure section names the same namespace once it has loaded, and an unscoped
    // getByText matched both on a busy machine (a strict-mode violation, not a product fault).
    await expect(page.getByRole('rowgroup', { name: 'Azure' }).getByText('Contoso Orders (dev)')).toBeVisible()
    await expect(page).toHaveURL(/panel=connections/)
    await expect(card(page)).toHaveCount(0)
  })
})
