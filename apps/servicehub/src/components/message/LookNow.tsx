import { Eye, Loader2, TriangleAlert } from 'lucide-react'
import { useMe } from '../../hooks/useIdentity'
import { permission } from '../../lib/permissions'
import { NotAllowed } from '../ui/NotAllowed'
import { useLookAtDeadLetters, type LookSummary } from '../../hooks/useDeadLetters'
import type { Namespace } from '../../lib/api/namespaces'

const plural = (n: number, one: string, many = `${one}s`) => `${n.toLocaleString()} ${n === 1 ? one : many}`

/**
 * Where ServiceHub does not look on its own (no repeatable peek — AWS SQS, Google Pub/Sub), a person can ask it
 * to look now. What it sees is recorded like any other dead letter, so it can be opened, read and replayed.
 * The cost is said before the button, not after: on these clouds a look counts as one delivery attempt.
 */
export function LookNow({ cloud, namespaces }: { cloud: string; namespaces: readonly Namespace[] }) {
  const look = useLookAtDeadLetters()
  const ids = namespaces.map((n) => n.id)
  const me = useMe().data
  // A look counts as a delivery attempt on these clouds, so it needs the Operator role in every namespace it touches.
  const may = namespaces.map((n) => permission(me, 'Operator', `look at ${cloud}’s dead letters`, { recover: true, namespaceId: n.id })).find((p) => !p.allowed) ?? { allowed: true, reason: null }

  return (
    <section aria-label={`Look at ${cloud}'s dead letters`} className="mb-4 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface-muted)] px-4 py-3 text-sm">
      <div className="flex flex-wrap items-center gap-3">
        <p className="min-w-0 flex-1 text-[var(--color-text-muted)]">
          ServiceHub doesn’t look in {cloud} on its own: reading a dead letter there counts as one delivery attempt. Ask it to
          look and it reads up to 100 per queue and keeps them here, so you can open, read and replay them.
        </p>
        <button
          type="button"
          disabled={look.isPending || ids.length === 0 || !may.allowed}
          onClick={() => look.mutate(ids)}
          className="inline-flex items-center gap-2 rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 font-semibold text-white hover:bg-[var(--color-primary-700)] disabled:opacity-60"
        >
          {look.isPending ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <Eye className="h-4 w-4" aria-hidden="true" />}
          {look.isPending ? 'Looking…' : `Look at ${cloud}’s dead letters now`}
        </button>
      </div>
      <NotAllowed reason={may.reason} />
      <div aria-live="polite">
        {look.isPending && <p className="mt-2 text-xs text-[var(--color-text-muted)]">This can take up to a minute per queue — {cloud} is asked, not a copy.</p>}
        {look.isError && (
          <p role="alert" className="mt-2 flex items-center gap-2 text-[var(--color-text)]">
            <TriangleAlert className="h-4 w-4 text-[var(--color-warning)]" aria-hidden="true" /> ServiceHub couldn’t ask {cloud}. Nothing was changed.{' '}
            <button type="button" onClick={() => look.mutate(ids)} className="font-medium text-[var(--color-primary-700)] hover:underline">Try again</button>
          </p>
        )}
        {look.data && <Result summary={look.data} cloud={cloud} />}
      </div>
    </section>
  )
}

function Result({ summary, cloud }: { summary: LookSummary; cloud: string }) {
  const time = summary.at.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  const parts = [`${plural(summary.newMessages, 'new dead letter')} recorded`]
  if (summary.resolved > 0) parts.push(`${summary.resolved.toLocaleString()} no longer in the queue`)
  return (
    <div className="mt-2 text-[var(--color-text)]">
      <p>
        Looked at {time} — {plural(summary.queuesExamined, 'queue')} with dead letters: {parts.join(', ')}.
      </p>
      {summary.unconfirmed > 0 && (
        <p className="mt-1 text-xs text-[var(--color-text-muted)]">{plural(summary.unconfirmed, 'queue')} could not be read to the end; what was recorded before is left as it was.</p>
      )}
      {summary.failed > 0 && (
        <p className="mt-1 text-xs text-[var(--color-text-muted)]">
          {plural(summary.failed, 'namespace')} in {cloud} could not be read{summary.reasons.length ? `: ${summary.reasons.join(' ')}` : '.'}
        </p>
      )}
    </div>
  )
}
