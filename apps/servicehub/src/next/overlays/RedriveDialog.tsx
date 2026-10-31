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
import { redriveWords } from '../lib/redriveWords'
import { Tip } from '../components/Tip'
import type { NextOverlayBodyProps } from './registry'

/** The word the record will say is shown bold wherever a sentence carries it. */
function boldUnverified(line: string): ReactNode {
  const at = line.indexOf('Unverified')
  return at < 0 ? line : <>{line.slice(0, at)}<b>Unverified</b>{line.slice(at + 'Unverified'.length)}</>
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return <div className="flex gap-4 border-b border-[var(--color-border)] py-2 text-sm"><dt className="w-32 shrink-0 text-[var(--color-text-muted)]">{label}</dt><dd className="min-w-0 break-words font-semibold">{children}</dd></div>
}

/**
 * Redrive (or resubmit) a failed run (build-plan units PP5 and R2), at `?dialog=redrive&ns=<connection>&component=<key>&item=<run id>`:
 * what the cloud will be asked to do, what it cannot promise, and then one button. The words come from `redriveWords(component)`, because
 * the two actions differ: a Step Functions redrive restarts the same run from the step that failed, while a Logic Apps resubmit starts a
 * new run and every step runs again. ServiceHub never reads the run's or the trigger's input. The answer says what the cloud accepted and nothing more: the result of the run is never known, so it is
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
  const words = redriveWords(componentKey)
  useOverlayTitle(words.title, words.subtitle)

  if (!namespaceId || !componentKey || !itemId) return <p className="text-sm text-[var(--color-text-muted)]">Open a failed run first, then choose {words.link.replace('…', '')}.</p>
  if (ns.isPending || detail.isPending || check.isPending) return <p role="status" className="text-sm text-[var(--color-text-muted)]">Working out what a {words.verb} would do…</p>
  if (ns.isError || detail.isError || check.isError || !detail.data.item) {
    return <p role="alert" className="text-sm text-red-800">ServiceHub couldn’t work out what a {words.verb} would do, so it won’t ask the cloud to do it. {detail.data?.item === null ? 'The run is no longer there.' : ''}</p>
  }

  const cloud = cloudLabel[ns.data.provider]
  const item = detail.data.item
  const now = new Date()
  const may = permission(me.data, 'Approver', `${words.verb} a run`, { recover: true, namespaceId })
  const allowed = check.data.canRedrive && may.allowed
  const outcome = redrive.data

  if (outcome) {
    return (
      <div className="space-y-3" role="status">
        <p className="text-base font-semibold">{outcome.result === 'accepted' ? `${cloud} accepted the request.` : outcome.result === 'rejected' ? `${cloud} did not accept it.` : 'Whether the request was accepted is not known.'}</p>
        <p className="text-sm">{outcome.message}</p>
        {outcome.result === 'accepted' && <p className="text-sm text-[var(--color-text-muted)]">The record says <b>Unverified</b>: ServiceHub asked, and the cloud agreed to try. Look at the run in the cloud’s console to see how it ended.</p>}
        {outcome.result === 'unknown' && <p className="text-sm text-amber-900">Do not ask again until you have checked the run in the cloud’s console. A second request could {words.verb} it twice.</p>}
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
          {words.will(cloud).map((line) => <li key={line}>{line}</li>)}
        </ul>
      </section>

      <section aria-label="What it cannot promise">
        <h3 className="mb-1 flex items-center text-sm font-bold">What it cannot promise<Tip label="dialog:Redrive what it cannot promise" /></h3>
        <ul className="list-disc space-y-1 pl-5 text-sm">
          {words.cannot(cloud).map((line) => <li key={line}>{boldUnverified(line)}</li>)}
        </ul>
      </section>

      {check.data.previousRequestAtUtc && (
        <p role="status" className="rounded-xl border border-amber-300 bg-amber-50 px-3.5 py-2.5 text-sm text-amber-900">
          The cloud already accepted a {words.verb} of this run at {formatWhen(check.data.previousRequestAtUtc, now)} ({formatAge(check.data.previousRequestAtUtc, now)} ago). Asking again will {words.again}. This is a warning, not a block.
        </p>
      )}
      {!check.data.canRedrive && <p role="status" className="rounded-xl border border-amber-300 bg-amber-50 px-3.5 py-2.5 text-sm text-amber-900">{check.data.message}</p>}
      {!may.allowed && <NotAllowed reason={may.reason} />}
      {redrive.isError && <p role="alert" className="text-sm text-red-800">That didn’t go through: {toProblem(redrive.error).message} Nothing was recorded as accepted.</p>}

      <div className="flex justify-end gap-2">
        <button type="button" onClick={close} disabled={redrive.isPending} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 text-sm font-semibold">Cancel</button>
        <button type="button" disabled={!allowed || redrive.isPending} onClick={() => redrive.mutate({ namespaceId, componentKey, itemId })}
          className="inline-flex items-center gap-2 rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-sm font-bold text-white disabled:opacity-50">
          <RotateCcw className="h-4 w-4" aria-hidden="true" />{redrive.isPending ? 'Asking…' : words.button(cloud)}
        </button>
      </div>
    </div>
  )
}
