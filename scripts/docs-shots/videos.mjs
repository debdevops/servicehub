// Walkthrough videos for the README and social posts: one per cloud, recorded from the real app (Playwright video),
// with a visible cursor and a caption bar so they read with the sound off. They cover ALL of Simple mode — Home, Dead letters
// (look, details, single replay, Replay selected, Replay All), Active messages, Replayed, Auto Replay, the approvals, Connections,
// Settings, Help — and then the switch to Advanced and its four read-only pages, and back.
//
// Nothing here types a credential: the "Add a cloud" window is shown and cancelled; the cloud being shown is already connected.
// Real things happen on the dev clouds: one message is replayed on its own, three go through Replay selected, and Replay All is started
// and then STOPPED after a few seconds (its Stop button is part of the story) — never left to replay a whole queue.
//
//   CLOUD=azure|aws|gcp  DOCS_BASE=http://localhost:3000  node scripts/docs-shots/videos.mjs
//
// Writes the raw .webm to VIDEO_RAW (default: a temp folder). build-videos.sh turns each one into an MP4, a poster and a GIF.
// Exits non-zero (after saving the video) if any scene failed, so a recording with a missing chapter is never mistaken for a good one.
import { chromium } from 'playwright'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'

const BASE = process.env.DOCS_BASE || 'http://localhost:3000'
const CLOUD = process.env.CLOUD || 'azure'
const VERSION = fs.readFileSync(new URL('../../.version', import.meta.url), 'utf-8').trim() // the title card must never name a stale release
const RAW = process.env.VIDEO_RAW || fs.mkdtempSync(path.join(os.tmpdir(), 'servicehub-video-'))
const C = {
  azure: { name: 'Azure Service Bus', label: 'Azure', tab: /^Azure Service Bus/, look: null, confirms: true },
  aws: { name: 'AWS SQS', label: 'AWS', tab: /^AWS SQS/, look: /Look at AWS/, confirms: false },
  gcp: { name: 'Google Cloud Pub/Sub', label: 'Google Cloud', tab: /^Google Pub/, look: /Look at Google/, confirms: false },
}[CLOUD]
if (!C) throw new Error('CLOUD must be azure, aws or gcp')

fs.mkdirSync(RAW, { recursive: true })
const browser = await chromium.launch({ channel: 'chrome' })

// ---------- before recording: find a message that will really stay fixed ----------
const api = async (p) => (await fetch(`${BASE}/api/v1${p}`)).json()

/** A dead letter worth replaying on camera: the sample app's "replay-cure" messages carry `shs-fail-mode: until:<time>`; once that
 *  time has passed they replay cleanly, so the result is a message that really stays fixed. Falls back to the first one listed. */
async function pickTarget() {
  let fallback = null
  for (let page = 1; page <= 6; page++) {
    const d = await api(`/dead-letters?provider=${CLOUD}&pageSize=100&page=${page}`)
    const rows = d.items || []
    fallback ??= rows[0] || null
    const details = await Promise.all(rows.map((r) => api(`/dead-letters/${r.id}`).catch(() => null)))
    for (let i = 0; i < rows.length; i++) {
      const props = JSON.parse(details[i]?.applicationPropertiesJson || '{}')
      const m = /^until:(.+)$/.exec(props['shs-fail-mode'] || '')
      if (m && Date.parse(m[1]) < Date.now() - 30_000) return rows[i]
    }
    if (page * 100 >= (d.paging?.total ?? 0)) break
  }
  return fallback
}
const target = await pickTarget()

// ---------- the recording ----------
const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 }, recordVideo: { dir: RAW, size: { width: 1366, height: 768 } } })
// A recorded video has no cursor and no captions, so draw both inside the page (they survive navigations).
await ctx.addInitScript(() => {
  const mount = () => {
    if (document.getElementById('__vcap')) return
    const st = document.createElement('style')
    st.textContent = `
      #__vcap{position:fixed;left:50%;bottom:26px;transform:translateX(-50%);z-index:2147483646;max-width:1080px;display:flex;gap:14px;align-items:center;
        background:rgba(15,23,42,.94);color:#fff;font:600 22px/1.3 system-ui,-apple-system,sans-serif;padding:14px 22px;border-radius:16px;
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
    document.head.append(st)
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
const page = await ctx.newPage()
// Dwell times (a caption that has to be read, a page that has to be taken in) scale with PACE; short mechanical waits do not.
const PACE = Number(process.env.PACE || 0.85)
const wait = (ms) => page.waitForTimeout(ms >= 1500 ? Math.round(ms * PACE) : ms)
const t0 = Date.now()
const chapters = []
const failures = []
// SCENES=a,b runs only those scenes (for trying one without recording the whole thing); the title and close cards always run.
const only = process.env.SCENES ? new Set(process.env.SCENES.split(',')) : null

const dismissHints = async () => { for (let i = 0; i < 3; i++) { const g = page.getByRole('button', { name: 'Got it' }).first(); if (await g.isVisible().catch(() => false)) { await g.click().catch(() => {}); await wait(250) } } }
const go = async (p, ms = 2800) => { await page.goto(BASE + p); await wait(ms); await dismissHints() }
const cap = async (chapter, text, sub = '') => page.evaluate(([n, text, sub]) => {
  const el = document.getElementById('__vcap'); if (!el) return
  el.firstChild.textContent = n; el.lastChild.innerHTML = text + (sub ? ` <span>— ${sub}</span>` : ''); el.classList.add('on')
}, [chapter, text, sub])
const uncap = () => page.evaluate(() => document.getElementById('__vcap')?.classList.remove('on'))
const card = async (html) => { await page.evaluate((h) => { const c = document.getElementById('__vcard'); c.innerHTML = h; c.classList.add('on') }, html) }
const uncard = () => page.evaluate(() => document.getElementById('__vcard')?.classList.remove('on'))
const glide = async (loc) => {
  // A long real list (hundreds of rows) can take a while to draw under load: wait for the control, don't time out on a measurement (one AWS run did, 2026-10-09).
  await loc.waitFor({ state: 'visible', timeout: 25000 }).catch(() => {})
  await loc.scrollIntoViewIfNeeded({ timeout: 6000 }).catch(() => {})
  const b = await loc.boundingBox({ timeout: 8000 })
  if (!b) throw new Error('not visible')
  await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2, { steps: 28 })
  await wait(350)
}
const click = async (loc) => { await glide(loc); await loc.click(); await wait(500) }
const hover = async (loc, ms = 900) => { await glide(loc); await wait(ms) }
/** Scrolls the page's scrolling region by `dy` px, smoothly, so a viewer can follow it. */
const scrollMain = async (dy, ms = 1400) => {
  await page.mouse.move(700, 420, { steps: 6 })
  const steps = 14
  for (let i = 0; i < steps; i++) { await page.mouse.wheel(0, dy / steps); await wait(ms / steps) }
}
const scene = async (name, fn) => {
  if (only && !only.has(name)) return
  chapters.push({ scene: name, at: Math.round((Date.now() - t0) / 1000) })
  try { await fn() } catch (e) { failures.push(name); console.error(`SCENE FAILED: ${name}: ${String(e.message || e).split('\n')[0]}`) }
}
const link = (n) => page.getByRole('link', { name: n, exact: true }).first()
const dlg = () => page.getByRole('dialog')
const search = () => page.getByPlaceholder(/Search ID/)
const dlgScroll = (top) => dlg().evaluate((d, top) => { const s = [...d.querySelectorAll('*')].find((e) => e.scrollHeight > e.clientHeight + 40 && /(auto|scroll)/.test(getComputedStyle(e).overflowY)); s?.scrollTo({ top, behavior: 'smooth' }) }, top).catch(() => {})

/** One bulk window, end to end: preview → run → watch. `stopAfterMs` set = press Stop now after that long (Replay All). */
async function bulkFlow({ label, stopAfterMs = 0 }) {
  await cap('Preview', 'A preview first: nothing has run yet', 'how many, how they failed, what a safety check holds back')
  await wait(3600)
  await dlgScroll(400)
  await cap('Pace', 'It runs gently, and stops by itself', 'about 2 messages a second, and it stops after 5 failed sends in a row')
  await wait(4200)
  await cap('Run', label, 'press the button to start')
  await click(dlg().getByRole('button', { name: /^Replay [\d,]+ messages?/ }).first()); await wait(1800)
  await cap('Watch', 'Watch it go: tried, accepted by the cloud, failed to send', 'the run happens on the server, not in this tab')
  if (stopAfterMs) {
    await wait(stopAfterMs)
    await cap('Stop', 'Stop now halts it before the next message', 'messages not yet sent are left exactly as they were')
    await click(dlg().getByRole('button', { name: 'Stop now' }).first()); await wait(4200)
  } else {
    // "3 of 3 tried": the run is over only when both numbers match (the text "0 accepted by the cloud" is there from the start).
    await page.waitForFunction(() => { const m = document.querySelector('[role=dialog]')?.innerText.match(/([\d,]+) of ([\d,]+) tried/); return !!m && m[1] === m[2] }, null, { timeout: 60000 }).catch(() => {})
    await wait(4200)
  }
  await click(dlg().getByRole('button', { name: /^(Close|Done)$/ }).first()); await wait(800)
}

try {
  // ============ 0 — title ============
  await go(`/?provider=${CLOUD}`, 2000)
  await page.mouse.move(683, 384)
  await card(`<div class="tag">ServiceHub ${VERSION} · Simple mode</div><h1>${C.name}</h1><p>Everything you do day to day, start to finish: find stuck messages, understand them, and put them back — one or many — with every step checked and recorded.</p>`)
  await wait(4600); await uncard(); await wait(300)

  // ============ 1 — connect (shown, never filled in) ============
  await scene('connect', async () => {
    await go(`/?provider=${CLOUD}&modal=add-cloud`, 2200)
    await click(dlg().getByRole('button', { name: C.tab }).first())
    await cap('Connect', 'Add a cloud', CLOUD === 'azure' ? 'a Service Bus connection string; encrypted, and it stays on your machine' : CLOUD === 'aws' ? 'an access key for one account and region; encrypted, on your machine' : 'a Google project and its service account; encrypted, on your machine')
    const help = page.getByText(/^Where do I get/).first()
    if (await help.isVisible().catch(() => false)) { await click(help); await wait(2600) } else { await wait(3200) }
    await click(dlg().getByRole('button', { name: 'Cancel' }).first())
    await uncap()
  })

  // ============ 2 — Home ============
  await scene('home-all', async () => {
    await go('/', 3400)
    await cap('Home', 'Home: every cloud you connected, in one place', 'what needs you, the Agent, and each cloud at a glance')
    await wait(3600)
    await scrollMain(520, 1800); await wait(2200)
    await scrollMain(-520, 1200)
  })
  await scene('home-cloud', async () => {
    if (only) await go('/', 3000)
    await click(page.getByRole('tab', { name: new RegExp(`^${C.label}`) }).first()); await wait(2600)
    await cap('Home', 'Pick one cloud to see its numbers', C.confirms ? 'Azure is watched automatically' : `${C.label} only knows what you ask it to look at`)
    await wait(3200)
    await scrollMain(560, 2200); await wait(2600)
    await scrollMain(560, 2200); await wait(2600)
    await scrollMain(-1120, 1400)
  })

  // ============ 3 — Dead letters ============
  await scene('dead-letters', async () => {
    if (only) await go(`/?provider=${CLOUD}`, 3000)
    await cap('Dead letters', 'Dead letters: messages that failed too many times')
    await click(link('Dead letters')); await wait(2400); await dismissHints()
    if (C.look) {
      await cap('Look now', 'Look now reads the dead-letter queue', 'each read counts as one delivery attempt, so ServiceHub never does it on its own')
      await click(page.getByRole('button', { name: C.look }).first()); await wait(6500)
    } else {
      await cap('Dead letters', 'Grouped by why they failed', 'ServiceHub reads the error the cloud recorded')
      await wait(3600)
    }
    await cap('Filter', 'Narrow the list: reason, status, time window, queue', 'and search by message id')
    await hover(page.locator('main button').filter({ hasText: /^(Stuck now|All time|All queues & topics)$/ }).first(), 1400)
    await hover(page.locator('main button').filter({ hasText: /All queues & topics/ }).first(), 1400)
    await hover(search(), 1000)
  })
  await scene('details', async () => {
    if (only) await go(`/?provider=${CLOUD}&tab=dlq`, 3000)
    await cap('Details', 'Open any message to see why it failed', 'its body, its history, and how many others failed the same way')
    await click(page.getByRole('link', { name: /^Details of message/ }).first()); await wait(3600)
    await scrollMain(300, 1200); await wait(1800)
    await page.keyboard.press('Escape'); await wait(800)
  })
  await scene('single-replay', async () => {
    if (only) await go(`/?provider=${CLOUD}&tab=dlq`, 3000)
    if (target?.messageId) { await click(search()); await page.keyboard.type(target.messageId.slice(0, 12), { delay: 110 }); await wait(2400) }
    await cap('Replay', 'Replay one message: see what will happen first', 'nothing is sent until you confirm')
    await click(page.getByRole('link', { name: /^Replay message/ }).first()); await wait(3600)
    await cap('Checks', 'Safety checks run before anything is sent', C.confirms ? 'this cloud can confirm the fix held' : 'this cloud cannot confirm a fix held, so it says so')
    await dlgScroll(260)
    await wait(3600)
    await cap('Replay', 'Press Replay: the message goes back to its queue')
    await click(dlg().getByRole('button', { name: /^Replay 1 message/ }).first()); await wait(4200)
    await cap('Replay', C.confirms ? 'Sent back, and ServiceHub watches that it stays out' : 'Sent back. It will read "verification required", never "verified"')
    await wait(3600)
    await page.keyboard.press('Escape'); await wait(700)
    await click(search()); await search().fill(''); await wait(1800)
  })

  // ============ 4 — many at once ============
  await scene('replay-selected', async () => {
    await go(`/?provider=${CLOUD}&tab=dlq`, 3000) // a clean list: no search text, nothing ticked
    await cap('Many', 'Many at once: tick the messages you want', 'the whole page, or any you pick')
    const boxes = page.getByRole('checkbox', { name: /^Select message/ })
    for (let i = 0; i < 3; i++) await click(boxes.nth(i))
    await wait(1200)
    await cap('Many', 'Then Replay selected', 'it opens the same preview, for just these')
    await click(page.getByRole('link', { name: /Replay selected/ }).or(page.getByRole('button', { name: /Replay selected/ })).first()); await wait(3000)
    await bulkFlow({ label: 'Replay these 3: press the button' })
  })
  await scene('replay-all', async () => {
    await go(`/?provider=${CLOUD}&tab=dlq`, 3000)
    await cap('Replay All', 'Replay All Messages: everything stuck in this cloud', 'it replays the whole scope, so the preview says how many')
    await hover(page.getByRole('button', { name: 'Replay All Messages' }).first(), 2200)
    await click(page.getByRole('button', { name: 'Replay All Messages' }).first()); await wait(3200)
    await bulkFlow({ label: 'Start the run and watch', stopAfterMs: 9000 })
  })

  // ============ 5 — the other tabs ============
  await scene('active', async () => {
    if (only) await go(`/?provider=${CLOUD}&tab=dlq`, 3000)
    await cap('Active', 'Active messages: what is flowing right now', C.confirms ? 'peek a message, download its body, send a test one' : 'counts and a test message; this cloud does not allow safe browsing')
    await click(link('Active messages')); await wait(3200); await dismissHints()
    await wait(3000)
    await go(`/?provider=${CLOUD}&modal=send`, 2600)
    await cap('Send', 'Send a message: a small test into a queue', 'to prove the path works, with an intent it records')
    await wait(3800)
    await page.keyboard.press('Escape'); await wait(700)
  })
  await scene('replayed', async () => {
    await go(`/?provider=${CLOUD}&tab=replayed`, 3000)
    await cap('Replayed', 'Replayed: what stayed fixed and what came back', 'who did it, when, and what the cloud said')
    await wait(4200)
    await scrollMain(380, 1400); await wait(2600)
  })
  await scene('auto-replay', async () => {
    if (only) await go(`/?provider=${CLOUD}`, 3000)
    await cap('Auto Replay', 'Auto Replay: rules for retries ServiceHub may do on its own', 'every rule shows what it matches and who decides')
    await click(link('Auto Replay')); await wait(3400); await dismissHints()
    await wait(3200)
    const how = page.getByText(/HOW AUTO REPLAY WORKS/i).first()
    if (await how.isVisible().catch(() => false)) { await click(how); await wait(3800) }
    await scrollMain(420, 1400); await wait(2600)
    await go(`/?provider=${CLOUD}`, 2400)
  })
  await scene('approvals', async () => {
    if (only) await go(`/?provider=${CLOUD}`, 3000)
    await cap('Needs you', 'Needs your attention: when the Agent stops and asks', 'the bell and Home both show it, with the reason in plain words')
    await hover(page.getByRole('button', { name: /waiting for you|Notifications/i }).first(), 2600)
    const review = page.getByRole('link', { name: /^Review/ }).first()
    if (await review.isVisible().catch(() => false)) {
      await click(review); await wait(3600)
      await cap('Needs you', 'Approve or decline, with a reason, in one window', 'nothing is sent until a person says yes')
      await wait(4200)
      await page.keyboard.press('Escape'); await wait(800)
    }
  })

  // ============ 6 — setup and settings ============
  await scene('connections', async () => {
    if (only) await go(`/?provider=${CLOUD}`, 3000)
    await cap('Connections', 'Connections: every cloud, tested and removable')
    await click(link('Connections')); await wait(3600); await dismissHints()
    await wait(3000)
    await page.keyboard.press('Escape'); await wait(600)
  })
  await scene('settings', async () => {
    if (only) await go(`/?provider=${CLOUD}`, 3000)
    await cap('Settings', 'Settings: notifications, preferences, access, backup')
    await click(link('Settings')); await wait(2600)
    for (const tab of ['Notifications', 'Preferences', 'Access & security', 'Backup']) {
      await click(dlg().getByRole('button', { name: tab }).first()); await wait(2600)
    }
    await page.keyboard.press('Escape'); await wait(700)
  })
  await scene('help', async () => {
    if (only) await go(`/?provider=${CLOUD}`, 3000)
    await cap('Help', 'Help: answers shaped like tasks, and the keyboard shortcuts')
    await click(link('Help')); await wait(4400)
    await page.keyboard.press('Escape'); await wait(700)
  })

  // ============ 7 — Simple ↔ Advanced ============
  await scene('to-advanced', async () => {
    await go(`/?provider=${CLOUD}`, 2400)
    await cap('Switch', 'Simple is where you act. Advanced only reads.', 'one switch at the top, and you can always come back')
    await hover(page.getByRole('link', { name: 'Advanced', exact: true }).first(), 2400)
    await click(page.getByRole('link', { name: 'Advanced', exact: true }).first()); await wait(3600); await dismissHints()
    await cap('Advanced', 'Overview: the breakdown behind Simple’s numbers', 'what recovered, what came back, who may do what')
    await wait(3600)
    await scrollMain(520, 1800); await wait(2600)
    await scrollMain(520, 1800); await wait(2400)
    await scrollMain(-1040, 1200)
  })
  await scene('advanced-ledger', async () => {
    if (only) await go('/advanced', 3000)
    await cap('Ledger', 'Recovery Ledger: every action, in order, tamper-evident', 'export it and an auditor verifies it offline')
    await click(page.getByRole('link', { name: /^Recovery Ledger/ }).first()); await wait(3600); await dismissHints()
    await hover(page.getByRole('button', { name: /Export evidence/ }).or(page.getByRole('link', { name: /Export evidence/ })).first(), 1800)
    await click(page.getByRole('button', { name: /^Details of ledger entry/ }).first()); await wait(3800)
    await page.keyboard.press('Escape'); await wait(800)
  })
  await scene('advanced-signatures', async () => {
    if (only) await go('/advanced', 3000)
    await cap('Signatures', 'Failure Signatures: failures grouped by how they fail', 'the Agent and the rules reason about these groups')
    await click(link('Failure Signatures')); await wait(3800); await dismissHints()
    await scrollMain(380, 1400); await wait(3000)
  })
  await scene('advanced-agents', async () => {
    if (only) await go('/advanced', 3000)
    await cap('Agents', 'Agents: the machinery behind it, and what each may do', 'you can pause any one at any time')
    await click(link('Agents')); await wait(3800); await dismissHints()
    await scrollMain(420, 1600); await wait(3200)
  })
  await scene('back-to-simple', async () => {
    if (only) await go('/advanced', 3000)
    await cap('Switch', 'Back to Simple whenever you like')
    await click(page.getByRole('link', { name: 'Simple', exact: true }).first()); await wait(3800); await dismissHints()
    await wait(1800)
    await uncap()
  })

  // ============ 8 — close ============
  await card(`<div class="tag">${C.name}</div><h1>Stuck, understood, put back.</h1><ul style="list-style:none;padding:0;margin:0"><li>✓ Simple: Home, Dead letters, Active, Replayed, Auto Replay</li><li>✓ Replay one, a few, or everything — always previewed, always stoppable</li><li>✓ ${C.confirms ? 'Azure confirms the fix held' : 'Honest: this cloud cannot confirm a fix held'}</li><li>✓ Advanced: a ledger you can verify, signatures, agents</li></ul><p style="margin-top:14px;font-size:22px">github.com/debdevops/servicehub</p>`)
  await wait(only ? 800 : 5000)
} finally {
  const v = page.video()
  await ctx.close()
  const out = await v.path()
  const dest = path.join(RAW, `${CLOUD}.webm`)
  fs.renameSync(out, dest)
  console.log('recorded', dest)
  fs.writeFileSync(path.join(RAW, `${CLOUD}.chapters.json`), JSON.stringify(chapters))
  await browser.close()
  if (failures.length) { console.error('FAILED SCENES:', failures.join(', ')); process.exitCode = 1 }
}
