import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { componentKeys, useComponent, useComponents } from '@/hooks/useComponents'
import { api } from '@/lib/api/client'
import { fetchComponent, fetchComponents, type ComponentOverview, type ConnectedComponent } from '@/lib/api/components'
import { components as routes } from '@/lib/demo/handlers/components'
import { DemoProblem, type Req } from '@/lib/demo/handlers/http'
import { familyOf } from '@/lib/demo/world/catalogue'
import { pendingOf } from '@/lib/demo/world/derive'
import { buildWorld } from '@/lib/demo/world/seed'

const NOW = Date.UTC(2026, 9, 4, 12)
const world = buildWorld(NOW)
const [listRoute, oneRoute] = routes

const ask = (path: string, params: Record<string, unknown> = {}): unknown => {
  const route = [listRoute, oneRoute].find(([, pattern]) => pattern.test(path))!
  return route[2](path.match(route[1])!, { w: world, params, body: {}, now: NOW } as Req)
}
const list = (params: Record<string, unknown> = {}) => (ask('/fleet/components', params) as ComponentOverview).components

describe('the demo’s connected components', () => {
  it('returns rows on all three clouds, and each cloud’s namespaces show up', () => {
    const all = list()
    expect(new Set(all.map((c) => c.cloud))).toEqual(new Set(['azure', 'aws', 'gcp']))
    expect(new Set(all.map((c) => c.namespaceId))).toEqual(new Set(world.namespaces.map((n) => n.id)))
  })

  it('groups by what the catalogue says: the demo’s AWS has queues only, so it is SQS and not an invented SNS row', () => {
    // The two-component case (SQS and SNS from one connection) is proven by the backend ComponentOverviewServiceTests.
    const aws = world.namespaces.find((n) => n.provider === 'aws')!
    expect(new Set(list().filter((c) => c.namespaceId === aws.id).map((c) => c.componentKey))).toEqual(new Set(['sqs']))
  })

  it('makes every total equal the sum of its entities, and never lists a dead-letter queue as its own place', () => {
    for (const c of list()) {
      const sum = c.entities.reduce((n, e) => n + (e.deadLetters.value ?? 0), 0)
      expect(c.deadLetters.value ?? 0, `${c.name} ${c.componentKey}`).toBe(sum)
      expect(c.needsYou).toBe(c.approvals + c.handOffs)
      expect(c.entities.reduce((n, e) => n + e.needsYou, 0)).toBeLessThanOrEqual(c.needsYou)
      for (const e of c.entities) expect(e.name.endsWith('-dlq'), e.name).toBe(false)
    }
  })

  it('says how far each count can be trusted, from the cloud’s own capabilities', () => {
    const byCloud = (cloud: string) => list({ cloud }).map((c) => c.deadLetters.kind)
    expect(new Set(byCloud('azure'))).toEqual(new Set(['exact']))
    expect(new Set(byCloud('aws'))).toEqual(new Set(['approximate']))
    for (const c of list({ cloud: 'gcp' })) expect(c.deadLetters).toEqual({ value: null, kind: 'notReported' })
  })

  it('shows what was recorded only where ServiceHub does not watch', () => {
    for (const c of list()) expect(c.recorded === null, `${c.cloud}`).toBe(c.watched)
  })

  it('narrows by cloud, answers one component by address, and refuses what it cannot find', () => {
    expect(list({ cloud: 'AWS' }).every((c) => c.cloud === 'aws')).toBe(true)
    const first = list()[0]
    expect(ask(`/fleet/components/${first.namespaceId}/${first.componentKey}`)).toEqual(first)
    expect(() => ask('/fleet/components', { cloud: 'oracle' })).toThrow(DemoProblem)
    expect(() => ask(`/fleet/components/${first.namespaceId}/nope`)).toThrow(DemoProblem)
  })
})

describe('the component calls', () => {
  afterEach(() => vi.restoreAllMocks())

  it('asks for one cloud, or for none', async () => {
    const get = vi.spyOn(api, 'get').mockResolvedValue({ data: { components: [], generatedAtUtc: '' } })
    await fetchComponents('aws')
    await fetchComponents()
    expect(get).toHaveBeenNthCalledWith(1, '/fleet/components', { params: { cloud: 'aws' } })
    expect(get).toHaveBeenNthCalledWith(2, '/fleet/components', { params: undefined })
  })

  it('addresses one component by connection and key', async () => {
    const body = { componentKey: 'sqs' } as ConnectedComponent
    const get = vi.spyOn(api, 'get').mockResolvedValue({ data: body })
    await expect(fetchComponent('n1', 'sqs')).resolves.toBe(body)
    expect(get).toHaveBeenCalledWith('/fleet/components/n1/sqs')
  })
})

describe('the component hooks', () => {
  afterEach(() => vi.restoreAllMocks())
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>{children}</QueryClientProvider>
  )

  it('keys a list by cloud, so one cloud’s rows never stand in for another’s', () => {
    expect(componentKeys.list('aws')).toEqual(['components', 'aws'])
    expect(componentKeys.list(null)).toEqual(['components', null])
  })

  it('loads the list', async () => {
    vi.spyOn(api, 'get').mockResolvedValue({ data: { components: [], generatedAtUtc: 'x' } })
    const { result } = renderHook(() => useComponents('gcp'), { wrapper })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(result.current.data?.generatedAtUtc).toBe('x')
  })

  it('does not ask for one component until it has the whole address', () => {
    const get = vi.spyOn(api, 'get')
    renderHook(() => useComponent('n1', undefined), { wrapper })
    expect(get).not.toHaveBeenCalled()
  })
})

describe('the demo’s pending work', () => {
  const approvals = pendingOf(world, {}).filter((i) => i.kind === 'approval')

  it('has approvals to look at', () => {
    expect(approvals.length).toBeGreaterThan(0)
  })

  it('says which component each approval is on, that it is an approval, and its reason’s family', () => {
    for (const a of approvals) {
      expect(a.type, a.id).toBe('approval')
      expect(a.componentKey, a.id).toBeTruthy()
      expect(a.family, `${a.id} ${a.reasonCode}`).toBeTruthy()
    }
  })

  it('leaves a rule that stopped itself with no component and no type', () => {
    for (const r of pendingOf(world, {}).filter((i) => i.kind === 'rule')) expect(r.componentKey ?? null).toBeNull()
  })

  it('gives a code it does not know no family, never a guess', () => {
    expect(familyOf('SOMETHING_NEW')).toBeNull()
    expect(familyOf('AUTONOMY_GRANT_INSUFFICIENT')).toBe('policyBlocked')
  })
})
