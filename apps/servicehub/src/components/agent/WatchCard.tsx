import { Eye } from 'lucide-react'
import type { ReplayListItem } from '../../lib/api/replay'
import { providerLabel } from '../../lib/providers'

const clock = (iso: string) => new Date(iso).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })

/**
 * Immediately after a replay, the drawer's agent slot becomes this: what ServiceHub is watching for, and until
 * when. It resolves in place into the {@link OutcomeCard} when the window ends (R9). The promise is only the one
 * the cloud can keep: "confirmed fixed" is promised only where absence can be proven (R4).
 */
export function WatchCard({ replay }: { replay: ReplayListItem }) {
  const v = replay.verification
  const cloud = providerLabel[replay.provider]
  return (
    <section aria-label="Watching this replay" className="rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] p-4">
      <p className="flex items-center gap-2 font-semibold"><Eye className="h-4 w-4" aria-hidden="true" /> Watching this one</p>
      {v.watchUntil && v.canConfirm && (
        <p className="mt-1 text-sm">If it doesn’t come back by <b>{clock(v.watchUntil)}</b>, it’s confirmed fixed.</p>
      )}
      {v.watchUntil && !v.canConfirm && (
        <>
          <p className="mt-1 text-sm">
            ServiceHub is watching until <b>{clock(v.watchUntil)}</b>, but {cloud} cannot prove the queue stays empty — so this will end as
            “Verification required”, not “Verified”.
          </p>
          {v.remedy === 'SETUP_DLQ_OBSERVER' && (
            <p className="mt-1 text-sm text-[var(--color-text-muted)]">Setting up the dead-letter observer for this namespace is what makes a verified result possible.</p>
          )}
        </>
      )}
      <p className="mt-1 text-xs text-[var(--color-text-muted)]">If it comes back, nothing retries it. It returns to the list and tells you.</p>
    </section>
  )
}
