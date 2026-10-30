import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'

/**
 * The 5.0.0 shell (`/next`, build-plan unit I4), in a real browser and in DEMO MODE like the rest of this folder: every page opens with
 * content on each cloud and on all three, nothing falls back to an error, axe finds nothing with colour contrast ON, and the
 * keyboard routes (⌘K Search, Help's guides, Show limits) work. Served from the production build, so it is the bundle people get.
 */
const pages = ['', '/attention', '/approvals', '/active', '/replays', '/rules', '/signatures', '/ledger', '/agents'] as const
const scopes = ['azure', 'aws', 'gcp', 'all'] as const
const standalone = ['/next/connections', '/next/configuration', '/next/coverage'] as const
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

/**
 * Axe on what a page itself draws, leaving out the shell's header: the header's colours are the cloud brand colours and have their own
 * checks. Used by the tests of the four components that were on request, so a header colour does not hide a defect in their pages.
 */
async function contentViolations(page: Page) {
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

  test('Cloud Scheduler on Google Cloud: list, jobs whose last attempt failed, one job opened, axe clean', async ({ page }) => {
    const failed: string[] = []
    page.on('pageerror', (e) => failed.push(e.message))
    await enterDemo(page)

    await open(page, '/next/gcp/c/cloud-scheduler')
    await sound(page, 'cloud scheduler list')
    await page.getByRole('link', { name: /^View / }).first().click()

    await expect(page.getByRole('table', { name: /Failed Cloud Scheduler runs/ })).toBeVisible()
    await expect(page.getByText(/Last attempt failed: Unavailable/).first()).toBeVisible()
    await sound(page, 'cloud scheduler page')
    expect(await violations(page), 'cloud scheduler failed jobs').toEqual([])
    await page.getByRole('button', { name: /^Show details of / }).first().click()
    await expect(page.getByText(/The target did not answer/).first()).toBeVisible()
    expect(await violations(page), 'cloud scheduler job opened').toEqual([])
    expect(failed).toEqual([])
  })

  test('Logic Apps on Azure: list, failed runs by Azure’s own words, one run opened, axe clean', async ({ page }) => {
    const failed: string[] = []
    page.on('pageerror', (e) => failed.push(e.message))
    await enterDemo(page)

    await open(page, '/next/azure/c/logic-apps')
    await sound(page, 'logic-apps list')
    await page.getByRole('link', { name: /^View / }).first().click()

    await expect(page.getByRole('table', { name: /Failed Logic Apps runs/ })).toBeVisible()
    await expect(page.getByText('Timed out', { exact: true }).first()).toBeVisible()
    await expect(page.getByText('Cancelled', { exact: true }).first()).toBeVisible()
    await sound(page, 'logic-apps page')
    expect(await contentViolations(page), 'logic apps failed runs').toEqual([])
    await expect(page.getByRole('region').getByRole('button', { name: /replay|re-run|resubmit|redrive|purge|retry/i })).toHaveCount(0)
    await page.getByRole('button', { name: /^Show details of / }).first().click()
    await expect(page.getByText(/Send_to_warehouse|waited longer than/).first()).toBeVisible()
    expect(await contentViolations(page), 'logic apps run opened').toEqual([])
    expect(failed).toEqual([])
  })

  test('Event Grid on Azure: failed records, a dead-lettered event opened with earliest and latest, axe clean', async ({ page }) => {
    const failed: string[] = []
    page.on('pageerror', (e) => failed.push(e.message))
    await enterDemo(page)

    await open(page, '/next/azure/c/event-grid')
    await sound(page, 'event-grid list')
    await page.getByRole('link', { name: /^View / }).first().click()

    await expect(page.getByRole('table', { name: /Failed Event Grid records/ })).toBeVisible()
    await expect(page.getByRole('heading', { name: /Failed records/ })).toBeVisible()
    await expect(page.getByText('Dead-lettered', { exact: true }).first()).toBeVisible()
    await sound(page, 'event-grid page')
    expect(await contentViolations(page), 'event grid records').toEqual([])
    await expect(page.getByText(/does not move work on for Event Grid/)).toBeVisible()
    await page.getByRole('button', { name: /^Show details of / }).first().click()
    await expect(page.getByText('MaxDeliveryAttemptsExceeded').first()).toBeVisible()
    await expect(page.getByText('Earliest', { exact: true })).toBeVisible()
    await expect(page.getByText('Latest', { exact: true })).toBeVisible()
    expect(await contentViolations(page), 'event grid record opened').toEqual([])
    expect(failed).toEqual([])
  })

  test('Kinesis and DynamoDB stream failures on AWS: each record says it points at the original, axe clean', async ({ page }) => {
    const failed: string[] = []
    page.on('pageerror', (e) => failed.push(e.message))
    await enterDemo(page)

    await open(page, '/next/aws/c/aws-streams')
    await sound(page, 'aws-streams list')
    await page.getByRole('link', { name: /^View / }).first().click()

    await expect(page.getByRole('table', { name: /Failed Kinesis, DynamoDB streams records/ })).toBeVisible()
    await expect(page.getByText('Points at the original, which may have expired.').first()).toBeVisible()
    await sound(page, 'aws-streams page')
    expect(await contentViolations(page), 'stream failures').toEqual([])
    await page.getByRole('button', { name: /^Show details of / }).first().click()
    await expect(page.getByText(/sequence 4959 to 4961/).first()).toBeVisible()
    expect(await contentViolations(page), 'stream failure opened').toEqual([])
    expect(failed).toEqual([])
  })

  test('Eventarc on Google Cloud: undelivered events, one opened, axe clean', async ({ page }) => {
    const failed: string[] = []
    page.on('pageerror', (e) => failed.push(e.message))
    await enterDemo(page)

    await open(page, '/next/gcp/c/eventarc')
    await sound(page, 'eventarc list')
    await page.getByRole('link', { name: /^View / }).first().click()

    await expect(page.getByRole('table', { name: /Failed Eventarc records/ })).toBeVisible()
    await expect(page.getByText('Undelivered', { exact: true }).first()).toBeVisible()
    await sound(page, 'eventarc page')
    expect(await contentViolations(page), 'eventarc records').toEqual([])
    await page.getByRole('button', { name: /^Show details of / }).first().click()
    await expect(page.getByText(/HTTP 503 from the Cloud Run service/).first()).toBeVisible()
    expect(await contentViolations(page), 'eventarc record opened').toEqual([])
    expect(failed).toEqual([])
  })

  test('all four components that were on request are in their cloud’s menu', async ({ page }) => {
    await enterDemo(page)
    for (const [scope, names] of [['azure', [/Event Grid/, /Logic Apps/]], ['aws', [/Kinesis, DynamoDB streams/]], ['gcp', [/Eventarc/]]] as const) {
      await open(page, `/next/${scope}`)
      const menu = page.getByRole('navigation', { name: 'ServiceHub' })
      for (const name of names) await expect(menu.getByRole('link', { name })).toBeVisible()
    }
  })

  test('it is in the AWS menu, and the menu has no Later (On request) section', async ({ page }) => {
    await enterDemo(page)
    await open(page, '/next/aws')
    const menu = page.getByRole('navigation', { name: 'ServiceHub' })
    await expect(menu.getByRole('link', { name: /Step Functions/ })).toBeVisible()
    await expect(page.getByText('Later (On request)')).toHaveCount(0)
    await open(page, '/next/gcp')
    await expect(page.getByRole('navigation', { name: 'ServiceHub' }).getByRole('link', { name: /Workflows/ })).toBeVisible()
  })
})

test.describe('5.0.0 shell: Add a component', () => {
  test('ticks and unticks a component, the menu follows, the (i) explains, axe clean', async ({ page }) => {
    const failed: string[] = []
    page.on('pageerror', (e) => failed.push(e.message))
    await enterDemo(page)
    await open(page, '/next/aws')
    const menu = page.getByRole('navigation', { name: 'ServiceHub' })
    await expect(menu.getByRole('link', { name: /Step Functions/ })).toBeVisible()

    await page.goto('/next/aws?dialog=add-component')
    const dialog = page.getByRole('dialog', { name: 'Add a component' })
    await expect(dialog.getByText(/never asks for a key/)).toBeVisible()
    // A message store is on and cannot be unticked here; a run history can.
    await expect(dialog.getByRole('checkbox', { name: 'SQS (Supported)' })).toBeDisabled()
    const stepFunctions = dialog.getByRole('checkbox', { name: 'Step Functions (Not yet validated)' })
    await expect(stepFunctions).toBeChecked()
    expect(await violations(page), 'add a component').toEqual([])

    // The (i) opens on hover and says what it never does.
    await dialog.getByRole('button', { name: /About Step Functions/ }).hover()
    await expect(page.getByText(/never starts, stops or redrives a run/)).toBeVisible()

    await stepFunctions.uncheck()
    await dialog.getByRole('button', { name: 'Save changes' }).click()
    await expect(dialog.getByText(/Removed\. Its history is kept/)).toBeVisible()
    await dialog.getByRole('button', { name: 'Done' }).click()
    await expect(menu.getByRole('link', { name: /Step Functions/ })).toHaveCount(0)

    // In-app, not a reload: the demo's world is written to the session a moment after a change.
    await page.getByRole('link', { name: /Add a component/ }).first().click()
    await dialog.getByRole('checkbox', { name: 'Step Functions (Not yet validated)' }).check()
    await expect(dialog.getByText(/No key to paste/)).toBeVisible()
    await dialog.getByRole('button', { name: 'Check access', exact: true }).click()
    await expect(dialog.getByRole('status').filter({ hasText: /states:ListExecutions\s*present/ })).toBeVisible()
    await dialog.getByRole('button', { name: 'Add 1 component' }).click()
    await expect(dialog.getByText(/Added\. It is on the menu now/)).toBeVisible()
    await dialog.getByRole('button', { name: 'Done' }).click()
    await expect(menu.getByRole('link', { name: /Step Functions/ })).toBeVisible()
    expect(failed).toEqual([])
  })
})

test.describe('5.0.0 shell: Connections', () => {
  test('every provider’s connections are in one menu page, with their components, and axe is clean', async ({ page }) => {
    const failed: string[] = []
    page.on('pageerror', (e) => failed.push(e.message))
    await enterDemo(page)
    await open(page, '/next')
    await page.getByRole('navigation', { name: 'ServiceHub' }).getByRole('link', { name: /^Connections/ }).click()
    await expect(page.getByRole('heading', { level: 1, name: /Connections/ })).toBeVisible()
    for (const cloud of ['Azure', 'AWS', 'Google Cloud']) {
      await expect(page.getByRole('region', { name: `${cloud} connections` })).toBeVisible()
    }
    await expect(page.getByRole('region', { name: 'AWS connections' }).getByText(/Components on/).first()).toBeVisible()
    await sound(page, 'connections')
    expect(await violations(page), 'connections').toEqual([])
    // From one connection's row the dialog opens on that connection.
    await page.getByRole('link', { name: /^Add a component to / }).first().click()
    await expect(page.getByRole('dialog', { name: 'Add a component' })).toBeVisible()
    expect(failed).toEqual([])
  })
})
