import { useMutation, useQuery } from '@tanstack/react-query'
import { Plus, Send, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useMe } from '../../hooks/useIdentity'
import { namespaceKeys, useNamespaces } from '../../hooks/useNamespaces'
import { toProblem } from '../../lib/api/client'
import { sendMessage } from '../../lib/api/messages'
import { fetchEntities } from '../../lib/api/namespaces'
import { permission } from '../../lib/permissions'
import { providerLabel } from '../../lib/providers'
import type { OverlayBodyProps } from '../overlays/registry'
import { useProviderScope } from '../provider/providerScope'
import { environmentMeta } from '../provider/scopeChoice'
import { NotAllowed } from '../ui/NotAllowed'

const input = 'w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 text-sm'
const label = 'mb-1 block text-xs font-semibold uppercase tracking-wide text-[var(--color-text-muted)]'

function jsonProblem(body: string, contentType: string): string | null {
  if (!contentType.includes('json') || !body.trim()) return null
  try {
    JSON.parse(body)
    return null
  } catch (e) {
    return `This is not valid JSON: ${(e as Error).message}`
  }
}

/**
 * Send a message (unit 6.14, `?modal=send`): one test message onto a queue or topic, without leaving Home. No scheduling,
 * sessions or batches — the common case. Production namespaces are not offered: 4.1.0 stays out of production (D46).
 */
export default function SendMessageModal({ close }: OverlayBodyProps) {
  const { selected } = useProviderScope()
  const [params] = useSearchParams()
  const all = useNamespaces().data ?? []
  const candidates = all.filter((n) => (!selected || n.provider === selected) && n.environment !== 'prod')
  const hiddenProd = all.some((n) => (!selected || n.provider === selected) && n.environment === 'prod')
  const [nsId, setNsId] = useState(() => candidates.find((n) => n.id === params.get('ns'))?.id ?? candidates[0]?.id ?? '')
  const ns = candidates.find((n) => n.id === nsId) ?? candidates[0]
  const entities = useQuery({ queryKey: namespaceKeys.entities(ns?.id ?? ''), queryFn: () => fetchEntities(ns!.id), enabled: !!ns })
  const targets = (entities.data?.entities ?? []).filter((e) => e.kind !== 'subscription')
  const [entity, setEntity] = useState('')
  const target = targets.find((e) => e.name === entity) ?? targets[0]
  const [body, setBody] = useState('{\n  "hello": "from ServiceHub"\n}')
  const [contentType, setContentType] = useState('application/json')
  const [props, setProps] = useState<{ key: string; value: string }[]>([])
  const may = permission(useMe().data, 'Operator', 'send a message here', { namespaceId: ns?.id })
  const send = useMutation({ mutationFn: sendMessage })

  if (candidates.length === 0) {
    return (
      <p className="text-sm">
        {hiddenProd ? 'Every namespace here is marked Production, and ServiceHub 4.1.0 does not send into production.' : 'Connect a cloud first — there is nowhere to send to yet.'}
      </p>
    )
  }

  const bad = jsonProblem(body, contentType)
  const dupKey = new Set(props.map((p) => p.key.trim())).size !== props.length
  const ready = !!target && body.trim() !== '' && !bad && !dupKey && props.every((p) => p.key.trim()) && may.allowed
  const submit = () =>
    target && send.mutate({
      namespaceId: ns!.id, entity: target.name, isTopic: target.kind === 'topic', body, contentType: contentType.trim() || undefined,
      properties: props.length ? Object.fromEntries(props.map((p) => [p.key.trim(), p.value])) : undefined,
    })

  return (
    <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); submit() }}>
      <div className="grid gap-3 sm:grid-cols-2">
        {candidates.length > 1 && (
          <label className="block">
            <span className={label}>Namespace</span>
            <select className={input} value={ns?.id} onChange={(e) => { setNsId(e.target.value); setEntity('') }}>
              {candidates.map((n) => <option key={n.id} value={n.id}>{providerLabel[n.provider]} · {n.displayName ?? n.name} · {environmentMeta[n.environment].label}</option>)}
            </select>
          </label>
        )}
        <label className="block">
          <span className={label}>Queue or topic</span>
          <select className={input} value={target?.name ?? ''} onChange={(e) => setEntity(e.target.value)} disabled={!entities.data}>
            {entities.isPending && <option>Reading…</option>}
            {targets.map((e) => <option key={e.name} value={e.name}>{e.name}{e.kind === 'topic' ? ' (topic)' : ''}</option>)}
          </select>
          {entities.data && targets.length === 0 && <span className="mt-1 block text-xs text-[var(--color-text-muted)]">No queues or topics were found here.</span>}
        </label>
      </div>
      <label className="block">
        <span className={label}>Body</span>
        <textarea className={`${input} h-40 font-mono text-[12.5px]`} value={body} onChange={(e) => setBody(e.target.value)} spellCheck={false} aria-invalid={!!bad} />
        {bad && <span role="alert" className="mt-1 block text-xs text-[#b91c1c]">{bad}</span>}
      </label>
      <label className="block">
        <span className={label}>Content type</span>
        <input className={input} value={contentType} onChange={(e) => setContentType(e.target.value)} placeholder="application/json" />
      </label>
      <fieldset>
        <legend className={label}>Properties</legend>
        <div className="space-y-2">
          {props.map((p, i) => (
            <div key={i} className="flex gap-2">
              <input className={input} aria-label={`Property ${i + 1} name`} placeholder="name" value={p.key} onChange={(e) => setProps(props.map((q, j) => (j === i ? { ...q, key: e.target.value } : q)))} />
              <input className={input} aria-label={`Property ${i + 1} value`} placeholder="value" value={p.value} onChange={(e) => setProps(props.map((q, j) => (j === i ? { ...q, value: e.target.value } : q)))} />
              <button type="button" aria-label={`Remove property ${i + 1}`} className="rounded-lg p-2 hover:bg-[var(--color-surface-muted)]" onClick={() => setProps(props.filter((_, j) => j !== i))}><Trash2 className="h-4 w-4" aria-hidden="true" /></button>
            </div>
          ))}
          {dupKey && <p role="alert" className="text-xs text-[#b91c1c]">Two properties have the same name.</p>}
          <button type="button" disabled={props.length >= 20} className="inline-flex items-center gap-1 text-sm font-medium text-[var(--color-primary-700)] hover:underline disabled:opacity-50" onClick={() => setProps([...props, { key: '', value: '' }])}>
            <Plus className="h-4 w-4" aria-hidden="true" /> Add a property
          </button>
        </div>
      </fieldset>
      {hiddenProd && <p className="text-xs text-[var(--color-text-muted)]">Production namespaces are not listed: ServiceHub 4.1.0 does not send into production.</p>}
      <NotAllowed reason={may.reason} />
      <div aria-live="polite">
        {send.isSuccess && <p className="rounded-lg bg-[var(--color-success-light,#ecfdf5)] px-3 py-2 text-sm">{send.data.detail} It will show on the Active tab once the cloud lists it.</p>}
        {send.isError && <p role="alert" className="rounded-lg bg-[#fef2f2] px-3 py-2 text-sm text-[#b91c1c]">{toProblem(send.error).message}</p>}
      </div>
      <div className="flex justify-end gap-2">
        <button type="button" onClick={close} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 text-sm font-semibold">{send.isSuccess ? 'Done' : 'Cancel'}</button>
        <button type="submit" disabled={!ready || send.isPending} className="inline-flex items-center gap-2 rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-sm font-semibold text-white disabled:opacity-50">
          <Send className="h-4 w-4" aria-hidden="true" /> {send.isPending ? 'Sending…' : send.isSuccess ? 'Send another' : 'Send'}
        </button>
      </div>
    </form>
  )
}
