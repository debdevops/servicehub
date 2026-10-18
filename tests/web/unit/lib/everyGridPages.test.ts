import { describe, expect, it } from 'vitest'
// @ts-expect-error -- the app has no Node type definitions; vitest runs in Node, from apps/servicehub
import { readdirSync, readFileSync, statSync } from 'node:fs'

const SRC = 'src'

function sourceFiles(dir: string): string[] {
  return (readdirSync(dir) as string[]).flatMap((name) => {
    const path = `${dir}/${name}`
    return (statSync(path) as { isDirectory(): boolean }).isDirectory() ? sourceFiles(path) : path.endsWith('.tsx') ? [path] : []
  })
}

/**
 * A grid that is not paged has to be a choice somebody wrote down. Every file that draws a `<table>` must page its rows — through the
 * shared `Pager` (server-paged grids) or `usePagedList` (rows already in memory), both at the application's page size (25 by default) —
 * or be named here with the reason it is not a data grid.
 */
const NOT_A_DATA_GRID: Readonly<Record<string, string>> = {
  'src/components/TrendChart.tsx': 'the text alternative of a chart: one row per day of a fixed window, not a list that grows',
  'src/components/ui/DataTable.tsx': 'the table shell; the caller pages the rows it is given',
  'src/next/components/home/SignaturePanel.tsx': 'a four-row summary beside "View all", which opens the paged grid',
  'src/next/components/detail/MessagesPreview.tsx': 'the newest five dead letters beside "Open the Dead-letter messages tab", which is the paged grid (unit D3)',
  'src/next/overlays/MessageDialog.tsx': 'the History tab lists the few events of ONE message (detected, asked, replayed); it is not a list that grows',
  'src/components/approve/ApproveModal.tsx': 'its own paging bar over the shared page size (it ticks across pages)',
}

describe('every data grid pages', () => {
  const files = sourceFiles(SRC).filter((f) => (readFileSync(f, 'utf8') as string).includes('<table'))

  it('finds the grids', () => {
    expect(files.length).toBeGreaterThan(8)
  })

  it.each(files.map((f) => [f] as const))('%s', (file) => {
    const source = readFileSync(file, 'utf8') as string
    const pages = /<Pager\b|usePagedList\(/.test(source) || /usePageSize\(/.test(source)
    expect(pages || file in NOT_A_DATA_GRID, `${file} draws a table but does not page it: use <Pager> or usePagedList, or list it in NOT_A_DATA_GRID with the reason`).toBe(true)
  })

  it('names no file that no longer draws a table', () => {
    for (const file of Object.keys(NOT_A_DATA_GRID)) expect(files, file).toContain(file)
  })
})
