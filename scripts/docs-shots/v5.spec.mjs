// 5.0.0 guide screenshots: the real app on localhost, one cloud at a time, 1366x768, numbered callouts (lib.mjs `shot`).
//   CLOUD=azure|aws|gcp DOCS_BASE=http://localhost:3000 node scripts/docs-shots/v5.spec.mjs
// Read-only by default: it opens screens, dialogs and the replay preview, and never presses a button that changes anything.
// With REPLAY_ONE=1 it also replays ONE message from the first dead-letter list (a real replay on that cloud) to capture the result.
// It never types a credential: the Connect dialog is shown empty and closed.
import { chromium } from 'playwright'
import { shot, finishRun, ROOT } from './lib.mjs'

const CLOUD = process.env.CLOUD || 'azure'
const BASE = process.env.DOCS_BASE || 'http://localhost:3000'
const API = process.env.DOCS_API || 'http://localhost:5153/api/v1'
const OUT = `${ROOT}/docs/screenshots/${CLOUD}-5`
const LABEL = { azure: 'Azure', aws: 'AWS', gcp: 'Google Cloud' }[CLOUD]
const STORE = { azure: 'service-bus', aws: 'sqs', gcp: 'pubsub' }[CLOUD]

const browser = await chromium.launch({ channel: 'chrome' })
const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 }, deviceScaleFactor: 2 })
const page = await ctx.newPage()
const failures = []
// Real identifiers never reach a documentation picture: a Google project id and any 12-digit AWS account id are replaced in the page text before every shot.
await page.addInitScript(() => {
  const scrub = (n) => { if (n.nodeType === 3) { const t = n.nodeValue.replace(/servicehub-\d{6}/g, 'my-gcp-project').replace(/\b\d{12}\b/g, '123456789012'); if (t !== n.nodeValue) n.nodeValue = t } else n.childNodes.forEach(scrub) }
  new MutationObserver(() => scrub(document.body ?? document)).observe(document, { childList: true, subtree: true, characterData: true })
})

const nsList = await (await fetch(`${API}/namespaces`)).json()
const ns = nsList.find((n) => n.provider === CLOUD && n.authType !== 'defaultAzureCredential')
if (!ns) throw new Error(`no ${CLOUD} connection registered`)
const DETAIL = `/${CLOUD}/c/${STORE}/${ns.id}`

async function go(path, wait = 6500) {
  await page.goto(BASE + path, { waitUntil: 'domcontentloaded' })
  await page.addStyleTag({ content: 'servicehub-bot{display:none!important}' })
  await page.waitForTimeout(wait)
}
const ONLY = process.env.ONLY ? process.env.ONLY.split(',') : null
async function step(name, fn) {
  if (ONLY && !ONLY.some((o) => name.startsWith(o))) return
  try { await fn() } catch (e) { failures.push(`${name}: ${String(e.message).split('\n')[0].slice(0, 140)}`); console.log('FAILED', name, String(e.message).split('\n')[0].slice(0, 140)) }
}
const f = (n) => `${OUT}/${n}.png`
const main = () => page.locator('main')
const text = (s, o) => page.getByText(s, o)

// Home, all clouds
await step('01-home-all', async () => {
  await go('/')
  await shot(page, f('01-home-all'), [
    { n: 1, loc: page.getByText('All clouds', { exact: true }).nth(1), t: 'The cloud switcher. All clouds shows every connection side by side; Azure, AWS and Google Cloud narrow every page to one cloud. The cloud is also in the address, so a link shows someone exactly what you see.' },
    { n: 2, loc: text('Needs your attention', { exact: true }).first(), t: 'What is waiting for a person across the clouds you connected. It is the number to watch.' },
    { n: 3, loc: page.getByText('No total across clouds.').first(), t: 'ServiceHub does not add the clouds together: one counts exactly, one approximately, one not at all, so a sum would mean nothing. Each cloud keeps its own number.' },
    { n: 4, loc: page.getByText('Emergency stop', { exact: true }).first(), t: 'Emergency stop. Switches off everything ServiceHub does on its own. It does not touch a replay a person starts.' },
  ])
})

// Connections
await step('02-connections', async () => {
  await go('/connections', 4500)
  await shot(page, f('02-connections'), [
    { n: 1, loc: main().locator('h1').first(), t: 'Every cloud ServiceHub is connected to. Connecting is how ServiceHub gets read access to your queues and topics.' },
    { n: 2, loc: page.getByRole('button', { name: /Connect a cloud/ }).first(), t: 'Connect a cloud opens the connect window. Your credentials are encrypted before they are stored and never shown again.' },
  ])
})

// Connect dialog (never filled)
await step('03-connect-dialog', async () => {
  await go(`/${CLOUD}?dialog=connect`, 4500)
  await page.getByRole('dialog').getByText({ azure: 'Azure Service Bus', aws: 'AWS SQS / SNS', gcp: 'Google Pub/Sub' }[CLOUD], { exact: true }).first().click({ timeout: 3000 }).catch(() => {})
  await page.waitForTimeout(800)
  await shot(page, f('03-connect-dialog'), [
    { n: 1, loc: page.getByRole('dialog').getByText({ azure: 'Azure Service Bus', aws: 'AWS SQS / SNS', gcp: 'Google Pub/Sub' }[CLOUD], { exact: true }).first(), t: `Pick the cloud. This guide uses ${LABEL}.` },
    { n: 2, loc: page.getByRole('dialog').getByText('Environment', { exact: true }).first(), t: 'Say what this connection is: Development, UAT or Production. Start with Development. Production gets the strictest rules.' },
    { n: 3, loc: page.getByRole('dialog').getByText('What ServiceHub may do here').first(), t: 'Read only, or Read and send. Read only means ServiceHub can look and can never replay on this connection. Nothing is saved until you press Connect, at the bottom of this window.' },
  ])
})

// Cloud home
await step('04-home-cloud', async () => {
  await go(`/${CLOUD}`, 9000)
  await shot(page, f('04-home-cloud'), [
    { n: 1, loc: page.getByText('Operational Tools', { exact: true }).first(), t: 'Operational Tools: the pages you use every day, the same for every cloud. Start with Needs your attention.' },
    { n: 2, loc: page.getByText('PaaS Components', { exact: true }).first(), t: `The ${LABEL} services ServiceHub can read. An amber dot means a component has not been proven against a real cloud yet.` },
    { n: 3, loc: page.getByText('Agent running').first(), t: 'The Agent is on. It watches and only replays what it has earned the right to. Hover the (i) beside it to see exactly what it may do.' },
    { n: 4, loc: page.getByRole('region', { name: 'Cloud health' }), t: 'Cloud health: how many of this cloud\u2019s components ServiceHub can reach right now.' },
    { n: 5, loc: page.getByRole('region', { name: new RegExp(`${LABEL} PaaS components|PaaS components`) }).first(), t: 'Every component you connected, whether it is reachable, what ServiceHub may do on it (Read only, or Read + Send), what is dead-lettered and what needs you. View opens it; the three dots hold Deallocate.' },
  ])
})

// A connection: overview, dead letters, a message, the replay preview
if (process.env.LOOK === '1') await step('05a-look-now', async () => {
  await go(`${DETAIL}?tab=dead-letters`, 8000)
  const b = page.getByRole('button', { name: /^Look now/ }).first()
  if (await b.count()) {
    await shot(page, f('05a-look-now'), [{ n: 1, loc: b, t: `${LABEL} is not watched in the background: there is no safe way to count dead letters without a delivery attempt. Look now records what is in the dead-letter queues right now, once, never faster than the cloud allows.` }])
    await b.click(); await page.waitForTimeout(20000)
  }
})
await step('05-detail-overview', async () => {
  await go(DETAIL, 9000)
  await shot(page, f('05-detail-overview'), [
    { n: 1, loc: page.getByRole('link', { name: /^Dead-letter messages/ }).first(), t: 'The tabs of this connection. Dead-letter messages is where you read and replay; Recovery shows what was replayed and whether it held; Access shows what ServiceHub may do here; Audit is every action.' },
    { n: 2, loc: text('In dead-letter now').first(), t: 'How many messages are dead-lettered right now, and whether the number is exact or approximate on this cloud.' },
    { n: 3, loc: page.getByRole('button', { name: /Send a test message/ }).first(), t: 'Sends one harmless test message to a queue you choose, so you can watch it travel. Optional.' },
  ])
})
await step('06-dead-letters', async () => {
  await go(`${DETAIL}?tab=dead-letters`, 9000)
  await shot(page, f('06-dead-letters'), [
    { n: 1, loc: page.getByRole('combobox').first(), t: 'Filter by queue, topic or state (Stuck now, No longer stuck, all).' },
    { n: 2, loc: page.getByPlaceholder(/Message ID or reason/).first(), t: 'Search by message id or by the reason it failed. It never searches inside message bodies.' },
    { n: 3, loc: page.getByRole('button', { name: /Replay selected/ }).first(), t: 'Tick one or more messages, then Replay selected. A preview opens first; nothing is sent until you confirm.' },
    { n: 4, loc: page.getByRole('link', { name: /^Replay (all|first)/ }).first(), t: 'Replay all N… opens the preview of every message that matches the filters (at most 500 in one replay, so it says "first 500" when more match). Nothing is sent until you confirm in the preview.' },
    { n: 5, loc: page.getByRole('link', { name: /^Open message/ }).first(), t: 'Open one message to read why it failed, its body and its history.' },
  ])
})
let firstId = null
await step('07-message', async () => {
  await go(`${DETAIL}?tab=dead-letters`, 9000)
  const link = page.locator('tr:has(input[type=checkbox])').getByRole('link', { name: /^Open message/ }).first()
  firstId = (await link.getAttribute('href'))?.split('id=')[1]
  await link.click(); await page.waitForTimeout(3500)
  await shot(page, f('07-message'), [
    { n: 1, loc: page.getByRole('dialog').getByText('Why it failed').first(), t: 'The reason and description the cloud recorded, and how many others failed the same way.' },
    { n: 2, loc: page.getByRole('dialog').getByText('Message body').first(), t: 'The message exactly as it was sent. ServiceHub never changes a message body.' },
    { n: 3, loc: page.getByRole('dialog').getByRole('link', { name: /^Replay/ }).first(), t: 'Replay… opens the preview. It is always a preview first.' },
  ])
})
await step('08-replay-preview', async () => {
  await page.getByRole('dialog').getByRole('link', { name: /^Replay/ }).first().click(); await page.waitForTimeout(4000)
  await shot(page, f('08-replay-preview'), [
    { n: 1, loc: page.getByRole('dialog').getByText('THE MESSAGE', { exact: false }).first(), t: 'The message that would be replayed, exactly as it is stored, and where it would go back to.' },
    { n: 2, loc: page.getByRole('dialog').getByRole('button', { name: /Replay 1 message/ }).first(), t: 'Replay 1 message sends it back. This is the only button that changes anything, and ServiceHub then watches whether the fix held.' },
    { n: 3, loc: page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).first(), t: 'Cancel closes the preview. Nothing has been sent.' },
  ])
})

// Operational tools
const pages = [
  ['09-attention', `/${CLOUD}/attention`, 'Needs your attention', 'Everything on this cloud that waits for a person: replays the Agent is holding, and items ServiceHub handed off. Review opens the decision.'],
  ['10-approvals', `/${CLOUD}/approvals`, 'Pending approvals', 'Replays the Agent stopped on because this kind of failure has not yet earned automatic replay. You approve or decline each, and the answer is recorded.'],
  ['11-active', `/${CLOUD}/active`, 'Active messages', 'Messages in the live queues now, not the dead-letter queues. Read-only.'],
  ['12-replays', `/${CLOUD}/replays`, 'Replay history', 'Every replay: who or what started it, how many messages, and the outcome ServiceHub saw afterwards (Watching, Verified, Came back, Unverified).'],
  ['13-rules', `/${CLOUD}/rules`, 'Auto Replay rules', 'What the Agent may replay by itself. A rule is on only for a failure pattern that has earned it; it stops and asks a person when results turn bad.'],
  ['14-signatures', `/${CLOUD}/signatures`, 'Failure signatures', 'Failures grouped by how they fail, so a thousand messages become a handful of causes.'],
  ['15-ledger', `/${CLOUD}/ledger`, 'Evidence ledger', 'The tamper-evident record of every decision. Export it and check it offline with verify-recovery-chain.py.'],
  ['16-agents', `/${CLOUD}/agents`, 'Agents', 'The background workers: what each does, when it last ran and whether it is healthy.'],
]
for (const [file, path, h1, t] of pages) {
  await step(file, async () => {
    await go(path, 8000)
    await shot(page, f(file), [{ n: 1, loc: main().locator('h1').first(), t }])
  })
}

// Approval review window
await step('17-review', async () => {
  await go(`/${CLOUD}/approvals`, 8000)
  const r = page.getByRole('link', { name: /^Review/ }).first()
  if (!(await r.count())) { console.log('no approval waiting on', CLOUD); return }
  await r.click(); await page.waitForTimeout(4000)
  await shot(page, f('17-review'), [
    { n: 1, loc: page.getByRole('dialog').getByText('The Agent stopped and asked you').first(), t: 'Why the Agent stopped: this kind of failure has not yet earned automatic replay, so a person decides.' },
    { n: 2, loc: page.getByRole('dialog').getByRole('button', { name: /^Decline/ }).first(), t: 'Decline needs a reason. Nothing is sent, and the reason goes in the ledger.' },
    { n: 3, loc: page.getByRole('dialog').getByRole('button', { name: /^Approve/ }).first(), t: 'Approve replays the messages listed here, once, through the same safety gate as a manual replay.' },
  ])
})

// Shell furniture
await step('18-help', async () => {
  await go(`/${CLOUD}?panel=help`, 5000)
  await shot(page, f('18-help'), [{ n: 1, loc: page.getByRole('dialog').getByPlaceholder(/Search help/).first(), t: 'Search the Help. Below it: short answers by task, then the step-by-step guide for each cloud. Press ? from any page to open Help.' }])
})
await step('19-search', async () => {
  await go(`/${CLOUD}?dialog=search`, 4500)
  await shot(page, f('19-search'), [{ n: 1, loc: page.getByRole('dialog').getByRole('combobox').or(page.getByRole('dialog').getByRole('textbox')).first(), t: 'Search finds a connection, a queue, a page or an action from anywhere (Cmd-K or Ctrl-K). It never searches inside messages.' }])
})
await step('20-emergency-stop', async () => {
  await go(`/${CLOUD}?dialog=emergency-stop`, 4500)
  await shot(page, f('20-emergency-stop'), [{ n: 1, loc: page.getByRole('dialog').getByText('What stops').first(), t: 'Exactly what stops (the Agent, automatic replays) and what does not (a replay a person starts, what is already recorded). Below: a reason, and you type STOP to confirm.' }])
})
await step('21-configuration', async () => {
  await go('/configuration', 5000)
  await shot(page, f('21-configuration'), [{ n: 1, loc: main().locator('h1').first(), t: 'Configuration: how ServiceHub looks, who may do what, backup and restore, and notifications.' }])
})
await step('22-coverage', async () => {
  await go('/coverage', 5000)
  await shot(page, f('22-coverage'), [{ n: 1, loc: main().locator('h1').first(), t: 'What ServiceHub covers: every component, per cloud, and how far each has been proven (Supported, or Being validated).' }])
})
await step('23-add-component', async () => {
  await go(`/${CLOUD}?dialog=add-component`, 6000)
  await shot(page, f('23-add-component'), [{ n: 1, loc: page.getByRole('dialog').getByRole('tab').first(), t: 'Add a component: pick the cloud, then tick each service to list its failed runs or dead-lettered events. Access is checked first, read-only. Unticking keeps every record it produced.' }])
})

// Optional: one real replay, for the result picture
if (process.env.REPLAY_ONE === '1' && firstId) {
  await step('24-replay-result', async () => {
    await go(`${DETAIL}?tab=dead-letters&dialog=replay&id=${firstId}`, 6000)
    await page.getByRole('dialog').getByRole('button', { name: /^(Replay|Send|Confirm)/ }).last().click()
    await page.waitForTimeout(9000)
    await shot(page, f('24-replay-result'), [{ n: 1, loc: page.getByRole('dialog').getByText('Sent back', { exact: true }).first(), t: 'The result, in plain words: the message is back on its queue and the dead letter has left the list. Sent back is not the same as fixed. ServiceHub now watches it, and marks it Verified or Came back.' }])
  })
}

const unreached = finishRun(OUT)
console.log(failures.length ? `\n${failures.length} FAILED:\n${failures.join('\n')}` : '\nall shots taken', `(unreached controls: ${unreached})`)
await browser.close()
process.exit(failures.length ? 1 : 0)
