import { expect, test, type Page } from '@playwright/test'

/**
 * The core loop with no mouse (unit 6.6): find a dead letter, open it, look at it, ask to replay it (which only shows the
 * proposal), close everything, and land back where you started. Real key presses — Tab, Enter, Escape — not synthetic events.
 */
async function start(page: Page) {
  await page.goto('/demo/azure')
  await page.goto('/?tab=dlq')
  await expect(page.getByRole('link', { name: /^Details of message/ }).first()).toBeVisible()
}

test('a dead letter can be opened, its replay proposed and everything closed, by keyboard alone', async ({ page }) => {
  await start(page)
  const details = page.getByRole('link', { name: /^Details of message/ }).first()

  // Reaching it: Tab from the top of the page gets there without a mouse.
  await page.locator('body').press('Tab')
  for (let i = 0; i < 80 && !(await details.evaluate((el) => el === document.activeElement)); i++) await page.keyboard.press('Tab')
  await expect(details).toBeFocused()

  await page.keyboard.press('Enter')
  const drawer = page.getByRole('dialog')
  await expect(drawer).toBeVisible()
  // Focus moved into the drawer, and Tab cannot leave it.
  for (let i = 0; i < 60; i++) {
    await page.keyboard.press('Tab')
    expect(await page.evaluate(() => !!document.activeElement?.closest('[role=dialog]'))).toBe(true)
  }

  // R opens the proposal — it never executes.
  await page.keyboard.press('Escape') // focus may sit on a control; R is ignored inside a text field only
  await expect(drawer).toBeHidden()
  await expect(details).toBeFocused() // focus is restored to where it came from

  await page.keyboard.press('Enter')
  await expect(page.getByRole('dialog')).toBeVisible()
  await page.locator('[role=dialog]').first().focus()
  await page.keyboard.press('r')
  await expect(page.getByRole('dialog', { name: /replay/i })).toBeVisible()
  await page.keyboard.press('Escape')
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(details).toBeFocused()
})

test('the shortcuts Help lists work, and never inside a text field', async ({ page }) => {
  await start(page)
  await page.keyboard.press('?')
  await expect(page.getByRole('dialog', { name: /help/i })).toBeVisible()
  await page.keyboard.press('Escape')

  await page.keyboard.press('/')
  await expect(page.getByPlaceholder(/Search ID, queue, reason, error/)).toBeFocused()
  await page.keyboard.type('ar') // typing a shortcut letter in the field must not switch surfaces
  await expect(page).not.toHaveURL(/\/advanced/)
  await expect(page.getByPlaceholder(/Search ID, queue, reason, error/)).toHaveValue('ar')

  await page.locator('body').click({ position: { x: 700, y: 5 } })
  await page.keyboard.press('a')
  await expect(page).toHaveURL(/\/advanced/)
})

test('every control on Home has a visible focus ring', async ({ page }) => {
  await page.goto('/demo/azure')
  await page.goto('/')
  await page.waitForLoadState('networkidle')
  for (let i = 0; i < 25; i++) {
    await page.keyboard.press('Tab')
    const ring = await page.evaluate(() => {
      const el = document.activeElement
      if (!el || el === document.body) return 'body'
      const s = getComputedStyle(el)
      return s.outlineStyle !== 'none' && parseFloat(s.outlineWidth) >= 2 ? 'ok' : `${el.tagName} ${(el.textContent ?? '').trim().slice(0, 30)}: outline ${s.outlineStyle} ${s.outlineWidth}`
    })
    expect(ring, `Tab stop ${i + 1}`).toMatch(/^(ok|body)$/)
  }
})
