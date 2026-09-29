import { expect, test, type Page } from '@playwright/test'

/**
 * The ease bar, U3 (ROADMAP §9): Simple speaks Simple's words. Words that belong to Advanced — signature, ledger, disposition,
 * autonomy level, grant, attestation, pillar — or a bare HTTP status must not appear on any Simple screen. Read from what a
 * person actually sees (demo mode), not from the source, so a string built at run time is caught too.
 */
const banned = /\b(signatures?|ledger|disposition|autonomy|attestation|pillar|L[345])\b|\bHTTP\s?\d{3}\b|\b(4\d\d|5\d\d) (error|status)\b/i

const simple: readonly (readonly [string, string])[] = [
  ['Home', '/'],
  ['Dead letters', '/?tab=dlq'],
  ['Active messages', '/?tab=active'],
  ['Replayed', '/?tab=replayed'],
  ['Auto Replay', '/?panel=rules'],
  ['Help', '/?panel=help'],
  ['Approve', '/?modal=approve'],
]

async function words(page: Page) {
  await page.waitForLoadState('networkidle')
  // Everything visible, including text only screen readers get (aria-labels, sr-only).
  return page.evaluate(() => {
    const seen = new Set<string>()
    for (const el of document.querySelectorAll('body *')) {
      const label = el.getAttribute('aria-label')
      if (label) seen.add(label)
    }
    // Every text node, not innerText: collapsed sections are still one click away, so their words count.
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT)
    const lines: string[] = []
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
      const parent = n.parentElement
      if (parent && ['SCRIPT', 'STYLE', 'NOSCRIPT'].includes(parent.tagName)) continue
      // A sentence split by <b> or <a> is joined back up, so a banned word cannot hide across an element boundary.
      lines.push(parent?.closest('p, li, h1, h2, h3, h4, dd, dt, label, button, a, span, div')?.textContent ?? n.textContent ?? '')
    }
    const body = [...new Set(lines)].join('\n')
    return `${body}\n${[...seen].join('\n')}`
  })
}

test.describe('Simple uses Simple’s words (U3)', () => {
  for (const [name, path] of simple) {
    test(name, async ({ page }) => {
      await page.goto('/demo/azure')
      await page.goto(path)
      const text = await words(page)
      const hit = text.split('\n').filter((l) => banned.test(l))
      expect(hit, 'Advanced vocabulary on a Simple screen').toEqual([])
    })
  }

  test('a message drawer and the replay proposal', async ({ page }) => {
    await page.goto('/demo/azure')
    await page.goto('/?tab=dlq')
    await page.getByRole('link', { name: /^Details of message/ }).first().click()
    await expect(page.getByRole('dialog')).toBeVisible()
    expect((await words(page)).split('\n').filter((l) => banned.test(l))).toEqual([])
    await page.getByRole('button', { name: /Replay this message/ }).click()
    await expect(page.getByRole('dialog', { name: /replay/i })).toBeVisible()
    expect((await words(page)).split('\n').filter((l) => banned.test(l))).toEqual([])
  })

  test('the Bulk Replay preview', async ({ page }) => {
    await page.goto('/demo/azure')
    await page.goto('/?tab=dlq')
    await page.getByRole('checkbox').nth(1).check()
    await page.getByRole('link', { name: /Replay selected/ }).first().click()
    await expect(page.getByRole('dialog', { name: /bulk replay/i })).toBeVisible()
    await expect(page.getByText('Preview — nothing has run yet')).toBeVisible()
    expect((await words(page)).split('\n').filter((l) => banned.test(l))).toEqual([])
  })
})
