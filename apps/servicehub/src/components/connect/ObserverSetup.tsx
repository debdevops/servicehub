import { useState, type FormEvent } from 'react'
import { CircleCheck, CircleHelp } from 'lucide-react'
import { useConfigureDlqObserver, useDlqObserver } from '../../hooks/useNamespaces'
import type { Namespace } from '../../lib/api/namespaces'
import { formatAgo } from '../../lib/format'
import { toProblem } from '../../lib/api/client'

/** What each cloud calls the two things a person has to name. The observer itself is set up in the cloud; this only says where it writes. */
const words = {
  azure: { log: '', queue: '' },
  aws: { log: 'Table the observer writes to (DynamoDB)', queue: 'Dead-letter queue to test (SQS queue name)' },
  gcp: { log: 'Collection the observer writes to (Firestore)', queue: 'Dead-letter topic to test (Pub/Sub topic name)' },
} as const

/**
 * Per-cloud DLQ observer setup (unit 4.2), shown only for a cloud that cannot confirm a replay stayed fixed on its own.
 * Turning it on is the approval for ServiceHub to send one small test message into the dead-letter queue named here and look it up in
 * the observer's log; the cloud counts as watched only while that keeps working — the status line never says more than the log showed.
 */
export function ObserverSetup({ ns }: { ns: Namespace }) {
  const needed = ns.capabilities ? !ns.capabilities.canProveDlqAbsence : false
  const observer = useDlqObserver(ns.id, needed)
  const save = useConfigureDlqObserver(ns.id)
  const [editing, setEditing] = useState(false)
  const [log, setLog] = useState('')
  const [queue, setQueue] = useState('')

  if (!needed || observer.isPending || observer.isError || !observer.data.needed) return null
  const o = observer.data
  const label = words[ns.provider]
  const Icon = o.live ? CircleCheck : CircleHelp

  const open = () => { setLog(o.observerReference ?? ''); setQueue(o.dlqEntityName ?? ''); setEditing(true) }
  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate({ enabled: true, observerReference: log.trim(), dlqEntityName: queue.trim() }, { onSuccess: () => setEditing(false) })
  }

  return (
    <div className="mt-3 rounded-md border border-[var(--color-border)] bg-[var(--color-surface-muted)] p-3" aria-label={`Observer for ${ns.displayName ?? ns.name}`}>
      <p className="text-xs font-semibold text-[var(--color-text)]">Dead-letter observer</p>
      <p className={`mt-1 flex items-start gap-1.5 text-xs ${o.live ? 'text-[#047857]' : 'text-[var(--color-text-muted)]'}`}>
        <Icon className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" /> <span>{o.status}</span>
      </p>
      {o.enabled && o.lastConfirmedAt && <p className="text-xs text-[var(--color-text-muted)]">Last seen working: {formatAgo(o.lastConfirmedAt, new Date())}</p>}
      {editing ? (
        <form onSubmit={submit} className="mt-2 space-y-2">
          <p className="text-xs text-[var(--color-text-muted)]">
            Set this up only after the observer has been created in your cloud. Turning it on lets ServiceHub send one small test message into the
            dead-letter queue below, now and then, to check the observer is still writing.
          </p>
          <label className="block text-xs text-[var(--color-text-muted)]">
            {label.log}
            <input value={log} onChange={(e) => setLog(e.target.value)} className="mt-1 w-full rounded-md border border-[var(--color-border)] bg-[var(--color-surface)] px-2 py-1.5 font-mono text-sm text-[var(--color-text)]" />
          </label>
          <label className="block text-xs text-[var(--color-text-muted)]">
            {label.queue}
            <input value={queue} onChange={(e) => setQueue(e.target.value)} className="mt-1 w-full rounded-md border border-[var(--color-border)] bg-[var(--color-surface)] px-2 py-1.5 font-mono text-sm text-[var(--color-text)]" />
          </label>
          {save.isError && <p role="alert" className="text-xs text-[var(--color-error)]">{toProblem(save.error).message}</p>}
          <div className="flex justify-end gap-2">
            <button type="button" onClick={() => setEditing(false)} className="rounded-md border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-1.5 text-xs font-medium hover:bg-[var(--color-surface-muted)]">Cancel</button>
            <button type="submit" disabled={!log.trim() || !queue.trim() || save.isPending} className="rounded-md bg-[var(--color-primary-700)] px-3 py-1.5 text-xs font-medium text-white disabled:opacity-50">
              {save.isPending ? 'Saving…' : 'Turn on'}
            </button>
          </div>
        </form>
      ) : (
        <div className="mt-2 flex gap-2">
          <button type="button" onClick={open} className="rounded-md border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-1.5 text-xs font-medium hover:bg-[var(--color-surface-muted)]">
            {o.enabled ? 'Change' : 'Set up the observer'}
          </button>
          {o.enabled && (
            <button type="button" disabled={save.isPending} onClick={() => save.mutate({ enabled: false, observerReference: o.observerReference ?? undefined, dlqEntityName: o.dlqEntityName ?? undefined })} className="rounded-md border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-1.5 text-xs font-medium hover:bg-[var(--color-surface-muted)] disabled:opacity-60">
              Turn off
            </button>
          )}
        </div>
      )}
    </div>
  )
}
