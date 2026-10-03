import { useCallback, useState } from 'react'

/** Every grid pages with the same choices and the same default, so no two screens disagree about "a page". */
export const PAGE_SIZES = [10, 25, 50] as const
export const DEFAULT_PAGE_SIZE = 10
const KEY = 'servicehub.pageSize'

const read = (): number => {
  try {
    const n = Number(localStorage.getItem(KEY))
    return (PAGE_SIZES as readonly number[]).includes(n) ? n : DEFAULT_PAGE_SIZE
  } catch {
    return DEFAULT_PAGE_SIZE
  }
}

/** The reader's rows-per-page, remembered across grids and visits. */
export function usePageSize(): [number, (size: number) => void] {
  const [size, set] = useState(read)
  const update = useCallback((n: number) => {
    set(n)
    try {
      localStorage.setItem(KEY, String(n))
    } catch {
      /* private window: the choice lasts until reload */
    }
  }, [])
  return [size, update]
}
