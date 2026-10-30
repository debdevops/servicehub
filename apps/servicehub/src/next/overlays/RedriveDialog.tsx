import { RotateCcw } from 'lucide-react'
import type { ReactNode } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useOverlayTitle } from '../../components/overlays/overlayTitle'
import { NotAllowed } from '../../components/ui/NotAllowed'
import { useFailedItem, useRedrive, useRedriveCheck } from '../../hooks/useComponentSource'
import { useMe } from '../../hooks/useIdentity'
import { useNamespace } from '../../hooks/useNamespaces'
import { toProblem } from '../../lib/api/client'
import { formatAge, formatWhen } from '../../lib/format'
import { permission } from '../../lib/permissions'
import { cloudLabel } from '../lib/clouds'
import { itemStatusWords } from '../lib/failedWords'
import { Tip } from '../components/Tip'
import type { NextOverlayBodyProps } from './registry'

function Row({ label, children }: { label: string; children: ReactNode }) {
  return <div className="flex gap-4 border-b border-[var(--color-border)] py-2 text-sm"><dt className="w-32 shrink-0 text-[var(--color-text-muted)]">{label}</dt><dd className="min-w-0 break-words font-semibold">{children}</dd></div>
}

/**
 * Redrive a failed run (build-plan unit PP5), at `?dialog=redrive&ns=<connection>&component=<key>&item=<run id>`: what the cloud will be
 * asked to do, what it cannot promise, and then one button. The cloud restarts the run itself from the step that failed, so ServiceHub
 * never reads the run's input. The answer says what the cloud accepted and nothing more: the result of the run is never known, so it is
 * never called fixed. The server's gate decides whether the button works at all (only a person, only an Approver, only where the
 * component offers it), and its reason is shown beside a disabled button, never hidden.
 */
export default function RedriveDialog({ close }: NextOverlayBodyProps) {
  const [params] = useSearchParams()
  const namespaceId = params.get('ns')
  const componentKey = params.get('component')
  const itemId = params.get('item')
  const ns = useNamespace(namespaceId ?? '')
  const detail = useFailedItem(namespaceId ?? '', componentKey ?? '', namespaceId && componentKey ? itemId : null)
  const check = useRedriveCheck(namespaceId, componentKey, itemId)
  const me = useMe()
  const redrive = useRedrive()
  useOverlayTitle('Redrive a run', 'Ask the cloud to restart one failed run from the step that failed.')

  if (!namespaceId || !componentKey || !itemId) return <p className="text-sm text-[var(--color-text-muted)]">Open a failed run first, then choose Redrive.</p>
  if (ns.isPending || detail.isPending || check.isPending) return <p role="status" className="text-sm text-[var(--color-text-muted)]">Working out what a redrive would do…</p>
  if (ns.isError || detail.isError || check.isError || !detail.data.item) {
    return <p role="alert" className="text-sm text-red-800">ServiceHub couldn’t work out what a redrive would do, so it won’t ask the cloud to do it. {detail.data?.item === null ? 'The run is no longer there.' : ''}</p>
  }

  const cloud = cloudLabel[ns.data.provider]
  const item = detail.data.item
  const now = new Date()
  const may = permission(me.data, 'Approver', 'redrive a run', { recover: true, namespaceId })
  const allowed = check.data.canRedrive && may.allowed
  const outcome = redrive.data

  if (outcome) {
    return (
      <div className="space-y-3" role="status">
        <p className="text-base font-semibold">{outcome.result === 'accepted' ? `${cloud} accepted the request.` : outcome.result === 'rejected' ? `${cloud} did not accept it.` : 'Whether the request was accepted is not known.'}</p>
        <p className="text-sm">{outcome.message}</p>
        {outcome.result === 'accepted' && <p className="text-sm text-[var(--color-text-muted)]">The record says <b>Unverified</b>: ServiceHub asked, and the cloud agreed to try. Look at the run in the cloud’s console to see how it ended.</p>}
        {outcome.result === 'unknown' && <p className="text-sm text-amber-900">Do not ask again until you have checked the run in the cloud’s console. A second request could redrive it twice.</p>}
        <button type="button" onClick={close} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 text-sm font-semibold">Done</button>
      </div>
    )
  }

  return (
    <div className="space-y-4">
      <dl>
        <Row label="Run">{item.summary}</Row>
        <Row label="Status">{itemStatusWords[item.status] ?? item.status}</Row>
        <Row label="Where">{ns.data.displayName ?? ns.data.name} · {cloud}</Row>
        {item.atUtc && <Row label="Ended">{formatWhen(item.atUtc, now)} ({formatAge(item.atUtc, now)} ago)</Row>}
      </dl>

      <section aria-label="What will happen">
        <h3 className="mb-1 flex items-center text-sm font-bold">What will happen<Tip label="dialog:Redrive what will happen" /></h3>
        <ul className="list-disc space-y-1 pl-5 text-sm">
          <li>{cloud} is asked to restart this one run from the step that failed, with the same input it already has.</li>
          <li>{cloud} does the work. ServiceHub never reads the run’s input and starts nothing itself.</li>
          <li>The request is recorded with your name before it is sent, and what {cloud} answers is recorded after.</li>
        </ul>
      </section>

      <section aria-label="What it cannot promise">
        <h3 className="mb-1 flex items-center text-sm font-bold">What it cannot promise<Tip label="dialog:Redrive what it cannot promise" /></h3>
        <ul className="list-disc space-y-1 pl-5 text-sm">
          <li>That the run will finish fixed. The record will say <b>Unverified</b>, whatever happens next.</li>
          <li>That steps which already ran will not run again. Steps that change something outside the cloud may do it twice.</li>
          <li>That {cloud} accepts it: a run that is still going, too old, or on a changed state machine is refused.</li>
        </ul>
      </section>

      {!check.data.canRedrive && <p role="status" className="rounded-xl border border-amber-300 bg-amber-50 px-3.5 py-2.5 text-sm text-amber-900">{check.data.message}</p>}
      {!may.allowed && <NotAllowed reason={may.reason} />}
      {redrive.isError && <p role="alert" className="text-sm text-red-800">That didn’t go through: {toProblem(redrive.error).message} Nothing was recorded as accepted.</p>}

      <div className="flex justify-end gap-2">
        <button type="button" onClick={close} disabled={redrive.isPending} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 text-sm font-semibold">Cancel</button>
        <button type="button" disabled={!allowed || redrive.isPending} onClick={() => redrive.mutate({ namespaceId, componentKey, itemId })}
          className="inline-flex items-center gap-2 rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-sm font-bold text-white disabled:opacity-50">
          <RotateCcw className="h-4 w-4" aria-hidden="true" />{redrive.isPending ? 'Asking…' : `Ask ${cloud} to redrive`}
        </button>
      </div>
    </div>
  )
}
