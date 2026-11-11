import { expect, test, type Page } from '@playwright/test'

/**
 * Performance / layout gate (unit 6.7): at 1366×768 — the machine someone actually has at 2 a.m. — nothing needs a horizontal
 * scroll: not the page, and not a table inside it. A table that hides its last column behind a side-scroll is a screen that
 * looks fine on the designer's monitor and loses the "Details" link on the real one.
 */
const screens: readonly (readonly [name: string, path: string])[] = [
  ['All clouds', '/'],
  ['Azure Home', '/azure'],
  ['AWS Home', '/aws'],
  ['Google Cloud Home', '/gcp'],
  ['Needs your attention', '/all/attention'],
  ['Pending approvals', '/all/approvals'],
  ['Active messages', '/all/active'],
  ['Replay history', '/all/replays'],
  ['Auto Replay rules', '/all/rules'],
  ['Failure signatures', '/all/signatures'],
  ['Evidence ledger', '/all/ledger'],
  ['Agents', '/all/agents'],
  ['Connections', '/connections'],
  ['Configuration', '/configuration'],
  ['What ServiceHub covers', '/coverage'],
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

/**
 * A laptop, the 1366 machine and a large monitor: nothing scrolls sideways, not even a table inside its own box. Below that
 * (1024, and a 768 tablet) the menu takes 224 px and a ten-column table may scroll inside its box; the PAGE still may not
 * (the last test). Changed with the 5.0.0 layout, which has a permanent left menu the 4.2.0 one did not.
 */
const widths = [1280, 1366, 1920] as const

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

  for (const width of [768, 1024]) {
    test(`the page itself never scrolls sideways at ${width} px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 1024 })
      await page.goto('/demo/azure')
      for (const [name, path] of screens) {
        await page.goto(path)
        await page.waitForLoadState('networkidle')
        expect((await sideScrolls(page)).filter((s) => s.startsWith('page:')), name).toEqual([])
      }
    })
  }
})

/**
 * 5.0.0 shell, 2026-10-23: the cloud cards on the first screen and the signatures panel on a cloud's Home sit in columns of 22–26 rem, and their
 * tables were wider than the column on real data (a connection called "ServiceHub Lab (dev) — AWS", a reason like "PaymentTimeout"),
 * so the whole page scrolled sideways while the demo's short names fit. The demo cannot show that, so this puts real-length names into
 * every cell of every table inside a narrow card, then asks whether the PAGE still fits.
 */
test.describe('cards with real-length names (5.0.0)', () => {
  for (const path of ['/', '/azure', '/aws', '/gcp']) {
    test(`${path} does not scroll the page sideways`, async ({ page }) => {
      await page.setViewportSize({ width: 1366, height: 768 })
      await page.goto('/demo/azure')
      await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
      await page.goto(path)
      await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
      await page.waitForLoadState('networkidle')
      const stretched = await page.evaluate(() => {
        let n = 0
        for (const table of document.querySelectorAll<HTMLTableElement>('section table')) {
          if (table.closest('section')!.getBoundingClientRect().width > 460) continue
          for (const cell of table.querySelectorAll<HTMLElement>('tbody td')) {
            if (cell.children.length === 0) cell.textContent = `${cell.textContent ?? ''} ServiceHub Lab (dev) — Production Payments`
            n++
          }
        }
        return n
      })
      test.skip(stretched === 0, 'this screen has no table in a narrow card')
      expect((await sideScrolls(page)).filter((s) => s.startsWith('page:'))).toEqual([])
    })
  }
})
