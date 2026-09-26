import { useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react'
import { Check, ChevronDown, Layers, Search } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'
import type { CloudProvider, EnvironmentKind, Namespace } from '../../lib/api/namespaces'
import { providerLabel } from '../../lib/providers'
import { asCloud, cloudColor, environmentMeta, groupByCloudEnvironment, groupByEnvironment, resolveScope } from './scopeChoice'

type Pick = { cloud?: CloudProvider; env?: EnvironmentKind; ns?: string }

/**
 * The namespace picker: Cloud → Environment → Namespace.
 *
 * The choice is `?ns=<id>` (one namespace) or `?env=<kind>` (an environment), so it is linkable and survives a
 * refresh; "All namespaces" removes both. Namespaces are grouped by environment and each group can be chosen as a whole.
 *
 * Where a page spans clouds it passes `cloudParam` (the URL key that holds the cloud) and the list nests one level deeper:
 * each cloud, then its environments, then its namespaces — and each cloud can be chosen as a whole, and an environment
 * within it ("Azure · Production"). Without `cloudParam` the caller has already narrowed to one cloud and the list is
 * Environment → Namespace. It is the same control for Azure, AWS and GCP — nothing here asks which cloud it is.
 * `compact` drops the top margin so it can sit in a page header beside other controls.
 */
export function NamespaceScope({
  namespaces,
  cloud,
  cloudParam,
  compact = false,
}: {
  namespaces: readonly Namespace[]
  /** Names the scope on the card: "Azure", or "All clouds". */
  cloud: string
  cloudParam?: string
  compact?: boolean
}) {
  const [params, setParams] = useSearchParams()
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const root = useRef<HTMLDivElement>(null)
  const listId = useId()

  const cloudChoice = cloudParam ? asCloud(params.get(cloudParam)) : null
  const pool = cloudChoice ? namespaces.filter((n) => n.provider === cloudChoice) : namespaces
  const choice = resolveScope(pool, params)

  useEffect(() => {
    if (!open) return
    const away = (e: MouseEvent) => root.current && !root.current.contains(e.target as Node) && setOpen(false)
    const esc = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    document.addEventListener('mousedown', away)
    document.addEventListener('keydown', esc)
    return () => {
      document.removeEventListener('mousedown', away)
      document.removeEventListener('keydown', esc)
    }
  }, [open])

  const shown = useMemo(() => {
    const q = query.trim().toLowerCase()
    return q ? namespaces.filter((n) => `${n.displayName ?? ''} ${n.name}`.toLowerCase().includes(q)) : namespaces
  }, [namespaces, query])

  function pick(next: Pick) {
    setParams((p) => {
      const out = new URLSearchParams(p)
      out.delete('ns')
      out.delete('env')
      if (cloudParam) out.delete(cloudParam)
      if (next.ns) out.set('ns', next.ns)
      if (next.env) out.set('env', next.env)
      if (next.cloud && cloudParam) out.set(cloudParam, next.cloud)
      // A page, message or queue from the last scope means nothing in this one.
      ;['page', 'entity', 'message', 'entry', 'signature'].forEach((k) => out.delete(k))
      return out
    })
    setOpen(false)
    setQuery('')
  }

  const nsCloud = choice.ns ? providerLabel[choice.ns.provider] : null
  const title = choice.ns
    ? (choice.ns.displayName ?? choice.ns.name)
    : choice.env
      ? cloudChoice
        ? `${providerLabel[cloudChoice]} · ${environmentMeta[choice.env].label}`
        : `All ${environmentMeta[choice.env].label}`
      : cloudChoice
        ? `All ${providerLabel[cloudChoice]}`
        : 'All namespaces'
  const sub = choice.ns
    ? `${cloudParam && nsCloud ? `${nsCloud} · ` : ''}${environmentMeta[choice.ns.environment].label}`
    : `${choice.namespaces.length} of ${namespaces.length} ${namespaces.length === 1 ? 'namespace' : 'namespaces'}`
  const dot = choice.ns ? environmentMeta[choice.ns.environment].dot : choice.env ? environmentMeta[choice.env].dot : cloudChoice ? cloudColor[cloudChoice] : 'var(--color-primary-500)'

  const nsRow = (n: Namespace, indent: 1 | 2) => (
    <Row key={n.id} selected={choice.ns?.id === n.id} onPick={() => pick({ ns: n.id })} indent={indent}>
      <span className="truncate">{n.displayName ?? n.name}</span>
      <span className="shrink-0 text-[11px] text-[var(--color-text-muted)]">{n.awsRegion ?? n.gcpProjectId ?? ''}</span>
    </Row>
  )

  const envBlock = (env: EnvironmentKind, items: readonly Namespace[], provider: CloudProvider | null) => {
    const meta = environmentMeta[env]
    const nested = provider !== null
    return (
      <li key={env} role="presentation" className={nested ? 'mt-0.5' : 'mt-1.5'}>
        <ul role="group" aria-label={nested ? `${providerLabel[provider]} · ${meta.label}` : meta.label}>
          <Row selected={choice.env === env && (nested ? cloudChoice === provider : true) && !choice.ns} onPick={() => pick(nested ? { cloud: provider, env } : { env })} header indent={nested ? 1 : 0}>
            <span className="flex items-center gap-2 text-[11px] font-bold uppercase tracking-wider">
              <span aria-hidden="true" className="inline-block h-2 w-2 rounded-full" style={{ background: meta.dot }} />
              {nested ? meta.label : `All ${meta.label}`}
            </span>
            <span className={`rounded-full px-2 py-0.5 text-[10.5px] font-semibold ${meta.chip}`}>{items.length}</span>
          </Row>
          {items.map((n) => nsRow(n, nested ? 2 : 1))}
        </ul>
      </li>
    )
  }

  const tree = cloudParam ? groupByCloudEnvironment(shown) : []
  const flat = cloudParam ? [] : groupByEnvironment(shown)
  const empty = cloudParam ? tree.length === 0 : flat.length === 0

  return (
    <div ref={root} className={`relative inline-block ${compact ? '' : 'mt-3'}`}>
      <button
        type="button"
        aria-label="Namespace"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listId}
        onClick={() => setOpen((o) => !o)}
        className={`group flex ${compact ? 'min-w-[240px]' : 'min-w-[280px]'} items-center gap-3 rounded-2xl border border-[var(--color-primary-200)] bg-gradient-to-br from-[var(--color-primary-50)] to-[var(--color-surface)] px-3.5 py-2.5 text-left shadow-[var(--shadow-card)] transition hover:border-[var(--color-primary-400)] hover:shadow-md`}
      >
        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-[var(--color-primary-100)] text-[var(--color-primary-700)]">
          <Layers className="h-[18px] w-[18px]" aria-hidden="true" />
        </span>
        <span className="min-w-0 flex-1">
          <span className="block text-[10.5px] font-bold uppercase tracking-wider text-[var(--color-text-muted)]">{cloud} · Namespace</span>
          <span className="flex items-center gap-1.5 text-[15px] font-bold leading-tight text-[var(--color-text)]">
            <span aria-hidden="true" className="inline-block h-2 w-2 shrink-0 rounded-full" style={{ background: dot }} />
            <span className="truncate">{title}</span>
          </span>
          <span className="block text-[11.5px] text-[var(--color-text-muted)]">{sub}</span>
        </span>
        <ChevronDown className={`h-4 w-4 shrink-0 text-[var(--color-text-muted)] transition-transform ${open ? 'rotate-180' : ''}`} aria-hidden="true" />
      </button>

      {open && (
        <div className="absolute left-0 z-30 mt-2 w-[380px] max-w-[92vw] overflow-hidden rounded-2xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-xl">
          {namespaces.length > 6 && (
            <label className="flex items-center gap-2 border-b border-[var(--color-border)] px-3 py-2">
              <Search className="h-4 w-4 text-[var(--color-text-muted)]" aria-hidden="true" />
              <input autoFocus value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Find a namespace" aria-label="Find a namespace" className="w-full bg-transparent text-[13px] outline-none" />
            </label>
          )}
          <ul id={listId} role="listbox" aria-label="Namespaces" className="max-h-[380px] overflow-y-auto p-1.5">
            <Row selected={!choice.ns && !choice.env && !cloudChoice} onPick={() => pick({})}>
              <span className="font-semibold">All namespaces</span>
              <span className="text-[11.5px] text-[var(--color-text-muted)]">{namespaces.length}</span>
            </Row>

            {tree.map(({ provider, environments }) => {
              const count = environments.reduce((n, e) => n + e.items.length, 0)
              return (
                <li key={provider} role="presentation" className="mt-2">
                  <ul role="group" aria-label={providerLabel[provider]}>
                    <Row selected={cloudChoice === provider && !choice.env && !choice.ns} onPick={() => pick({ cloud: provider })} cloudHeader>
                      <span className="flex items-center gap-2.5">
                        <span aria-hidden="true" className="flex h-5 w-5 items-center justify-center rounded-md text-[10.5px] font-extrabold" style={{ background: `${cloudColor[provider]}22`, color: cloudColor[provider] }}>
                          {providerLabel[provider].charAt(0)}
                        </span>
                        <span className="text-[13px] font-bold">All {providerLabel[provider]}</span>
                      </span>
                      <span className="text-[11.5px] text-[var(--color-text-muted)]">{count}</span>
                    </Row>
                    {environments.map((e) => envBlock(e.env, e.items, provider))}
                  </ul>
                </li>
              )
            })}
            {flat.map((g) => envBlock(g.env, g.items, null))}
            {empty && <li role="presentation" className="px-3 py-4 text-center text-[13px] text-[var(--color-text-muted)]">No namespace matches.</li>}
          </ul>
        </div>
      )}
    </div>
  )
}

function Row({ selected, onPick, children, header, cloudHeader, indent = 0 }: { selected: boolean; onPick: () => void; children: ReactNode; header?: boolean; cloudHeader?: boolean; indent?: 0 | 1 | 2 }) {
  return (
    <li
      role="option"
      aria-selected={selected}
      tabIndex={0}
      onClick={onPick}
      onKeyDown={(e) => (e.key === 'Enter' || e.key === ' ') && (e.preventDefault(), onPick())}
      className={[
        'flex cursor-pointer items-center justify-between gap-3 rounded-lg px-3 py-2 text-[13px] outline-none transition-colors focus-visible:ring-2 focus-visible:ring-[var(--color-primary-400)]',
        indent === 1 ? 'ml-4' : indent === 2 ? 'ml-8' : '',
        cloudHeader ? 'bg-[var(--color-surface-muted)] text-[var(--color-text)]' : header ? 'text-[var(--color-text-muted)]' : 'text-[var(--color-text)]',
        selected ? 'bg-[var(--color-primary-50)]' : 'hover:bg-[var(--color-surface-muted)]',
      ].join(' ')}
    >
      <span className="flex min-w-0 flex-1 items-center justify-between gap-3">{children}</span>
      {selected && <Check className="h-4 w-4 shrink-0 text-[var(--color-primary-700)]" aria-hidden="true" />}
    </li>
  )
}
