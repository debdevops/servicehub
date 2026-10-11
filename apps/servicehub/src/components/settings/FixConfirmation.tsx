import { useState } from 'react'
import type { Namespace } from '../../lib/api/namespaces'
import { toProblem } from '../../lib/api/client'
import { useCheckDlqObserver, useConfigureDlqObserver, useDlqObserver } from '../../hooks/useNamespaces'
import { formatAgo } from '../../lib/format'

const btn = 'rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-1.5 text-sm font-semibold hover:bg-[var(--color-surface-muted)] disabled:opacity-50'
const input = 'w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 text-sm'

/**
 * "Confirm fixes on this cloud" (Settings → Connections). Only a cloud that cannot tell by itself whether a replayed message came back
 * (`canProveDlqAbsence` false — asked of the capability, never of the cloud's name, R4) shows it. Off until an Admin switches it on;
 * switching it on confirms nothing by itself — the line says so until ServiceHub has actually been able to see the cloud's dead letters.
 */
export function FixConfirmation({ ns, admin }: { ns: Namespace; admin: { allowed: boolean } }) {
  const wanted = ns.capabilities !== null && !ns.capabilities.canProveDlqAbsence
  const observer = useDlqObserver(ns.id, wanted)
  const configure = useConfigureDlqObserver(ns.id)
  const check = useCheckDlqObserver(ns.id)
  const [reference, setReference] = useState('')
  const [topic, setTopic] = useState('')
  if (!wanted || !observer.data) return null

  const o = observer.data
  const canSwitchOn = !o.needsReference || (reference.trim() !== '' && topic.trim() !== '')
  const result = check.data
  return (
    <div className="mt-2 w-full rounded-lg bg-[var(--color-surface-muted)] px-3 py-2.5 text-sm" aria-label={`Confirming fixes on ${ns.displayName ?? ns.name}`}>
      <p className="font-semibold">Confirm fixes on this cloud <span className="ml-1 font-normal text-[var(--color-text-muted)]">{o.enabled ? (o.live ? 'On — working' : 'On — not working yet') : 'Off'}</span></p>
      <p className="mt-0.5 text-[var(--color-text-muted)]">{o.status}{o.lastConfirmedAt ? ` Last seen ${formatAgo(o.lastConfirmedAt, new Date())}.` : ''}</p>
      {!o.enabled && o.needsReference && (
        <div className="mt-2 grid gap-2 sm:grid-cols-2">
          <label className="block text-xs font-semibold">Subscription ServiceHub reads
            <input className={`${input} mt-1 font-mono`} value={reference} onChange={(e) => setReference(e.target.value)} placeholder="my-dlq-servicehub-observer" />
          </label>
          <label className="block text-xs font-semibold">Dead-letter topic it belongs to
            <input className={`${input} mt-1 font-mono`} value={topic} onChange={(e) => setTopic(e.target.value)} placeholder="my-dlq-topic" />
          </label>
          {o.referenceHint && <p className="text-xs text-[var(--color-text-muted)] sm:col-span-2">{o.referenceHint}</p>}
        </div>
      )}
      <div className="mt-2 flex flex-wrap items-center gap-2">
        {o.enabled ? (
          <button type="button" className={btn} disabled={!admin.allowed || configure.isPending} onClick={() => configure.mutate({ enabled: false })}>Switch off</button>
        ) : (
          <button type="button" className={btn} disabled={!admin.allowed || !canSwitchOn || configure.isPending}
            onClick={() => configure.mutate({ enabled: true, ...(o.needsReference ? { observerReference: reference.trim(), dlqEntityName: topic.trim() } : {}) })}>Switch on</button>
        )}
        {o.enabled && (
          <button type="button" className={btn} disabled={!admin.allowed || check.isPending} onClick={() => check.mutate()}>{check.isPending ? 'Checking…' : 'Check now'}</button>
        )}
      </div>
      {configure.isError && <p role="alert" className="mt-1.5 text-xs text-[#b91c1c]">{toProblem(configure.error).message}</p>}
      {check.isError && <p role="alert" className="mt-1.5 text-xs text-[#b91c1c]">{toProblem(check.error).message}</p>}
      {result && (
        <p className="mt-1.5 text-xs" role="status">
          {result.healthy ? 'ServiceHub can read this cloud’s dead letters.' : `ServiceHub cannot read this cloud’s dead letters right now${result.reason ? ` (${result.reason})` : ''}.`}
        </p>
      )}
    </div>
  )
}
