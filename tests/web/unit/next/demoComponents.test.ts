import { describe, expect, it } from 'vitest'
import type { ComponentEntry, ComponentList } from '@/lib/api/componentEnablement'
import type { ComponentSource, FailedItemDetail, FailedItems } from '@/lib/api/componentSources'
import { componentEnablement } from '@/lib/demo/handlers/componentEnablement'
import { componentSources } from '@/lib/demo/handlers/componentSources'
import { DemoProblem, type Req, type Route } from '@/lib/demo/handlers/http'
import { catalogue } from '@/lib/demo/world/catalogue'
import { buildWorld } from '@/lib/demo/world/seed'

const NOW = Date.UTC(2026, 9, 4, 12)
const world = buildWorld(NOW)

const ask = (routes: readonly Route[], method: string, path: string, params: Record<string, unknown> = {}, body: Record<string, unknown> = {}): unknown => {
  const route = routes.find(([m, pattern]) => m === method && pattern.test(path))
  if (!route) throw new Error(`no demo route for ${method} ${path}`)
  return route[2](path.match(route[1])!, { w: world, params, body, now: NOW } as Req)
}

const connection = (provider: 'azure' | 'aws' | 'gcp') => world.namespaces.find((n) => n.provider === provider)!
const readOnDemand = catalogue.filter((r) => (r.detection === 'runs' || r.detection === 'records') && r.inSidebar)
const byKey = new Map(readOnDemand.map((r) => [r.componentKey, r]))

describe('the demo knows every component that is read on demand', () => {
  it('has all seven, two per Azure and one or two elsewhere, none on request', () => {
    expect([...byKey.keys()].sort()).toEqual(['aws-streams', 'cloud-scheduler', 'event-grid', 'eventarc', 'logic-apps', 'step-functions', 'workflows'])
    expect(catalogue.filter((r) => r.status === 'onRequest')).toEqual([])
    expect(readOnDemand.every((r) => r.status === 'beingValidated')).toBe(true)
  })
})

describe.each([...byKey.values()].map((r) => [r.componentKey, r.cloud] as const))('the demo’s %s on %s', (key, cloud) => {
  const ns = connection(cloud)
  const url = (tail: string) => `/namespaces/${ns.id}/components/${key}/${tail}`

  it('answers the source with places of the cloud’s own kind and a console link for each', () => {
    const source = ask(componentSources, 'get', url('source')) as ComponentSource
    expect(source.available).toBe(true)
    expect(source.reason).toBeNull()
    expect(source.places.length).toBeGreaterThan(0)
    for (const p of source.places) {
      expect(p.name).toBeTruthy()
      expect(p.kind).toBeTruthy()
      expect(p.id).toBeTruthy()
    }
    expect(new Set(source.places.map((p) => p.id)).size).toBe(source.places.length)
  })

  it('lists failed items newest first, each in a place the source returned, with a plain summary', () => {
    const source = ask(componentSources, 'get', url('source')) as ComponentSource
    const failed = ask(componentSources, 'get', url('failed')) as FailedItems
    expect(failed.items.length).toBeGreaterThan(0)
    const times = failed.items.map((i) => Date.parse(i.atUtc!))
    expect(times).toEqual([...times].sort((a, b) => b - a))
    for (const i of failed.items) {
      expect(source.places.map((p) => p.id), i.id).toContain(i.place)
      expect(i.summary.length).toBeGreaterThan(3)
      expect(i.status).toBeTruthy()
      expect(i.reasonCode, 'the list never carries a reason; it is read on demand').toBeNull()
    }
    expect(new Set(failed.items.map((i) => i.id)).size).toBe(failed.items.length)
  })

  it('narrows to one place', () => {
    const source = ask(componentSources, 'get', url('source')) as ComponentSource
    const one = source.places[0]
    const failed = ask(componentSources, 'get', url('failed'), { place: one.id }) as FailedItems
    expect(failed.items.every((i) => i.place === one.id)).toBe(true)
  })

  it('opens every item with a reason and a cause read only then', () => {
    const failed = ask(componentSources, 'get', url('failed')) as FailedItems
    for (const i of failed.items) {
      const detail = ask(componentSources, 'get', url('failed/detail'), { itemId: i.id }) as FailedItemDetail
      expect(detail.item?.id, i.id).toBe(i.id)
      expect(detail.stoppedUtc).toBe(i.atUtc)
      expect(detail.startedUtc).not.toBeNull()
      expect(Date.parse(detail.startedUtc!)).toBeLessThan(Date.parse(detail.stoppedUtc!))
    }
  })

  it('says an item is gone rather than guessing', () => {
    const detail = ask(componentSources, 'get', url('failed/detail'), { itemId: 'no-such-item' }) as FailedItemDetail
    expect(detail.item).toBeNull()
  })

  it('refuses an item id that is missing', () => {
    expect(() => ask(componentSources, 'get', url('failed/detail'))).toThrow(DemoProblem)
  })

  it('has no reader on the other clouds', () => {
    for (const other of (['azure', 'aws', 'gcp'] as const).filter((c) => c !== cloud)) {
      expect(() => ask(componentSources, 'get', `/namespaces/${connection(other).id}/components/${key}/source`)).toThrow(DemoProblem)
    }
  })

  it('is listed in Add a component as tickable with written prerequisites, and starts on for the first connection', () => {
    const list = ask(componentEnablement, 'get', `/namespaces/${ns.id}/components`) as ComponentList
    const entry = list.components.find((c) => c.componentKey === key) as ComponentEntry
    expect(entry).toBeDefined()
    expect(entry.canToggle).toBe(true)
    expect(entry.access).toBe('sameConnection')
    expect(entry.status).toBe('beingValidated')
    expect(entry.what).toBeTruthy()
    expect(entry.appears).toContain('Read only')
    expect(entry.needs).toBeTruthy()
    expect(entry.steps.at(-1)).toContain('Check access')
    expect(entry.permissions.length).toBeGreaterThan(0)
    expect(entry.state).toBe('on')
  })

  it('can be unticked and ticked again, and the menu list follows', () => {
    const enabled = () => (ask(componentEnablement, 'get', '/namespaces/components/enabled') as { componentKey: string; namespaceId: string }[])
    const put = (on: boolean) => ask(componentEnablement, 'put', `/namespaces/${ns.id}/enabled-components/${key}`, {}, { enabled: on }) as ComponentEntry

    expect(put(false).state).toBe('off')
    expect(enabled().some((e) => e.componentKey === key && e.namespaceId === ns.id)).toBe(false)
    expect(put(true).state).toBe('on')
    expect(enabled().some((e) => e.componentKey === key && e.namespaceId === ns.id)).toBe(true)
    expect(put(true).state).toBe('on')
    expect(enabled().filter((e) => e.componentKey === key && e.namespaceId === ns.id)).toHaveLength(1)
  })

  it('passes its check and names the permissions it tested', () => {
    const check = ask(componentEnablement, 'post', `/namespaces/${ns.id}/enabled-components/${key}/check`) as { ok: boolean; permissions: string[] }
    expect(check.ok).toBe(true)
    expect(check.permissions.length).toBeGreaterThan(0)
  })
})

describe('Add a component in the demo, for what is not read on demand', () => {
  it('keeps a message store on and refuses to tick or untick it', () => {
    const aws = connection('aws')
    const list = ask(componentEnablement, 'get', `/namespaces/${aws.id}/components`) as ComponentList
    expect(list.components.find((c) => c.componentKey === 'sqs')).toMatchObject({ state: 'alwaysOn', canToggle: false })
    expect(() => ask(componentEnablement, 'put', `/namespaces/${aws.id}/enabled-components/sqs`, {}, { enabled: false })).toThrow(DemoProblem)
  })

  it('refuses a component of another cloud', () => {
    expect(() => ask(componentEnablement, 'put', `/namespaces/${connection('aws').id}/enabled-components/eventarc`, {}, { enabled: true })).toThrow(DemoProblem)
    expect(() => ask(componentEnablement, 'put', `/namespaces/${connection('gcp').id}/enabled-components/logic-apps`, {}, { enabled: true })).toThrow(DemoProblem)
  })

  it('lists exactly the components of each cloud, each once', () => {
    for (const cloud of ['azure', 'aws', 'gcp'] as const) {
      const keys = (ask(componentEnablement, 'get', `/namespaces/${connection(cloud).id}/components`) as ComponentList).components.map((c) => c.componentKey)
      expect(new Set(keys).size).toBe(keys.length)
    }
  })
})
