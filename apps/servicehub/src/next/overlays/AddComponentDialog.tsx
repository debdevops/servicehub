import { useState } from 'react'
import { Link, useLocation, useSearchParams } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, Copy, Loader2, ShieldCheck, TriangleAlert, XCircle } from 'lucide-react'
import { useMe } from '../../hooks/useIdentity'
import { useNamespaces } from '../../hooks/useNamespaces'
import { componentKeys, useComponentList } from '../../hooks/useComponentEnablement'
import { useCoverage } from '../../hooks/useCoverage'
import { toProblem } from '../../lib/api/client'
import { checkComponent, setComponent, type ComponentCheck, type ComponentEntry } from '../../lib/api/componentEnablement'
import { readsOnDemand, type CloudKind, type DetectionKind } from '../../lib/api/coverage'
import type { Namespace } from '../../lib/api/namespaces'
import { permission } from '../../lib/permissions'
import { NotAllowed } from '../../components/ui/NotAllowed'
import { Select } from '../../components/ui/Select'
import { Tip } from '../components/Tip'
import { cloudBrand, cloudLabel, cloudOf, parseScope } from '../lib/clouds'
import { nextEntry, nextHref } from '../nav/navigation'
import type { NextOverlayBodyProps } from './registry'

/**
 * Add a component (docs-private/future/PLAN-ADD-A-COMPONENT-5.0.0.md; designs `d15`, `d16`, `d17`), at `?dialog=add-component`.
 *
 * It lists **every PaaS component ServiceHub can bring in**, on every cloud, grouped by cloud: what is on, what can be ticked, and what is
 * only on request (greyed, so nothing is hidden). Each cloud uses its own connection (a picker when it has several). Ticking a row opens it in
 * place: what it needs, copy-ready prerequisites, and a read-only **Check access**. **Add** checks again and saves each row on its own, and
 * the result is drawn on the row: a failed check saves nothing for that row and names what is missing, and never blocks the others.
 *
 * It never asks for a key. A cloud with no connection says so and links to Connect a cloud, which stays the first-ever step. Admin only; the
 * server enforces it, this only says so before the click.
 */
type Outcome = { readonly ok: boolean; readonly message: string }
type CheckState = ComponentCheck | 'checking'

const CLOUDS: readonly CloudKind[] = ['azure', 'aws', 'gcp']
const idOf = (namespaceId: string, componentKey: string) => `${namespaceId}|${componentKey}`

/** One cloud's connection (the chosen one, or the one the dialog was opened for, or the first) and what can be brought in on it. */
function useCloud(cloud: CloudKind, all: readonly Namespace[], wanted: string | null) {
  const [chosen, setChosen] = useState<string | null>(null)
  const connections = all.filter((n) => n.provider === cloud)
  const connection = connections.find((c) => c.id === (chosen ?? wanted)) ?? connections[0]
  const list = useComponentList(connection?.id)
  return { cloud, connections, connection, list, setChosen }
}
type CloudData = ReturnType<typeof useCloud>

export default function AddComponentDialog({ close }: NextOverlayBodyProps) {
  const { pathname } = useLocation()
  const wanted = useSearchParams()[0].get('ns') // opened from one connection's row: that connection first, whatever page this is
  const namespaces = useNamespaces()
  const all = namespaces.data ?? []
  const first = all.find((n) => n.id === wanted)?.provider ?? cloudOf(parseScope(pathname.split('/')[2]))
  const may = permission(useMe().data, 'Admin', 'add or remove a component')
  const queryClient = useQueryClient()
  const coverage = useCoverage()

  const azure = useCloud('azure', all, wanted)
  const aws = useCloud('aws', all, wanted)
  const gcp = useCloud('gcp', all, wanted)
  const byCloud = { azure, aws, gcp }
  const sections = CLOUDS.map((c) => byCloud[c]) // a fixed order, so the tabs never reshuffle

  const [desired, setDesired] = useState<Readonly<Record<string, boolean>>>({})
  const [checks, setChecks] = useState<Readonly<Record<string, CheckState>>>({})
  const [outcomes, setOutcomes] = useState<Readonly<Record<string, Outcome>>>({})
  const [saving, setSaving] = useState(false)
  // One tab per cloud, starting on the cloud the page or connection is on. A tab that is switched away keeps its ticks.
  const [tab, setTab] = useState<CloudKind | null>(null)
  const active = tab ?? first ?? CLOUDS[0]!

  if (namespaces.isPending) return <p role="status" className="text-sm text-[var(--color-text-muted)]">Loading…</p>

  const entriesOf = (s: CloudData) => (s.connection && s.list.data ? s.list.data.components.map((entry) => ({ s, entry, id: idOf(s.connection!.id, entry.componentKey) })) : [])
  const rows = sections.flatMap(entriesOf)
  const wantedOn = (r: { entry: ComponentEntry; id: string }) => desired[r.id] ?? r.entry.state === 'on'
  const adds = rows.filter((r) => r.entry.canToggle && r.entry.state === 'off' && wantedOn(r))
  const removes = rows.filter((r) => r.entry.canToggle && r.entry.state === 'on' && !wantedOn(r))
  const changes = adds.length + removes.length
  const finished = Object.keys(outcomes).length > 0 && !saving

  const runCheck = (namespaceId: string, key: string) => {
    const id = idOf(namespaceId, key)
    setChecks((c) => ({ ...c, [id]: 'checking' }))
    checkComponent(namespaceId, key).then(
      (result) => setChecks((c) => ({ ...c, [id]: result })),
      (error: unknown) => setChecks((c) => ({ ...c, [id]: { componentKey: key, ok: false, reason: toProblem(error).message, permissions: [] } })),
    )
  }

  const apply = async () => {
    setSaving(true)
    const next: Record<string, Outcome> = {}
    // One row at a time, each on its own: a failure on one never blocks the others, and nothing half-saved is left behind.
    for (const r of [...adds, ...removes]) {
      const enable = adds.includes(r)
      try {
        await setComponent(r.s.connection!.id, r.entry.componentKey, enable)
        next[r.id] = { ok: true, message: enable ? 'Added. It is on the menu now.' : 'Removed. Its history is kept.' }
      } catch (error) {
        next[r.id] = { ok: false, message: toProblem(error).message }
      }
    }
    await queryClient.invalidateQueries({ queryKey: componentKeys.all })
    setOutcomes(next)
    setSaving(false)
  }

  return (
    <div className="space-y-4">
      <p className="text-sm">Every PaaS component ServiceHub can bring in, on every cloud. Tick the ones ServiceHub should watch.<Tip label="addc:which" /></p>
      <p className="text-xs text-[var(--color-text-muted)]">Adding a component never asks for a key. It switches on what that cloud’s connection can already see. Nothing is created or changed in your cloud.</p>
      {!may.allowed && <NotAllowed reason={may.reason} />}

      <div role="tablist" aria-label="Cloud" className="flex gap-1 border-b-2 border-slate-300">
        {sections.map((s) => {
          const pending = rows.filter((r) => r.s === s && ((r.entry.canToggle && r.entry.state === 'off' && wantedOn(r)) || (r.entry.canToggle && r.entry.state === 'on' && !wantedOn(r)))).length
          const selected = s.cloud === active
          return (
            <button key={s.cloud} type="button" role="tab" id={`add-component-tab-${s.cloud}`} aria-selected={selected} aria-controls={`add-component-${s.cloud}`}
              onClick={() => setTab(s.cloud)}
              style={selected ? { backgroundColor: cloudBrand[s.cloud].bg, borderColor: cloudBrand[s.cloud].accent, borderBottom: `4px solid ${cloudBrand[s.cloud].accent}` } : undefined}
              className={`rounded-t-lg border border-b-0 px-5 py-2 text-sm font-bold ${selected ? 'text-white' : 'border-slate-300 bg-slate-200 text-slate-700 hover:bg-slate-300'}`}>
              {cloudLabel[s.cloud]}
              {pending > 0 && <span className={`ml-2 rounded-full px-2 py-0.5 text-[11px] ${selected ? 'bg-white text-slate-900' : 'bg-blue-100 text-blue-800'}`} aria-label={`${pending} to change`}>{pending}</span>}
            </button>
          )
        })}
      </div>

      {sections.map((s) => (
        <CloudSection key={s.cloud} s={s} pathname={pathname} active={s.cloud === active} coverageNames={coverageNames(coverage.data?.rows ?? [], s.cloud)}
          disabled={!may.allowed || saving || finished} wantedOn={wantedOn} checks={checks} outcomes={outcomes}
          onToggle={(id, v) => setDesired((d) => ({ ...d, [id]: v }))} onCheck={runCheck} />
      ))}

      <div className="sticky bottom-0 flex items-center gap-3 border-t border-[var(--color-border)] bg-[var(--color-surface)] pt-3">
        <span className="text-xs text-[var(--color-text-muted)]" role="status">
          {finished ? `${Object.values(outcomes).filter((o) => o.ok).length} saved, ${Object.values(outcomes).filter((o) => !o.ok).length} not saved.` : `${changes} to change.`}
        </span>
        <span className="ml-auto" />
        {finished ? (
          <button type="button" onClick={close} className="rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-xs font-semibold text-white">Done</button>
        ) : (
          <>
            <button type="button" onClick={close} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 text-xs font-semibold">Cancel</button>
            <button type="button" disabled={changes === 0 || !may.allowed || saving} onClick={() => void apply()}
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

/** The components a cloud can bring in, from the catalogue, for a cloud with no connection yet (nothing can be read without one). */
function coverageNames(rows: readonly { cloud: CloudKind; componentKey: string; name: string; status: string; detection: DetectionKind }[], cloud: CloudKind): string[] {
  const seen = new Set<string>()
  return rows
    .filter((r) => r.cloud === cloud && r.status !== 'notSupported' && (r.detection === 'store' || readsOnDemand(r.detection)) && !seen.has(r.componentKey) && seen.add(r.componentKey))
    .map((r) => (r.status === 'onRequest' ? `${r.name} (on request)` : r.name))
}

function CloudSection({ s, pathname, active, coverageNames: names, disabled, wantedOn, checks, outcomes, onToggle, onCheck }: {
  s: CloudData; pathname: string; active: boolean; coverageNames: readonly string[]; disabled: boolean
  wantedOn: (r: { entry: ComponentEntry; id: string }) => boolean
  checks: Readonly<Record<string, CheckState>>; outcomes: Readonly<Record<string, Outcome>>
  onToggle: (id: string, value: boolean) => void; onCheck: (namespaceId: string, key: string) => void
}) {
  const { connection, connections, list } = s
  const entries = list.data?.components ?? []
  const onCount = entries.filter((e) => e.state === 'on' || e.state === 'alwaysOn').length
  const request = entries.filter((e) => e.state === 'unavailable').length
  const summary = !connection ? 'no connection yet' : list.data ? `${onCount} on${request > 0 ? ` · ${request} on request` : ''}` : 'reading…'
  return (
    <section role="tabpanel" id={`add-component-${s.cloud}`} aria-labelledby={`add-component-tab-${s.cloud}`} hidden={!active} className="space-y-2">
      <div className="flex flex-wrap items-center gap-2 rounded-lg border border-[var(--color-border)] bg-[var(--color-primary-50)] px-3 py-2 text-sm">
        <h3 className="flex items-center font-semibold">{cloudLabel[s.cloud]}<Tip label="addc:cloud-section" /></h3>
        <span className="text-xs text-[var(--color-text-muted)]">{summary}</span>
        {connection && (
          <>
            <span>using your existing connection <strong>{connection.displayName ?? connection.name}</strong></span><Tip label="addc:connection" />
            {connections.length > 1 && (
              <span className="ml-auto flex items-center gap-2 text-xs font-semibold">
                Connection
                <Select ariaLabel={`${cloudLabel[s.cloud]} connection`} variant="inline" value={connection.id} onChange={(v) => s.setChosen(v)}>
                  {connections.map((c) => <option key={c.id} value={c.id}>{c.displayName ?? c.name}</option>)}
                </Select>
              </span>
            )}
          </>
        )}
      </div>

      {!connection && (
        <div className="rounded-lg border border-dashed border-[var(--color-border)] px-3 py-3 text-sm">
          <p>Connect {cloudLabel[s.cloud]} first: a component is brought in on a cloud that is already connected.</p>
          {names.length > 0 && <p className="mt-1 text-xs text-[var(--color-text-muted)]">It can bring in: {names.join(', ')}.</p>}
          <Link to={nextHref(nextEntry('connect'), {}, pathname)} className="mt-2 inline-flex rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-xs font-semibold text-white">Connect {cloudLabel[s.cloud]}</Link>
        </div>
      )}
      {connection && list.isPending && <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading {cloudLabel[s.cloud]} components…</p>}
      {connection && list.isError && <p role="alert" className="text-sm text-red-700">{toProblem(list.error).message}</p>}
      {connection && list.data && (
        <ul className="space-y-2">
          {list.data.components.map((entry) => {
            const id = idOf(connection.id, entry.componentKey)
            return (
              <Row key={id} entry={entry} on={wantedOn({ entry, id })} disabled={disabled}
                onToggle={(v) => onToggle(id, v)} check={checks[id]} onCheck={() => onCheck(connection.id, entry.componentKey)} outcome={outcomes[id]} />
            )
          })}
        </ul>
      )}
    </section>
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

