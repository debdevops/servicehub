import { describe, expect, it } from 'vitest'
import {
  navigation,
  pages,
  SCREEN_CEILING,
  entriesOn,
  hrefOf,
  landingPath,
  surfaceOf,
  visibleEntries,
} from './navigation'
import { routes } from '../router'

/**
 * The navigation seam, asserted.
 *
 * These tests are why navigation cannot drift: the array and the router are checked against each other, and the
 * product's own rules about the sidebar are checked against the array.
 */
describe('navigation', () => {
  it('has a unique id per entry, a unique path per page, and a unique value per tab host and overlay kind', () => {
    const ids = navigation.map((e) => e.id)
    expect(new Set(ids).size).toBe(ids.length)

    const pagePaths = pages.map((p) => p.path)
    expect(new Set(pagePaths).size).toBe(pagePaths.length)

    const keys = navigation
      .filter((e) => e.kind !== 'page')
      .map((e) => (e.kind === 'tab' ? `${e.path}?tab=${e.value}` : `${e.kind}=${e.value}`))
    expect(new Set(keys).size).toBe(keys.length)
  })

  it('keeps each surface within its ceiling — pages, panels and modals are places; tabs are not', () => {
    // Passing a ceiling is a decision recorded in the decisions log, not something that happens by
    // adding one more entry on a busy afternoon (PLAN 1.1, ADR-0016 as amended by D45).
    const places = (surface: 'simple' | 'advanced') => entriesOn(surface).filter((e) => e.kind !== 'tab').length
    expect(places('simple')).toBeLessThanOrEqual(SCREEN_CEILING.simple)
    expect(places('advanced')).toBeLessThanOrEqual(SCREEN_CEILING.advanced)
    // Pinned, so adding a place is a visible change here: 11 of 12 after Send a message (6.14). One left.
    expect(places('simple')).toBe(11)
  })

  it('keeps Simple to two pages — most work happens in place (D45)', () => {
    expect(pages.filter((p) => p.surface === 'simple').map((p) => p.id)).toEqual(['fleet', 'home'])
  })

  it('gives Advanced pages only — nothing on the Advanced surface opens a modal that acts (ADR-0016 D3)', () => {
    expect(entriesOn('advanced').every((e) => e.kind === 'page')).toBe(true)
  })

  it('puts every page and tab on exactly the surface its URL says — the URL is the mode', () => {
    for (const entry of navigation) {
      if (entry.kind === 'page' || entry.kind === 'tab') {
        expect(surfaceOf(entry.path), entry.id).toBe(entry.surface)
      }
    }
    expect(navigation.filter((e) => e.surface === 'advanced').every((e) => e.group === 'advanced')).toBe(true)
    expect(navigation.filter((e) => e.surface === 'simple').some((e) => e.group === 'advanced')).toBe(false)
  })

  it('hosts every tab on a page that exists', () => {
    const pagePaths = new Set(pages.map((p) => p.path))
    for (const entry of navigation) {
      if (entry.kind === 'tab') expect(pagePaths.has(entry.path), entry.id).toBe(true)
    }
  })

  it('never lets a page or component ask which surface it is on — that is the rejected mode flag', () => {
    // ADR-0016 D2: only the layout picks a sidebar from the URL. A page that branches on the surface
    // is a page maintained twice, which GUARDRAILS §2.2 rejected in writing.
    const sources = import.meta.glob<string>(
      ['../pages/**/*.{ts,tsx}', '../components/**/*.{ts,tsx}', '!**/*.test.{ts,tsx}'],
      { query: '?raw', import: 'default', eager: true },
    )
    const offenders = Object.entries(sources)
      .filter(([, source]) => /\bsurfaceOf\b|\bentriesOn\b/.test(source))
      .map(([file]) => file)
    expect(offenders).toEqual([])
  })

  it('decides the surface from the path alone', () => {
    expect(surfaceOf('/advanced')).toBe('advanced')
    expect(surfaceOf('/advanced/ledger')).toBe('advanced')
    expect(surfaceOf('/advancedish')).toBe('simple')
    expect(surfaceOf('/fleet')).toBe('simple')
    expect(surfaceOf('/')).toBe('simple')
  })

  it('carries every tab, panel and modal in the URL', () => {
    const byId = (id: string) => navigation.find((e) => e.id === id)!
    expect(hrefOf(byId('dlq'))).toBe('/?tab=dlq')
    expect(hrefOf(byId('auto-replay'), '/fleet')).toBe('/fleet?panel=rules')
    expect(hrefOf(byId('add-cloud'))).toBe('/?modal=add-cloud')
    expect(hrefOf(byId('ledger'))).toBe('/advanced/ledger')
  })

  it('routes exactly the pages it lists — no orphan routes, no unrouted pages', () => {
    const routed = (routes[0].children ?? []).filter((child) => (child as { path?: string }).path !== '*').map((child) =>
      'index' in child && child.index ? '/' : `/${(child as { path: string }).path}`,
    )

    expect([...routed].sort()).toEqual([...pages.map((p) => p.path)].sort())
  })

  it('shows the work only once a cloud is connected, and Fleet Overview only when there are two', () => {
    const idsWith = (count: number) => visibleEntries(count).map((e) => e.id)

    expect(idsWith(0)).toEqual(expect.arrayContaining(['home', 'add-cloud', 'settings', 'help']))
    expect(idsWith(0)).not.toContain('dlq')
    expect(idsWith(1)).toContain('dlq')
    expect(idsWith(1)).not.toContain('fleet')
    expect(idsWith(2)).toContain('fleet')
  })

  it('lands where the connected clouds say, not where a setting says', () => {
    expect(landingPath(0)).toBe('/') // Home is the welcome — there is no Connect page (D45)
    expect(landingPath(1)).toBe('/')
    expect(landingPath(3)).toBe('/fleet')
  })

  it('leads the primary group with Fleet Overview, then Home', () => {
    const primary = navigation.filter((e) => e.group === 'primary').map((e) => e.id)
    expect(primary).toEqual(['fleet', 'home'])
  })
})
