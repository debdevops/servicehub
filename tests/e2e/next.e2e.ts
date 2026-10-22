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

test.describe('5.0.0 shell: the left menu', () => {
  // Found on the live app: a long name ("Kinesis, DynamoDB streams") ran into its "On request" chip because rows had a fixed height.
  for (const scope of ['azure', 'aws', 'gcp'] as const) {
    test(`${scope}: no menu row lets its text spill out, at a narrow window too`, async ({ page }) => {
      await page.setViewportSize({ width: 1100, height: 700 })
      await enterDemo(page)
      await open(page, `/next/${scope}`)
      // A row that is too short for its wrapped text lets the text spill out of it (scrollHeight > clientHeight).
      const overlaps = await page.evaluate(() => Array.from(document.querySelectorAll('aside li a, aside li > span, nav li a, nav li > span'))
        .filter((el) => el.scrollHeight > el.clientHeight + 1)
        .map((el) => el.textContent ?? ''))
      expect(overlaps).toEqual([])
    })
  }
})

test.describe('5.0.0 shell: a component that is not a message store (Step Functions)', () => {
  test('its list, its failed runs, one run opened, and its Access tab work in the demo, with axe clean', async ({ page }) => {
    const failed: string[] = []
    page.on('pageerror', (e) => failed.push(e.message))
    await enterDemo(page)

    await open(page, '/next/aws/c/step-functions')
    await sound(page, 'step-functions list')
    expect(await violations(page), 'list').toEqual([])
    await page.getByRole('link', { name: /^View / }).first().click()

    await expect(page.getByRole('table', { name: /Failed Step Functions runs/ })).toBeVisible()
    await sound(page, 'step-functions page')
    expect(await violations(page), 'failed runs').toEqual([])
    // Read-only: nothing on the page offers to run, replay or purge a run.
    await expect(page.getByRole('region').getByRole('button', { name: /replay|re-run|redrive|purge|retry/i })).toHaveCount(0)

    await page.getByRole('button', { name: /^Show details of / }).first().click()
    await expect(page.getByRole('heading', { name: 'Failed: run-1', exact: false }).first()).toBeVisible()
    await expect(page.getByText(/Reason/).first()).toBeVisible()
    expect(await violations(page), 'run opened').toEqual([])

    await page.getByRole('link', { name: 'Access', exact: true }).click()
    await expect(page.getByText('Can read Step Functions')).toBeVisible()
    expect(await violations(page), 'access').toEqual([])
    expect(failed).toEqual([])
  })

  test('Workflows on Google Cloud: list, failed runs, one run opened, axe clean', async ({ page }) => {
    const failed: string[] = []
    page.on('pageerror', (e) => failed.push(e.message))
    await enterDemo(page)

    await open(page, '/next/gcp/c/workflows')
    await sound(page, 'workflows list')
    await page.getByRole('link', { name: /^View / }).first().click()

    await expect(page.getByRole('table', { name: /Failed Workflows runs/ })).toBeVisible()
    await expect(page.getByText('Cancelled', { exact: true }).first()).toBeVisible()
    await sound(page, 'workflows page')
    expect(await violations(page), 'workflows failed runs').toEqual([])
    await page.getByRole('button', { name: /^Show details of / }).first().click()
    await expect(page.getByText(/HTTP server responded with error code 503|Error in step/).first()).toBeVisible()
    expect(await violations(page), 'workflows run opened').toEqual([])
    expect(failed).toEqual([])
  })

  test('it is in the AWS menu and not under Later (On request)', async ({ page }) => {
    await enterDemo(page)
    await open(page, '/next/aws')
    const menu = page.getByRole('navigation', { name: 'ServiceHub' })
    await expect(menu.getByRole('link', { name: /Step Functions/ })).toBeVisible()
    await expect(menu.locator('li', { hasText: 'On request' }).getByText('Step Functions')).toHaveCount(0)
    await open(page, '/next/gcp')
    await expect(page.getByRole('navigation', { name: 'ServiceHub' }).getByRole('link', { name: /Workflows/ })).toBeVisible()
  })
})
