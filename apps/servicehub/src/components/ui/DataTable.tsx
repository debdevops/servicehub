import { Fragment, type ReactNode } from 'react'
import type { ColumnHelp } from '../../content/columns'
import { InfoTip } from './InfoTip'

export interface Column<Row> {
  readonly key: string
  readonly header: string
  readonly render: (row: Row) => ReactNode
  /** Extra classes for the cells of this column. */
  readonly className?: string
  /** Numbers line up. */
  readonly numeric?: boolean
  /** What this column means. Every column with a header should carry one, so nothing on screen has to be guessed. */
  readonly info?: ColumnHelp
  /** A minimum width (a Tailwind class) for a column that needs room, such as the one that explains a failure. */
  readonly width?: string
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
  groupBy,
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
  /** Names the group a row belongs to (for example "Azure › orders-dev"). Rows must already be in group order; a heading row is drawn where the group changes. */
  groupBy?: (row: Row) => string
}) {
  const pageKeys = rows.map(rowKey)
  const selectedOnPage = selection ? pageKeys.filter((k) => selection.selected.has(k)).length : 0
  const allOnPage = rows.length > 0 && selectedOnPage === rows.length

  const pad = compact ? 'px-1.5 py-2' : 'px-4 py-3'

  return (
    <div className="relative overflow-x-auto">
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
              <th key={c.key} scope="col" className={`${compact ? 'px-1.5' : 'px-4'} py-2 font-semibold whitespace-nowrap ${c.numeric ? 'text-right' : ''} ${c.width ?? ''}`}>
                {c.header}
                {c.info && <InfoTip help={c.info} />}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => {
            const key = rowKey(row)
            const group = groupBy?.(row)
            const heading = group !== undefined && (i === 0 || groupBy?.(rows[i - 1]!) !== group)
            const isSelected = selection?.selected.has(key) ?? false
            return (
              <Fragment key={key}>
              {heading && (
                <tr className="bg-[var(--color-surface-muted)]">
                  <th scope="colgroup" colSpan={columns.length + (selection ? 1 : 0)} className="px-4 py-1.5 text-left text-[11px] font-bold uppercase tracking-wide text-[var(--color-text-muted)]">{group}</th>
                </tr>
              )}
              <tr
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
              </Fragment>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
