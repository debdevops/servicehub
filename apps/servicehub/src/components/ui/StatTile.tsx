import type { LucideIcon } from 'lucide-react'
import { Link } from 'react-router-dom'
import type { ColumnHelp } from '../../content/columns'
import { InfoTip } from './InfoTip'

const tones = {
  red: { bar: 'linear-gradient(90deg,#ef4444,#fb7185)', bg: '#fee2e2', fg: '#dc2626' },
  blue: { bar: 'linear-gradient(90deg,#0ea5e9,#38bdf8)', bg: '#e0f2fe', fg: '#0284c7' },
  green: { bar: 'linear-gradient(90deg,#10b981,#34d399)', bg: '#d1fae5', fg: '#059669' },
  amber: { bar: 'linear-gradient(90deg,#f59e0b,#fbbf24)', bg: '#fef3c7', fg: '#d97706' },
} as const

/**
 * One number and what it is. Every tile is a link — a number nobody can act on is decoration.
 *
 * `value` is null when the cloud cannot count: the tile then says so in words instead of a
 * number (R5). A tile with no source at all is not rendered by the page, never shown as 0.
 */
export function StatTile({
  label,
  value,
  note,
  unavailable,
  to,
  action,
  tone = 'blue',
  icon: Icon,
  info,
}: {
  label: string
  value: number | null
  /** The footer line — what the number is a reading of (for example, "right now"). */
  note: string
  /** What to say instead of a number when `value` is null. */
  unavailable: string
  to: string
  action: string
  tone?: keyof typeof tones
  icon?: LucideIcon
  /** What the number is and where it comes from. */
  info?: ColumnHelp
}) {
  const t = tones[tone]
  return (
    <div className="relative flex flex-col overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] px-4 py-[15px] shadow-[var(--shadow-card)]">
      <div aria-hidden="true" className="absolute inset-x-0 top-0 h-[3px]" style={{ background: t.bar }} />
      <div className="flex items-start gap-3">
        {Icon && (
          <span className="flex h-[42px] w-[42px] shrink-0 items-center justify-center rounded-[11px]" style={{ background: t.bg, color: t.fg }}>
            <Icon className="h-5 w-5" aria-hidden="true" />
          </span>
        )}
        <div>
          {value === null ? (
            <div className="text-sm text-[var(--color-text-muted)]">{unavailable}</div>
          ) : (
            <div className="tabular text-[30px] font-extrabold leading-[1.05] tracking-tight text-[var(--color-text)]">{value.toLocaleString()}</div>
          )}
          <div className="mt-px flex items-center text-[11.5px] font-semibold text-[var(--color-text-muted)]">{label}{info && <InfoTip help={info} />}</div>
        </div>
      </div>
      <div className="mt-[13px] flex items-center justify-between border-t border-[#f3f4f6] pt-[11px]">
        <Link to={to} className="text-[11.5px] font-semibold text-[var(--color-primary-600)] hover:underline">
          {action} →
        </Link>
        <span className="text-[11.5px] text-[#9ca3af]">{note}</span>
      </div>
    </div>
  )
}
