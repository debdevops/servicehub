import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import * as dlqApi from '@/lib/api/deadLetters'
import type { DeadLetterPage } from '@/lib/api/deadLetters'
import { ReasonStrip } from '@/components/message/ReasonStrip'

vi.mock('@/lib/api/deadLetters')

const page = (groups: DeadLetterPage['groups']): DeadLetterPage => ({
  items: [], paging: { total: 265, page: 1, pageSize: 25 }, groups, otherReasons: null, entities: [],
})

describe('the reason strip', () => {
  it('says "No reason recorded" for dead letters with no recorded reason — never the internal filter key', () => {
    vi.mocked(dlqApi.fetchDeadLetterTrend).mockResolvedValue({ series: [] } as never)
    render(
      <QueryClientProvider client={new QueryClient()}>
        <ReasonStrip provider="aws" page={page([{ reason: null, count: 265 }])} />
      </QueryClientProvider>,
    )
    expect(screen.getByText('No reason recorded')).toBeInTheDocument()
    expect(screen.queryByText(/__none__/)).toBeNull()
  })
})
