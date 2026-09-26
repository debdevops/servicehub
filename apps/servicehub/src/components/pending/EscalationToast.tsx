import { Clock, X } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { usePendingWork } from '../../hooks/usePendingWork'
import { pendingRows, type PendingRow } from '../../lib/pendingRows'
import { useResolveHref } from './PendingWorkList'

/**
 * The live toast (5.4): something new is waiting, said while you are looking. It watches the DURABLE pending list — which a
 * stream event or the 30-second poll refreshes — rather than trusting the event itself, so a dropped event cannot lose an
 * escalation: the next read finds it and toasts it. What was already waiting when the page opened is not toasted (the bell
 * shows it). "Later" hides the toast only; the item stays in the bell.
 */
export function EscalationToast() {
  const pending = usePendingWork()
  const seen = useRef<Set<string> | null>(null)
  const [fresh, setFresh] = useState<PendingRow | null>(null)
  const resolve = useResolveHref()

  useEffect(() => {
    const items = pending.data?.items
    if (!items) return
    if (seen.current === null) {
      seen.current = new Set(items.map((i) => i.id))
      return
    }
    const added = items.filter((i) => !seen.current!.has(i.id))
    added.forEach((i) => seen.current!.add(i.id))
    if (added.length > 0) setFresh(pendingRows(added)[0])
  }, [pending.data])

  if (!fresh) return null
  const heading = fresh.kind === 'approval' ? 'The Agent stopped and asked you' : fresh.kind === 'rule' ? 'A rule stopped itself' : 'An agent stopped working'

  return (
    <div role="status" aria-live="polite" className="fixed bottom-5 right-5 z-50 w-[440px] max-w-[calc(100vw-24px)] rounded-2xl bg-[#0f172a] p-4 text-white shadow-2xl">
      <div className="flex items-start gap-3">
        <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-[#78350f] text-[#fcd34d]"><Clock className="h-4 w-4" aria-hidden="true" /></span>
        <div className="min-w-0 flex-1">
          <p className="font-bold">{heading}</p>
          <p className="mt-0.5 text-[13px] text-slate-300">{fresh.kind === 'approval' ? `${fresh.title} · ` : ''}{fresh.where}. {fresh.why}</p>
          <div className="mt-3 flex gap-2">
            <Link to={resolve(fresh.action.href)} onClick={() => setFresh(null)} className="rounded-lg bg-[#f59e0b] px-3.5 py-1.5 text-sm font-bold text-[#0f172a] hover:bg-[#fbbf24]">
              {fresh.action.label}
            </Link>
            <button type="button" onClick={() => setFresh(null)} className="rounded-lg border border-slate-500 px-3.5 py-1.5 text-sm font-semibold hover:bg-slate-800">Later</button>
          </div>
        </div>
        <button type="button" aria-label="Close" onClick={() => setFresh(null)} className="text-slate-400 hover:text-white"><X className="h-4 w-4" aria-hidden="true" /></button>
      </div>
    </div>
  )
}
