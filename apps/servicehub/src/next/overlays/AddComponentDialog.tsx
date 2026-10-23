import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { CheckCircle2, Copy, Loader2, ShieldCheck, TriangleAlert, XCircle } from 'lucide-react'
import { useMe } from '../../hooks/useIdentity'
import { useNamespaces } from '../../hooks/useNamespaces'
import { useCheckComponent, useComponentList, useSetComponent } from '../../hooks/useComponentEnablement'
import { toProblem } from '../../lib/api/client'
import type { ComponentCheck, ComponentEntry } from '../../lib/api/componentEnablement'
import { permission } from '../../lib/permissions'
import { NotAllowed } from '../../components/ui/NotAllowed'
import { Select } from '../../components/ui/Select'
import { Tip } from '../components/Tip'
import { cloudLabel, cloudOf, parseScope } from '../lib/clouds'
import { nextEntry, nextHref } from '../nav/navigation'
import type { CloudKind } from '../../lib/api/coverage'
import type { NextOverlayBodyProps } from './registry'

/**
 * Add a component (docs-private/future/PLAN-ADD-A-COMPONENT-5.0.0.md; designs `d15`, `d16`, `d17`), at `?dialog=add-component`.
 *
 * One dialog to choose, connect and bring in a PaaS component on a cloud that is already connected. Ticking a row opens it in place: what it
 * needs, copy-ready prerequisites, and a read-only **Check access**. **Add** checks again and saves each row on its own, and the result is
 * drawn on the row: a failed check saves nothing for that row and names what is missing, and never blocks the others.
 *
 * It never asks for a key. A first-ever connection (the credential) stays in Connect a cloud, which this dialog links to. Admin only; the
 * server enforces it, this only says so before the click.
 */
type Outcome = { readonly ok: boolean; readonly message: string }

export default function AddComponentDialog({ close }: NextOverlayBodyProps) {
  const { pathname } = useLocation()
  const scoped = cloudOf(parseScope(pathname.split('/')[2]))
  const [picked, setPicked] = useState<CloudKind | null>(null)
  const cloud = scoped ?? picked
  const namespaces = useNamespaces()
  const connections = (namespaces.data ?? []).filter((n) => n.provider === cloud)
  const [chosen, setChosen] = useState<string | null>(null)
  const connection = connections.find((c) => c.id === chosen) ?? connections[0]
  const may = permission(useMe().data, 'Admin', 'add or remove a component', { namespaceId: connection?.id })

  if (cloud === null) {
    return (
      <div className="space-y-3">
        <p className="text-sm">Which cloud?<Tip label="addc:cloud" /></p>
        <div className="flex gap-2">
          {(['azure', 'aws', 'gcp'] as const).map((c) => (
            <button key={c} type="button" onClick={() => setPicked(c)} className="rounded-lg border border-[var(--color-border)] px-3 py-2 text-sm font-semibold hover:bg-[var(--color-surface-muted)]">
              {cloudLabel[c]}
            </button>
          ))}
        </div>
      </div>
    )
  }

  if (namespaces.isPending) return <p role="status" className="text-sm text-[var(--color-text-muted)]">Loading…</p>

  if (connection === undefined) {
    return (
      <div className="space-y-3">
        <p className="text-sm">No {cloudLabel[cloud]} connection yet. A component is brought in on a cloud that is already connected, so connect the cloud first.</p>
        <Link to={nextHref(nextEntry('connect'), {}, pathname)} className="inline-flex rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-xs font-semibold text-white">
          Connect {cloudLabel[cloud]}
        </Link>
      </div>
    )
  }

  return (
    <Body key={connection.id} close={close} connectionId={connection.id} may={may}
      connectionPicker={connections.length > 1 ? (
        <label className="flex items-center gap-2 text-xs font-semibold">
          Connection
          <Select ariaLabel="Connection" variant="inline" value={connection.id} onChange={(v) => setChosen(v)}>
            {connections.map((c) => <option key={c.id} value={c.id}>{c.displayName ?? c.name}</option>)}
          </Select>
        </label>
      ) : null} />
  )
}

function Body({ close, connectionId, may, connectionPicker }: { close: () => void; connectionId: string; may: { allowed: boolean; reason: string | null }; connectionPicker: React.ReactNode }) {
  const list = useComponentList(connectionId)
  const check = useCheckComponent(connectionId)
  const set = useSetComponent(connectionId)
  const [desired, setDesired] = useState<Readonly<Record<string, boolean>>>({})
  const [checks, setChecks] = useState<Readonly<Record<string, ComponentCheck | 'checking'>>>({})
  const [outcomes, setOutcomes] = useState<Readonly<Record<string, Outcome>>>({})
  const [saving, setSaving] = useState(false)

  if (list.isPending) return <p role="status" className="text-sm text-[var(--color-text-muted)]">Loading…</p>
  if (list.isError) return <p role="alert" className="text-sm text-red-700">{toProblem(list.error).message}</p>

  const entries = list.data.components
  const wanted = (e: ComponentEntry) => desired[e.componentKey] ?? e.state === 'on'
  const adds = entries.filter((e) => e.canToggle && e.state === 'off' && wanted(e))
  const removes = entries.filter((e) => e.canToggle && e.state === 'on' && !wanted(e))
  const changes = adds.length + removes.length
  const finished = Object.keys(outcomes).length > 0 && !saving

  const runCheck = (key: string) => {
    setChecks((c) => ({ ...c, [key]: 'checking' }))
    check.mutate(key, {
      onSuccess: (result) => setChecks((c) => ({ ...c, [key]: result })),
      onError: (error) => setChecks((c) => ({ ...c, [key]: { componentKey: key, ok: false, reason: toProblem(error).message, permissions: [] } })),
    })
  }

  const apply = async () => {
    setSaving(true)
    const next: Record<string, Outcome> = {}
    // One row at a time, each on its own: a failure on one never blocks the others, and nothing half-saved is left behind.
    for (const e of [...adds, ...removes]) {
      const enable = adds.includes(e)
      try {
        await set.mutateAsync({ componentKey: e.componentKey, enabled: enable })
        next[e.componentKey] = { ok: true, message: enable ? 'Added. It is on the menu now.' : 'Removed. Its history is kept.' }
      } catch (error) {
        next[e.componentKey] = { ok: false, message: toProblem(error).message }
      }
    }
    setOutcomes(next)
    setSaving(false)
  }

  return (
    <div className="space-y-4">
      <p className="flex flex-wrap items-center gap-2 rounded-lg border border-[var(--color-border)] bg-[var(--color-primary-50)] px-3 py-2 text-sm">
        Using your existing {list.data.cloud} connection <strong>{list.data.connectionName}</strong><Tip label="addc:connection" />
        <span className="ml-auto">{connectionPicker}</span>
      </p>
      <p className="text-xs text-[var(--color-text-muted)]">Adding a component never asks for a key. It switches on what this connection can already see. Nothing is created or changed in your cloud.</p>
      {!may.allowed && <NotAllowed reason={may.reason} />}

      <h3 className="flex items-center text-sm font-semibold">Which components should ServiceHub watch?<Tip label="addc:which" /></h3>
      <ul className="space-y-2">
        {entries.map((e) => (
          <Row key={e.componentKey} entry={e} on={wanted(e)} disabled={!may.allowed || saving || finished}
            onToggle={(v) => setDesired((d) => ({ ...d, [e.componentKey]: v }))}
            check={checks[e.componentKey]} onCheck={() => runCheck(e.componentKey)} outcome={outcomes[e.componentKey]} />
        ))}
      </ul>

      <div className="flex items-center gap-3 border-t border-[var(--color-border)] pt-3">
        <span className="text-xs text-[var(--color-text-muted)]" role="status">
          {finished ? `${Object.values(outcomes).filter((o) => o.ok).length} saved, ${Object.values(outcomes).filter((o) => !o.ok).length} not saved.` : `${changes} to change.`}
        </span>
        <span className="ml-auto" />
        {finished ? (
          <button type="button" onClick={close} className="rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-xs font-semibold text-white">Done</button>
        ) : (
          <>
            <button type="button" onClick={close} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 text-xs font-semibold">Cancel</button>
            <button type="button" disabled={changes === 0 || !may.allowed || saving} onClick={apply}
              className="inline-flex items-center gap-1.5 rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-xs font-semibold text-white disabled:opacity-50">
              {saving && <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />}
              {removes.length === 0 ? `Add ${adds.length} component${adds.length === 1 ? '' : 's'}` : 'Save changes'}
            </button>
          </>
        )}
      </div>
    </div>
  )
}

function Row({ entry: e, on, disabled, onToggle, check, onCheck, outcome }: {
  entry: ComponentEntry; on: boolean; disabled: boolean; onToggle: (value: boolean) => void
  check: ComponentCheck | 'checking' | undefined; onCheck: () => void; outcome: Outcome | undefined
}) {
  const locked = e.state === 'alwaysOn' || e.state === 'included' || e.state === 'unavailable'
  const expanded = e.canToggle && e.state === 'off' && on
  const failed = outcome?.ok === false
  const tone = failed ? 'border-red-300 bg-red-50' : expanded ? 'border-[var(--color-primary-600)] bg-[var(--color-primary-50)]' : locked && e.state !== 'alwaysOn' ? 'border-dashed border-[var(--color-border)] bg-[var(--color-surface-muted)] text-[var(--color-text-muted)]' : 'border-[var(--color-border)] bg-[var(--color-surface)]'
  const status = e.status === 'beingValidated' ? 'Not yet validated' : e.statusWords // the menu and the designs say it this way
  const label = `${e.name} (${status})`
  return (
    <li className={`rounded-xl border p-3 ${tone}`}>
      <div className="flex gap-3">
        <input type="checkbox" aria-label={label} checked={e.state === 'alwaysOn' ? true : on} disabled={disabled || locked || !e.canToggle}
          onChange={(ev) => onToggle(ev.target.checked)} className="mt-1 h-4 w-4" />
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-sm font-semibold">{e.name}</span><Tip label={`addc:${e.componentKey}`} />
            {e.state === 'alwaysOn' && <span className="rounded bg-green-100 px-2 py-0.5 text-[11px] font-semibold text-green-800">On</span>}
            {e.state === 'on' && !outcome && <span className="rounded bg-green-100 px-2 py-0.5 text-[11px] font-semibold text-green-800">On</span>}
            {expanded && <span className="rounded bg-blue-100 px-2 py-0.5 text-[11px] font-semibold text-blue-800">To be added</span>}
            <span className="rounded bg-purple-100 px-2 py-0.5 text-[11px] font-semibold text-purple-800">{status}</span><Tip label={`addc:status:${e.status}`} />
          </div>
          <p className="mt-0.5 text-xs text-[var(--color-text-muted)]">Where it gets stuck: {e.stuckPlace}</p>
          <p className="mt-1 text-sm">{e.what ?? e.note ?? e.stuckPlace}</p>
          {e.appears && <p className="mt-1 text-xs"><span className="text-[var(--color-text-muted)]">Where it appears: </span>{e.appears}</p>}
          {e.needs && !locked && <p className="mt-1 text-xs"><span className="text-[var(--color-text-muted)]">It needs<Tip label="addc:needs" /> </span>{e.needs}</p>}
          {e.state === 'alwaysOn' && <p className="mt-1 text-xs text-[var(--color-text-muted)]">Watched on every connection of this cloud. Removing the connection removes it.</p>}

          {expanded && <Setup entry={e} check={check} onCheck={onCheck} />}

          {outcome && (
            <p role={outcome.ok ? 'status' : 'alert'} className={`mt-2 flex items-start gap-1.5 text-sm font-medium ${outcome.ok ? 'text-green-800' : 'text-red-800'}`}>
              {outcome.ok ? <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" /> : <XCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}
              {outcome.ok ? outcome.message : outcome.message}
            </p>
          )}
        </div>
      </div>
    </li>
  )
}

function Setup({ entry: e, check, onCheck }: { entry: ComponentEntry; check: ComponentCheck | 'checking' | undefined; onCheck: () => void }) {
  return (
    <div className="mt-3 space-y-3 border-t border-[var(--color-border)] pt-3 text-xs">
      <section>
        <h4 className="font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">Connection</h4>
        <p>{e.access === 'sameConnection' ? <>Uses your existing connection. <strong>No key to paste.</strong></> : <>This is a separate resource, so it has its own credential. Connect it in <strong>Connect a cloud</strong>; the credential is stored encrypted and never shown again.</>}</p>
      </section>
      {e.steps.length > 0 && (
        <section>
          <h4 className="font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">Before you add it</h4>
          <ol className="ml-5 list-decimal space-y-1">{e.steps.map((s) => <li key={s}>{s}</li>)}</ol>
          {e.snippets.map((s) => <Snippet key={s.label} label={s.label} text={s.text} />)}
        </section>
      )}
      <section>
        <h4 className="flex items-center font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">What it lets ServiceHub do<Tip label="addc:readonly" /></h4>
        <p>See the failed runs, with the error each recorded. <strong>Read only.</strong> A failed run is handed to a person; there is no replay for this component.</p>
      </section>
      <div className="flex flex-wrap items-center gap-2">
        <button type="button" onClick={onCheck} disabled={check === 'checking'} className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-1.5 text-xs font-semibold disabled:opacity-60">
          {check === 'checking' ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <ShieldCheck className="h-4 w-4" aria-hidden="true" />} Check access
        </button><Tip label="addc:check" />
        <span className="text-[var(--color-text-muted)]">Only lists and reads. Nothing is started, stopped or saved.</span>
      </div>
      {check !== undefined && check !== 'checking' && (
        <div role="status" className="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] p-2">
          {check.ok ? (
            <>
              {check.permissions.map((p) => <p key={p} className="flex items-center gap-1.5"><CheckCircle2 className="h-4 w-4 text-green-700" aria-hidden="true" />{p} <span className="text-[var(--color-text-muted)]">present</span></p>)}
              {check.permissions.length === 0 && <p className="flex items-center gap-1.5"><CheckCircle2 className="h-4 w-4 text-green-700" aria-hidden="true" />This connection can read it.</p>}
            </>
          ) : (
            <>
              <p className="flex items-start gap-1.5 font-medium text-red-800"><TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />{check.reason}</p>
              {check.permissions.length > 0 && <p className="mt-1 text-[var(--color-text-muted)]">Tested: {check.permissions.join(', ')}</p>}
            </>
          )}
        </div>
      )}
    </div>
  )
}

function Snippet({ label, text }: { label: string; text: string }) {
  const [copied, setCopied] = useState(false)
  const copy = () => {
    try {
      void navigator.clipboard?.writeText(text).then(() => { setCopied(true); setTimeout(() => setCopied(false), 1500) })
    } catch {
      // Clipboard may be blocked; the text is selectable beside the button.
    }
  }
  return (
    <div className="relative mt-2 rounded-lg bg-slate-900 p-3 text-slate-100">
      <span className="mb-1 block text-[11px] text-slate-400">{label}</span>
      <pre className="whitespace-pre-wrap break-all font-mono text-[11.5px] leading-relaxed">{text}</pre>
      <button type="button" onClick={copy} className="absolute right-2 top-2 inline-flex items-center gap-1 text-[11px] font-semibold text-blue-300">
        <Copy className="h-3.5 w-3.5" aria-hidden="true" />{copied ? 'Copied' : 'Copy'}
      </button>
    </div>
  )
}

