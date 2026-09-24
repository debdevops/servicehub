import { Check, CircleHelp, RotateCcw, TriangleAlert } from 'lucide-react'
import type { ReplayListItem } from '../lib/api/replay'
import { formatAge } from '../lib/format'
import { providerLabel } from '../lib/providers'

/**
 * The outcome of a replay — the same shape on every cloud (C5), so clouds are compared, not re-learned. Only the
 * status and its words change:
 *
 *  - **Verified** (green) — the cloud can prove the queue stayed empty.
 *  - **Verification required** (amber) — it cannot. Not a failure: the replay may have worked perfectly; what is
 *    unproven is the confirmation. Carries the remedy (C4). Never says or implies the message was not replayed (C3).
 *  - **Came back** (red) — the failure returned inside the watch window.
 *
 * "✓ Replayed" is deliberately not a state: it would read the same on all three clouds, and be false on two (C2).
 */
export function OutcomeCard({ replay, now = new Date() }: { replay: ReplayListItem; now?: Date }) {
  const v = replay.verification
  const cloud = providerLabel[replay.provider]
  const tone = TONES[v.status]

  return (
    <section aria-label="Replay result" className={`rounded-xl border p-4 ${tone.box}`}>
      <p className="flex items-center justify-between text-xs text-[var(--color-text-muted)]">
        <span>Replayed 1 message</span>
        <span className="uppercase tracking-wide">{cloud} · {formatAge(replay.replayedAt, now)} ago</span>
      </p>
      <p className={`mt-1 flex items-center gap-2 font-semibold ${tone.text}`}>
        {tone.icon}
        {tone.title}
      </p>
      <div className="mt-1 space-y-1 text-sm">
        {v.status === 'verified' && <p>The message stayed out of the dead-letter queue for the whole watch window, and {cloud} could see the whole queue.</p>}
        {v.status === 'verification_required' && (
          <>
            <p>{cloud} cannot prove the queue stayed empty. The replay may well have worked — what is unproven is the confirmation.</p>
            {v.remedy === 'SETUP_DLQ_OBSERVER' && (
              <p className="text-[var(--color-text-muted)]">To get a verified result here, set up the dead-letter observer for this namespace.</p>
            )}
          </>
        )}
        {v.status === 'returned' && (
          <p>
            It failed again ({v.confidence === 'Exact' ? 'matched by its recovery ID' : 'matched by its contents'}). Nothing retries it — it is back in Dead
            letters, and the next step is yours.
          </p>
        )}
        {v.status === 'not_sent' && <p>The cloud did not accept it, so nothing was sent back. It is still in the dead-letter queue.</p>}
        {v.status === 'unknown' && <p>Whether it was sent back is not known. Look in {replay.targetEntity} before trying again, or a second copy may be sent.</p>}
      </div>
    </section>
  )
}

const TONES = {
  verified: { box: 'border-[var(--color-success)] bg-[var(--color-success-light)]', text: 'text-[var(--color-text)]', title: 'Verified', icon: <Check className="h-4 w-4" aria-hidden="true" /> },
  verification_required: { box: 'border-[var(--color-warning)] bg-[var(--color-warning-light)]', text: 'text-[var(--color-text)]', title: 'Verification required', icon: <TriangleAlert className="h-4 w-4" aria-hidden="true" /> },
  returned: { box: 'border-[var(--color-error)] bg-[var(--color-error-light)]', text: 'text-[var(--color-text)]', title: 'Came back', icon: <RotateCcw className="h-4 w-4" aria-hidden="true" /> },
  not_sent: { box: 'border-[var(--color-border)] bg-[var(--color-surface-muted)]', text: 'text-[var(--color-text)]', title: 'Not sent', icon: <TriangleAlert className="h-4 w-4" aria-hidden="true" /> },
  unknown: { box: 'border-[var(--color-warning)] bg-[var(--color-warning-light)]', text: 'text-[var(--color-text)]', title: 'Outcome unknown', icon: <CircleHelp className="h-4 w-4" aria-hidden="true" /> },
  // `watching` renders as the WatchCard, never here; it is listed so the map is total.
  watching: { box: 'border-[var(--color-border)]', text: '', title: 'Watching', icon: null },
} as const
