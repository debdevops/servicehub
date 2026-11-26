import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { usePagedList } from '@/hooks/usePagedList'
import { DEFAULT_PAGE_SIZE, PAGE_SIZES } from '@/lib/pageSize'

const rows = (n: number) => Array.from({ length: n }, (_, i) => i + 1)

describe('usePagedList', () => {
  afterEach(() => localStorage.removeItem('servicehub.pageSize'))

  it('the application default is 25 rows, one of the offered sizes', () => {
    expect(DEFAULT_PAGE_SIZE).toBe(25)
    expect(PAGE_SIZES).toContain(DEFAULT_PAGE_SIZE)
  })

  it('shows the first 25 and says how many there are', () => {
    const { result } = renderHook(() => usePagedList(rows(60)))
    expect(result.current.items).toHaveLength(25)
    expect(result.current.items[0]).toBe(1)
    expect(result.current.pager).toMatchObject({ page: 1, pageSize: 25, total: 60 })
  })

  it('moves between pages and keeps the order given', () => {
    const { result } = renderHook(() => usePagedList(rows(60)))
    act(() => result.current.pager.onPage(3))
    expect(result.current.items).toEqual(rows(60).slice(50))
  })

  it('a size choice returns to page 1 and is remembered by the next grid', () => {
    const first = renderHook(() => usePagedList(rows(60)))
    act(() => first.result.current.pager.onPage(2))
    act(() => first.result.current.pager.onPageSize(50))
    expect(first.result.current.pager).toMatchObject({ page: 1, pageSize: 50 })
    expect(first.result.current.items).toHaveLength(50)

    const second = renderHook(() => usePagedList(rows(80)))
    expect(second.result.current.pager.pageSize).toBe(50)
  })

  it('is clamped when the rows shrink, never left on a page that no longer exists', () => {
    const { result, rerender } = renderHook(({ n }) => usePagedList(rows(n)), { initialProps: { n: 60 } })
    act(() => result.current.pager.onPage(3))
    rerender({ n: 30 })
    expect(result.current.pager.page).toBe(2)
    rerender({ n: 0 })
    expect(result.current.items).toEqual([])
    expect(result.current.pager.page).toBe(1)
  })

  it('returns to page 1 when its reset key changes (a tab, a search)', () => {
    const { result, rerender } = renderHook(({ key }) => usePagedList(rows(60), key), { initialProps: { key: 'all' } })
    act(() => result.current.pager.onPage(2))
    expect(result.current.pager.page).toBe(2)
    rerender({ key: 'approvals' })
    expect(result.current.pager.page).toBe(1)
  })

  it('falls back to the default when the remembered size is not an offered one', () => {
    localStorage.setItem('servicehub.pageSize', '7')
    const { result } = renderHook(() => usePagedList(rows(60)))
    expect(result.current.pager.pageSize).toBe(25)
  })
})
