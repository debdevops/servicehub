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

test.describe('no horizontal scroll at 1366×768 (6.7)', () => {
  for (const [name, path] of screens) {
    test(name, async ({ page }) => {
      await page.goto('/demo/azure')
      await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
      await page.goto(path)
      await page.waitForLoadState('networkidle')
      expect(await sideScrolls(page)).toEqual([])
    })
  }

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
