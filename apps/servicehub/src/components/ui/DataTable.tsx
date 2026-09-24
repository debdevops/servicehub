import type { ReactNode } from 'react'

export interface Column<Row> {
  readonly key: string
  readonly header: string
  readonly render: (row: Row) => ReactNode
  /** Extra classes for the cells of this column. */
  readonly className?: string
  /** Numbers line up. */
  readonly numeric?: boolean
}

export interface Selection {
  readonly selected: ReadonlySet<string>
  readonly onToggle: (key: string) => void
  /** Toggles every row on the page. */
  readonly onTogglePage: () => void
}

/**
 * The table every list in ServiceHub is built from (dead letters now; Active messages and the
 * Recovery Ledger reuse it). Real `<table>` semantics with a caption — not a div grid — so a screen
 * reader gets headers and a keyboard gets ordinary controls.
 *
 * It renders what it is given. It does not page, fetch or act: paging is the API's (the table only
 * ever sees one page), and an action is a link or a button a column chooses to put in a cell.
 */
export function DataTable<Row>({
  caption,
  columns,
  rows,
  rowKey,
  selection,
  compact = false,
  rowLabel,
}: {
  /** Says what the table is; visually hidden. */
  caption: string
  columns: readonly Column<Row>[]
  rows: readonly Row[]
  rowKey: (row: Row) => string
  selection?: Selection
  compact?: boolean
  /** Names a row for its checkbox: "Select message m-1". */
  rowLabel?: (row: Row) => string
}) {
  const pageKeys = rows.map(rowKey)
  const selectedOnPage = selection ? pageKeys.filter((k) => selection.selected.has(k)).length : 0
  const allOnPage = rows.length > 0 && selectedOnPage === rows.length

  const pad = compact ? 'px-3 py-2' : 'px-4 py-3'

  return (
    <div className="overflow-x-auto">
      <table className="w-full border-collapse text-left text-sm">
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr className="border-b border-[var(--color-border)] text-xs uppercase tracking-wide text-[var(--color-text-muted)]">
            {selection && (
              <th scope="col" className="w-10 px-3 py-2">
                <input
                  type="checkbox"
                  aria-label="Select all on this page"
                  checked={allOnPage}
                  ref={(el) => {
                    if (el) el.indeterminate = selectedOnPage > 0 && !allOnPage
                  }}
                  onChange={selection.onTogglePage}
                />
              </th>
            )}
            {columns.map((c) => (
              <th key={c.key} scope="col" className={`${compact ? 'px-3' : 'px-4'} py-2 font-semibold ${c.numeric ? 'text-right' : ''}`}>
                {c.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => {
            const key = rowKey(row)
            const isSelected = selection?.selected.has(key) ?? false
            return (
              <tr
                key={key}
                aria-selected={selection ? isSelected : undefined}
                className={`border-b border-[var(--color-border)] last:border-b-0 ${isSelected ? 'bg-[var(--color-primary-50)]' : 'hover:bg-[var(--color-surface-muted)]'}`}
              >
                {selection && (
                  <td className="w-10 px-3 py-2">
                    <input
                      type="checkbox"
                      aria-label={rowLabel ? rowLabel(row) : 'Select row'}
                      checked={isSelected}
                      onChange={() => selection.onToggle(key)}
                    />
                  </td>
                )}
                {columns.map((c) => (
                  <td key={c.key} className={`${pad} align-top ${c.numeric ? 'tabular text-right' : ''} ${c.className ?? ''}`}>
                    {c.render(row)}
                  </td>
                ))}
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
