import { Pause, Play } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { peekMessages, type Message } from '../../lib/api/messages'
import { formatBytes, formatWhen } from '../../lib/format'

/** How often it looks, and how much it keeps. Bounded both ways: a tab left open must never become a load test. */
export const TAIL_EVERY_MS = 3000
export const TAIL_KEEP = 200
const TAIL_PAGE = 50

/**
 * Follow live (unit 6.16): watch messages arrive. It looks only while this view is open and not paused, a page at a time from
 * the last message it saw, and stops the moment it closes. Offered only where looking has no side effects (a repeatable peek) —
 * the caller decides that from the namespace's capabilities, never a cloud's name.
 */
export function LiveTail({ namespaceId, entity, subscription }: { namespaceId: string; entity: string; subscription?: string }) {
  const [paused, setPaused] = useState(false)
  const [seen, setSeen] = useState<readonly Message[]>([])
  const [failed, setFailed] = useState(false)
  const [lastLook, setLastLook] = useState<Date | null>(null)
  const cursor = useRef<number | undefined>(undefined)

  useEffect(() => {
    cursor.current = undefined
    setSeen([])
  }, [namespaceId, entity, subscription])

  useEffect(() => {
    if (paused) return
    let stopped = false
    const look = async () => {
      try {
        const page = await peekMessages(namespaceId, { entity, subscription, max: TAIL_PAGE, from: cursor.current })
        if (stopped) return
        setFailed(false)
        setLastLook(new Date())
        if (page.messages.length > 0) {
          cursor.current = Math.max(...page.messages.map((m) => m.sequenceNumber)) + 1
          setSeen((old) => {
            const known = new Set(old.map((m) => m.sequenceNumber))
            const fresh = page.messages.filter((m) => !known.has(m.sequenceNumber)).reverse()
            return [...fresh, ...old].slice(0, TAIL_KEEP)
          })
        }
      } catch {
        if (!stopped) setFailed(true)
      }
    }
    void look()
    const timer = setInterval(() => void look(), TAIL_EVERY_MS)
    return () => { stopped = true; clearInterval(timer) }
  }, [paused, namespaceId, entity, subscription])

  const now = new Date()
  return (
    <div>
      <div className="flex flex-wrap items-center gap-3 border-b border-[var(--color-border)] px-4 py-2.5 text-[12.5px]">
        <span className={`inline-flex items-center gap-1.5 font-semibold ${paused ? 'text-[var(--color-text-muted)]' : 'text-[#047857]'}`}>
          <span aria-hidden="true" className={`h-2 w-2 rounded-full ${paused ? 'bg-[var(--color-text-muted)]' : 'animate-pulse bg-[#10b981]'}`} />
          {paused ? 'Paused' : 'Following'}
        </span>
        <span className="text-[var(--color-text-muted)]">Looks every {TAIL_EVERY_MS / 1000} s while this is open · newest on top · keeps the last {TAIL_KEEP}{lastLook ? ` · last looked ${formatWhen(lastLook.toISOString(), now)}` : ''}</span>
        <button type="button" onClick={() => setPaused((p) => !p)} className="ml-auto inline-flex items-center gap-1.5 rounded-lg border border-[var(--color-border)] px-2.5 py-1.5 font-semibold">
          {paused ? <><Play className="h-3.5 w-3.5" aria-hidden="true" /> Resume</> : <><Pause className="h-3.5 w-3.5" aria-hidden="true" /> Pause</>}
        </button>
      </div>
      {failed && <p role="alert" className="px-4 py-2 text-sm">ServiceHub couldn’t look just now; it will try again.</p>}
      {seen.length === 0 ? (
        <p className="px-4 py-6 text-center text-sm text-[var(--color-text-muted)]">Nothing has arrived yet. New messages appear here as they come in.</p>
      ) : (
        <ul aria-label="Arrived messages" aria-live="polite" className="max-h-[60vh] divide-y divide-[var(--color-border)] overflow-y-auto">
          {seen.map((m) => (
            <li key={m.sequenceNumber} className="px-4 py-2 text-[12.5px]">
              <div className="flex gap-3 text-[var(--color-text-muted)]">
                <span>{formatWhen(m.enqueuedTime, now)}</span><span>#{m.sequenceNumber}</span><span>{formatBytes(m.sizeInBytes)}</span>{m.contentType && <span>{m.contentType}</span>}
              </div>
              <pre className="mt-1 truncate font-mono text-[12px]">{m.body ?? '(no body)'}</pre>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
