import { describe, expect, it } from 'vitest'
import {
  componentTabs, NEXT_ROOT, nextEntriesIn, nextEntry, nextHref, nextNavigation, nextPages, nextProgress, scopeSegment,
} from '@/next/nav/navigation'
import { legacyTarget, nextRoute } from '@/next/router'
import { routes } from '@/router'

/**
 * The 5.0.0 navigation seam, asserted — the same guarantees `navigation.test.ts` gives the 4.2.0 array, so the new layout
 * cannot drift either while it is being built.
 */
describe('the 5.0.0 navigation array', () => {
  it('has a unique id per entry, a unique path per page and a unique value per overlay kind', () => {
    const ids = nextNavigation.map((e) => e.id)
    expect(new Set(ids).size).toBe(ids.length)

    const paths = nextPages.map((p) => p.path)
    expect(new Set(paths).size).toBe(paths.length)

    for (const kind of ['dialog', 'panel'] as const) {
      const values = nextNavigation.flatMap((e) => (e.kind === kind ? [e.value] : []))
      expect(new Set(values).size, kind).toBe(values.length)
    }
    expect(new Set(componentTabs.map((t) => t.value)).size).toBe(componentTabs.length)
  })

  it('routes exactly the pages it lists — no orphan routes, no unrouted pages', () => {
    const routed = (nextRoute.children ?? [])
      .filter((child) => (child as { path?: string }).path !== '*')
      // 4.2.0 addresses kept as redirects are not pages; the test below pins where each one goes.
      .filter((child) => !child.id?.startsWith('legacy:'))
      .map((child) => ('index' in child && child.index ? '' : (child as { path: string }).path))

    expect([...routed].sort()).toEqual([...nextPages.map((p) => p.path)].sort())
  })

  it('is the app: mounted at the root, with no prefix on any address (cutover, I1)', () => {
    expect(routes[0]).toBe(nextRoute)
    expect(NEXT_ROOT).toBe('')
    expect(nextRoute.path).toBe('/')
    expect(nextHref(nextEntry('home-all'))).toBe('/')
    expect(nextHref(nextEntry('attention'), { cloud: 'aws' })).toBe('/aws/attention')
    expect(nextHref(nextEntry('connections'))).toBe('/connections')
  })

  it('sends every 4.2.0 address somewhere real: a bookmark never 404s', () => {
    expect(legacyTarget('?tab=dlq&provider=aws')).toBe('/aws')
    expect(legacyTarget('?tab=dlq')).toBe('/')
    expect(legacyTarget('?tab=active&provider=gcp')).toBe('/gcp/active')
    expect(legacyTarget('?tab=replayed')).toBe('/all/replays')
    expect(legacyTarget('?panel=rules')).toBe('/all/rules')
    expect(legacyTarget('?panel=approvals&provider=AZURE')).toBe('/azure/approvals')
    // A query this layout owns, or none, is simply the first screen.
    expect(legacyTarget('')).toBeNull()
    expect(legacyTarget('?dialog=connect')).toBeNull()
    expect(legacyTarget('?tab=dlq&provider=other')).toBe('/')

    const legacy = (nextRoute.children ?? []).filter((child) => child.id?.startsWith('legacy:')).map((child) => (child as { path: string }).path)
    expect(legacy.sort()).toEqual(['advanced', 'advanced/agents', 'advanced/ledger', 'advanced/signatures', 'fleet', 'next', 'next/*'])
  })

  it('reads the scope from the first segment of an address, and nothing from an address without one', () => {
    expect(scopeSegment('/aws/attention')).toBe('aws')
    expect(scopeSegment('/connections')).toBe('connections')
    expect(scopeSegment('/')).toBeUndefined()
  })

  it('names the build-plan unit and the accepted design for every screen, so nothing is built from memory', () => {
    for (const entry of [...nextNavigation, ...componentTabs]) {
      const name = 'id' in entry ? entry.id : entry.value
      expect(entry.unit, name).toMatch(/^([A-I]|PP)\d+$/)
      expect(entry.design, name).toMatch(/^[a-z0-9-]+$/)
    }
  })

  it('lists the tools in the order the accepted design shows them', () => {
    expect(nextEntriesIn('tool').map((e) => e.label)).toEqual([
      'Needs your attention', 'Pending approvals', 'Active messages', 'Replay history', 'Auto Replay rules', 'Failure signatures',
      'Evidence ledger', 'Agents',
    ])
  })

  it('carries the scope, the component and the connection in the path, and overlays in the query', () => {
    expect(nextHref(nextEntry('home-all'))).toBe('/')
    expect(nextHref(nextEntry('home'), { cloud: 'azure' })).toBe('/azure')
    expect(nextHref(nextEntry('attention'), { cloud: 'aws' })).toBe('/aws/attention')
    expect(nextHref(nextEntry('component-list'), { cloud: 'azure', component: 'service-bus' })).toBe('/azure/c/service-bus')
    expect(nextHref(nextEntry('component-detail'), { cloud: 'gcp', component: 'pubsub', namespaceId: 'a b' })).toBe('/gcp/c/pubsub/a%20b')
    expect(nextHref(nextEntry('coverage'))).toBe('/coverage')
    expect(nextHref(nextEntry('replay'), {}, '/azure')).toBe('/azure?dialog=replay')
    expect(nextHref(nextEntry('help'), {}, '/azure')).toBe('/azure?panel=help')
  })

  it('refuses to build an address with a missing part rather than writing "undefined" into it', () => {
    expect(() => nextHref(nextEntry('home'))).toThrow(/needs "cloud"/)
    expect(() => nextEntry('no-such-screen')).toThrow(/No 5.0.0 navigation entry/)
  })

  it('counts what is real, so progress is a number and not an opinion', () => {
    const { built, total } = nextProgress()
    expect(total).toBe(nextNavigation.length + componentTabs.length)
    expect(built).toBe([...nextNavigation, ...componentTabs].filter((e) => e.built).length)
    expect(built).toBeGreaterThanOrEqual(1)
  })
})
