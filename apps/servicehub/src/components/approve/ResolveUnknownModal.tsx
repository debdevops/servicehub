import { useState } from 'react'
import { Check, CircleHelp } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'
import { useMe } from '../../hooks/useIdentity'
import { usePendingWork, useResolvePending } from '../../hooks/usePendingWork'
import { toProblem } from '../../lib/api/client'
import { formatAge } from '../../lib/format'
import { permission } from '../../lib/permissions'
import { providerLabel } from '../../lib/providers'
import { useOverlayTitle } from '../overlays/overlayTitle'
import { NotAllowed } from '../ui/NotAllowed'
import { Skeleton } from '../ui/Skeleton'

/**
 * Say what happened (`?modal=approve&entry=<id>`, shown by the Approve modal when the entry is an attempt with no answer). ServiceHub put one message back, or tried to, and stopped (or lost contact
 * with the cloud) before it could record the answer. It cannot know whether the message was sent, so it will not send it again
 * until a person has looked at the queue.
 *
 * The person writes what they found. ServiceHub records it with their name and closes the attempt as written off — it does **not**
 * mark the attempt recovered or failed, because nobody has proved either. Afterwards a new replay is a fresh, gated, deliberate one.
 */
export default function ResolveUnknownModal({ close }: { readonly close: () => void }) {
  const [params] = useSearchParams()
  const entryId = params.get('entry')
  const pending = usePendingWork()
  const me = useMe()
  const resolve = useResolvePending()
  const [found, setFound] = useState('')
  useOverlayTitle('Say what happened', 'An attempt to put a message back has no recorded answer. Nothing is replayed here.')

  if (resolve.isSuccess) {
    return (
      <div className="space-y-3">
        <p className="flex items-center gap-2 text-base font-semibold"><Check className="h-5 w-5 text-[var(--color-success)]" aria-hidden="true" /> Recorded with your name.</p>
        <p className="text-[13px] text-[var(--color-text-muted)]">
          The attempt is closed without a verdict. If you want the message put back, replay it from Dead letters — that is a new, checked attempt.
        </p>
        <div className="flex justify-end"><button type="button" onClick={close} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 font-semibold">Done</button></div>
      </div>
    )
  }

  if (!pending.data) return <Skeleton label="Loading what is waiting" variant="block" />
  const item = pending.data.items.find((i) => i.kind === 'unresolved' && i.entryId === entryId)
  if (!item) {
    return (
      <div className="space-y-3">
        <p className="text-[13px]">Nothing unresolved is waiting under that link — it may already have been settled.</p>
        <div className="flex justify-end"><button type="button" onClick={close} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 font-semibold">Close</button></div>
      </div>
    )
  }

  const may = permission(me.data, 'Approver', 'say what happened to an attempt', { recover: true, namespaceId: item.namespaceId ?? undefined })
  const where = [item.provider ? providerLabel[item.provider] : null, item.namespaceName, item.entity].filter(Boolean).join(' · ')

  return (
    <div className="space-y-3.5">
      <div className="flex items-start gap-3 rounded-lg border border-[#fcd34d] bg-[#fffbeb] p-3">
        <CircleHelp className="mt-0.5 h-5 w-5 shrink-0 text-[#d97706]" aria-hidden="true" />
        <div>
          <p className="text-[13px] font-bold text-[#92400e]">{where}</p>
          <p className="text-[13px] text-[var(--color-text)]">{item.reason}</p>
          <p className="mt-1 text-[11.5px] text-[var(--color-text-muted)]">waiting {formatAge(item.since, new Date())} · <span className="font-mono">{item.reasonCode}</span></p>
        </div>
      </div>

      <ol className="list-decimal space-y-1 pl-5 text-[13px]">
        <li>Open the queue in the cloud and look for this message. There may be a copy back in the main queue, or only the dead letter, or neither.</li>
        <li>Write what you found below. It is recorded with your name.</li>
        <li>Nothing is replayed, and the attempt is not marked recovered or failed — only you know.</li>
      </ol>

      <label className="block text-[13px] font-semibold" htmlFor="resolve-found">What did you find? <span className="font-normal text-[var(--color-text-muted)]">(required)</span></label>
      <textarea id="resolve-found" autoFocus value={found} onChange={(e) => setFound(e.target.value)} maxLength={400} rows={3}
        className="w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2"
        placeholder="e.g. the main queue holds one copy, so nothing more needs sending" />

      {!may.allowed && <NotAllowed reason={may.reason} />}
      {resolve.isError && <p role="alert" className="text-[#b91c1c]">That didn’t go through: {toProblem(resolve.error).message} Nothing was recorded.</p>}

      <div className="flex flex-wrap items-center justify-end gap-2">
        <button type="button" onClick={close} disabled={resolve.isPending} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 font-semibold">Not now</button>
        <button type="button" disabled={!found.trim() || resolve.isPending || !may.allowed}
          onClick={() => resolve.mutate({ entryId: item.entryId!, reason: found.trim() })}
          className="rounded-lg bg-[#b45309] px-4 py-2 font-bold text-white hover:bg-[#92400e] disabled:opacity-50">
          {resolve.isPending ? 'Recording…' : 'Record what I found'}
        </button>
      </div>
    </div>
  )
}
