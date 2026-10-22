import { describe, expect, it, vi } from 'vitest'
import { sidebarComponents } from '@/hooks/useCoverage'
import { api } from '@/lib/api/client'
import { fetchCoverage, type CoverageResponse } from '@/lib/api/coverage'
import { coverage } from '@/lib/demo/handlers/coverage'
import type { Req } from '@/lib/demo/handlers/http'

/** The demo answers from a copy of the real catalogue (a backend test keeps the copy equal). These check the copy is usable. */
describe('the coverage list the demo answers with', () => {
  const [method, pattern, handler] = coverage[0]
  const answer = handler('/fleet/coverage'.match(pattern)!, {} as Req) as CoverageResponse

  it('answers GET /fleet/coverage and nothing else', () => {
    expect(method).toBe('get')
    expect(pattern.test('/fleet/coverage')).toBe(true)
    expect(pattern.test('/fleet/coverage/x')).toBe(false)
  })

  it('lists the seven components 5.0.0 puts in the menu, in order, per cloud', () => {
    expect(sidebarComponents(answer.rows, 'azure').map((r) => r.name)).toEqual(['Service Bus', 'Storage Queues'])
    expect(sidebarComponents(answer.rows, 'aws').map((r) => r.name)).toEqual(['SQS', 'SNS', 'Step Functions'])
    expect(sidebarComponents(answer.rows, 'gcp').map((r) => r.name)).toEqual(['Pub/Sub', 'Workflows'])
  })

  it('never offers a replay on a row that is not supported, and lets only a provable row earn autonomy', () => {
    for (const row of answer.rows) {
      if (row.status !== 'supported') expect(row.offersReplay || row.offersCloudRedrive, row.id).toBe(false)
      if (row.mayEarnAutonomy) expect(row.class, row.id).toBe('r1ReplayableProvable')
    }
    // Today exactly one place can prove a fix held.
    expect(answer.rows.filter((r) => r.mayEarnAutonomy).map((r) => r.id)).toEqual(['service-bus.dead-letter'])
  })

  it('gives every row that is not supported a reason or the unit that delivers it', () => {
    for (const row of answer.rows) {
      if (row.status === 'notSupported') expect(row.note, row.id).toBeTruthy()
      if (row.status === 'beingValidated' || row.status === 'onRequest') expect(row.planUnit, row.id).toBeTruthy()
    }
  })
})

describe('fetchCoverage', () => {
  it('asks the one endpoint and returns its body', async () => {
    const body: CoverageResponse = { rows: [] }
    const get = vi.spyOn(api, 'get').mockResolvedValue({ data: body })

    await expect(fetchCoverage()).resolves.toBe(body)
    expect(get).toHaveBeenCalledWith('/fleet/coverage')
    get.mockRestore()
  })
})
