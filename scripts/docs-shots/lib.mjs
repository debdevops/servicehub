// Screenshot toolkit for the docs and the Help page: real app, 1366×768, numbered callouts injected into the page.
import { chromium } from 'playwright'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

/** The repository root, whichever directory this is run from. */
export const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..')

export const BASE = process.env.DOCS_BASE || 'http://localhost:3099'
export async function open() {
  const browser = await chromium.launch()
  const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 }, deviceScaleFactor: 2 })
  const page = await ctx.newPage()
  return { browser, ctx, page }
}

/** callouts: [{ n, loc: Locator, side?: 'left'|'right'|'top'|'bottom' }] — draws a ring around the element and a numbered badge. */
export async function shot(page, file, callouts = [], scope = null) {
  await page.mouse.move(1, 1); await page.waitForTimeout(250) // a hover tooltip must not cover a callout
  callouts = [...callouts].sort((a, b) => a.n - b.n)
  recordKeys(file, callouts)
  const boxes = []
  for (const c of callouts) {
    // One numbered callout may ring several controls: `loc` can be a list, and `all` takes every match of a locator (table rows).
    const locs = (Array.isArray(c.loc) ? c.loc : [c.loc])
    let first = true
    for (const l of locs) {
      const targets = c.all ? await l.all() : [l.first()]
      for (const t of targets) {
        const b = await t.boundingBox({ timeout: 4000 }).catch(() => null)
        if (!b) { if (c.all || locs.length > 1) continue; throw new Error(`callout ${c.n}: element not visible for ${file}`) }
        boxes.push({ n: c.n, side: c.side || 'top', badge: first, ...b }); first = false
      }
    }
    if (first && !c.all) throw new Error(`callout ${c.n}: nothing matched for ${file}`)
    if (first) console.log(`note: callout ${c.n} matched nothing in ${file.split('/').pop()} (nothing to annotate)`)
  }
  await recordCoverage(page, file, boxes, scope)
  await page.evaluate((boxes) => {
    document.querySelectorAll('.__co').forEach((e) => e.remove())
    for (const b of boxes) {
      const ring = document.createElement('div'); ring.className = '__co'
      Object.assign(ring.style, { position: 'fixed', left: b.x - 4 + 'px', top: b.y - 4 + 'px', width: b.width + 8 + 'px', height: b.height + 8 + 'px',
        border: '2.5px solid #e11d48', borderRadius: '8px', zIndex: 2147483646, pointerEvents: 'none', boxShadow: '0 0 0 2px rgba(255,255,255,.7)' })
      const badge = document.createElement('div'); badge.className = '__co'; badge.textContent = String(b.n)
      const pos = b.side === 'right' ? [b.x + b.width - 12, b.y - 16] : [b.x - 16, b.y - 16]
      Object.assign(badge.style, { position: 'fixed', left: pos[0] + 'px', top: pos[1] + 'px', width: '26px', height: '26px', borderRadius: '13px',
        background: '#e11d48', color: '#fff', font: '700 14px system-ui', display: 'flex', alignItems: 'center', justifyContent: 'center',
        zIndex: 2147483647, pointerEvents: 'none', boxShadow: '0 1px 4px rgba(0,0,0,.4)', border: '2px solid #fff' })
      document.body.append(ring)
      if (b.badge) document.body.append(badge)
    }
  }, boxes)
  fs.mkdirSync(file.replace(/\/[^/]+$/, ''), { recursive: true })
  await page.screenshot({ path: file })
  await page.evaluate(() => document.querySelectorAll('.__co').forEach((e) => e.remove()))
}

function recordKeys(file, callouts) {
  const dir = file.replace(/\/[^/]+$/, ''); const reg = dir + '/keys.json'
  let all = {}; try { all = JSON.parse(fs.readFileSync(reg, 'utf8')) } catch {}
  all[file.split('/').pop()] = callouts.map((c) => ({ n: c.n, t: c.t || '' }))
  fs.mkdirSync(dir, { recursive: true }); fs.writeFileSync(reg, JSON.stringify(all, null, 1))
}

/** Every visible control (button, link, field, tab, switch, menu item) in the frame — the open dialog, else the page — whose
 *  centre is not inside any callout. Written next to keys.json as uncovered.json: the inventory gap, from the running app. */
async function recordCoverage(page, file, boxes, scope) {
  const collect = (root) => {
    root = root || document.querySelector('[role=dialog]') || document
    const sel = 'button, a[href], input, select, textarea, [role=tab], [role=switch], [role=menuitem], [role=checkbox], [role=combobox], summary'
    return [...root.querySelectorAll(sel)].flatMap((e) => {
      for (let d = e.closest('details'); d; d = d.parentElement && d.parentElement.closest('details')) if (!d.open && !d.querySelector(':scope > summary').contains(e)) return [] // folded away: nobody can see it yet
      const r = e.getBoundingClientRect(); const cs = getComputedStyle(e)
      if (r.width < 4 || r.height < 4 || cs.visibility === 'hidden' || cs.display === 'none' || r.bottom < 0 || r.top > innerHeight || r.right < 0 || r.left > innerWidth) return []
      const hit = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2) // scrolled out from under the sticky bar: not on screen
      if (hit && !e.contains(hit) && !hit.contains(e)) return []
      const name = (e.getAttribute('aria-label') || e.textContent || e.getAttribute('placeholder') || e.getAttribute('title') || e.tagName).trim().replace(/\s+/g, ' ').slice(0, 60)
      const inMain = !!e.closest('main')
      return [{ name, tag: e.tagName.toLowerCase(), shell: !inMain && !!e.closest('header, nav') && !e.closest('[role=dialog]'), x: r.x + r.width / 2, y: r.y + r.height / 2 }]
    })
  }
  // `scope` limits the check to one part of the screen (a Settings section is one stretch of a long scroll).
  const controls = scope ? await scope.first().evaluate(collect) : await page.evaluate(collect)
  // Shared furniture is documented once, where its screenshot owns it, and exempt everywhere else:
  // the top bar and sidebar (05b), every table's column-header "About …" buttons (07), a window's (?) and ✕ (02-add-cloud).
  const base = file.split('/').pop()
  const exempt = (c) =>
    (c.shell !== base.startsWith('05b')) || // 05b documents the shell and only the shell; every other screenshot skips it

    (/^About /.test(c.name) && !base.startsWith('07-')) ||
    (/^(Help for (?!this page)|Close$)/.test(c.name) && base !== '02-add-cloud.png') ||
    (c.name === 'Help for this page' && !/^(05-home|06-)/.test(base)) // documented on Home and Dead letters, exempt on every other page
  const pad = 6
  const gap = controls.filter((c) => !exempt(c)).filter((c) => !boxes.some((b) => c.x >= b.x - pad && c.x <= b.x + b.width + pad && c.y >= b.y - pad && c.y <= b.y + b.height + pad))
  const dir = file.replace(/\/[^/]+$/, ''); const reg = dir + '/uncovered.json'
  let all = {}; try { all = JSON.parse(fs.readFileSync(reg, 'utf8')) } catch {}
  all[file.split('/').pop()] = gap.map((g) => `${g.tag}: ${g.name}`)
  fs.writeFileSync(reg, JSON.stringify(all, null, 1))
}
