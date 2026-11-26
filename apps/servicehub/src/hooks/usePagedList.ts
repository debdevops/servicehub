import { useEffect, useState } from 'react'
import { usePageSize } from '../lib/pageSize'

export interface PagedList<T> {
  /** The rows of the current page, in the order given. */
  readonly items: readonly T[]
  /** Pass straight to `<Pager {...pager} />`. */
  readonly pager: {
    readonly page: number
    readonly pageSize: number
    readonly total: number
    readonly onPage: (page: number) => void
    readonly onPageSize: (size: number) => void
  }
}

/**
 * Pages rows a screen already holds in memory — the same page sizes, the same default (25) and the same remembered choice as every
 * server-paged grid (`usePageSize`), so no two grids in the app disagree about "a page".
 *
 * The page is clamped when the rows shrink (a filter, a refresh), and returns to 1 when `resetKey` changes (a tab, a search): a person
 * must never be left on page 4 of a list that now has one page.
 */
export function usePagedList<T>(rows: readonly T[], resetKey: unknown = null): PagedList<T> {
  const [size, setSize] = usePageSize()
  const [requested, setPage] = useState(1)
  useEffect(() => setPage(1), [resetKey])

  const pages = Math.max(1, Math.ceil(rows.length / size))
  const page = Math.min(requested, pages)
  return {
    items: rows.slice((page - 1) * size, page * size),
    pager: {
      page,
      pageSize: size,
      total: rows.length,
      onPage: setPage,
      onPageSize: (n) => { setSize(n); setPage(1) },
    },
  }
}
