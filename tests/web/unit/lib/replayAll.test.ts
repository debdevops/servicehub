import { beforeEach, describe, expect, it, vi } from 'vitest'
import { QueryClient } from '@tanstack/react-query'

const previewBulk = vi.fn()
const startBulk = vi.fn()
const fetchBulk = vi.fn()
const fetchDeadLetters = vi.fn()
vi.mock('../../../../apps/servicehub/src/lib/api/bulk', () => ({
  previewBulk: (...a: unknown[]) => previewBulk(...a),
  startBulk: (...a: unknown[]) => startBulk(...a),
  fetchBulk: (...a: unknown[]) => fetchBulk(...a),
  cancelBulk: vi.fn(),
  isEnded: (s: string) => s !== 'running' && s !== 'previewed',
}))
vi.mock('../../../../apps/servicehub/src/lib/api/deadLetters', () => ({ fetchDeadLetters: (...a: unknown[]) => fetchDeadLetters(...a) }))

import { aggregate, closeReplayAll, minimizeReplayAll, openReplayAll, startReplayAll } from '../../../../apps/servicehub/src/lib/replayAll'
import { dismissNotice } from '../../../../apps/servicehub/src/lib/notice'

const preview = (n: number) => ({ previewId: `p${n}`, selected: n, willReplay: n, heldBackCount: 0, groups: [], heldBack: [], perSecond: 2, stopAfterConsecutiveFailures: 5 })
const done = (n: number) => ({ id: 'j', status: 'completed', selected: n, willReplay: n, sent: n, failed: 0, unknown: 0, remaining: 0, heldBack: 0, sampleOnly: false, endedReason: null })

describe('replay all', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    closeReplayAll()
    dismissNotice()
  })

  it('carries a queue bigger than one bulk run as consecutive runs, and sums them', async () => {
    // 520 stuck messages: pages of 100 → 6 requests.
    fetchDeadLetters.mockImplementation(async ({ page }: { page: number }) => ({
      items: Array.from({ length: page === 6 ? 20 : 100 }, (_, i) => ({ id: (page - 1) * 100 + i + 1 })),
      paging: { total: 520 },
    }))
    previewBulk.mockImplementation(async (ids: number[]) => preview(ids.length))
    startBulk.mockImplementation(async (previewId: string) => ({ ...done(previewId === 'p500' ? 500 : 20), status: 'running' }))
    fetchBulk.mockImplementation(async () => done(startBulk.mock.calls.length === 1 ? 500 : 20))

    await openReplayAll({ provider: 'azure' }, 'All namespaces in Azure')
    const { useReplayAllSnapshot } = await import('./replayAllSnapshot')
    expect(useReplayAllSnapshot()?.phase).toBe('ready')
    expect(useReplayAllSnapshot()?.chunks.map((c) => c.ids.length)).toEqual([500, 20])
    // Replay all ignores the table's filters: it asks for what is stuck, nothing narrower.
    expect(fetchDeadLetters.mock.calls[0]![0]).toMatchObject({ status: 'active', reason: undefined, q: undefined })

    minimizeReplayAll() // minimized work still runs
    await startReplayAll(new QueryClient())
    const s = useReplayAllSnapshot()!
    expect(s.phase).toBe('ended')
    expect(startBulk).toHaveBeenCalledTimes(2)
    expect(aggregate(s)).toMatchObject({ sent: 520, willReplay: 520, failed: 0, remaining: 0, status: 'completed' })
  }, 15000)

  it('says there is nothing to replay when nothing is stuck, and sends nothing', async () => {
    fetchDeadLetters.mockResolvedValue({ items: [], paging: { total: 0 } })
    await openReplayAll({ provider: 'aws' }, 'All namespaces in AWS')
    const { useReplayAllSnapshot } = await import('./replayAllSnapshot')
    expect(useReplayAllSnapshot()?.phase).toBe('error')
    expect(startBulk).not.toHaveBeenCalled()
  })
})
