// 5.0.0 walkthrough videos, one set per cloud, recorded from the real app on localhost (Playwright video): a visible cursor and a caption bar,
// so they read with the sound off.
//   MODE=short  about 1.5 minutes, for a person who works in this cloud and has never seen ServiceHub: where the pain is (a count, not a story;
//               replaying blind; nobody knows if it worked; the same failure again and again) and what ServiceHub gives (the message list,
//               the detail, a replay with a preview, proof it held, and the Agent that only acts once it has earned it).
//   MODE=full   every PaaS component of this cloud and every function: Connections, Add a component, each component's own page, the message
//               list, one message, a real single replay, the bulk preview, every tab, every Operational Tool, rules, signatures, the ledger,
//               agents, how autonomy is earned, security, Configuration, Coverage, Help, Search, Emergency stop.
//
//   CLOUD=azure|aws|gcp MODE=full|short DOCS_BASE=http://localhost:3000 VIDEO_RAW=<dir> node scripts/docs-shots/videos5.mjs
//
// Nothing here types a credential and nothing that changes something is confirmed except ONE message replayed in MODE=full (and Look now on
// AWS and Google). The Connect, Add a component, New rule and Emergency stop windows are opened and closed. No approval is answered.
// A cloud read that takes a long time (some components read the cloud for 10 to 20 seconds) is recorded and sped up afterwards (the caption says
// so); build-videos5.sh reads `<cloud>-<mode>.speed.json`. Exits non-zero (after saving) if a scene failed.
import { chromium } from 'playwright'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'

const BASE = process.env.DOCS_BASE || 'http://localhost:3000'
const CLOUD = process.env.CLOUD || 'azure'
const MODE = process.env.MODE || 'full'
const VERSION = fs.readFileSync(new URL('../../.version', import.meta.url), 'utf-8').trim()
const RAW = process.env.VIDEO_RAW || fs.mkdtempSync(path.join(os.tmpdir(), 'servicehub-video-'))
const C = {
  azure: { name: 'Azure', service: 'Azure Service Bus', tab: 'Azure Service Bus', store: 'Service Bus', key: 'service-bus', look: false, proves: true,
    others: [['Storage Queues', 'storage-queues', 'The poison queue: messages Azure Functions or Web Jobs gave up on'], ['Event Grid', 'event-grid', 'Events Event Grid could not deliver, with the reason'], ['Logic Apps', 'logic-apps', 'Failed runs, and a person can ask Azure to resubmit one']] },
  aws: { name: 'AWS', service: 'AWS SQS and SNS', tab: 'AWS SQS / SNS', store: 'SQS', key: 'sqs', look: true, proves: false,
    others: [['SNS', 'sns', 'The dead-letter queues of each SNS subscriber'], ['Step Functions', 'step-functions', 'Failed runs, and a person can ask AWS to redrive one'], ['Kinesis, DynamoDB streams', 'aws-streams', 'Failure records that point at the original, read only']] },
  gcp: { name: 'Google Cloud', service: 'Google Cloud Pub/Sub', tab: 'Google Pub/Sub', store: 'Pub/Sub', key: 'pubsub', look: true, proves: false,
    others: [['Eventarc', 'eventarc', 'Events a trigger could not deliver'], ['Workflows', 'workflows', 'Failed and cancelled runs']] },
}[CLOUD]
if (!C) throw new Error('CLOUD must be azure, aws or gcp')
const SHORT = MODE === 'short'
fs.mkdirSync(RAW, { recursive: true })
const browser = await chromium.launch({ channel: 'chrome' })
const nsList = await (await fetch(`${BASE}/api/v1/namespaces`)).json()
const ns = nsList.find((n) => n.provider === CLOUD && n.authType !== 'defaultAzureCredential')
if (!ns) throw new Error(`no ${CLOUD} connection registered`)
const DETAIL = `/${CLOUD}/c/${C.key}/${ns.id}`

const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 }, recordVideo: { dir: RAW, size: { width: 1366, height: 768 } } })
// A recorded video has no cursor and no captions, so draw both inside the page (they survive navigations).
await ctx.addInitScript(() => {
  const mount = () => {
    if (document.getElementById('__vcap')) return
    const st = document.createElement('style')
    st.textContent = `
      #__vcap{position:fixed;left:244px;bottom:22px;z-index:2147483646;max-width:640px;display:flex;gap:14px;align-items:center;
        background:rgba(15,23,42,.94);color:#fff;font:600 19px/1.3 system-ui,-apple-system,sans-serif;padding:14px 22px;border-radius:16px;
        box-shadow:0 8px 30px rgba(0,0,0,.35);opacity:0;transition:opacity .25s;pointer-events:none}
      #__vcap.on{opacity:1}
      #__vcap b{flex:none;min-width:34px;height:34px;padding:0 10px;border-radius:17px;background:#0ea5e9;display:flex;align-items:center;justify-content:center;font-size:14px;letter-spacing:.5px;text-transform:uppercase}
      #__vcap span{font-weight:500;opacity:.85}
      #__vcur{position:fixed;left:-50px;top:-50px;width:26px;height:26px;margin:-13px 0 0 -13px;border-radius:50%;z-index:2147483647;pointer-events:none;
        background:rgba(225,29,72,.25);border:3px solid #e11d48;transition:transform .12s}
      #__vcur.down{transform:scale(.6);background:rgba(225,29,72,.6)}
      #__vcard{position:fixed;inset:0;z-index:2147483645;display:none;flex-direction:column;align-items:center;justify-content:center;gap:18px;text-align:center;
        background:linear-gradient(135deg,#0b3b5c,#0f172a);color:#fff;font-family:system-ui,-apple-system,sans-serif;padding:60px}
      #__vcard.on{display:flex}
      #__vcard h1{font-size:60px;margin:0;font-weight:800;letter-spacing:-1px}
      #__vcard p{font-size:27px;margin:0;opacity:.85;max-width:1000px;line-height:1.4}
      #__vcard li{font-size:26px;text-align:left;margin:8px 0;opacity:.95}
      #__vcard .tag{font-size:20px;letter-spacing:3px;text-transform:uppercase;color:#7dd3fc;font-weight:700}`
    st.textContent += '#__vcap{pointer-events:none} servicehub-bot{display:none!important}'; document.head.append(st)
    const cap = document.createElement('div'); cap.id = '__vcap'; cap.innerHTML = '<b></b><div></div>'
    const cur = document.createElement('div'); cur.id = '__vcur'
    const card = document.createElement('div'); card.id = '__vcard'
    document.body.append(cap, cur, card)
    addEventListener('mousemove', (e) => { cur.style.left = e.clientX + 'px'; cur.style.top = e.clientY + 'px' }, true)
    addEventListener('mousedown', () => cur.classList.add('down'), true)
    addEventListener('mouseup', () => cur.classList.remove('down'), true)
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount); else mount()
  const again = new MutationObserver(mount); again.observe(document.documentElement, { childList: true })
})

// A Google project id and any 12-digit account id are replaced in the page text, so a recording never shows a real identifier.
await ctx.addInitScript(() => {
  const scrub = (n) => { if (n.nodeType === 3) { const t = n.nodeValue.replace(/servicehub-\d{6}/g, 'my-gcp-project').replace(/\b\d{12}\b/g, '123456789012'); if (t !== n.nodeValue) n.nodeValue = t } else n.childNodes.forEach(scrub) }
  new MutationObserver(() => scrub(document.body ?? document)).observe(document, { childList: true, subtree: true, characterData: true })
})
const page = await ctx.newPage()
const PACE = Number(process.env.PACE || (SHORT ? 0.75 : 0.8))
const wait = (ms) => page.waitForTimeout(ms >= 1500 ? Math.round(ms * PACE) : ms)
const t0 = Date.now()
const chapters = []
const failures = []
const speedups = []
const only = process.env.SCENES ? new Set(process.env.SCENES.split(',')) : null
const now = () => (Date.now() - t0) / 1000

const go = async (p, ms = 3000) => { await page.goto(BASE + p); await wait(ms) }
const cap = async (chapter, text, sub = '') => page.evaluate(([n, text, sub]) => {
  const el = document.getElementById('__vcap'); if (!el) return
  el.firstChild.textContent = n; el.lastChild.innerHTML = text + (sub ? ` <span>— ${sub}</span>` : ''); el.classList.add('on')
}, [chapter, text, sub])
const uncap = () => page.evaluate(() => document.getElementById('__vcap')?.classList.remove('on'))
const card = async (html) => { await page.evaluate((h) => { const c = document.getElementById('__vcard'); c.innerHTML = h; c.classList.add('on') }, html) }
const uncard = () => page.evaluate(() => document.getElementById('__vcard')?.classList.remove('on'))
/** Waits until the page stops saying "Loading". A slow cloud read is recorded as a segment that build-videos5.sh plays faster. */
const loaded = async (what) => {
  const a = now()
  await cap('Reading', `Reading ${what} from the cloud`, 'sped up')
  await page.waitForFunction(() => !/Loading/.test(document.querySelector('main')?.innerText || 'Loading'), null, { timeout: 70000 }).catch(() => {})
  const b = now()
  if (b - a > 4) speedups.push([+(a + 1.2).toFixed(2), +(b - 0.2).toFixed(2)])
  await uncap(); await wait(600)
}
const glide = async (loc) => {
  await loc.waitFor({ state: 'visible', timeout: 25000 })
  await loc.scrollIntoViewIfNeeded({ timeout: 6000 }).catch(() => {})
  const b = await loc.boundingBox({ timeout: 8000 })
  if (!b) throw new Error('not visible')
  await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2, { steps: 26 })
  await wait(350)
}
const click = async (loc) => { await glide(loc); await loc.click(); await wait(600) }
const hover = async (loc, ms = 900) => { await glide(loc); await wait(ms) }
const scrollMain = async (dy, ms = 1400) => {
  await page.mouse.move(700, 420, { steps: 6 })
  for (let i = 0; i < 14; i++) { await page.mouse.wheel(0, dy / 14); await wait(ms / 14) }
}
const scene = async (name, fn) => {
  if (only && !only.has(name)) return
  chapters.push({ scene: name, at: Math.round(now()) })
  try { await fn() } catch (e) { failures.push(name); console.error(`SCENE FAILED: ${name}: ${String(e.message || e).split('\n')[0]}`) }
}
const menu = (name) => page.locator('nav, aside').getByRole('link', { name: new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}`) }).first()
const dlg = () => page.getByRole('dialog')
const dlgScroll = (top) => dlg().evaluate((d, top) => { const s = [...d.querySelectorAll('*')].find((e) => e.scrollHeight > e.clientHeight + 40 && /(auto|scroll)/.test(getComputedStyle(e).overflowY)); s?.scrollTo({ top, behavior: 'smooth' }) }, top).catch(() => {})
const tab = (name) => page.getByRole('navigation', { name: 'Sections of this connection' }).getByRole('link', { name: new RegExp(`^${name}`) }).first()
const firstReplayable = () => page.locator('tr:has(input[type=checkbox])').getByRole('link', { name: /^Open message/ }).first()
const esc = async () => { await page.keyboard.press('Escape'); await wait(800) }
const storyCard = async (tag, h, lines, ms) => {
  await card(`<div class="tag">${tag}</div><h1 style="font-size:46px">${h}</h1><ul style="list-style:none;padding:0;margin:0;max-width:1120px">${lines.map((l) => `<li>${l}</li>`).join('')}</ul>`)
  await wait(ms); await uncard(); await wait(300)
}

const why = {
  azure: 'Azure Service Bus sets a message aside after too many failed tries. The portal shows you a count.',
  aws: 'SQS moves a message to a dead-letter queue after too many failed tries. The console shows you a count.',
  gcp: 'Pub/Sub forwards a message to a dead-letter topic after too many failed tries. The console shows you almost nothing.',
}[CLOUD]
const good = {
  azure: ['Azure counts exactly and ServiceHub watches by itself, so the numbers are always current', 'It can also prove that a replayed message stayed fixed'],
  aws: ['SQS cannot be read without a delivery attempt, so ServiceHub looks only when you press Look now', 'Counts are approximate, and it says so instead of pretending'],
  gcp: ['Pub/Sub reports no counts and often no reason, so ServiceHub says that plainly', 'It shows what it recorded when you looked, and never pretends to more'],
}[CLOUD]

try {
  await go(`/${CLOUD}`, 1200)
  await page.mouse.move(683, 384)

  if (SHORT) {
    // ============ the pain, then what ServiceHub gives ============
    await storyCard(`${C.name} · where is the pain`, 'A message failed. Now what?', [why, '✗ A count, not a story: which messages, and why?', '✗ Replaying is a script and a prayer: if you are wrong they all fail again', '✗ Nobody knows whether the fix held', '✗ The same failure comes back, again and again'], 9000)
    await storyCard(`${C.name} · what ServiceHub gives you`, `ServiceHub ${VERSION}`, ['✓ Every failed message, grouped by why it failed', '✓ A replay with a preview: see before you send', '✓ Proof afterwards: Verified, Came back, or honestly Unverified', '✓ An Agent that replays repeats only once it has earned the right', ...good.map((g) => '✓ ' + g)], 9000)

    await scene('home', async () => {
      await go(`/${CLOUD}`, 6000)
      await cap(C.name, 'One page for what is dead-lettered and what needs a person', C.proves ? 'counted exactly, watched automatically' : 'it only knows what you ask it to look at')
      await wait(4400)
    })
    await scene('list', async () => {
      await go(`${DETAIL}?tab=dead-letters`, 6500)
      if (C.look) {
        await cap('Look now', 'Not watched in the background: Look now reads the dead-letter queue', 'each read is a delivery attempt, so it only happens when you ask')
        await wait(3400)
        await click(page.getByRole('button', { name: /^Look now/ }).first()); await wait(12000)
      }
      await cap('The message list', 'Pain: “4,000 dead-lettered”. ServiceHub: the messages, grouped by why they failed', CLOUD === 'gcp' ? 'Google often records no reason, and the page says so' : 'the reason the cloud recorded')
      await wait(5200)
      await hover(page.getByPlaceholder(/Message ID or reason/).first(), 1400)
    })
    await scene('detail', async () => {
      await cap('Detail', 'Open one: why it failed, its body, its history', 'read-only: ServiceHub never changes a message')
      await click(firstReplayable()); await wait(4200)
      await dlgScroll(300); await wait(2600)
      await click(dlg().getByText('History', { exact: true }).first()); await wait(2400)
    })
    await scene('replay', async () => {
      await cap('Replay', 'Replay… shows a preview first', 'what will be sent, where, and which safety checks ran')
      await click(dlg().getByRole('link', { name: /^Replay…/ }).first()); await wait(4400)
      await dlgScroll(260); await wait(3000)
      await cap('Safe', 'Nothing has been sent. Cancel changes nothing.', C.proves ? 'after a replay ServiceHub watches that it stays out' : 'after a replay it says “verification required”, never “verified”')
      await hover(dlg().getByRole('button', { name: 'Cancel' }).first(), 3600)
      await click(dlg().getByRole('button', { name: 'Cancel' }).first()); await wait(900)
    })
    await scene('outcome', async () => {
      await go(`/${CLOUD}/replays`, 6500)
      await cap('Proof', 'Afterwards every replay is watched', 'Verified, Came back, or Unverified, in plain words')
      await wait(5400)
    })
    await scene('autonomous', async () => {
      await go(`/${CLOUD}/rules`, 5500)
      await cap('Autonomous', 'Repeats: the Agent can replay on its own, but only after it has earned it', 'a rule starts as “ask a person” and earns trust with verified fixes')
      await wait(5400)
      await go(`/${CLOUD}/approvals`, 5500)
      if (await page.getByRole('link', { name: /^Review/ }).first().isVisible().catch(() => false)) {
        await cap('Autonomous', 'Until then it stops and asks: approve, or decline with a reason', 'every answer is written to a tamper-evident ledger')
        await wait(5400)
      } else {
        await go(`/${CLOUD}/signatures`, 5500)
        await cap('Autonomous', 'Each kind of failure has its own record, and its own level of trust', 'until it has earned automatic replay, a person decides')
        await wait(5600)
      }
      await go(`/${CLOUD}?panel=autonomy`, 4500)
      await cap('Autonomous', 'How autonomy is earned, in plain words', 'and Emergency stop switches it all off')
      await wait(5400)
      await esc()
    })
    await storyCard(`${C.name} · what you take away`, 'Stuck, understood, put back, proven.', ['✓ See why, not just how many', '✓ Replay with a preview, never blind', '✓ Know the fix held', '✓ Let the Agent handle repeats, only when it has earned it'], 7000)
  } else {
    await card(`<div class="tag">ServiceHub ${VERSION} · ${C.name}</div><h1>${C.service}</h1><p>Every PaaS component of ${C.name} and every function, screen by screen: find the failed messages, understand why, replay safely, see whether the fix held, and let the Agent help only when it has earned it.</p>`)
    await wait(5600); await uncard(); await wait(300)

    // ============ setup ============
    await scene('connections', async () => {
      await go('/connections', 3600)
      await cap('Connections', 'Connections: every cloud ServiceHub can read', 'its status, its components, Test, Add a component, remove')
      await wait(4000)
      await hover(page.getByRole('button', { name: 'Test' }).first(), 1500)
      await go(`/${CLOUD}?dialog=connect`, 2600)
      await click(dlg().getByText(C.tab, { exact: true }).first())
      await cap('Connect', `Connect ${C.name}: nothing is saved until you press Connect`, 'credentials are encrypted and never shown again')
      await wait(5000); await esc(); await uncap()
    })
    await scene('add-component', async () => {
      await go(`/${CLOUD}?dialog=add-component`, 5500)
      await cap('Add a component', 'Add a component: tick a service to read its failures', 'access is checked first, read-only; unticking keeps every record')
      await wait(4200); await dlgScroll(500); await wait(4200); await dlgScroll(1100); await wait(3200); await esc(); await uncap()
    })
    await scene('home-all', async () => {
      await go('/', 4800)
      await cap('Home', 'Home: every connected cloud, side by side', 'what needs you, what is dead-lettered, and no misleading total')
      await wait(3800); await scrollMain(520, 1800); await wait(2400); await scrollMain(-520, 1200)
    })
    await scene('home-cloud', async () => {
      await go(`/${CLOUD}`, 6500)
      await cap(C.name, `${C.name}: its own page, in its own terms`, C.proves ? 'counted exactly and watched automatically' : 'it only knows what you ask it to look at')
      await wait(3600); await scrollMain(560, 2000); await wait(2400); await scrollMain(-560, 1200)
    })

    // ============ the message store ============
    await scene('store-overview', async () => {
      await click(menu(C.store)); await wait(2400)
      await go(DETAIL, 5500)
      await cap('Overview', `${C.store}: one connection, its queues and what is dead-lettered`, C.proves ? 'exact counts, and the transfer dead-letter queue' : 'counts are what the cloud reports')
      await wait(4000); await scrollMain(420, 1600); await wait(2600); await scrollMain(-420, 1000)
    })
    await scene('store-list', async () => {
      await click(tab('Dead-letter messages')); await wait(4500)
      if (C.look) {
        await cap('Look now', 'Not watched in the background: Look now reads the dead-letter queue', 'each read is a delivery attempt, so it only happens when you ask')
        await wait(3600); await click(page.getByRole('button', { name: /^Look now/ }).first()); await wait(15000)
      }
      await cap('Dead letters', 'The list, grouped by why they failed', CLOUD === 'gcp' ? 'Google often records no reason, and the page says so once' : 'the reason the cloud recorded')
      await wait(4200)
      await cap('Filter', 'Narrow it: queue, state, reason, or message id')
      await hover(page.getByRole('combobox').first(), 1400); await hover(page.getByPlaceholder(/Message ID or reason/).first(), 1200)
    })
    await scene('message', async () => {
      await cap('Message', 'Open a message: why it failed, its body, properties and history', 'ServiceHub never changes a body')
      await click(firstReplayable()); await wait(3800); await dlgScroll(300); await wait(2200)
      for (const t of ['Body', 'Properties', 'History']) { await click(dlg().getByText(t, { exact: true }).first()); await wait(1800) }
      await cap('Replay', 'Replay… shows a preview first', 'what will be sent, where, and which safety checks ran')
      await click(dlg().getByRole('link', { name: /^Replay…/ }).first()); await wait(4200); await dlgScroll(260); await wait(2800)
      await cap('Replay', 'Press Replay: the message goes back to its queue')
      await click(dlg().getByRole('button', { name: /^Replay 1 message/ }).last()); await wait(5000)
      await cap('Sent back', C.proves ? 'Sent back. ServiceHub now watches that it stays out' : 'Sent back. The result will read “verification required”', 'sent back is not the same as fixed')
      await wait(4200); await click(dlg().getByRole('button', { name: 'Done' }).first()); await wait(900)
    })
    await scene('bulk', async () => {
      await go(`${DETAIL}?tab=dead-letters`, 6500)
      await cap('Many', 'Many at once: tick the messages you want', 'then Replay selected, which opens the same preview')
      const boxes = page.locator('tbody input[type=checkbox]:not([disabled])')
      for (let i = 0; i < 4; i++) await click(boxes.nth(i))
      await click(page.getByText('Replay selected…').first()); await wait(4000)
      await cap('Preview', 'Bulk preview: how many, how they failed, what a check holds back', 'nothing has run. Cancel closes it.')
      await dlgScroll(400); await wait(4400)
      await click(dlg().getByRole('button', { name: 'Cancel' }).first()); await wait(900)
    })
    await scene('send', async () => {
      await go(`${DETAIL}?dialog=send`, 4200)
      await cap('Send', 'Send a test message: prove the path works', 'optional, and recorded; we only look and close it')
      await wait(4600); await esc()
    })
    if (C.key === 'service-bus') await scene('deferred', async () => {
      await go(`${DETAIL}?tab=stuck`, 4200)
      await cap('Deferred', 'Deferred messages: set aside by the consumer, listed on request', 'read only, and only when you press Look now')
      await wait(4800)
    })
    await scene('store-tabs', async () => {
      await go(DETAIL, 5000)
      for (const [t, text, sub] of [
        ['Recovery', 'Recovery: what was replayed here, and whether it held', 'Verified, Watching, Came back, Unverified'],
        ['Access', 'Access: who you are and what ServiceHub may do here', 'a role on this connection'],
        ['Configuration', 'Configuration: the cloud’s own settings and ServiceHub’s', C.proves ? '' : 'including Confirm fixes on this cloud'],
        ['Audit', 'Audit: every action on this connection', 'who, what, when, and the outcome'],
      ]) { await click(tab(t)); await cap(t, text, sub); await wait(3800) }
    })

    // ============ every other PaaS component ============
    for (const [label, key, blurb] of C.others) {
      await scene(`component-${key}`, async () => {
        await click(menu(label)); await loaded(label)
        // A message-store component (SNS) opens on a list of its connections first; a failure-record component opens on its own page.
        const conn = page.locator(`main a[href*="/c/${key}/"]`).first()
        if (await conn.isVisible().catch(() => false)) { await cap(label, `${label}: the connections that have it`, 'open one'); await wait(3000); await click(conn); await loaded(label) }
        await cap(label, `${label}: ${blurb}`, 'a component page, read-only unless it says otherwise')
        await wait(4400)
        const details = page.getByRole('button', { name: 'Details' }).first()
        if (await details.isVisible().catch(() => false)) {
          await click(details); await wait(3600)
          await cap(label, 'Open one failure: its reason and what the cloud recorded'); await dlgScroll(260); await wait(3200); await esc()
        }
        const act = page.getByRole('button', { name: /^(Redrive|Resubmit|Retry)/ }).first()
        if (await act.isVisible().catch(() => false)) {
          await click(act); await wait(3200)
          await cap(label, 'A person can ask the cloud to retry a failed run', 'a preview first; we only look and close it')
          await wait(3800); await esc()
        }
        await click(page.getByRole('link', { name: /^Access/ }).first()); await loaded(`${label} access`)
        await cap(label, 'Access: what ServiceHub can and cannot do here'); await wait(3600)
      })
    }

    // ============ Operational Tools ============
    await scene('tools', async () => {
      await go(`/${CLOUD}`, 4500)
      for (const [m, text, sub] of [
        ['Needs your attention', 'Needs your attention: what is waiting for a person', 'replays the Agent holds, and hand-offs'],
        ['Pending approvals', 'Pending approvals: the Agent stopped and asked', 'approve, or decline with a reason'],
        ['Active messages', 'Active messages: what is flowing right now', 'read-only'],
        ['Replay history', 'Replay history: what stayed fixed and what came back', 'who did it and what the cloud said'],
        ['Auto Replay rules', 'Auto Replay rules: what the Agent may do by itself', 'a rule only acts once the failure has earned trust'],
        ['Failure signatures', 'Failure signatures: failures grouped by how they fail', 'a thousand messages become a handful of causes'],
        ['Evidence ledger', 'Evidence ledger: every decision, in order, tamper-evident', 'export it and verify it offline'],
        ['Agents', 'Agents: the background workers and what each may do', 'pause any one at any time'],
      ]) {
        await click(menu(m)); await cap(m, text, sub); await wait(m === 'Pending approvals' ? 3600 : 4400)
        if (m === 'Pending approvals') {
          const review = page.getByRole('link', { name: /^Review/ }).first()
          if (await review.isVisible().catch(() => false)) {
            await click(review); await cap('Review', 'Review: why the Agent stopped, then Approve or Decline…', 'nothing is sent until a person says yes'); await wait(5200); await esc()
          }
        }
        if (m === 'Needs your attention') {
          const view = page.getByRole('link', { name: /^View/ }).first()
          if (await view.isVisible().catch(() => false)) { await click(view); await cap('Hand-off', 'A hand-off: ServiceHub stopped, and a person decides', 'it says what it knows and what it does not'); await wait(4400); await esc() }
        }
        if (m === 'Auto Replay rules') {
          const nr = page.getByRole('button', { name: /New rule/ }).first()
          if (await nr.isVisible().catch(() => false)) { await click(nr); await cap('New rule', 'Make a rule: which failure, how fast, who decides', 'we only look and close it'); await wait(4400); await esc() }
        }
        if (m === 'Failure signatures') {
          await scrollMain(380, 1400); await wait(1800)
          const tr = page.getByRole('tab', { name: /Trace a message/ }).or(page.getByRole('link', { name: /Trace a message/ })).first()
          if (await tr.isVisible().catch(() => false)) { await click(tr); await cap('Trace', 'Trace a message across clouds by its correlation id'); await wait(3800) }
        }
        if (['Agents', 'Evidence ledger'].includes(m)) { await scrollMain(380, 1400); await wait(1800) }
      }
    })

    // ============ autonomy, safety, settings ============
    await scene('autonomy', async () => {
      await go(`/${CLOUD}?panel=autonomy`, 4500)
      await cap('Autonomy', 'How autonomy is earned: trust is built from verified fixes', 'a person decides until a failure has earned automatic replay'); await wait(5400); await esc()
      await go(`/${CLOUD}?panel=security`, 4500)
      await cap('Security', 'Security and data: what is stored, what is encrypted, who may do what', 'credentials are encrypted; message text is kept, so treat the data folder like the queue'); await wait(5400); await esc()
    })
    await scene('settings', async () => {
      await click(menu('Configuration')); await wait(3200)
      await cap('Configuration', 'Configuration: connections, notifications, access, preferences, backup')
      for (const t of ['Notifications', 'Access', 'Preferences', 'Backup']) {
        const b = page.getByRole('tab', { name: t }).or(page.getByRole('link', { name: new RegExp(`^${t}`) })).or(page.getByRole('button', { name: t })).first()
        if (await b.isVisible().catch(() => false)) { await click(b); await wait(2800) }
      }
      await click(menu('What ServiceHub covers')); await cap('Coverage', 'What ServiceHub covers: every component, and how far it is proven', 'Supported, or Being validated'); await wait(4600); await scrollMain(520, 1800); await wait(2600)
    })
    await scene('help', async () => {
      await go(`/${CLOUD}?panel=help`, 3200); await cap('Help', 'Help: answers by task, and a guide for each cloud', 'press ? from any page'); await wait(4600); await esc()
      await go(`/${CLOUD}?dialog=search`, 2800); await cap('Search', 'Search finds a connection, a page or an action', '⌘K or Ctrl-K; it never searches inside messages'); await wait(3800); await esc()
      await go(`/${CLOUD}?dialog=emergency-stop`, 3200); await cap('Safety', 'Emergency stop: switches off everything ServiceHub does by itself', 'it needs a reason and the word STOP; we only look and close it'); await wait(5400); await esc()
    })
    await uncap()
    await card(`<div class="tag">${C.service}</div><h1>Stuck, understood, put back, proven.</h1><ul style="list-style:none;padding:0;margin:0"><li>✓ Every component of ${C.name}, in one layout</li><li>✓ Replay with a preview, never blind</li><li>✓ See whether the fix held</li><li>✓ A person decides until the Agent has earned it</li></ul>`)
    await wait(5000)
  }
  await uncap()
} finally {
  const v = page.video()
  await ctx.close()
  const out = await v.path()
  const dest = path.join(RAW, `${CLOUD}-${MODE}.webm`)
  fs.renameSync(out, dest)
  console.log('recorded', dest)
  fs.writeFileSync(path.join(RAW, `${CLOUD}-${MODE}.chapters.json`), JSON.stringify(chapters))
  fs.writeFileSync(path.join(RAW, `${CLOUD}-${MODE}.speed.json`), JSON.stringify(speedups))
  await browser.close()
  if (failures.length) { console.error('FAILED SCENES:', failures.join(', ')); process.exitCode = 1 }
}
