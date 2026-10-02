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
    // The Help guides document the Advanced pages by name (Failure Signatures, Recovery Ledger), so their body is not scanned; the vitest guard
    // in HelpPanel.test.tsx keeps those words to the Advanced step and Part 1. The Help panel's own headings, buttons and labels still are.
    const inGuide = (el: Element | null) => !!el?.closest('[data-guide-body]')
    for (const el of document.querySelectorAll('body *')) {
      if (inGuide(el)) continue
      const label = el.getAttribute('aria-label')
      if (label) seen.add(label)
    }
    // Every text node, not innerText: collapsed sections are still one click away, so their words count.
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT)
    const lines: string[] = []
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
      const parent = n.parentElement
      if (parent && ['SCRIPT', 'STYLE', 'NOSCRIPT'].includes(parent.tagName)) continue
      if (inGuide(parent)) continue
      // A sentence split by <b> or <a> is joined back up, so a banned word cannot hide across an element boundary.
      const block = parent?.closest('p, li, h1, h2, h3, h4, dd, dt, label, button, a, span, div')
      if (!block) { lines.push(n.textContent ?? ''); continue }
      // The block may sit around a guide body (the Help panel's per-cloud summary does): read it without the guide.
      const copy = block.cloneNode(true) as Element
      copy.querySelectorAll('[data-guide-body]').forEach((g) => g.remove())
      lines.push(copy.textContent ?? '')
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
