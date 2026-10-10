import type { ColumnHelp } from '../../content/columns'
import { Link } from 'react-router-dom'
import { InfoTip } from './InfoTip'

export interface TabDef {
  readonly id: string
  readonly label: string
  /** The number beside the label; `undefined` while it is still being read, `'none'` for a tab that has no count. */
  readonly count?: number | string | 'none'
  /** What the tab shows, in plain words — opened by its (i). */
  readonly help: ColumnHelp
  /** Amber for a tab that is waiting on a person. */
  readonly tone?: 'attention' | 'good' | 'bad' | 'neutral'
  /** When the tab is a link (its own URL) rather than a button. */
  readonly to?: string
}

/**
 * A row of real tabs: each a raised, bordered card that joins the panel below when chosen, with its own (i) saying what it holds.
 * The (i) is a sibling of the tab button, never inside it — a button cannot hold a button.
 */
const pillTone: Record<NonNullable<TabDef['tone']> | 'plain', { on: string; off: string }> = {
  plain: { on: 'border-[var(--color-primary-600)] bg-[var(--color-primary-600)] text-white', off: 'border-[var(--color-primary-200)] bg-[var(--color-primary-50)] text-[var(--color-primary-700)]' },
  attention: { on: 'border-[#d97706] bg-[#d97706] text-white', off: 'border-[#fde68a] bg-[#fffbeb] text-[#92400e]' },
  good: { on: 'border-[#047857] bg-[#047857] text-white', off: 'border-[#a7f3d0] bg-[#ecfdf5] text-[#047857]' },
  bad: { on: 'border-[#dc2626] bg-[#dc2626] text-white', off: 'border-[#fecaca] bg-[#fef2f2] text-[#b91c1c]' },
  neutral: { on: 'border-[#4b5563] bg-[#4b5563] text-white', off: 'border-[var(--color-border)] bg-[var(--color-surface-muted)] text-[var(--color-text-muted)]' },
}

export function TabBar({ label, tabs, active, onSelect, variant = 'folder' }: { label: string; tabs: readonly TabDef[]; active: string | null; onSelect: (id: string) => void; variant?: 'folder' | 'pills' }) {
  if (variant === 'pills') {
    return (
      <nav aria-label={label} className="flex flex-wrap items-center gap-2 border-b border-[var(--color-border)] px-4 py-3">
        {tabs.map((t) => {
          const on = t.id === active
          const tone = pillTone[t.tone ?? 'plain']
          return (
            <div key={t.id} className={`flex items-center rounded-lg border pl-3 pr-0.5 ${on ? tone.on : tone.off}`}>
              <button type="button" aria-current={on ? 'page' : undefined} onClick={() => onSelect(t.id)} className={`py-1.5 text-sm ${on ? 'font-semibold' : 'font-medium'}`}>
                {t.label}
                {t.count !== 'none' && <span className={`ml-2 rounded-full px-2 py-0.5 text-xs font-semibold ${on ? 'bg-black/20' : 'bg-white/70'}`}>{t.count ?? '…'}</span>}
              </button>
              <InfoTip help={t.help} className={on ? '!text-white/80 hover:!text-white' : ''} />
            </div>
          )
        })}
      </nav>
    )
  }
  return (
    <nav aria-label={label} className="flex flex-wrap items-end gap-1 border-b border-[var(--color-border)] bg-[var(--color-surface-muted)] px-2 pt-2">
      {tabs.map((t) => {
        const on = t.id === active
        return (
          <div
            key={t.id}
            className={`-mb-px flex items-center rounded-t-lg border border-b-0 pl-3 pr-0.5 ${on
              ? `bg-[var(--color-surface)] ${t.tone === 'attention' ? 'border-t-2 border-t-[#d97706]' : 'border-t-2 border-t-[var(--color-primary-600)]'} border-x-[var(--color-border)]`
              : 'border-transparent hover:bg-[var(--color-surface)]/60'}`}
          >
            {(() => {
              const cls = `py-2 text-sm ${on ? 'font-semibold text-[var(--color-text)]' : 'text-[var(--color-text-muted)] hover:text-[var(--color-text)]'}`
              const body = (
                <>
                  {t.label}
                  {t.count !== 'none' && (
                    <>{' '}<span className={`ml-0.5 rounded-full px-2 py-0.5 text-xs ${t.tone === 'attention' ? 'bg-[#fef3c7] text-[#92400e]' : 'bg-[var(--color-surface-muted)]'}`}>{t.count ?? '…'}</span></>
                  )}
                </>
              )
              return t.to
                ? <Link to={t.to} aria-current={on ? 'page' : undefined} className={cls}>{body}</Link>
                : <button type="button" aria-current={on ? 'page' : undefined} onClick={() => onSelect(t.id)} className={cls}>{body}</button>
            })()}
            <InfoTip help={t.help} />
          </div>
        )
      })}
    </nav>
  )
}
