import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'

/**
 * Accessibility gate (unit 6.6): every screen, in a real browser, against WCAG 2.x A + AA — colour contrast included.
 * Zero violations, or the build fails. Fix the element; never disable the rule.
 */
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

async function enterDemo(page: Page) {
  await page.goto('/demo/azure')
  await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
}

async function violations(page: Page) {
  const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  return result.violations.map((v) => `${v.id} (${v.impact}): ${v.help}\n  ${v.nodes.slice(0, 3).map((n) => `${n.target.join(' ')} — ${n.failureSummary?.split('\n')[1] ?? ''}`).join('\n  ')}`)
}

test.describe('axe, zero violations (6.6)', () => {
  for (const [name, path] of screens) {
    test(name, async ({ page }) => {
      await enterDemo(page)
      await page.goto(path)
      await page.waitForLoadState('networkidle')
      expect(await violations(page)).toEqual([])
    })
  }

  test('a message drawer', async ({ page }) => {
    await enterDemo(page)
    await page.goto('/?tab=dlq')
    await page.getByRole('link', { name: /^Details of message/ }).first().click()
    await expect(page.getByRole('dialog')).toBeVisible()
    expect(await violations(page)).toEqual([])
  })

  // States that only exist after an action — the ones a static page scan never reaches (found in the 6.6 colour audit:
  // white-on-amber and white-on-green buttons that sat inside them).
  test('the bell panel, open', async ({ page }) => {
    await enterDemo(page)
    await page.goto('/')
    await page.getByRole('button', { name: /^(Waiting for you|Needs you|Nothing waiting)/ }).first().click()
    await expect(page.getByRole('dialog').or(page.getByRole('region', { name: /waiting/i })).first()).toBeVisible()
    expect(await violations(page)).toEqual([])
  })

  test('the replay proposal', async ({ page }) => {
    await enterDemo(page)
    await page.goto('/?tab=dlq')
    await page.getByRole('link', { name: /^Details of message/ }).first().click()
    await page.getByRole('button', { name: /Replay this message/ }).click()
    await expect(page.getByRole('dialog', { name: /replay/i })).toBeVisible()
    expect(await violations(page)).toEqual([])
  })

  test('the Bulk Replay preview', async ({ page }) => {
    await enterDemo(page)
    await page.goto('/?tab=dlq')
    await page.getByRole('checkbox').nth(1).check()
    await page.getByRole('link', { name: /Replay selected/ }).click()
    await expect(page.getByRole('dialog', { name: /bulk replay/i })).toBeVisible()
    await page.waitForLoadState('networkidle')
    expect(await violations(page)).toEqual([])
  })
})
