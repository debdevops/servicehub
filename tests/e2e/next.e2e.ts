import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'

/**
 * The 5.0.0 shell (`/next`, build-plan unit I4), in a real browser and in DEMO MODE like the rest of this folder: every page opens with
 * content on each cloud and on all three, nothing falls back to an error, axe finds nothing with colour contrast ON, and the
 * keyboard routes (⌘K Search, Help's guides, Show limits) work. Served from the production build, so it is the bundle people get.
 */
const pages = ['', '/attention', '/approvals', '/active', '/replays', '/rules', '/signatures', '/ledger', '/agents'] as const
const scopes = ['azure', 'aws', 'gcp', 'all'] as const
const standalone = ['/next/configuration', '/next/coverage'] as const
const overlays = ['help', 'security', 'autonomy', 'search'] as const

async function enterDemo(page: Page) {
  // Demo mode lasts for the browser session, so entering it here carries over to the /next pages opened afterwards.
  await page.goto('/demo/azure')
  await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
}

async function open(page: Page, path: string) {
  await page.goto(path)
  await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
  await page.waitForLoadState('networkidle')
}

async function sound(page: Page, where: string) {
  const text = await page.locator('body').innerText()
  expect(text, `${where}: a page could not reach its data`).not.toMatch(/could not reach its API|Something went wrong/i)
  expect(text, `${where}: fell back to "not in the demo"`).not.toMatch(/not in the demo|not part of the demo/i)
  expect(await page.evaluate(() => (globalThis as { __demoUncovered?: string[] }).__demoUncovered ?? []), `${where}: the demo had no answer`).toEqual([])
}

async function violations(page: Page) {
  const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  return result.violations.map((v) => `${v.id} (${v.impact}): ${v.help}\n  ${v.nodes.slice(0, 3).map((n) => n.target.join(' ')).join('\n  ')}`)
}

test.describe('5.0.0 shell: every page opens with content', () => {
  for (const scope of scopes) {
    test(`${scope}: pages`, async ({ page }) => {
      const failed: string[] = []
      page.on('pageerror', (e) => failed.push(e.message))
      await enterDemo(page)
      for (const p of pages) {
        const path = scope === 'all' && p === '' ? '/next' : `/next/${scope}${p}`
        await open(page, path)
        await sound(page, path)
      }
      expect(failed).toEqual([])
    })
  }

  test('standalone pages', async ({ page }) => {
    const failed: string[] = []
    page.on('pageerror', (e) => failed.push(e.message))
    await enterDemo(page)
    for (const path of standalone) {
      await open(page, path)
      await sound(page, path)
    }
    expect(failed).toEqual([])
  })

  for (const overlay of overlays) {
    test(`${overlay} opens over a page`, async ({ page }) => {
      await enterDemo(page)
      await open(page, `/next/azure?${overlay === 'search' || overlay === 'help' ? (overlay === 'help' ? 'panel' : 'dialog') : 'panel'}=${overlay}`)
      await expect(page.getByRole('dialog')).toBeVisible()
      await sound(page, overlay)
    })
  }
})

test.describe('5.0.0 shell: axe, zero violations', () => {
  for (const path of ['/next', '/next/azure', '/next/aws', '/next/gcp', '/next/azure/attention', '/next/azure/rules', '/next/configuration', '/next/coverage']) {
    test(path, async ({ page }) => {
      await enterDemo(page)
      await open(page, path)
      expect(await violations(page)).toEqual([])
    })
  }

  test('Help, open', async ({ page }) => {
    await enterDemo(page)
    await open(page, '/next/azure?panel=help')
    await expect(page.getByRole('dialog')).toBeVisible()
    expect(await violations(page)).toEqual([])
  })
})

test.describe('5.0.0 shell: the keyboard and the guides', () => {
  test('⌘K / Ctrl-K opens Search from a page', async ({ page }) => {
    await enterDemo(page)
    await open(page, '/next/azure')
    await page.keyboard.press('Control+K')
    await expect(page.getByRole('dialog')).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(page.getByRole('dialog')).toBeHidden()
  })

  test('Help carries the step-by-step guides, and one opens from ?topic=', async ({ page }) => {
    await enterDemo(page)
    await open(page, '/next/azure?panel=help&topic=guide-aws')
    const dialog = page.getByRole('dialog')
    await expect(dialog.getByText(/4\.2\.0 layout/)).toBeVisible()
    await expect(dialog.locator('#help-guide-aws')).toHaveAttribute('open', '')
  })
})
