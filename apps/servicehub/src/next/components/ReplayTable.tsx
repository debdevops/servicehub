import { Link } from 'react-router-dom'
import type { ReplayListItem } from '../../lib/api/replay'
import { entityLabel } from '../lib/entityRows'
import { outcomeTone, outcomeWords } from '../lib/words'

const th = 'px-3 py-2 text-left text-xs font-semibold text-[var(--color-text-muted)]'
const td = 'px-3 py-2.5 align-top text-sm'

/**
 * The replays, one row each, in the product's outcome words — shared by a connection's Recovery tab (D4) and the Replay history page
 * (E4) so the two can never word an outcome differently. `connection` adds a Connection column for a list that spans several.
 * Nothing here sends anything; Open goes to the Message dialog.
 */
export function ReplayTable({ items, caption, openHref, fmt, connection }: {
  items: readonly ReplayListItem[]; caption: string; openHref: (r: ReplayListItem) => string; fmt: (iso: string) => string
  connection?: (r: ReplayListItem) => string
}) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full border-t border-[var(--color-border)]">
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr>
            <th className={th}>Message</th>{connection && <th className={th}>Connection</th>}<th className={th}>Entity</th><th className={th}>Replayed by</th>
            <th className={th}>When</th><th className={th}>Outcome</th><th className={th}>What was observed</th><th className={th}>Actions</th>
          </tr>
        </thead>
        <tbody>
          {items.map((r) => {
            const o = outcomeWords(r.verification, fmt)
            return (
              <tr key={r.id} className="border-t border-[var(--color-border)]">
                <td className={`${td} font-mono text-xs`}>{r.messageId}</td>
                {connection && <td className={td}>{connection(r)}</td>}
                <td className={td}>{entityLabel(r.sourceEntity, null)}{r.targetEntity !== r.sourceEntity && <span className="block text-xs text-[var(--color-text-muted)]">sent to {entityLabel(r.targetEntity, null)}</span>}</td>
                <td className={td}>{r.actor.label}</td>
                <td className={td}>{fmt(r.replayedAt)}</td>
                <td className={td}><span className={`inline-block rounded-md px-2 py-0.5 text-xs font-semibold ${outcomeTone[o.tone]}`}>{o.word}</span></td>
                <td className={td}>{o.observed}</td>
                <td className={td}><Link to={openHref(r)} aria-label={`Open message ${r.messageId}`} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]">Open</Link></td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
