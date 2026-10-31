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

  it('lists the message stores the menu always shows, in order, per cloud, and a run history only where it was ticked', () => {
    expect(sidebarComponents(answer.rows, 'azure').map((r) => r.name)).toEqual(['Service Bus', 'Storage Queues'])
    expect(sidebarComponents(answer.rows, 'aws').map((r) => r.name)).toEqual(['SQS', 'SNS'])
    expect(sidebarComponents(answer.rows, 'gcp').map((r) => r.name)).toEqual(['Pub/Sub'])
    const ticked = [
      { namespaceId: 'a', cloud: 'aws' as const, componentKey: 'step-functions' },
      { namespaceId: 'g', cloud: 'gcp' as const, componentKey: 'workflows' },
    ]
    expect(sidebarComponents(answer.rows, 'aws', ticked).map((r) => r.name)).toEqual(['SQS', 'SNS', 'Step Functions'])
    expect(sidebarComponents(answer.rows, 'gcp', ticked).map((r) => r.name)).toEqual(['Pub/Sub', 'Workflows'])
    expect(sidebarComponents(answer.rows, 'aws', [{ namespaceId: 'g', cloud: 'gcp', componentKey: 'workflows' }]).map((r) => r.name)).toEqual(['SQS', 'SNS'])
  })

  it('lists a failed-records component (Event Grid, Eventarc, stream failures) only where it was ticked, like a run history', () => {
    const ticked = [
      { namespaceId: 'z', cloud: 'azure' as const, componentKey: 'event-grid' },
      { namespaceId: 'a', cloud: 'aws' as const, componentKey: 'aws-streams' },
      { namespaceId: 'g', cloud: 'gcp' as const, componentKey: 'eventarc' },
    ]
    expect(sidebarComponents(answer.rows, 'azure', ticked).map((r) => r.name)).toEqual(['Service Bus', 'Storage Queues', 'Event Grid'])
    expect(sidebarComponents(answer.rows, 'aws', ticked).map((r) => r.name)).toEqual(['SQS', 'SNS', 'Kinesis, DynamoDB streams'])
    expect(sidebarComponents(answer.rows, 'gcp', ticked).map((r) => r.name)).toEqual(['Pub/Sub', 'Eventarc'])
    expect(sidebarComponents(answer.rows, null, ticked).map((r) => r.componentKey)).toEqual(expect.arrayContaining(['event-grid', 'aws-streams', 'eventarc']))
  })

  it('keeps all four components that used to be on request out of that state, and being validated', () => {
    for (const id of ['event-grid.dead-letter-blobs', 'logic-apps.failed-runs', 'aws-streams.failure-records', 'eventarc.undelivered']) {
      const row = answer.rows.find((r) => r.id === id)!
      expect(row.status, id).toBe('beingValidated')
      expect(row.statusWords, id).toBe('Being validated')
      expect(row.inSidebar, id).toBe(true)
      // Logic Apps names the one retry it will offer once a live run is recorded; the rest are handed to a person.
      expect(row.movesOnBy, id).toBe(id === 'logic-apps.failed-runs' ? 'A person asks Azure to resubmit the run' : 'Handed to a person')
      expect(row.offersCloudRedrive, id).toBe(false)
    }
    expect(answer.rows.filter((r) => r.detection === 'records').map((r) => r.componentKey).sort()).toEqual(['aws-streams', 'event-grid', 'eventarc'])
    expect(answer.rows.find((r) => r.id === 'eventarc.undelivered')!.detectionWords).toBe('Reads the failed records the service keeps')
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
