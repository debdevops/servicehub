import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { api, toProblem } from '../api/client'
import { fetchDeadLetters } from '../api/deadLetters'
import { fetchNamespaces } from '../api/namespaces'
import { fetchReplays, replayMessage } from '../api/replay'
import { enterDemo, isDemo, leaveDemo } from './state'
import { demoNamespaces } from './fixtures'

describe('demo mode', () => {
  beforeEach(() => enterDemo())
  afterEach(() => leaveDemo())

  it('answers the real client from made-up data, with each cloud’s real capability differences', async () => {
    expect(isDemo()).toBe(true)
    const namespaces = await fetchNamespaces()
    const byCloud = Object.fromEntries(namespaces.map((n) => [n.provider, n.capabilities!]))
    expect(byCloud.azure.canProveDlqAbsence).toBe(true)
    expect(byCloud.aws.canProveDlqAbsence).toBe(false)
    expect(byCloud.gcp.supportsMessageCounts).toBe(false)
    expect(byCloud.azure.supportsPurge).toBe(false)
  })

  it('shows the amber state where a cloud cannot prove a fix — never a uniformly green demo', async () => {
    const aws = await fetchReplays({ provider: 'aws' })
    expect(aws.items.length).toBeGreaterThan(0)
    expect(aws.items.every((r) => r.verification.status === 'verification_required')).toBe(true)
    const azure = await fetchReplays({ provider: 'azure' })
    expect(azure.items.some((r) => r.verification.status === 'verified')).toBe(true)
  })

  it('filters like the API does', async () => {
    const page = await fetchDeadLetters({ provider: 'azure', status: 'active', page: 1, pageSize: 5 })
    expect(page.items).toHaveLength(5)
    expect(page.items.every((d) => d.namespaceId === demoNamespaces[0].id && d.status === 'active')).toBe(true)
    expect(page.groups.reduce((n, g) => n + g.count, 0)).toBe(page.paging.total)
  })

  it('never sends: every write is refused in words', async () => {
    const error = await replayMessage(1).catch((e: unknown) => e)
    expect(toProblem(error).message).toMatch(/demo — nothing is sent/)
    await expect(api.delete('/namespaces/x')).rejects.toBeTruthy()
  })
})
