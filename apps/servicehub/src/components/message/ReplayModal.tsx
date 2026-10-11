import { Play, Check, TriangleAlert, Ban } from 'lucide-react'
import { Link, useLocation, useSearchParams } from 'react-router-dom'
import type { OverlayBodyProps } from '../overlays/registry'
import { useDeadLetter } from '../../hooks/useDeadLetter'
import { useMe } from '../../hooks/useIdentity'
import { permission } from '../../lib/permissions'
import { Collapsible } from '../ui/Collapsible'
import { NotAllowed } from '../ui/NotAllowed'
import { useReplay, useReplayProposal } from '../../hooks/useReplay'
import { explainFailure } from '../../lib/analyzer'
import type { ReplayCheck, ReplayOutcome, ReplayProposal } from '../../lib/api/replay'
import { providerLabel } from '../../lib/providers'
import { UnlockHint } from '../UnlockHint'
import { RetryLink } from '../ui/RetryLink'
import { sectionHelp } from '../../content/sections'
import { formatAgo, formatBytes, formatWhen } from '../../lib/format'
import type { DeadLetterDetail } from '../../lib/api/deadLetters'
import { describeEntity } from '../../lib/entities'


const hoursLabel = (h: number) => (h < 1 ? `${Math.round(h * 60)} minutes` : h === 1 ? '1 hour' : `${h} hours`)

/**
 * The proposal for replaying one message (`?modal=replay&message=<id>`), shown BEFORE anything runs:
 * what will happen, why it might fail again, the checks, and what comes after. Then the one button.
 * When replay is blocked the button stays, disabled, with the reason beside it — never hidden (P4).
 */
export default function ReplayModal({ close }: OverlayBodyProps) {
  const [params] = useSearchParams()
  // `replay=` is a row's own Replay: this modal alone, no message drawer behind it. `message=` is the drawer's Replay, over the drawer.
  const raw = params.get('replay') ?? params.get('message')
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
        ServiceHub couldn’t work out what replay would do, so it won’t replay. <RetryLink onRetry={() => void proposal.refetch()} />
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
      <div className="sticky top-0 z-10 -mx-5 -mt-4 space-y-3 border-b border-[var(--color-border)] bg-[var(--color-surface)] px-5 py-3">
        <p className="text-sm text-[var(--color-text-muted)]">
          <span className="font-mono text-[13px]">{p.sourceEntity}</span> → <span className="font-mono text-[13px]">{p.targetEntity}</span> · {cloud} ·{' '}
          {p.namespaceName} · <span className="rounded-full bg-[var(--color-surface-muted)] px-2 py-0.5 text-xs font-medium">{p.environment}</span>
        </p>
        <button
          type="button"
          disabled={!p.canExecute || !may.allowed || replay.isPending}
          onClick={() => replay.mutate(p.dlqMessageId)}
          className="flex w-full items-center justify-center gap-2 rounded-xl bg-[var(--color-primary-600)] px-4 py-2.5 text-sm font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
        >
          <Play className="h-4 w-4 fill-current" aria-hidden="true" /> {replay.isPending ? 'Replaying…' : 'Replay 1 message'}
        </button>
        {/* Pinned with the button, so a refusal or failure is seen where the click happened — not below the fold. */}
        {replay.isError && (
          <p role="alert" className="text-sm text-[var(--color-error)]">
            {(replay.error as { response?: { data?: { detail?: string } } })?.response?.data?.detail ?? 'The replay did not go through. Check Replayed before trying again, in case it was accepted.'}
          </p>
        )}
        {replay.isPending && (
          <p role="status" className="text-sm text-[var(--color-text-muted)]">
            {cloud} is being asked for this one message. With a long dead-letter queue this can take up to a minute — keep this open; nothing is lost if you leave.
          </p>
        )}
        <NotAllowed reason={may.reason} />
      </div>

      <MessageToReplay detail={message.data} pending={message.isPending} />

      <section aria-label="What will happen">
        <Collapsible title="What will happen">
        <p className="text-sm">
          1 message is sent back to <span className="font-mono text-[13px] font-semibold">{p.targetEntity}</span>. Its copy in the dead-letter queue is removed
          once {cloud} accepts it.{' '}
          {p.stampsRecoveryMarker
            ? 'It carries a recovery ID, so ServiceHub can tell if it comes back.'
            : `${cloud} cannot carry a recovery ID, so a return can only be matched by its contents.`}
        </p>
        </Collapsible>
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
        <Collapsible title="Safety checks" summary={needAttention === 0 ? 'all passed' : `${needAttention} to look at`} defaultOpen={needAttention > 0}>
        <ul className="divide-y divide-[var(--color-border)] rounded-xl border border-[var(--color-border)]">
          {p.checks.map((c) => <CheckRow key={c.id} check={c} />)}
        </ul>
        </Collapsible>
      </section>

      <section aria-label="After it runs">
        <Collapsible title="After it runs">
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
        </Collapsible>
      </section>

      {!p.canExecute && <UnlockHint code={p.blockedCode} approvable={p.approvable} />}

      <footer className="sticky bottom-0 z-10 -mx-5 -mb-4 flex items-center justify-between gap-3 border-t border-[var(--color-border)] bg-[var(--color-surface)] px-5 py-3">
        <p className="text-xs text-[var(--color-text-muted)]">
          {me.data ? (me.data.actor.isSession ? 'Recorded as from this browser session' : <>Recorded with your name — <b>{me.data.actor.label}</b></>) : 'Recorded with your replay'}
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
  const { search } = useLocation()
  // The same cloud and namespace, on another tab, with this message's modal and drawer put away.
  const follow = (tab: 'active' | 'replayed') => {
    const p = new URLSearchParams(search)
    ;['modal', 'message', 'replay', 'view', 'page', 'reason', 'entity', 'q'].forEach((k) => p.delete(k))
    p.set('tab', tab)
    return `/?${p.toString()}`
  }
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
            Recorded — you can find it under Replayed. {proposal.canConfirm ? 'ServiceHub will say whether it stayed fixed when the watch window ends.' : 'The result will read “Verification required” — this cloud cannot prove the queue stayed empty.'}
          </p>
        )}
        {outcome.result === 'unknown' && (
          <p className="mt-2 text-xs text-[var(--color-text-muted)]">Look in {proposal.targetEntity} before trying again, or a second copy may be sent.</p>
        )}
      </div>
      {outcome.result === 'accepted' && (
        <p className="text-sm text-[var(--color-text-muted)]">
          It is now on <span className="font-mono text-[13px]">{proposal.targetEntity}</span>, and the dead letter has left the list. Where the consumer is running it is picked up straight away, so it may already be gone from{' '}
          <Link to={follow('active')} onClick={close} className="font-medium text-[var(--color-primary-700)] hover:underline">Active messages</Link>; its result is recorded in{' '}
          <Link to={follow('replayed')} onClick={close} className="font-medium text-[var(--color-primary-700)] hover:underline">Replayed</Link>.
        </p>
      )}
      <div className="flex justify-end">
        <button type="button" onClick={close} className="rounded-xl bg-[var(--color-primary-600)] px-4 py-2 text-sm font-semibold text-white">Done</button>
      </div>
    </div>
  )
}

/** The body as a person reads it: pretty JSON when it is JSON, as-is when it is not. */
function readable(body: string): string {
  try {
    return JSON.stringify(JSON.parse(body), null, 2)
  } catch {
    return body
  }
}

/**
 * The message about to be replayed, so nobody presses the button without knowing which one it is: its ID, where it is stuck, why it
 * failed (the recorded reason and error text), when it was set aside, how often it was tried, its size and what it says.
 */
function MessageToReplay({ detail, pending }: { detail: DeadLetterDetail | undefined; pending: boolean }) {
  if (pending) return <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading the message…</p>
  if (!detail) return null
  const m = detail.item
  const e = describeEntity(m.entityName, m.entityType, m.topicName)
  const now = new Date()
  const facts: [string, React.ReactNode][] = [
    ['Message ID', <span key="id" className="break-all font-mono text-[12px]">{m.messageId}</span>],
    ['Stuck in', <span key="q" className="font-mono text-[12px]">{e.topic ? `${e.topic} › ` : ''}{e.name}</span>],
    ['Set aside', <span key="w">{formatWhen(m.detectedAtUtc, now)} · {formatAgo(m.detectedAtUtc, now)}</span>],
    ['Tried', <span key="t">{m.deliveryCount > 0 ? `${m.deliveryCount} ${m.deliveryCount === 1 ? 'time' : 'times'}` : 'not reported by this cloud'}</span>],
    ['Size', <span key="s">{formatBytes(m.sizeInBytes)}</span>],
  ]
  return (
    <section aria-label="The message to be replayed" className="rounded-xl border border-[var(--color-border)]">
      <Collapsible title="The message" help={sectionHelp.replay.message}>
        <div className="space-y-3 px-3 pb-3 text-sm">
          <dl className="grid grid-cols-[6.5rem_1fr] gap-y-1.5">
            {facts.map(([k, v]) => (<div key={k} className="contents"><dt className="text-[var(--color-text-muted)]">{k}</dt><dd>{v}</dd></div>))}
          </dl>
          <div>
            <span className="inline-block rounded-full bg-[var(--color-error-light)] px-2.5 py-0.5 text-xs font-semibold text-[#b91c1c]">{m.deadLetterReason ?? 'Reason not recorded'}</span>
            <p className="mt-1 text-[12.5px]">{m.deadLetterErrorDescription ?? 'The cloud gave no error text for this one.'}</p>
          </div>
          {detail.bodyPreview ? (
            <div>
              <p className="mb-1 text-xs font-semibold text-[var(--color-text-muted)]">What it says{detail.bodyIsPreview ? ' (first part)' : ''}</p>
              <pre tabIndex={0} aria-label="What the message says, text" className="max-h-44 overflow-auto rounded-lg bg-[var(--color-surface-muted)] p-3 font-mono text-[11.5px] leading-relaxed">{readable(detail.bodyPreview)}</pre>
            </div>
          ) : (
            <p className="text-xs text-[var(--color-text-muted)]">This message has no body to show.</p>
          )}
        </div>
      </Collapsible>
    </section>
  )
}
