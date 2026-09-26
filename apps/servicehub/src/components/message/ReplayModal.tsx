import { Play, Check, TriangleAlert, Ban } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'
import type { OverlayBodyProps } from '../overlays/registry'
import { useDeadLetter } from '../../hooks/useDeadLetter'
import { useMe } from '../../hooks/useIdentity'
import { permission } from '../../lib/permissions'
import { NotAllowed } from '../ui/NotAllowed'
import { useReplay, useReplayProposal } from '../../hooks/useReplay'
import { explainFailure } from '../../lib/analyzer'
import type { ReplayCheck, ReplayOutcome, ReplayProposal } from '../../lib/api/replay'
import { providerLabel } from '../../lib/providers'

/** What each gate reason code means and what to do about it. The code is the fact; this is its remedy. */
const remedies: Readonly<Record<string, string>> = {
  PRODUCTION_ELEVATION_REQUIRED: 'This is a production namespace. This version of ServiceHub does not replay in production.',
  NOT_ACTIVE: 'ServiceHub has already seen this message leave the dead-letter queue.',
  RECURRENCE_CAP_EXCEEDED: 'This message has come back too many times to be put back automatically.',
  PROVIDER_CANNOT_VERIFY_ABSENCE: 'This cloud cannot prove the queue stayed empty.',
  EMERGENCY_STOP_ACTIVE: 'Emergency stop is on.',
}

const hoursLabel = (h: number) => (h < 1 ? `${Math.round(h * 60)} minutes` : h === 1 ? '1 hour' : `${h} hours`)

/**
 * The proposal for replaying one message (`?modal=replay&message=<id>`), shown BEFORE anything runs:
 * what will happen, why it might fail again, the checks, and what comes after. Then the one button.
 * When replay is blocked the button stays, disabled, with the reason beside it — never hidden (P4).
 */
export default function ReplayModal({ close }: OverlayBodyProps) {
  const [params] = useSearchParams()
  const raw = params.get('message')
  const id = raw !== null && /^\d+$/.test(raw) ? Number(raw) : null
  const proposal = useReplayProposal(id)
  const message = useDeadLetter(id)
  const me = useMe()
  const replay = useReplay()

  if (id === null) return <p className="text-sm text-[var(--color-text-muted)]">Open a message first, then choose Replay.</p>
  if (proposal.isPending) return <p role="status" className="text-sm text-[var(--color-text-muted)]">Working out what replay would do…</p>
  if (proposal.isError || !proposal.data) {
    return (
      <p role="alert" className="text-sm text-[var(--color-error)]">
        ServiceHub couldn’t work out what replay would do, so it won’t replay. Close this and try again.
      </p>
    )
  }

  if (replay.data) return <Result outcome={replay.data} proposal={proposal.data} close={close} />

  const p = proposal.data
  const explanation = message.data
    ? explainFailure(message.data.item.deadLetterReason, message.data.item.deadLetterErrorDescription, message.data.item.deliveryCount, {
        body: message.data.bodyPreview,
        propertiesJson: message.data.applicationPropertiesJson,
      })
    : null
  const mayFailAgain = !!explanation?.failingField || p.priorAttempts > 0
  const cloud = providerLabel[p.provider]
  const needAttention = p.checks.filter((c) => c.state !== 'passed').length
  const may = permission(me.data, 'Operator', 'replay this message', { recover: true, namespaceId: message.data?.item.namespaceId })

  return (
    <div className="space-y-5">
      <p className="text-sm text-[var(--color-text-muted)]">
        <span className="font-mono text-[13px]">{p.sourceEntity}</span> → <span className="font-mono text-[13px]">{p.targetEntity}</span> · {cloud} ·{' '}
        {p.namespaceName} · <span className="rounded-full bg-[var(--color-surface-muted)] px-2 py-0.5 text-xs font-medium">{p.environment}</span>
      </p>

      <section aria-label="What will happen">
        <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">What will happen</h3>
        <p className="text-sm">
          1 message is sent back to <span className="font-mono text-[13px] font-semibold">{p.targetEntity}</span>. Its copy in the dead-letter queue is removed
          once {cloud} accepts it.{' '}
          {p.stampsRecoveryMarker
            ? 'It carries a recovery ID, so ServiceHub can tell if it comes back.'
            : `${cloud} cannot carry a recovery ID, so a return can only be matched by its contents.`}
        </p>
      </section>

      {mayFailAgain && (
        <section aria-label="Risk" className="rounded-xl border border-[var(--color-warning)] bg-[var(--color-warning-light)] p-4 text-sm">
          <p className="flex items-center gap-2 font-semibold"><TriangleAlert className="h-4 w-4" aria-hidden="true" /> It may fail the same way again</p>
          {explanation?.failingField && (
            <p className="mt-1">
              It failed because <code className="font-semibold">{explanation.failingField}</code> is missing. Replaying the same body only helps if the consumer or the sender was fixed since.
            </p>
          )}
          {p.priorAttempts > 0 && (
            <p className="mt-1">This message has been replayed {p.priorAttempts} {p.priorAttempts === 1 ? 'time' : 'times'} before.</p>
          )}
          {p.othersLikeIt > 0 && <p className="mt-1"><b>{p.othersLikeIt} other {p.othersLikeIt === 1 ? 'message' : 'messages'}</b> in this queue failed the same way.</p>}
        </section>
      )}

      <section aria-label="Safety checks">
        <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">
          Safety checks — {needAttention === 0 ? 'all passed' : `${needAttention} to look at`}
        </h3>
        <ul className="divide-y divide-[var(--color-border)] rounded-xl border border-[var(--color-border)]">
          {p.checks.map((c) => <CheckRow key={c.id} check={c} />)}
        </ul>
      </section>

      <section aria-label="After it runs">
        <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">After it runs</h3>
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="rounded-xl border border-[var(--color-border)] p-3 text-sm">
            ServiceHub records this replay and opens a <b>{hoursLabel(p.observationWindowHours)}</b> watch window.{' '}
            {p.canConfirm
              ? `${cloud} can prove whether it stayed fixed.`
              : `${cloud} cannot prove it stayed fixed, so the result will read “verification required”.`}{' '}
          </div>
          <div className="rounded-xl border border-[var(--color-border)] p-3 text-sm">
            If it comes back, <b>nothing retries it</b>. It returns to the list, and the next step is yours.
          </div>
        </div>
      </section>

      {!p.canExecute && (
        <p role="status" className="flex items-start gap-2 rounded-xl bg-[var(--color-surface-muted)] p-3 text-sm">
          <Ban className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
          <span>
            {p.blockedCode ? remedies[p.blockedCode] ?? 'Replay is not allowed here.' : 'Replay is not allowed here.'}
            {p.blockedCode && <span className="ml-1 font-mono text-xs text-[var(--color-text-muted)]">{p.blockedCode}</span>}
            {p.approvable && ' A person with approval rights can decide this.'}
          </span>
        </p>
      )}

      {replay.isError && (
        <p role="alert" className="text-sm text-[var(--color-error)]">
          {(replay.error as { response?: { data?: { detail?: string } } })?.response?.data?.detail ?? 'The replay did not go through.'}
        </p>
      )}

      <footer className="flex items-center justify-between gap-3 border-t border-[var(--color-border)] pt-4">
        <p className="text-xs text-[var(--color-text-muted)]">
          {me.data ? (me.data.actor.isSession ? 'Recorded as from this browser session' : <>Recorded with your name — <b>{me.data.actor.label}</b></>) : 'Recorded in the ledger'}
        </p>
        <div className="flex gap-2">
          <button type="button" onClick={close} className="rounded-xl border border-[var(--color-border)] px-4 py-2 text-sm font-semibold">Cancel</button>
          <button
            type="button"
            disabled={!p.canExecute || !may.allowed || replay.isPending}
            onClick={() => replay.mutate(p.dlqMessageId)}
            className="flex items-center gap-2 rounded-xl bg-[var(--color-primary-600)] px-4 py-2 text-sm font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
          >
            <Play className="h-4 w-4 fill-current" aria-hidden="true" /> {replay.isPending ? 'Replaying…' : 'Replay 1 message'}
          </button>
        </div>
      </footer>
      <NotAllowed reason={may.reason} />
    </div>
  )
}

function CheckRow({ check }: { check: ReplayCheck }) {
  const icon =
    check.state === 'passed' ? <Check className="h-4 w-4 text-[var(--color-success)]" aria-label="Passed" />
    : check.state === 'warning' ? <TriangleAlert className="h-4 w-4 text-[var(--color-warning)]" aria-label="Look at this" />
    : <Ban className="h-4 w-4 text-[var(--color-error)]" aria-label="Blocked" />
  return (
    <li className="flex items-start gap-3 px-3 py-2 text-sm">
      <span className="mt-0.5">{icon}</span>
      <span className="flex-1">
        {check.label}
        {check.detail && <span className="block text-xs text-[var(--color-text-muted)]">{check.detail}</span>}
      </span>
    </li>
  )
}

/** What happened. Three honest endings: sent back, the cloud refused, or nobody can tell. */
function Result({ outcome, proposal, close }: { outcome: ReplayOutcome; proposal: ReplayProposal; close: () => void }) {
  const border =
    outcome.result === 'accepted' ? 'border-[var(--color-success)]' : outcome.result === 'rejected' ? 'border-[var(--color-error)]' : 'border-[var(--color-warning)]'
  const heading =
    outcome.result === 'accepted' ? 'Sent back' : outcome.result === 'rejected' ? 'The cloud did not accept it' : 'Outcome unknown'
  return (
    <div className="space-y-4" role="status">
      <div className={`rounded-xl border p-4 ${border}`}>
        <p className="font-semibold">{heading}</p>
        <p className="mt-1 text-sm">{outcome.message}</p>
        {outcome.result === 'accepted' && !outcome.markerApplied && proposal.stampsRecoveryMarker && (
          <p className="mt-2 text-xs text-[var(--color-text-muted)]">
            The recovery ID could not be attached to this copy, so a return can only be matched by its contents.
          </p>
        )}
        {outcome.result === 'accepted' && (
          <p className="mt-2 text-xs text-[var(--color-text-muted)]">
            Recorded in the ledger. {proposal.canConfirm ? 'ServiceHub will say whether it stayed fixed when the watch window ends.' : 'The result will read “Verification required” — this cloud cannot prove the queue stayed empty.'}
          </p>
        )}
        {outcome.result === 'unknown' && (
          <p className="mt-2 text-xs text-[var(--color-text-muted)]">Look in {proposal.targetEntity} before trying again, or a second copy may be sent.</p>
        )}
      </div>
      <div className="flex justify-end">
        <button type="button" onClick={close} className="rounded-xl bg-[var(--color-primary-600)] px-4 py-2 text-sm font-semibold text-white">Done</button>
      </div>
    </div>
  )
}
