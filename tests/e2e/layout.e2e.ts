import { expect, test, type Page } from '@playwright/test'

/**
 * Performance / layout gate (unit 6.7): at 1366×768 — the machine someone actually has at 2 a.m. — nothing needs a horizontal
 * scroll: not the page, and not a table inside it. A table that hides its last column behind a side-scroll is a screen that
 * looks fine on the designer's monitor and loses the "Details" link on the real one.
 */
const screens: readonly (readonly [name: string, path: string])[] = [
  ['Home', '/'],
  ['Dead letters', '/?tab=dlq'],
  ['Active messages', '/?tab=active'],
  ['Replayed', '/?tab=replayed'],
  ['Auto Replay', '/?panel=rules'],
  ['Advanced Overview', '/advanced'],
  ['Recovery Ledger', '/advanced/ledger'],
  ['Failure Signatures', '/advanced/signatures'],
  ['Agents', '/advanced/agents'],
]

async function sideScrolls(page: Page) {
  return page.evaluate(() => {
    const out: string[] = []
    const root = document.documentElement
    if (root.scrollWidth > window.innerWidth) out.push(`page: ${root.scrollWidth}px wide in a ${window.innerWidth}px window`)
    for (const el of document.querySelectorAll<HTMLElement>('main *')) {
      const overflowX = getComputedStyle(el).overflowX
      if ((overflowX === 'auto' || overflowX === 'scroll') && el.scrollWidth > el.clientWidth + 1) {
        out.push(`${el.tagName.toLowerCase()}.${el.className.toString().split(' ').slice(0, 3).join('.')}: ${el.scrollWidth}px of content in ${el.clientWidth}px`)
      }
    }
    return out
  })
}

/** Small laptop, the 1366 machine, a desktop and a large monitor. Tablets (768) are not asserted: a table may scroll inside its own box there, the page may not. */
const widths = [1024, 1280, 1366, 1920] as const

test.describe('no horizontal scroll from 1024 px up (6.7)', () => {
  for (const width of widths) for (const [name, path] of screens) {
    test(`${name} at ${width}`, async ({ page }) => {
      await page.setViewportSize({ width, height: 768 })
      await page.goto('/demo/azure')
      await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
      await page.goto(path)
      await page.waitForLoadState('networkidle')
      expect(await sideScrolls(page)).toEqual([])
    })
  }

  test('the page itself never scrolls sideways on a 768 px tablet', async ({ page }) => {
    await page.setViewportSize({ width: 768, height: 1024 })
    await page.goto('/demo/azure')
    for (const [, path] of screens) {
      await page.goto(path)
      await page.waitForLoadState('networkidle')
      expect((await sideScrolls(page)).filter((s) => s.startsWith('page:'))).toEqual([])
    }
  })

  test('Recovery Ledger with an entry open (the docked pane takes 360 px)', async ({ page }) => {
    await page.goto('/demo/azure')
    await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
    await page.goto('/advanced/ledger')
    await page.getByRole('button', { name: /^Details of ledger entry/ }).first().click()
    await expect(page.locator('aside[aria-label="Entry"]')).toBeVisible()
    await page.waitForLoadState('networkidle')
    expect(await sideScrolls(page)).toEqual([])
  })
})
