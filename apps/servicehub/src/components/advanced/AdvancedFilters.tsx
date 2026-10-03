import { useEffect, useState } from 'react'
import { Bot, Search, User, Users } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'
import { EntityPicker } from '../message/EntityPicker'
import { InfoTip } from '../ui/InfoTip'
import { sectionHelp } from '../../content/sections'
import type { CloudProvider, Namespace } from '../../lib/api/namespaces'
import { providerLabel } from '../../lib/providers'
import { asCloud, environmentMeta, environmentOrder, resolveScope } from '../provider/scopeChoice'
import { Select } from '../ui/Select'

export type By = 'people' | 'autonomous'
export const asBy = (v: string | null): By | undefined => (v === 'people' || v === 'autonomous' ? v : undefined)

const selectClass = 'w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 text-sm text-[var(--color-text)]'

function Field({ label, help, className = '', children }: { label: string; help: (typeof sectionHelp.filters)[keyof typeof sectionHelp.filters]; className?: string; children: React.ReactNode }) {
  return (
    <div className={`flex min-w-[9rem] flex-col ${className}`}>
      <span className="flex items-center text-[12px] font-medium text-[var(--color-text-muted)]">{label}<InfoTip help={help} /></span>
      {children}
    </div>
  )
}

/**
 * The filter row Advanced's Ledger and Signatures share: Cloud → Namespace → Queue or topic, who made it (By) and a word to search.
 * Every choice is in the URL (`provider`, `ns`/`env`, `entity`, `by`, `q`), so a filtered view is a link, and "clear" is removing them.
 * It is the same control for Azure, AWS and Google: the list of clouds is whatever is connected.
 */
export function AdvancedFilters({ namespaces, searchPlaceholder }: { namespaces: readonly Namespace[]; searchPlaceholder: string }) {
  const [params, setParams] = useSearchParams()
  const provider: CloudProvider | null = asCloud(params.get('provider'))
  const pool = provider ? namespaces.filter((n) => n.provider === provider) : namespaces
  const choice = resolveScope(pool, params)
  const clouds = [...new Set(namespaces.map((n) => n.provider))]
  const by = asBy(params.get('by'))
  const q = params.get('q') ?? ''

  const set = (patch: Record<string, string | null>) =>
    setParams((current) => {
      const next = new URLSearchParams(current)
      for (const [k, v] of Object.entries(patch)) {
        if (v === null || v === '') next.delete(k)
        else next.set(k, v)
      }
      // A different filter starts at the top; a page, entry or signature from the last view means nothing in this one.
      ;['page', 'entry', 'signature'].forEach((k) => next.delete(k))
      return next
    }, { replace: true })

  // Search is typed, so it is applied a moment after the last keystroke rather than on each one.
  const [draft, setDraft] = useState(q)
  useEffect(() => setDraft(q), [q])
  useEffect(() => {
    if (draft === q) return
    const t = setTimeout(() => set({ q: draft.trim() || null }), 300)
    return () => clearTimeout(t)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [draft])

  const nsValue = choice.ns ? `ns:${choice.ns.id}` : choice.env ? `env:${choice.env}` : ''
  const envs = environmentOrder.filter((e) => pool.some((n) => n.environment === e))
  const ByIcon = by === 'autonomous' ? Bot : by === 'people' ? User : Users

  return (
    <div className="flex flex-wrap items-end gap-3 border-b border-[var(--color-border)] px-4 py-3">
      <Field label="Cloud" help={sectionHelp.filters.cloud} className="w-44">
        <Select ariaLabel="Cloud" value={provider ?? ''} onChange={(v) => set({ provider: v || null, ns: null, env: null, entity: null })}>
          <option value="">All clouds</option>
          {clouds.map((c) => <option key={c} value={c}>{providerLabel[c]}</option>)}
        </Select>
      </Field>
      <Field label="Namespace" help={sectionHelp.filters.namespace} className="w-52">
        <Select
          ariaLabel="Namespace"
          value={nsValue}
          onChange={(v) => {
            const [kind, id] = v.split(':')
            set({ ns: kind === 'ns' ? id : null, env: kind === 'env' ? id : null, entity: null })
          }}
        >
          <option value="">All namespaces</option>
          {envs.length > 1 && envs.map((e) => <option key={e} value={`env:${e}`}>All {environmentMeta[e].label}</option>)}
          {pool.map((n) => <option key={n.id} value={`ns:${n.id}`}>{n.displayName ?? n.name}</option>)}
        </Select>
      </Field>
      <Field label="Queue or topic" help={sectionHelp.filters.entity} className="w-64 flex-1">
        <EntityPicker
          namespaces={choice.ns ? [choice.ns] : choice.env ? pool.filter((n) => n.environment === choice.env) : pool}
          cloud={provider ? providerLabel[provider] : 'All clouds'}
          recorded={[]}
          value={params.get('entity') ?? undefined}
          onChange={(entity) => set({ entity })}
        />
      </Field>
      <Field label="By" help={sectionHelp.filters.by} className="w-56">
        <Select ariaLabel="By" value={by ?? ''} onChange={(v) => set({ by: v || null })} icon={<ByIcon aria-hidden="true" className="h-4 w-4 shrink-0 text-[var(--color-text-muted)]" />}>
          <option value="">All</option>
          <option value="people">People</option>
          <option value="autonomous">ServiceHub autonomous</option>
        </Select>
      </Field>
      <Field label="Search" help={sectionHelp.filters.search} className="w-72 flex-1">
        <span className="relative">
          <Search aria-hidden="true" className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--color-text-muted)]" />
          <input type="search" aria-label="Search" value={draft} onChange={(e) => setDraft(e.target.value)} placeholder={searchPlaceholder} className={`${selectClass} pl-8`} />
        </span>
      </Field>
    </div>
  )
}
